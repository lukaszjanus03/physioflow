using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using PhysioFlow.Infrastructure.Identity;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class ReviewTestSteps
{
    public const string OwnUrl = "/api/patient-accounts/me/therapist-reviews";
    public const string ModerationUrl = "/api/moderation/therapist-reviews";

    public static async Task<PatientCalendarScenario> CompletedScenarioAsync(this PatientCalendarFactory factory)
    {
        factory.Clock.UtcNow = DateTime.UtcNow;
        var scenario = await factory.CalendarScenarioAsync();
        var appointment = await scenario.BookOkAsync();
        factory.Clock.UtcNow = appointment.EndsAt.AddMinutes(1);
        (await scenario.Therapist.PostAsync($"/api/appointments/{appointment.Id}/complete", null))
            .EnsureSuccessStatusCode();
        return scenario;
    }

    public static async Task<OwnTherapistReviewDto> CreateReviewAsync(this HttpClient patient, Guid therapistId,
        int rating = 5, string? comment = "Dobra organizacja wizyty.")
    {
        var response = await patient.PostAsJsonAsync($"{OwnUrl}/{therapistId}", new { rating, comment });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OwnTherapistReviewDto>(CooperationTestSteps.Json))!;
    }

    public static async Task SetAdminAsync(this TestWebApplicationFactory factory, string email, bool enabled = true)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        var result = enabled ? await users.AddToRoleAsync(user, "Admin") : await users.RemoveFromRoleAsync(user, "Admin");
        result.Succeeded.Should().BeTrue();
    }

    public static async Task<HttpClient> CreateModeratorAsync(this TestWebApplicationFactory factory)
    {
        var (client, email) = await factory.CreateConfirmedPatientClientAsync();
        await factory.SetAdminAsync(email);
        // The existing token has no Admin claim. Permission comes from server-side membership.
        return client;
    }

    public static Task<HttpResponseMessage> DecideAsync(this HttpClient moderator, OwnTherapistReviewDto review,
        string action = "approve", string? reason = null) => moderator.PostAsJsonAsync(
            $"{ModerationUrl}/{review.Id}/{action}", new { version = review.Version, reason });

    public static async Task<OwnTherapistReviewDto> OwnReviewAsync(this PatientCalendarScenario scenario) =>
        (await scenario.Patient.GetFromJsonAsync<ReviewEligibilityDto>($"{OwnUrl}/{scenario.TherapistId}", CooperationTestSteps.Json))!.Review!;

    public static async Task<PublicTherapistReviewsDto> PublicReviewsAsync(this TestWebApplicationFactory factory, Guid therapistId) =>
        (await factory.CreateClient().GetFromJsonAsync<PublicTherapistReviewsDto>($"/api/therapists/{therapistId}/reviews"))!;

    public static async Task AddCompletedVisitAsync(this TestWebApplicationFactory factory, Guid therapistId, string email)
    {
        await factory.WithDbAsync(async db =>
        {
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var card = new Patient { TherapistId = therapistId, UserId = userId, FirstName = "Jan", LastName = "Kartowy" };
            db.Patients.Add(card);
            db.Appointments.Add(new Appointment
            {
                TherapistId = therapistId, PatientId = card.Id, PatientUserId = userId,
                StartsAt = DateTime.UtcNow.AddDays(-2), EndsAt = DateTime.UtcNow.AddDays(-2).AddHours(1),
                Duration = AppointmentDuration.Sixty, Status = AppointmentStatus.Completed
            });
            return await db.SaveChangesAsync();
        });
    }
}

