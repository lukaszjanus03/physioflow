using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class ReviewModerationTests : IClassFixture<PatientCalendarFactory>
{
    private readonly PatientCalendarFactory _factory;
    public ReviewModerationTests(PatientCalendarFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_patients_and_therapists_should_not_moderate_or_delete_other_reviews()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId, 1);
        foreach (var (client, status) in new[] { (_factory.CreateClient(), HttpStatusCode.Unauthorized),
                     (scenario.Patient, HttpStatusCode.Forbidden), (scenario.Therapist, HttpStatusCode.Forbidden) })
        {
            (await client.GetAsync(ReviewTestSteps.ModerationUrl)).StatusCode.Should().Be(status);
            foreach (var action in new[] { "approve", "reject", "withdraw" })
                (await client.DecideAsync(review, action, "Reason")).StatusCode.Should().Be(status);
        }
        (await scenario.Therapist.DeleteAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await scenario.OwnReviewAsync()).Rating.Should().Be(1);
        // Registration and account updates have no writable moderation permission.
        var (clientWithOldToken, email) = await _factory.CreateConfirmedPatientClientAsync();
        await _factory.SetAdminAsync(email);
        (await clientWithOldToken.GetAsync(ReviewTestSteps.ModerationUrl)).EnsureSuccessStatusCode();
        await _factory.SetAdminAsync(email, false);
        (await clientWithOldToken.GetAsync(ReviewTestSteps.ModerationUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_should_not_moderate_reviews_of_own_profile_or_own_authorship()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var review = await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
        var therapistEmail = await _factory.WithDbAsync(async db =>
        {
            var userId = await db.Therapists.Where(t => t.Id == scenario.TherapistId).Select(t => t.UserId).SingleAsync();
            return (await db.Users.SingleAsync(u => u.Id == userId)).Email!;
        });
        await _factory.SetAdminAsync(therapistEmail);
        await _factory.SetAdminAsync(scenario.Email);
        foreach (var client in new[] { scenario.Therapist, scenario.Patient })
        {
            var page = (await client.GetFromJsonAsync<PaginatedList<ModerationReviewDto>>(
                ReviewTestSteps.ModerationUrl, CooperationTestSteps.Json))!;
            page.Items.Should().NotContain(r => r.Id == review.Id);
            foreach (var action in new[] { "approve", "reject", "withdraw" })
            {
                var response = await client.DecideAsync(review, action, "Reason");
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await response.ErrorCodeAsync()).Should().Be("review_self_moderation_forbidden");
            }
        }
    }

    [Fact]
    public async Task Moderator_should_reject_stale_versions_invalid_transitions_and_missing_reasons()
    {
        var scenario = await _factory.CompletedScenarioAsync();
        var original = await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
        var moderator = await _factory.CreateModeratorAsync();
        (await moderator.DecideAsync(original, "reject")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noVersion = await moderator.PostAsJsonAsync($"{ReviewTestSteps.ModerationUrl}/{original.Id}/approve", new { });
        noVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noVersion.Content.ReadAsStringAsync()).Should().Contain("review_version_required");
        var withdrawPending = await moderator.DecideAsync(original, "withdraw", "Reason");
        (await withdrawPending.ErrorCodeAsync()).Should().Be("review_status_conflict");
        (await scenario.Patient.PutAsJsonAsync($"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}",
            new { rating = 1, comment = "Zmieniona treść do sprawdzenia." })).EnsureSuccessStatusCode();
        var stale = await moderator.DecideAsync(original);
        (await stale.ErrorCodeAsync()).Should().Be("concurrent_modification");
        (await _factory.PublicReviewsAsync(scenario.TherapistId)).ReviewCount.Should().Be(0);
        var current = await scenario.OwnReviewAsync();
        (await moderator.DecideAsync(current)).EnsureSuccessStatusCode();
        current = await scenario.OwnReviewAsync();
        var repeat = await moderator.DecideAsync(current);
        (await repeat.ErrorCodeAsync()).Should().Be("review_status_conflict");
        (await moderator.DecideAsync(current, "withdraw", "  Wymaga ponownej oceny.  ")).EnsureSuccessStatusCode();
        (await scenario.OwnReviewAsync()).RejectionReason.Should().Be("Wymaga ponownej oceny.");
        var publicBody = await _factory.CreateClient().GetStringAsync($"/api/therapists/{scenario.TherapistId}/reviews");
        publicBody.Should().NotContain("Wymaga").And.NotContain("rejectionReason").And.NotContain("patientUserId");
    }

    [Fact]
    public async Task Moderation_queue_should_paginate_validate_and_allow_finding_published_reviews_for_withdrawal()
    {
        var (_, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var moderator = await _factory.CreateModeratorAsync();
        var reviews = new List<OwnTherapistReviewDto>();
        for (var i = 0; i < 2; i++)
        {
            var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
            await _factory.AddCompletedVisitAsync(therapistId, email);
            reviews.Add(await patient.CreateReviewAsync(therapistId));
        }
        // This factory is shared with other tests, so assert page traversal against the queue's total.
        var queue = (await moderator.GetFromJsonAsync<PaginatedList<ModerationReviewDto>>(
            ReviewTestSteps.ModerationUrl + "?pageSize=50", CooperationTestSteps.Json))!;
        queue.Items.Select(r => r.Id).Should().Contain(reviews.Select(r => r.Id));
        var paged = new List<Guid>();
        for (var page = 1; page <= queue.TotalCount; page++)
        {
            var item = (await moderator.GetFromJsonAsync<PaginatedList<ModerationReviewDto>>(
                ReviewTestSteps.ModerationUrl + $"?pageSize=1&pageNumber={page}", CooperationTestSteps.Json))!;
            paged.Add(item.Items.Single().Id);
        }
        paged.Should().Equal(queue.Items.Select(r => r.Id));
        (await moderator.DecideAsync(reviews[0])).EnsureSuccessStatusCode();
        var approved = (await moderator.GetFromJsonAsync<PaginatedList<ModerationReviewDto>>(
            ReviewTestSteps.ModerationUrl + "?status=Approved&pageSize=50", CooperationTestSteps.Json))!;
        approved.Items.Should().Contain(r => r.Id == reviews[0].Id);
        foreach (var query in new[] { "pageSize=0", "pageSize=51", "pageNumber=0", "pageNumber=2147483647", "status=99" })
            (await moderator.GetAsync(ReviewTestSteps.ModerationUrl + "?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class ReviewRateLimitTests
{
    private class LowReviewLimitFactory : PatientCalendarFactory
    {
        protected override int TherapistReviewWritePermitLimit => 2;
    }

    [Fact]
    public async Task Creates_and_edits_should_share_per_account_limit_with_stable_429_code()
    {
        using var factory = new LowReviewLimitFactory();
        var scenario = await factory.CompletedScenarioAsync();
        await scenario.Patient.CreateReviewAsync(scenario.TherapistId);
        var url = $"{ReviewTestSteps.OwnUrl}/{scenario.TherapistId}";
        (await scenario.Patient.PutAsJsonAsync(url, new { rating = 3 })).EnsureSuccessStatusCode();
        var response = await scenario.Patient.PutAsJsonAsync(url, new { rating = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await response.ErrorCodeAsync()).Should().Be("rate_limit_exceeded");
        response.Headers.RetryAfter.Should().NotBeNull();
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        // A different account on the same IP has its own allowance.
        var otherResponse = await other.PostAsJsonAsync(url, new { rating = 4 });
        otherResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await scenario.Patient.GetAsync(url)).EnsureSuccessStatusCode();
        (await scenario.Patient.DeleteAsync(url)).EnsureSuccessStatusCode();
    }
}
