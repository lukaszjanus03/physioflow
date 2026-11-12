using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class FavoriteInsertBarrier : SaveChangesInterceptor
{
    private int _arrived;
    private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Enabled { get; set; }
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && eventData.Context!.ChangeTracker.Entries<FavoriteTherapist>().Any(e => e.State == EntityState.Added))
        {
            if (Interlocked.Increment(ref _arrived) == 2) _bothReady.TrySetResult();
            await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public class PostgresFavoritesFactory : PostgresProfileFactory
{
    public FavoriteInsertBarrier Inserts { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(Inserts));
    }
}

public class PostgresFavoriteTherapistsTests
{
    private const string Url = FavoriteTherapistsTests.Url;

    [PostgresFact]
    public async Task Concurrent_inserts_of_same_bookmark_should_both_succeed_and_store_one_pair()
    {
        using var factory = new PostgresFavoritesFactory();
        var (_, id) = await factory.CreatePublishedTherapistAsync();
        var (patient, email) = await factory.CreateConfirmedPatientClientAsync();
        factory.Inserts.Enabled = true;
        // Both requests pass the existence check before either INSERT is allowed to proceed.
        var results = await Task.WhenAll(patient.PutAsync($"{Url}/{id}", null), patient.PutAsync($"{Url}/{id}", null));
        factory.Inserts.Enabled = false;
        foreach (var response in results)
            response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var account = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        (await factory.WithDbAsync(db => db.FavoriteTherapists.CountAsync(f => f.PatientUserId == account && f.TherapistId == id)))
            .Should().Be(1);
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        (await other.PutAsync($"{Url}/{id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await patient.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await patient.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await other.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.TotalCount.Should().Be(1);
    }

    [PostgresFact]
    public async Task Favorite_migration_should_preserve_profiles_cards_cooperation_and_visits()
    {
        using var factory = new PostgresFavoritesFactory();
        var scenario = await factory.CalendarScenarioAsync();
        var appointment = await scenario.BookOkAsync();
        var before = await scenario.Therapist.GetFromJsonAsync<TherapistOwnProfileDto>("/api/therapists/me/profile");
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261112143145_ExpandTherapistMarketplaceProfile");
            await migrator.MigrateAsync();
            return 0;
        });
        (await scenario.Therapist.GetFromJsonAsync<TherapistOwnProfileDto>("/api/therapists/me/profile"))
            .Should().BeEquivalentTo(before);
        (await factory.WithDbAsync(db => db.Patients.AnyAsync(p => p.Id == scenario.CardId))).Should().BeTrue();
        (await factory.WithDbAsync(db => db.CooperationRequests.AnyAsync(r => r.Id == scenario.CooperationId))).Should().BeTrue();
        (await factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == appointment.Id))).Status.Should().Be(appointment.Status);
        (await scenario.Patient.PutAsync($"{Url}/{scenario.TherapistId}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await scenario.Patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.Items.Should().ContainSingle();
    }

    [PostgresFact]
    public async Task Combined_public_filters_should_translate_on_postgres_and_hidden_bookmarks_should_keep_their_place()
    {
        using var factory = new PostgresFavoritesFactory();
        var tag = Guid.NewGuid().ToString("N");
        async Task<(HttpClient Owner, Guid Id)> Publish(string city, string specialization)
        {
            var owner = await factory.CreateTherapistClientAsync();
            var response = await owner.PutAsJsonAsync("/api/therapists/me/profile",
                TherapistProfileTests.Draft(city, tag, true, firstName: "Łukasz") with
                { Marketplace = new TherapistMarketplaceDto([specialization]) });
            response.EnsureSuccessStatusCode();
            return (owner, (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!.Id);
        }
        var a = await Publish("Kraków", "sports");
        var b = await Publish("Kraków", "sports");
        await Publish("Warszawa", "sports");
        await Publish("Kraków", "orthopedic");
        var anonymous = factory.CreateClient();
        var query = $"/api/therapists?search={Uri.EscapeDataString($" ŁUKASZ {tag} ")}&city={Uri.EscapeDataString(" KRAKÓW ")}&specialization=sports&pageSize=1";
        var first = (await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(query))!;
        var second = (await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(query + "&pageNumber=2"))!;
        first.TotalCount.Should().Be(2);
        first.Items.Concat(second.Items).Select(p => p.Id).Should().Equal(new[] { a.Id, b.Id }.OrderBy(id => id));
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        foreach (var id in new[] { a.Id, b.Id }) (await patient.PutAsync($"{Url}/{id}", null)).EnsureSuccessStatusCode();
        (await a.Owner.PutAsJsonAsync("/api/therapists/me/profile", TherapistProfileTests.Draft("Kraków", tag, false)))
            .EnsureSuccessStatusCode();
        var favorites = (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!;
        favorites.TotalCount.Should().Be(2);
        favorites.Items.Single(f => f.TherapistId == a.Id).Should().Match<FavoriteTherapistDto>(f => !f.IsAvailable && f.Profile == null);
        favorites.Items.Single(f => f.TherapistId == b.Id).IsAvailable.Should().BeTrue();
        (await patient.DeleteAsync($"{Url}/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
