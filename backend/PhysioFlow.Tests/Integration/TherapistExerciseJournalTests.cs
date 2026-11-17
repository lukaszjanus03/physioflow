using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class TherapistJournalTestSteps
{
    public static string Url(Guid patientId) => $"/api/patients/{patientId}/exercise-journal";
    public static string Entry(Guid patientId, Guid id) => Url(patientId) + "/" + id;
    public static async Task<PaginatedList<ExerciseJournalEntryDto>> TherapistJournal(this SharingScenario s, string query = "") =>
        await (await s.Therapist.GetAsync(Url(s.CardId) + query)).Body<PaginatedList<ExerciseJournalEntryDto>>();
    public static async Task<ExerciseJournalSummaryDto> JournalSummary(this SharingScenario s, string query = "") =>
        await (await s.Therapist.GetAsync(Url(s.CardId) + "/summary" + query)).Body<ExerciseJournalSummaryDto>();
    public static async Task<ExerciseJournalEntryDto> TherapistReport(this SharingScenario s, Guid id) =>
        await (await s.Therapist.GetAsync(Entry(s.CardId, id))).Body<ExerciseJournalEntryDto>();
    public static async Task<ExerciseJournalEntryDto> Submit(this SharingScenario s, TherapyPlanDto plan,
        ExerciseCompletionStatus status = ExerciseCompletionStatus.Completed, DateOnly? date = null) =>
        await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(plan) with
        {
            Status = status, PerformedOn = date ?? ExerciseJournalTestSteps.WarsawToday,
            ActualSets = status == ExerciseCompletionStatus.Skipped ? null : 2,
            ActualRepetitions = status == ExerciseCompletionStatus.Skipped ? null : 5
        })).Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);

    public static async Task AssertUnavailable(this SharingScenario s, Guid entryId)
    {
        foreach (var url in new[] { Url(s.CardId), Url(s.CardId) + "/summary", Entry(s.CardId, entryId) })
            (await s.Therapist.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
    }
}

