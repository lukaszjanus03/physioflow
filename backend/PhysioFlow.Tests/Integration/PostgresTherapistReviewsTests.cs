using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class ReviewWriteBarrier : SaveChangesInterceptor
{
    private int _arrived;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public EntityState? Operation { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Operation is { } operation && eventData.Context!.ChangeTracker.Entries<TherapistReview>().Any(e => e.State == operation))
        {
            if (Interlocked.Increment(ref _arrived) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public class PostgresReviewsFactory : PostgresProfileFactory
{
    public ReviewWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(Writes));
    }
}

public class PostgresTherapistReviewsTests
{
    [PostgresFact]
    public async Task Concurrent_creates_should_return_one_201_one_conflict_and_commit_only_one_review_and_audit()
    {
        using var factory = new PostgresReviewsFactory();
        var scenario = await factory.CompletedScenarioAsync();
        factory.Writes.Operation = EntityState.Added;
        var url = $"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}";
        var responses = await Task.WhenAll(scenario.Patient.PostAsJsonAsync(url, new { rating = 2 }),
            scenario.Patient.PostAsJsonAsync(url, new { rating = 5 }));
        factory.Writes.Operation = null;
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("review_already_exists");
        var review = await factory.WithDbAsync(db => db.TherapistReviews.SingleAsync(r => r.TherapistId == scenario.TherapistId));
        (await factory.WithDbAsync(db => db.ReviewStatusChanges.CountAsync(c => c.ReviewId == review.Id))).Should().Be(1);
    }

    [PostgresFact]
    public async Task Concurrent_edit_and_approval_should_never_publish_unreviewed_content()
    {
        using var factory = new PostgresReviewsFactory();
        var scenario = await factory.CompletedScenarioAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId, 5, "Pierwotna treść.");
        var moderator = await factory.CreateModeratorAsync();
        factory.Writes.Operation = EntityState.Modified;
        var responses = await Task.WhenAll(moderator.DecideAsync(review), scenario.Patient.PutAsJsonAsync(
            $"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}", new { rating = 1, comment = "Nowa treść." }));
        factory.Writes.Operation = null;
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
        var current = await scenario.OwnReviewAsync();
        var published = await factory.PublicReviewsAsync(scenario.TherapistId);
        if (current.Status == ReviewStatus.Approved)
        {
            current.Comment.Should().Be("Pierwotna treść.");
            published.Reviews.Items.Should().ContainSingle().Which.Comment.Should().Be("Pierwotna treść.");
        }
        else
        {
            current.Status.Should().Be(ReviewStatus.Pending);
            current.Comment.Should().Be("Nowa treść.");
            published.Reviews.Items.Should().BeEmpty();
        }
        (await factory.WithDbAsync(db => db.ReviewStatusChanges.CountAsync(c => c.ReviewId == review.Id))).Should().Be(2);
    }

    [PostgresFact]
    public async Task Migration_should_preserve_existing_bookings_favorites_and_profiles_and_enforce_rating_constraint()
    {
        using var factory = new PostgresReviewsFactory();
        var scenario = await factory.CalendarScenarioAsync();
        var appointment = await scenario.BookOkAsync();
        (await scenario.Patient.PutAsync($"{FavoriteTherapistsTests.Url}/{scenario.TherapistId}", null)).EnsureSuccessStatusCode();
        var before = await scenario.Therapist.GetFromJsonAsync<TherapistOwnProfileDto>("/api/therapists/me/profile");
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261112185308_AddFavoriteTherapists");
            await migrator.MigrateAsync();
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return 0;
        });
        (await scenario.Therapist.GetFromJsonAsync<TherapistOwnProfileDto>("/api/therapists/me/profile"))
            .Should().BeEquivalentTo(before);
        (await factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == appointment.Id))).Status.Should().Be(appointment.Status);
        (await factory.WithDbAsync(db => db.CooperationRequests.AnyAsync(r => r.Id == scenario.CooperationId))).Should().BeTrue();
        (await factory.WithDbAsync(db => db.Patients.AnyAsync(p => p.Id == scenario.CardId))).Should().BeTrue();
        (await factory.WithDbAsync(db => db.FavoriteTherapists.CountAsync(f => f.TherapistId == scenario.TherapistId))).Should().Be(1);
        (await factory.PublicReviewsAsync(scenario.TherapistId)).AverageRating.Should().BeNull();
        var badWrite = () => factory.WithDbAsync(async db =>
        {
            var userId = await db.Users.Where(u => u.Email == scenario.Email).Select(u => u.Id).SingleAsync();
            db.TherapistReviews.Add(new TherapistReview { TherapistId = scenario.TherapistId, PatientUserId = userId, Rating = 6 });
            return await db.SaveChangesAsync();
        });
        await badWrite.Should().ThrowAsync<DbUpdateException>();
    }

    [PostgresFact]
    public async Task Approved_review_summaries_and_public_author_projection_should_translate_and_refresh_on_postgres()
    {
        using var factory = new PostgresReviewsFactory();
        var scenario = await factory.CompletedScenarioAsync();
        var moderator = await factory.CreateModeratorAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId, 2);
        (await moderator.DecideAsync(review)).EnsureSuccessStatusCode();
        var (other, email) = await factory.CreateConfirmedPatientClientAsync("Ewa", "Nowak");
        await factory.AddCompletedVisitAsync(scenario.TherapistId, email);
        var second = await other.CreateReviewAsync(scenario.TherapistId, 5);
        (await moderator.DecideAsync(second)).EnsureSuccessStatusCode();
        var publicReviews = await factory.PublicReviewsAsync(scenario.TherapistId);
        publicReviews.ReviewCount.Should().Be(2);
        publicReviews.AverageRating.Should().Be(3.5);
        publicReviews.Reviews.Items.Should().Contain(r => r.FirstName == "Ewa" && r.LastNameInitial == "N.");
        var profile = (await factory.CreateClient().GetFromJsonAsync<TherapistCatalogEntryDto>($"/api/therapists/{scenario.TherapistId}"))!;
        profile.AverageRating.Should().Be(3.5);
        profile.ReviewCount.Should().Be(2);
        (await scenario.Patient.PutAsync($"{FavoriteTherapistsTests.Url}/{scenario.TherapistId}", null)).EnsureSuccessStatusCode();
        var favorites = (await scenario.Patient.GetFromJsonAsync<PhysioFlow.Application.Common.Models.PaginatedList<FavoriteTherapistDto>>(
            FavoriteTherapistsTests.Url))!;
        favorites.Items.Single().Profile.Should().BeEquivalentTo(profile);
        (await other.DeleteAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}")).EnsureSuccessStatusCode();
        publicReviews = await factory.PublicReviewsAsync(scenario.TherapistId);
        publicReviews.AverageRating.Should().Be(2);
        publicReviews.ReviewCount.Should().Be(1);
    }
}
