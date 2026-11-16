using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class ExerciseJournalTestSteps
{
    public const string Url = "/api/patient-portal/exercise-journal";
    public static string Entry(Guid id) => Url + "/" + id;
    public static DateOnly WarsawToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")));
    public static CreateExerciseJournalRequest Report(TherapyPlanDto plan) => new()
    {
        RequestId = Guid.NewGuid(), TherapyPlanId = plan.Id, PlanVersionNumber = plan.Publication!.VersionNumber,
        PlanItemId = plan.Content.Exercises[0].Id, PerformedOn = WarsawToday,
        Status = ExerciseCompletionStatus.Completed, ActualSets = 3, ActualRepetitions = 10,
        PainLevel = 0, Comment = "PATIENT_HEALTH_COMMENT"
    };
    public static UpdateExerciseJournalRequest Edit(ExerciseJournalEntryDto entry) => new()
    {
        Version = entry.Version, PerformedOn = entry.PerformedOn, Status = entry.Status,
        ActualSets = entry.ActualSets, ActualRepetitions = entry.ActualRepetitions,
        ActualDurationSeconds = entry.ActualDurationSeconds, PainLevel = entry.PainLevel, Comment = entry.Comment
    };
    public static Task<HttpResponseMessage> DeleteReport(this HttpClient client, Guid id, Guid version) => client.SendAsync(
        new HttpRequestMessage(HttpMethod.Delete, Entry(id)) { Content = JsonContent.Create(new { version }) });
    public static async Task<ExerciseJournalEntryDto> ReadReport(this HttpClient client, Guid id) =>
        await (await client.GetAsync(Entry(id))).Body<ExerciseJournalEntryDto>();
    public static async Task<PaginatedList<ExerciseJournalEntryDto>> Journal(this HttpClient client, string query = "") =>
        await (await client.GetAsync(Url + query)).Body<PaginatedList<ExerciseJournalEntryDto>>();
}