public class TherapistExerciseJournalTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Only_owning_therapist_can_read_and_no_therapist_route_can_modify_reports()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var entry = await s.Submit(plan);
        var stranger = await factory.CreateTherapistClientAsync();
        var otherCard = await s.Therapist.CreateCardAsync();
        var (otherPatient, _) = await factory.CreateConfirmedPatientClientAsync();
        var otherRequest = await otherPatient.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(otherRequest.Id, otherCard)).EnsureSuccessStatusCode();
        var other = new SharingScenario(s.Documentation with { PatientId = otherCard }, otherPatient, otherRequest.Id);
        var otherEntry = await other.Submit(await other.Publish(await other.ActivePlan()), ExerciseCompletionStatus.Partial);
        (await s.TherapistJournal()).Items.Should().ContainSingle().Which.Id.Should().Be(entry.Id);
        (await other.TherapistJournal()).Items.Should().ContainSingle().Which.Id.Should().Be(otherEntry.Id);
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(1, 0, 0, entry.PerformedOn));
        (await other.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 1, 0, otherEntry.PerformedOn));
        var url = TherapistJournalTestSteps.Url(s.CardId);
        foreach (var suffix in new[] { "", "/summary", "/" + entry.Id })
        {
            (await factory.CreateClient().GetAsync(url + suffix)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await s.Patient.GetAsync(url + suffix)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await stranger.GetAsync(url + suffix)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await s.Therapist.GetAsync(TherapistJournalTestSteps.Entry(otherCard, entry.Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.GetAsync(TherapistJournalTestSteps.Entry(s.CardId, otherEntry.Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.GetAsync(url + "/" + Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var response = await s.Therapist.GetAsync(url + "/" + entry.Id);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Body<ExerciseJournalEntryDto>()).Should().BeEquivalentTo(entry);
        (await s.Therapist.GetAsync(url)).Headers.CacheControl!.NoStore.Should().BeTrue();
        (await s.Therapist.GetAsync(url + "/summary")).Headers.CacheControl!.NoStore.Should().BeTrue();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            foreach (var target in new[] { url, url + "/" + entry.Id })
                (await s.Therapist.SendAsync(new HttpRequestMessage(method, target)
                { Content = JsonContent.Create(ExerciseJournalTestSteps.Edit(entry)) }))
                    .StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        }
        (await s.Therapist.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id), ExerciseJournalTestSteps.Edit(entry)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Therapist.DeleteReport(entry.Id, entry.Version)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
    }

    [Fact]
    public async Task Ending_and_resuming_same_account_start_new_therapist_history_without_losing_author_history()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var old = await s.Submit(plan);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        await s.AssertUnavailable(old.Id);
        var request = await s.Patient.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(request.Id, s.CardId)).EnsureSuccessStatusCode();
        (await s.TherapistJournal()).Items.Should().BeEmpty();
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 0, null));
        (await s.Therapist.GetAsync(TherapistJournalTestSteps.Entry(s.CardId, old.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.ReadReport(old.Id)).Should().BeEquivalentTo(old);
        plan = await s.Publish(await s.Therapist.ReadPlan(plan.Id));
        var current = await s.Submit(plan, ExerciseCompletionStatus.Partial);
        (await s.TherapistJournal()).Items.Should().ContainSingle().Which.Id.Should().Be(current.Id);
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 1, 0, current.PerformedOn));
        (await s.Patient.Journal()).TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task New_account_on_same_card_cannot_inherit_previous_authors_reports_even_after_republication()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var old = await s.Submit(plan);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        var request = await other.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(request.Id, s.CardId)).EnsureSuccessStatusCode();
        var relinked = s with { Patient = other, CooperationId = request.Id };
        (await relinked.TherapistJournal()).TotalCount.Should().Be(0);
        (await relinked.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 0, null));
        (await other.Journal()).Items.Should().BeEmpty();
        plan = await relinked.Publish(await s.Therapist.ReadPlan(plan.Id));
        var current = await relinked.Submit(plan);
        (await relinked.TherapistJournal()).Items.Should().ContainSingle().Which.Id.Should().Be(current.Id);
        (await relinked.JournalSummary()).CompletedCount.Should().Be(1);
        (await s.Therapist.GetAsync(TherapistJournalTestSteps.Entry(s.CardId, old.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync(ExerciseJournalTestSteps.Entry(old.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.ReadReport(old.Id)).Should().BeEquivalentTo(old);
    }

    [Fact]
    public async Task Archive_blocks_all_reads_and_unarchive_restores_current_link_history()
    {
        var s = await factory.SharingScenario();
        var entry = await s.Submit(await s.Publish(await s.ActivePlan()));
        (await s.Therapist.DeleteAsync($"/api/patients/{s.CardId}")).EnsureSuccessStatusCode();
        await s.AssertUnavailable(entry.Id);
        (await s.Therapist.PostAsync($"/api/patients/{s.CardId}/unarchive", null)).EnsureSuccessStatusCode();
        (await s.TherapistReport(entry.Id)).Should().BeEquivalentTo(entry);
        (await s.JournalSummary()).CompletedCount.Should().Be(1);
        // Restoring an archived card cannot restore a terminated cooperation.
        (await s.Therapist.DeleteAsync($"/api/patients/{s.CardId}")).EnsureSuccessStatusCode();
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        (await s.Therapist.PostAsync($"/api/patients/{s.CardId}/unarchive", null)).EnsureSuccessStatusCode();
        await s.AssertUnavailable(entry.Id);
    }

    [Fact]
    public async Task Access_requires_existing_profile_card_confirmed_account_and_matching_active_cooperation()
    {
        foreach (var reason in new[] { "profile", "card", "confirmation", "cooperation", "unlink" })
        {
            var s = await factory.SharingScenario();
            var entry = await s.Submit(await s.Publish(await s.ActivePlan()));
            await factory.WithDbAsync(async db =>
            {
                var card = await db.Patients.SingleAsync(p => p.Id == s.CardId);
                switch (reason)
                {
                    case "profile": (await db.Therapists.SingleAsync(t => t.Id == s.TherapistId)).IsDeleted = true; break;
                    case "card": card.IsDeleted = true; break;
                    case "confirmation": (await db.Users.SingleAsync(u => u.Id == card.UserId)).EmailConfirmed = false; break;
                    case "cooperation": (await db.CooperationRequests.SingleAsync(r => r.Id == s.CooperationId)).Status = CooperationRequestStatus.Ended; break;
                    case "unlink": card.UserId = null; break;
                }
                return await db.SaveChangesAsync();
            });
            await s.AssertUnavailable(entry.Id);
        }
    }

    [Fact]
    public async Task Historical_snapshots_survive_library_plan_corrections_revocation_and_plan_deletion()
    {
        var s = await factory.SharingScenario();
        var exercise = await s.Therapist.NewExercise("Historical exercise");
        var plan = await s.Publish(await s.ActivePlan(exercise));
        var entry = await s.Submit(plan);
        entry.Exercise.Sets.Should().Be(3);
        entry.ActualSets.Should().Be(2);
        entry.Exercise.Repetitions.Should().Be(10);
        entry.ActualRepetitions.Should().Be(5);
        (await s.Therapist.PutAsJsonAsync(TherapyTestSteps.Exercise(exercise.Id), new SaveExerciseRequest
        { Version = exercise.Version, Name = "Changed exercise", Instructions = "Changed instructions", Category = "Back" }))
            .EnsureSuccessStatusCode();
        plan = await s.Therapist.ReadPlan(plan.Id);
        var corrected = await (await s.Therapist.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/corrections",
            TherapyTestSteps.Correction(plan) with { Name = "Changed plan", Exercises = TherapyTestSteps.Complete(exercise.Id).Exercises }))
            .Body<TherapyPlanDto>();
        corrected = await s.Publish(corrected);
        var current = await s.Submit(corrected);
        current.PlanVersionNumber.Should().Be(2);
        current.Exercise.Name.Should().Be("Changed exercise");
        (await s.TherapistReport(entry.Id)).Should().BeEquivalentTo(entry);
        corrected = await s.Therapist.ReadPlan(plan.Id);
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { corrected.Version }))
            .EnsureSuccessStatusCode();
        (await s.TherapistReport(entry.Id)).Should().BeEquivalentTo(entry);
        await factory.WithDbAsync(async db =>
        {
            (await db.TherapyPlans.SingleAsync(p => p.Id == plan.Id)).IsDeleted = true;
            (await db.Exercises.SingleAsync(e => e.Id == exercise.Id)).IsDeleted = true;
            return await db.SaveChangesAsync();
        });
        (await s.TherapistReport(entry.Id)).Should().BeEquivalentTo(entry);
        (await s.TherapistJournal()).TotalCount.Should().Be(2);
        (await s.JournalSummary()).CompletedCount.Should().Be(2);
    }

    [Fact]
    public async Task All_filters_apply_to_full_summary_and_stable_pages_count_independent_daily_reports()
    {
        var s = await factory.SharingScenario();
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 0, null));
        var plan = await s.Publish(await s.ActivePlan());
        var secondPlan = await s.Publish(await s.ActivePlan());
        await factory.WithDbAsync(async db =>
        {
            foreach (var p in await db.TherapyPlans.Where(p => p.PatientId == s.CardId).ToListAsync())
                p.PublishedAt = DateTime.UtcNow.AddDays(-5);
            return await db.SaveChangesAsync();
        });
        var today = ExerciseJournalTestSteps.WarsawToday;
        var first = await s.Submit(plan, date: today.AddDays(-1));
        var second = await s.Submit(plan, date: today.AddDays(-1));
        var partial = await s.Submit(plan, ExerciseCompletionStatus.Partial, today.AddDays(-2));
        var skipped = await s.Submit(plan, ExerciseCompletionStatus.Skipped);
        var other = await s.Submit(secondPlan);
        // Force equal date/time keys so the final ID key must keep page boundaries stable.
        await factory.WithDbAsync(async db =>
        {
            var rows = await db.ExerciseJournalEntries.Where(e => e.PatientId == s.CardId).ToListAsync();
            foreach (var row in rows) row.CreatedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            return await db.SaveChangesAsync();
        });
        var page1 = await s.TherapistJournal("?pageSize=2");
        var page2 = await s.TherapistJournal("?pageSize=2&pageNumber=2");
        var page3 = await s.TherapistJournal("?pageSize=2&pageNumber=3");
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
        page1.HasNext.Should().BeTrue();
        var ids = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(e => e.Id).ToList();
        ids.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(new[] { first.Id, second.Id, partial.Id, skipped.Id, other.Id });
        (await s.TherapistJournal("?pageSize=2")).Items.Should().BeEquivalentTo(page1.Items, o => o.WithStrictOrdering());
        var all = await s.TherapistJournal();
        ids.Should().Equal(all.Items.Select(e => e.Id));
        all.Items.Select(e => e.PerformedOn).Should().BeInDescendingOrder();
        (await s.TherapistJournal("?pageSize=2&pageNumber=4")).Items.Should().BeEmpty();
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(3, 1, 1, today));
        var checks = new (string Query, Guid[] Ids, ExerciseJournalSummaryDto Summary)[]
        {
            ($"?therapyPlanId={plan.Id}", [first.Id, second.Id, partial.Id, skipped.Id], new(2, 1, 1, today.AddDays(-1))),
            ($"?planItemId={plan.Content.Exercises[0].Id}", [first.Id, second.Id, partial.Id, skipped.Id], new(2, 1, 1, today.AddDays(-1))),
            ("?status=Partial", [partial.Id], new(0, 1, 0, today.AddDays(-2))),
            ("?status=Skipped", [skipped.Id], new(0, 0, 1, null)),
            ($"?from={today:yyyy-MM-dd}", [skipped.Id, other.Id], new(1, 0, 1, today)),
            ($"?to={today.AddDays(-2):yyyy-MM-dd}", [partial.Id], new(0, 1, 0, today.AddDays(-2))),
            ($"?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}&therapyPlanId={plan.Id}&planItemId={first.PlanItemId}&status=Completed", [first.Id, second.Id], new(2, 0, 0, today.AddDays(-1))),
            ($"?therapyPlanId={Guid.NewGuid()}", [], new(0, 0, 0, null)),
            ($"?planItemId={Guid.NewGuid()}", [], new(0, 0, 0, null)),
            ($"?therapyPlanId={secondPlan.Id}&planItemId={first.PlanItemId}", [], new(0, 0, 0, null))
        };
        foreach (var check in checks)
        {
            var list = await s.TherapistJournal(check.Query + "&pageSize=1");
            list.TotalCount.Should().Be(check.Ids.Length, check.Query);
            (await s.TherapistJournal(check.Query)).Items.Select(e => e.Id).Should().BeEquivalentTo(check.Ids, check.Query);
            (await s.JournalSummary(check.Query)).Should().BeEquivalentTo(check.Summary, check.Query);
        }
    }

    [Fact]
    public async Task Patient_edits_and_soft_deletes_update_details_filters_and_summary_without_coalescing_null_pain()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        await factory.WithDbAsync(async db =>
        {
            (await db.TherapyPlans.SingleAsync(p => p.Id == plan.Id)).PublishedAt = DateTime.UtcNow.AddDays(-5);
            return await db.SaveChangesAsync();
        });
        var first = await s.Submit(plan, date: ExerciseJournalTestSteps.WarsawToday.AddDays(-1));
        var second = await s.Submit(plan);
        (await s.TherapistReport(first.Id)).PainLevel.Should().Be(0);
        var changed = await (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(first.Id),
            ExerciseJournalTestSteps.Edit(first) with
            {
                Status = ExerciseCompletionStatus.Partial, PainLevel = null, Comment = "Edited by author",
                PerformedOn = ExerciseJournalTestSteps.WarsawToday
            }))
            .Body<ExerciseJournalEntryDto>();
        (await s.TherapistReport(first.Id)).Should().BeEquivalentTo(changed);
        (await s.TherapistReport(first.Id)).PainLevel.Should().BeNull();
        (await s.TherapistJournal("?status=Completed")).Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        (await s.TherapistJournal($"?to={first.PerformedOn:yyyy-MM-dd}")).Items.Should().BeEmpty();
        (await s.JournalSummary($"?to={first.PerformedOn:yyyy-MM-dd}"))
            .Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 0, null));
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(1, 1, 0, changed.PerformedOn));
        (await s.Patient.DeleteReport(second.Id, second.Version)).EnsureSuccessStatusCode();
        (await s.Therapist.GetAsync(TherapistJournalTestSteps.Entry(s.CardId, second.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.TherapistJournal()).Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 1, 0, changed.PerformedOn));
        (await s.JournalSummary("?status=Completed")).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 0, null));
        changed = await (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(changed.Id),
            ExerciseJournalTestSteps.Edit(changed) with
            { Status = ExerciseCompletionStatus.Skipped, ActualSets = null, ActualRepetitions = null }))
            .Body<ExerciseJournalEntryDto>();
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 1, null));
        (await s.TherapistJournal("?status=Partial")).Items.Should().BeEmpty();
        (await s.Patient.DeleteReport(changed.Id, changed.Version)).EnsureSuccessStatusCode();
        (await s.TherapistJournal()).Items.Should().BeEmpty();
        (await s.JournalSummary()).Should().BeEquivalentTo(new ExerciseJournalSummaryDto(0, 0, 0, null));
    }

    [Fact]
    public async Task Invalid_filters_and_pagination_return_validation_errors_on_the_applicable_endpoints()
    {
        var s = await factory.SharingScenario();
        var url = TherapistJournalTestSteps.Url(s.CardId);
        var invalidFilters = new[]
        {
            "?from=garbage", "?to=garbage", "?from=0001-01-01", "?to=0001-01-01",
            "?from=2026-11-17&to=2026-11-16", "?status=999", "?status=Unknown",
            "?therapyPlanId=garbage", "?planItemId=garbage", "?therapyPlanId=" + Guid.Empty,
            "?planItemId=" + Guid.Empty
        };
        foreach (var suffix in new[] { "", "/summary" })
            foreach (var query in invalidFilters)
            {
                var response = await s.Therapist.GetAsync(url + suffix + query);
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest, suffix + query);
                (await response.ErrorCodeAsync()).Should().Be("validation_failed");
            }
        foreach (var query in new[] { "?pageSize=0", "?pageSize=101", "?pageNumber=0", "?pageNumber=-1",
            "?pageSize=100&pageNumber=2147483647", "?pageNumber=garbage", "?pageSize=garbage" })
            (await s.Therapist.GetAsync(url + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest, query);
    }
}