public class TherapistReviewsTests : IClassFixture<PatientCalendarFactory>
{
    private readonly PatientCalendarFactory _factory;
    public TherapistReviewsTests(PatientCalendarFactory factory) => _factory = factory;

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Linked_card_and_cooperation_without_completed_visit_should_not_allow_review(AppointmentStatus status)
    {
        _factory.Clock.UtcNow = DateTime.UtcNow;
        var scenario = await _factory.CalendarScenarioAsync();
        var appointment = await scenario.BookOkAsync();
        await _factory.WithDbAsync(async db =>
        {
            (await db.Appointments.SingleAsync(a => a.Id == appointment.Id)).Status = status;
            return await db.SaveChangesAsync();
        });
        var eligibility = (await scenario.Patient.GetFromJsonAsync<ReviewEligibilityDto>(
            $"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}"))!;
        eligibility.CanCreate.Should().BeFalse();
        eligibility.ReasonCode.Should().Be("review_completed_visit_required");
        var response = await scenario.Patient.PostAsJsonAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}", new { rating = 5 });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("review_completed_visit_required");
    }

    [Fact]
    public async Task Completed_visit_should_allow_one_review_with_session_ownership_and_isolated_mutations()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var (other, otherEmail) = await _factory.CreateConfirmedPatientClientAsync();
        var otherId = await _factory.WithDbAsync(db => db.Users.Where(u => u.Email == otherEmail).Select(u => u.Id).SingleAsync());
        var url = $"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}";
        (await scenario.Patient.GetFromJsonAsync<ReviewEligibilityDto>(url))!.CanCreate.Should().BeTrue();
        var response = await scenario.Patient.PostAsJsonAsync(url, new
        {
            rating = 1, comment = "  Punktualna wizyta.  ", patientUserId = otherId, status = "Approved", rejectionReason = "ignored"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var review = (await response.Content.ReadFromJsonAsync<OwnTherapistReviewDto>(CooperationTestSteps.Json))!;
        review.Status.Should().Be(ReviewStatus.Pending);
        review.Comment.Should().Be("Punktualna wizyta.");
        review.RejectionReason.Should().BeNull();
        var ownerId = await _factory.WithDbAsync(db => db.Users.Where(u => u.Email == scenario.Email).Select(u => u.Id).SingleAsync());
        (await _factory.WithDbAsync(db => db.TherapistReviews.SingleAsync(r => r.Id == review.Id))).PatientUserId.Should().Be(ownerId);
        (await other.GetFromJsonAsync<ReviewEligibilityDto>(url))!.Review.Should().BeNull();
        (await other.PutAsJsonAsync(url, new { rating = 5 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await scenario.OwnReviewAsync()).Id.Should().Be(review.Id);
        var duplicate = await scenario.Patient.PostAsJsonAsync(url, new { rating = 4 });
        (await duplicate.ErrorCodeAsync()).Should().Be("review_already_exists");
        (await scenario.Patient.DeleteAsync(url)).EnsureSuccessStatusCode();
        (await scenario.Patient.GetFromJsonAsync<ReviewEligibilityDto>(url))!.CanCreate.Should().BeTrue();
        await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
    }

    [Fact]
    public async Task Approval_edit_rejection_resubmission_withdrawal_and_delete_should_recompute_public_summaries()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var moderator = await _factory.CreateModeratorAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId, 1);
        await AssertSummaryAsync(scenario.TherapistId, 0, null);
        (await moderator.DecideAsync(review)).EnsureSuccessStatusCode();
        await AssertSummaryAsync(scenario.TherapistId, 1, 1);
        var (second, email) = await _factory.CreateConfirmedPatientClientAsync("Ewa", "Nowak");
        await _factory.AddCompletedVisitAsync(scenario.TherapistId, email);
        var secondReview = await second.CreateReviewAsync(scenario.TherapistId, 4);
        await AssertSummaryAsync(scenario.TherapistId, 1, 1);
        (await moderator.DecideAsync(secondReview)).EnsureSuccessStatusCode();
        await AssertSummaryAsync(scenario.TherapistId, 2, 2.5);

        var edit = await scenario.Patient.PutAsJsonAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}",
            new { rating = 3, comment = "Zmieniona opinia." });
        edit.EnsureSuccessStatusCode();
        review = await scenario.OwnReviewAsync();
        review.Status.Should().Be(ReviewStatus.Pending);
        review.DecidedAt.Should().BeNull();
        await AssertSummaryAsync(scenario.TherapistId, 1, 4);
        (await moderator.DecideAsync(review, "reject", "Usuń szczegóły dokumentacji medycznej."))
            .EnsureSuccessStatusCode();
        (await scenario.OwnReviewAsync()).RejectionReason.Should().Be("Usuń szczegóły dokumentacji medycznej.");
        await AssertSummaryAsync(scenario.TherapistId, 1, 4);
        (await scenario.Patient.PutAsJsonAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}", new { rating = 5 }))
            .EnsureSuccessStatusCode();
        review = await scenario.OwnReviewAsync();
        review.RejectionReason.Should().BeNull();
        (await moderator.DecideAsync(review)).EnsureSuccessStatusCode();
        await AssertSummaryAsync(scenario.TherapistId, 2, 4.5);
        review = await scenario.OwnReviewAsync();
        (await moderator.DecideAsync(review, "withdraw", "Ponowna kontrola treści."))
            .EnsureSuccessStatusCode();
        await AssertSummaryAsync(scenario.TherapistId, 1, 4);
        (await second.DeleteAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}")).EnsureSuccessStatusCode();
        await AssertSummaryAsync(scenario.TherapistId, 0, null);
        var changes = await _factory.WithDbAsync(db => db.ReviewStatusChanges.Where(c => c.ReviewId == review.Id).ToListAsync());
        changes.Select(c => c.Action).Should().BeEquivalentTo(new[] { ReviewChangeAction.Created, ReviewChangeAction.Approved,
            ReviewChangeAction.Edited, ReviewChangeAction.Rejected, ReviewChangeAction.Edited, ReviewChangeAction.Approved, ReviewChangeAction.Withdrawn });
        (await _factory.WithDbAsync(db => db.ReviewStatusChanges.AnyAsync(c => c.ReviewId == secondReview.Id
            && c.Action == ReviewChangeAction.Deleted))).Should().BeTrue();
    }

    private async Task AssertSummaryAsync(Guid therapistId, int count, double? average)
    {
        var publicReviews = await _factory.PublicReviewsAsync(therapistId);
        publicReviews.ReviewCount.Should().Be(count);
        publicReviews.AverageRating.Should().Be(average);
        publicReviews.Reviews.TotalCount.Should().Be(count);
        var details = (await _factory.CreateClient().GetFromJsonAsync<TherapistCatalogEntryDto>($"/api/therapists/{therapistId}"))!;
        details.ReviewCount.Should().Be(count);
        details.AverageRating.Should().Be(average);
        var catalog = (await _factory.CreateClient().GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>("/api/therapists?pageSize=50"))!;
        catalog.Items.Single(p => p.Id == therapistId).Should().BeEquivalentTo(details);
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        (await patient.PutAsync($"{FavoriteTherapistsTests.Url}/{therapistId}", null)).EnsureSuccessStatusCode();
        var favorite = (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(FavoriteTherapistsTests.Url))!.Items.Single();
        favorite.Profile.Should().BeEquivalentTo(details);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(null)]
    public async Task Invalid_ratings_should_have_stable_validation_codes(int? rating)
    {
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var response = await patient.SendAsync(new HttpRequestMessage(method, $"{ReviewTestSteps.OwnUrl}/{Guid.NewGuid()}")
                { Content = JsonContent.Create(new { rating }) });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.ErrorCodeAsync()).Should().Be("validation_failed");
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            body.GetProperty("errorCodes").GetProperty("rating").EnumerateArray()
                .Should().Contain(c => c.GetString() == "review_rating_invalid");
        }
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("\"bad\"")]
    public async Task Non_integer_rating_should_have_stable_binding_error_code(string rating)
    {
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var response = await patient.PostAsync($"{ReviewTestSteps.OwnUrl}/{Guid.NewGuid()}",
            new StringContent("{\"rating\":" + rating + "}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ErrorCodeAsync()).Should().Be("validation_failed");
    }

    [Theory]
    [InlineData("email@example.pl")]
    [InlineData("+48 123 456 789")]
    [InlineData("https://example.pl")]
    public async Task Contact_details_should_be_rejected_for_create_and_edit(string comment)
    {
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var response = await patient.SendAsync(new HttpRequestMessage(method, $"{ReviewTestSteps.OwnUrl}/{Guid.NewGuid()}")
                { Content = JsonContent.Create(new { rating = 5, comment }) });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("review_contact_details");
        }
    }

    [Fact]
    public async Task Comment_length_boundary_and_optional_comments_should_be_supported()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var url = $"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}";
        var tooLong = await scenario.Patient.PostAsJsonAsync(url, new { rating = 5, comment = new string('a', 1001) });
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).Should().Contain("review_comment_too_long");
        (await scenario.Patient.CreateReviewAsync(scenario.TherapistId, comment: new string('a', 1000))).Comment!.Length.Should().Be(1000);
        (await scenario.Patient.PutAsJsonAsync(url, new { rating = 2, comment = new string('a', 1001) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await scenario.Patient.PutAsJsonAsync(url, new { rating = 2, comment = "   " })).EnsureSuccessStatusCode();
        (await scenario.OwnReviewAsync()).Comment.Should().BeNull();
    }

    [Fact]
    public async Task Public_reviews_should_expose_only_allowlisted_fields_and_paginate()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
        var moderator = await _factory.CreateModeratorAsync();
        (await moderator.DecideAsync(review)).EnsureSuccessStatusCode();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync("Ewa", "Nowak");
        await _factory.AddCompletedVisitAsync(scenario.TherapistId, email);
        (await moderator.DecideAsync(await patient.CreateReviewAsync(scenario.TherapistId, 3))).EnsureSuccessStatusCode();
        var url = $"/api/therapists/{scenario.TherapistId}/reviews?pageSize=1";
        var response = await _factory.CreateClient().GetAsync(url);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var item = json.GetProperty("reviews").GetProperty("items")[0];
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("rating", "comment", "date", "firstName", "lastNameInitial");
        item.GetProperty("firstName").GetString().Should().Be("Ewa");
        item.GetProperty("lastNameInitial").GetString().Should().Be("N.");
        var next = (await _factory.CreateClient().GetFromJsonAsync<PublicTherapistReviewsDto>(url + "&pageNumber=2"))!;
        next.Reviews.Items.Should().ContainSingle().Which.FirstName.Should().Be("Jan");
        next.Reviews.TotalCount.Should().Be(2);
        next.ReviewCount.Should().Be(2);
        (await _factory.CreateClient().GetFromJsonAsync<PublicTherapistReviewsDto>(url + "&pageNumber=3"))!.Reviews.Items.Should().BeEmpty();
        foreach (var query in new[] { "pageSize=0", "pageSize=51", "pageNumber=0", "pageNumber=2147483647" })
            (await _factory.CreateClient().GetAsync($"/api/therapists/{scenario.TherapistId}/reviews?{query}"))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("deleted")]
    [InlineData("inactive")]
    public async Task Unavailable_profiles_should_not_expose_reviews_but_author_can_manage_review(string reason)
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
        (await (await _factory.CreateModeratorAsync()).DecideAsync(review)).EnsureSuccessStatusCode();
        await _factory.WithDbAsync(async db =>
        {
            var profile = await db.Therapists.SingleAsync(t => t.Id == scenario.TherapistId);
            if (reason == "hidden") profile.IsPublished = false;
            if (reason == "deleted") db.Therapists.Remove(profile);
            if (reason == "inactive") (await db.Users.SingleAsync(u => u.Id == profile.UserId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        (await _factory.CreateClient().GetAsync($"/api/therapists/{scenario.TherapistId}/reviews"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await scenario.Patient.PutAsJsonAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}", new { rating = 2 }))
            .EnsureSuccessStatusCode();
        (await scenario.Patient.DeleteAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}")).EnsureSuccessStatusCode();
        await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
    }

    [Fact]
    public async Task Ending_cooperation_and_relinking_card_should_preserve_review_and_original_visit_eligibility()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId, 1);
        (await (await _factory.CreateModeratorAsync()).DecideAsync(review)).EnsureSuccessStatusCode();
        (await scenario.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{scenario.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        var (other, _) = await _factory.CreateConfirmedPatientClientAsync();
        var request = await other.SendRequestOkAsync(scenario.TherapistId);
        (await scenario.Therapist.AcceptWithExistingCardAsync(request.Id, scenario.CardId)).EnsureSuccessStatusCode();
        (await _factory.PublicReviewsAsync(scenario.TherapistId)).ReviewCount.Should().Be(1);
        var eligibility = (await other.GetFromJsonAsync<ReviewEligibilityDto>($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}"))!;
        eligibility.CanCreate.Should().BeFalse();
        eligibility.ReasonCode.Should().Be("review_completed_visit_required");
        (await scenario.Patient.PutAsJsonAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}", new { rating = 2 }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Patient_routes_should_require_patient_role_and_confirmed_email()
    {
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var therapist = await _factory.CreateTherapistClientAsync();
        await _factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Email == email)).EmailConfirmed = false;
            return await db.SaveChangesAsync();
        });
        foreach (var (client, status) in new[] { (_factory.CreateClient(), HttpStatusCode.Unauthorized),
                     (therapist, HttpStatusCode.Forbidden), (patient, HttpStatusCode.Unauthorized) })
        {
            var url = $"{ReviewTestSteps.OwnUrl}/{Guid.NewGuid()}";
            (await client.GetAsync(url)).StatusCode.Should().Be(status);
            (await client.PostAsJsonAsync(url, new { rating = 5 })).StatusCode.Should().Be(status);
            (await client.PutAsJsonAsync(url, new { rating = 5 })).StatusCode.Should().Be(status);
            (await client.DeleteAsync(url)).StatusCode.Should().Be(status);
        }
    }
}