public class ExerciseJournalTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Reports_keep_immutable_version_and_position_snapshots_after_library_and_plan_changes()
    {
        var s = await factory.SharingScenario();
        var exercise = await s.Therapist.NewExercise("Original exercise");
        var plan = await s.Publish(await s.ActivePlan(exercise));
        var request = ExerciseJournalTestSteps.Report(plan);
        var response = await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request);
        var entry = await response.Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        entry.PatientId.Should().Be(s.CardId);
        entry.PlanVersionNumber.Should().Be(1);
        entry.PlanItemId.Should().Be(plan.Content.Exercises[0].Id);
        entry.Exercise.Name.Should().Be("Original exercise");
        entry.Exercise.Instructions.Should().Be(exercise.Instructions);
        entry.PainLevel.Should().Be(0);
        entry.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        entry.PerformedOn.Should().Be(request.PerformedOn);
        entry.TherapyPlanVersionId.Should().Be(await factory.WithDbAsync(db => db.TherapyPlanVersions
            .Where(v => v.TherapyPlanId == plan.Id && v.VersionNumber == 1).Select(v => v.Id).SingleAsync()));
        var editedLibrary = await (await s.Therapist.PutAsJsonAsync(TherapyTestSteps.Exercise(exercise.Id), new SaveExerciseRequest
        {
            Version = exercise.Version, Name = "Changed library exercise", Instructions = "Changed instructions", Category = "Back"
        })).Body<ExerciseDto>();
        var replacement = await s.Therapist.NewExercise("Replacement");
        plan = await s.Therapist.ReadPlan(plan.Id); // Journal writes also fence concurrent publication changes.
        var corrected = await (await s.Therapist.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/corrections",
            TherapyTestSteps.Correction(plan) with { Name = "New plan", Exercises = TherapyTestSteps.Complete(replacement.Id).Exercises }))
            .Body<TherapyPlanDto>();
        corrected = await s.Publish(corrected);
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
        (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { RequestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound); // Version 1 is no longer shared.
        var current = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(corrected)))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        current.PlanVersionNumber.Should().Be(2);
        current.TherapyPlanVersionId.Should().NotBe(entry.TherapyPlanVersionId);
        (await s.Patient.ReadReport(entry.Id)).Exercise.Name.Should().Be("Original exercise");
        editedLibrary.Name.Should().Be("Changed library exercise");
    }

    [Fact]
    public async Task Only_confirmed_owner_can_report_currently_shared_positions_or_access_own_entries()
    {
        var s = await factory.SharingScenario();
        var active = await s.ActivePlan();
        var plan = await s.Publish(active);
        var report = ExerciseJournalTestSteps.Report(plan);
        var (stranger, _) = await factory.CreateConfirmedPatientClientAsync();
        var moderator = await factory.CreateModeratorAsync();
        foreach (var client in new[] { stranger, moderator })
        {
            (await client.PostAsJsonAsync(ExerciseJournalTestSteps.Url, report)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Journal()).Items.Should().BeEmpty();
        }
        (await factory.CreateClient().PostAsJsonAsync(ExerciseJournalTestSteps.Url, report)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await s.Therapist.PostAsJsonAsync(ExerciseJournalTestSteps.Url, report)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var invalid in new[]
        {
            report with { PlanItemId = Guid.NewGuid() }, report with { PlanVersionNumber = 2 },
            report with { TherapyPlanId = Guid.NewGuid() },
            report with { PlanItemId = (await s.Therapist.NewExercise()).Id }
        })
            (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, invalid)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unshared = await s.ActivePlan();
        (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, report with { TherapyPlanId = unshared.Id,
            PlanItemId = unshared.Content.Exercises[0].Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, report)).Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        foreach (var client in new[] { stranger, moderator })
        {
            (await client.GetAsync(ExerciseJournalTestSteps.Entry(entry.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id), ExerciseJournalTestSteps.Edit(entry))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.DeleteReport(entry.Id, entry.Version)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await s.Therapist.GetAsync(ExerciseJournalTestSteps.Entry(entry.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await factory.WithDbAsync(async db =>
        {
            var accountId = await db.ExerciseJournalEntries.Where(e => e.Id == entry.Id).Select(e => e.PatientUserId).SingleAsync();
            (await db.Users.SingleAsync(u => u.Id == accountId)).EmailConfirmed = false;
            return await db.SaveChangesAsync();
        });
        (await s.Patient.GetAsync(ExerciseJournalTestSteps.Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revocation_stops_new_reports_but_history_edit_delete_and_retries_remain_owned_by_author()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var request = ExerciseJournalTestSteps.Report(plan);
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request)).Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        plan = await s.Therapist.ReadPlan(plan.Id);
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { plan.Version })).EnsureSuccessStatusCode();
        (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { RequestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request)).Body<ExerciseJournalEntryDto>()).Id.Should().Be(entry.Id);
        (await s.Patient.Journal()).Items.Should().ContainSingle().Which.Id.Should().Be(entry.Id);
        var updated = await (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id),
            ExerciseJournalTestSteps.Edit(entry) with { Comment = "Edited after revoke", PainLevel = null }))
            .Body<ExerciseJournalEntryDto>();
        updated.PainLevel.Should().BeNull();
        updated.Exercise.Should().BeEquivalentTo(entry.Exercise);
        (await s.Patient.DeleteReport(updated.Id, updated.Version)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await s.Patient.Journal()).Items.Should().BeEmpty();
        (await s.Patient.GetAsync(ExerciseJournalTestSteps.Entry(entry.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var retryDeleted = await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request);
        (await retryDeleted.ErrorCodeAsync()).Should().Be("journal_entry_deleted");
        (await factory.WithDbAsync(db => db.ExerciseJournalEntries.CountAsync(e => e.Id == entry.Id && e.IsDeleted))).Should().Be(1);
    }

    [Fact]
    public async Task Ending_cooperation_relinking_and_parent_deletion_preserve_author_history_without_transferring_it()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var request = ExerciseJournalTestSteps.Report(plan);
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request)).Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)).EnsureSuccessStatusCode();
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
        (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { RequestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        var otherRequest = await other.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(otherRequest.Id, s.CardId)).EnsureSuccessStatusCode();
        (await other.Journal()).Items.Should().BeEmpty();
        (await other.GetAsync(ExerciseJournalTestSteps.Entry(entry.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id), ExerciseJournalTestSteps.Edit(entry))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{otherRequest.Id}/end", null)).EnsureSuccessStatusCode();
        var renewed = await s.Patient.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(renewed.Id, s.CardId)).EnsureSuccessStatusCode();
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
        (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { RequestId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        plan = await s.Publish(await s.Therapist.ReadPlan(plan.Id));
        var second = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { RequestId = Guid.NewGuid() }))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        var rows = await factory.WithDbAsync(db => db.ExerciseJournalEntries.Where(e => e.PatientId == s.CardId).ToListAsync());
        rows.Should().HaveCount(2);
        rows.Select(e => e.PublicationLinkVersion).Distinct().Should().HaveCount(2);
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.CardId)).IsDeleted = true;
            (await db.TherapyPlans.SingleAsync(p => p.Id == plan.Id)).IsDeleted = true;
            (await db.Therapists.SingleAsync(t => t.Id == s.TherapistId)).IsDeleted = true;
            return await db.SaveChangesAsync();
        });
        (await s.Patient.Journal()).Items.Should().HaveCount(2);
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
        (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(second.Id), ExerciseJournalTestSteps.Edit(second) with { Comment = "Still mine" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Idempotent_retry_never_duplicates_or_overwrites_an_edited_report_and_new_keys_allow_multiple_daily_reports()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var request = ExerciseJournalTestSteps.Report(plan);
        var first = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request)).Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        var replay = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request)).Body<ExerciseJournalEntryDto>();
        replay.Should().BeEquivalentTo(first);
        var different = await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { Comment = "Other content" });
        different.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await different.ErrorCodeAsync()).Should().Be("idempotency_key_reused");
        var edited = await (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(first.Id),
            ExerciseJournalTestSteps.Edit(first) with { Status = ExerciseCompletionStatus.Partial, ActualRepetitions = 5 })).Body<ExerciseJournalEntryDto>();
        (await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request)).Body<ExerciseJournalEntryDto>()).Should().BeEquivalentTo(edited);
        var second = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, request with { RequestId = Guid.NewGuid() }))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        second.Id.Should().NotBe(first.Id);
        (await s.Patient.Journal()).TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Stable_pagination_and_inclusive_filters_distinguish_partial_skipped_and_missing_reports()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var otherPlan = await s.Publish(await s.ActivePlan());
        (await s.Patient.Journal()).TotalCount.Should().Be(0); // No implicit skipped entries.
        var date = ExerciseJournalTestSteps.WarsawToday;
        foreach (var status in Enum.GetValues<ExerciseCompletionStatus>())
            (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(plan) with
            { Status = status, ActualSets = null, ActualRepetitions = null, PainLevel = null })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(otherPlan))).StatusCode.Should().Be(HttpStatusCode.Created);
        var first = await s.Patient.Journal("?pageSize=2");
        var second = await s.Patient.Journal("?pageSize=2&pageNumber=2");
        first.TotalCount.Should().Be(4);
        first.TotalPages.Should().Be(2);
        first.HasNext.Should().BeTrue();
        first.Items.Select(e => e.Id).Intersect(second.Items.Select(e => e.Id)).Should().BeEmpty();
        (await s.Patient.Journal("?pageSize=2")).Items.Should().BeEquivalentTo(first.Items, o => o.WithStrictOrdering());
        (await s.Patient.Journal("?pageNumber=3&pageSize=2")).Items.Should().BeEmpty();
        var filtered = await s.Patient.Journal($"?from={date:yyyy-MM-dd}&to={date:yyyy-MM-dd}&therapyPlanId={plan.Id}&status=Partial");
        filtered.Items.Should().ContainSingle().Which.Status.Should().Be(ExerciseCompletionStatus.Partial);
        filtered.Items[0].PainLevel.Should().BeNull();
        (await s.Patient.Journal($"?to={date.AddDays(-1):yyyy-MM-dd}")).Items.Should().BeEmpty();
        (await s.Patient.Journal("?therapyPlanId=" + Guid.NewGuid())).Items.Should().BeEmpty();
        foreach (var query in new[] { "?pageSize=101", "?pageNumber=0", "?pageSize=100&pageNumber=2147483647",
            "?from=2026-11-17&to=2026-11-16", "?status=999", "?status=Unknown", "?from=garbage", "?from=0001-01-01", "?therapyPlanId=" + Guid.Empty })
            (await s.Patient.GetAsync(ExerciseJournalTestSteps.Url + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest, query);
    }

    [Fact]
    public async Task Validation_requires_status_valid_dates_dose_pain_comment_and_edit_tokens()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var body = ExerciseJournalTestSteps.Report(plan);
        foreach (var invalid in new[]
        {
            body with { Status = null }, body with { Status = (ExerciseCompletionStatus)999 },
            body with { PerformedOn = default }, body with { PerformedOn = body.PerformedOn.AddDays(1) },
            body with { PerformedOn = body.PerformedOn.AddDays(-1) }, body with { PainLevel = -1 }, body with { PainLevel = 11 },
            body with { ActualSets = 0 }, body with { ActualSets = 101 }, body with { ActualRepetitions = 1001 },
            body with { ActualDurationSeconds = 86401, ActualRepetitions = null }, body with { ActualDurationSeconds = 10 },
            body with { Status = ExerciseCompletionStatus.Skipped }, body with { RequestId = Guid.Empty },
            body with { PlanVersionNumber = 0 }, body with { PlanItemId = Guid.Empty }, body with { TherapyPlanId = Guid.Empty },
            body with { Comment = new string('x', 2001) }
        })
        {
            var response = await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, invalid);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.ErrorCodeAsync()).Should().Be("validation_failed");
        }
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, body with
        { ActualSets = 100, ActualRepetitions = null, ActualDurationSeconds = 86400, PainLevel = 10, Comment = new string('x', 2000) }))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id), ExerciseJournalTestSteps.Edit(entry) with { PainLevel = 11 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id), ExerciseJournalTestSteps.Edit(entry) with { Version = Guid.Empty }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.DeleteReport(entry.Id, Guid.Empty)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.Journal()).TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Stale_edit_and_delete_tokens_cannot_overwrite_newer_reports()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(plan)))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        var changed = await (await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id),
            ExerciseJournalTestSteps.Edit(entry) with { Comment = "New version" })).Body<ExerciseJournalEntryDto>();
        changed.Version.Should().NotBe(entry.Version);
        changed.CreatedAt.Should().Be(entry.CreatedAt);
        changed.UpdatedAt.Should().NotBeNull();
        foreach (var stale in new[]
        {
            await s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id), ExerciseJournalTestSteps.Edit(entry)),
            await s.Patient.DeleteReport(entry.Id, entry.Version)
        })
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await stale.ErrorCodeAsync()).Should().Be("concurrent_modification");
        }
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(changed);
    }
}
