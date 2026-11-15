using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public record SharingScenario(DocumentationScenario Documentation, HttpClient Patient, Guid CooperationId)
{
    public HttpClient Therapist => Documentation.Therapist;
    public Guid CardId => Documentation.PatientId;
    public Guid TherapistId => Documentation.TherapistId;
}

public static class SharingTestSteps
{
    public const string Plans = "/api/patient-portal/therapy-plans";
    public const string Summaries = "/api/patient-portal/visit-summaries";
    public static string Plan(Guid id) => Plans + "/" + id;
    public static string Summary(Guid id) => Summaries + "/" + id;
    public static string Playback(Guid planId, Guid itemId) => Plan(planId) + $"/exercises/{itemId}/video/playback";
    public static string PublishPlan(Guid id) => TherapyTestSteps.Plan(id) + "/publish";
    public static string RevokePlan(Guid id) => TherapyTestSteps.Plan(id) + "/revoke";
    public static string PublishSummary(Guid id) => DocumentationTestSteps.RecordUrl(id) + "/patient-summary/publish";
    public static string RevokeSummary(Guid id) => DocumentationTestSteps.RecordUrl(id) + "/patient-summary/revoke";

    public static async Task<SharingScenario> SharingScenario(this TestWebApplicationFactory factory)
    {
        var documentation = await factory.DocumentationScenarioAsync();
        (await documentation.Therapist.PutAsJsonAsync("/api/therapists/me/profile", new
        {
            firstName = "Anna", lastName = "Terapeutka", city = "Krakow", practiceName = "Gabinet", isPublished = true
        })).EnsureSuccessStatusCode();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        var cooperation = await patient.SendRequestOkAsync(documentation.TherapistId);
        (await documentation.Therapist.AcceptWithExistingCardAsync(cooperation.Id, documentation.PatientId)).EnsureSuccessStatusCode();
        return new(documentation, patient, cooperation.Id);
    }

    public static async Task<TherapyPlanDto> ActivePlan(this SharingScenario s, ExerciseDto? exercise = null)
    {
        exercise ??= await s.Therapist.NewExercise();
        return await s.Therapist.Action(await s.Therapist.NewPlan(s.CardId, TherapyTestSteps.Complete(exercise.Id)), "activate");
    }

    public static async Task<TherapyPlanDto> Publish(this SharingScenario s, TherapyPlanDto plan, int? number = null) =>
        await (await s.Therapist.PostAsJsonAsync(PublishPlan(plan.Id), new { plan.Version, versionNumber = number ?? plan.CurrentVersion }))
            .Body<TherapyPlanDto>();

    public static async Task<MedicalRecordDto> Publish(this SharingScenario s, MedicalRecordDto record, int? number = null) =>
        await (await s.Therapist.PostAsJsonAsync(PublishSummary(record.Id), new { record.Version, versionNumber = number ?? record.CurrentVersion }))
            .Body<MedicalRecordDto>();

    public static async Task<MedicalRecordDto> ApprovedRecord(this SharingScenario s) =>
        await s.Documentation.ApproveRecordAsync(await s.Documentation.CreateRecordAsync());

    public static async Task<PatientTherapyPlanDto> Read(this SharingScenario s, TherapyPlanDto plan) =>
        await (await s.Patient.GetAsync(Plan(plan.Id))).Body<PatientTherapyPlanDto>();

    public static async Task<PatientVisitSummaryDto> Read(this SharingScenario s, MedicalRecordDto record) =>
        await (await s.Patient.GetAsync(Summary(record.Id))).Body<PatientVisitSummaryDto>();
}

public class PatientSharingTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Revocation_during_signing_does_not_return_the_in_flight_link()
    {
        var s = await factory.SharingScenario();
        var exercise = await (await s.Therapist.UploadVideo(await s.Therapist.NewExercise())).Body<ExerciseDto>();
        var plan = await s.Publish(await s.ActivePlan(exercise));
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Videos.BeforePlayback = async () =>
        {
            arrived.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
        };
        try
        {
            var pending = s.Patient.GetAsync(SharingTestSteps.Playback(plan.Id, plan.Content.Exercises[0].Id));
            await arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { plan.Version })).EnsureSuccessStatusCode();
            release.TrySetResult();
            var response = await pending;
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().NotContain("signature=");
        }
        finally
        {
            release.TrySetResult();
            factory.Videos.BeforePlayback = null;
        }
    }

    [Fact]
    public async Task Plans_require_explicit_publication_of_a_snapshot_and_corrections_do_not_replace_it()
    {
        var s = await factory.SharingScenario();
        var exercise = await s.Therapist.NewExercise();
        var draft = await s.Therapist.NewPlan(s.CardId, TherapyTestSteps.Complete(exercise.Id));
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(draft.Id), new { draft.Version, versionNumber = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Patient.GetAsync(SharingTestSteps.Plan(draft.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var active = await s.Therapist.Action(draft, "activate");
        // Neither legacy broad flag is a grant to a plan or full documentation.
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.CardId)).DocumentationSharedWithAccount = true;
            (await db.TherapyPlans.SingleAsync(p => p.Id == active.Id)).IsPublished = true;
            return await db.SaveChangesAsync();
        });
        (await (await s.Patient.GetAsync(SharingTestSteps.Plans)).Body<PaginatedList<PatientTherapyPlanListItemDto>>()).Items.Should().BeEmpty();
        (await s.Patient.GetAsync(SharingTestSteps.Plan(active.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var published = await s.Publish(active);
        published.Publication!.VersionNumber.Should().Be(1);
        published.Publication.IsAccessible.Should().BeTrue();
        published.Version.Should().NotBe(active.Version);
        var patientPlan = await s.Read(published);
        patientPlan.VersionNumber.Should().Be(1);
        patientPlan.Content.Exercises[0].Notes.Should().Be(active.Content.Exercises[0].Notes);
        var response = await s.Patient.GetAsync(SharingTestSteps.Plan(published.Id));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "PRIVATE_LIBRARY_NOTE", "exerciseId", "therapistNotes", "correctionReason", "authorUserId", "concurrencyStamp", "storageKey", "patientUserId" })
            json.Should().NotContain(forbidden);

        var corrected = await (await s.Therapist.PostAsJsonAsync(TherapyTestSteps.Plan(published.Id) + "/corrections",
            TherapyTestSteps.Correction(published) with { Name = "Corrected name" })).Body<TherapyPlanDto>();
        corrected.CurrentVersion.Should().Be(2);
        corrected.Publication!.VersionNumber.Should().Be(1);
        (await s.Read(corrected)).Should().BeEquivalentTo(patientPlan);
        var list = await (await s.Patient.GetAsync(SharingTestSteps.Plans)).Body<PaginatedList<PatientTherapyPlanListItemDto>>();
        list.Items.Should().ContainSingle().Which.Name.Should().Be(active.Content.Name);
        var ownerList = await (await s.Therapist.GetAsync(TherapyTestSteps.Plans(s.CardId))).Body<PaginatedList<TherapyPlanListItemDto>>();
        ownerList.Items.Single().Publication!.VersionNumber.Should().Be(1);
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(published.Id), new { published.Version }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(corrected.Id), new { corrected.Version, versionNumber = 999 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var republished = await s.Publish(corrected);
        (await s.Read(republished)).Content.Name.Should().Be("Corrected name");
        var older = await s.Publish(republished, 1);
        (await s.Read(older)).Content.Name.Should().Be(active.Content.Name);
        var revoked = await (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(older.Id), new { older.Version })).Body<TherapyPlanDto>();
        revoked.Publication.Should().BeNull();
        (await s.Patient.GetAsync(SharingTestSteps.Plan(older.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await s.Patient.GetAsync(SharingTestSteps.Plans)).Body<PaginatedList<PatientTherapyPlanListItemDto>>()).Items.Should().BeEmpty();
        (await s.Therapist.History(older.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Visit_summaries_publish_only_nonempty_approved_text_and_keep_previous_content_during_corrections()
    {
        var s = await factory.SharingScenario();
        var draft = await s.Documentation.CreateRecordAsync();
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishSummary(draft.Id), new { draft.Version, versionNumber = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var approved = await s.Documentation.ApproveRecordAsync(draft);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(approved.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var published = await s.Publish(approved);
        var original = await s.Read(published);
        original.Summary.Should().Be("PATIENT_SUMMARY");
        var response = await s.Patient.GetAsync(SharingTestSteps.Summary(approved.Id));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "PRIVATE_", "LEGACY_INTERNAL_NOTE", "BOOKING_MESSAGE", "interview", "functionalAssessment", "performedActions", "recommendations", "correctionReason", "authorUserId", "attachments", "content" })
            json.Should().NotContain(forbidden);
        var corrected = await (await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(published.Id) + "/corrections",
            DocumentationTestSteps.Correction(published) with { PatientSummary = "  " })).Body<MedicalRecordDto>();
        corrected.Publication!.VersionNumber.Should().Be(1);
        (await s.Read(corrected)).Should().BeEquivalentTo(original);
        var empty = await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishSummary(corrected.Id), new { corrected.Version, versionNumber = 2 });
        empty.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await empty.ErrorCodeAsync()).Should().Be("patient_summary_empty");
        (await s.Read(corrected)).Should().BeEquivalentTo(original);
        corrected = await (await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(published.Id) + "/corrections",
            DocumentationTestSteps.Correction(corrected) with { PatientSummary = "NEW_SUMMARY" })).Body<MedicalRecordDto>();
        (await s.Read(corrected)).Summary.Should().Be("PATIENT_SUMMARY");
        var republished = await s.Publish(corrected);
        (await s.Read(republished)).Summary.Should().Be("NEW_SUMMARY");
        var list = await (await s.Patient.GetAsync(SharingTestSteps.Summaries)).Body<PaginatedList<PatientVisitSummaryListItemDto>>();
        list.Items.Should().ContainSingle().Which.VersionNumber.Should().Be(3);
        var ownerList = await (await s.Therapist.GetAsync(s.Documentation.ListUrl)).Body<PaginatedList<MedicalRecordListItemDto>>();
        ownerList.Items.Single().Publication!.VersionNumber.Should().Be(3);
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokeSummary(published.Id), new { published.Version }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var revoked = await (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokeSummary(republished.Id), new { republished.Version })).Body<MedicalRecordDto>();
        revoked.Publication.Should().BeNull();
        (await s.Patient.GetAsync(SharingTestSteps.Summary(published.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await s.Patient.GetAsync(SharingTestSteps.Summaries)).Body<PaginatedList<PatientVisitSummaryListItemDto>>()).Items.Should().BeEmpty();
        (await s.Therapist.HistoryAsync(published.Id)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Roles_and_resource_ids_never_allow_foreign_access_or_patient_writes()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var record = await s.Publish(await s.ApprovedRecord());
        var (stranger, _) = await factory.CreateConfirmedPatientClientAsync();
        var otherTherapist = await factory.CreateTherapistClientAsync();
        var moderator = await factory.CreateModeratorAsync();
        foreach (var url in new[] { SharingTestSteps.Plan(plan.Id), SharingTestSteps.Summary(record.Id),
            SharingTestSteps.Playback(plan.Id, plan.Content.Exercises[0].Id) })
        {
            (await stranger.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await otherTherapist.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            // Review moderators are patient accounts; the extra role grants no clinical access.
            (await moderator.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await factory.CreateClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        (await (await stranger.GetAsync(SharingTestSteps.Plans)).Body<PaginatedList<PatientTherapyPlanListItemDto>>()).Items.Should().BeEmpty();
        (await (await stranger.GetAsync(SharingTestSteps.Summaries)).Body<PaginatedList<PatientVisitSummaryListItemDto>>()).Items.Should().BeEmpty();
        foreach (var url in new[] { TherapyTestSteps.Plan(plan.Id), DocumentationTestSteps.RecordUrl(record.Id),
            TherapyTestSteps.Plan(plan.Id) + "/versions", DocumentationTestSteps.RecordUrl(record.Id) + "/versions", "/api/exercises" })
            (await s.Patient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var url in new[] { SharingTestSteps.PublishPlan(plan.Id), SharingTestSteps.RevokePlan(plan.Id),
            SharingTestSteps.PublishSummary(record.Id), SharingTestSteps.RevokeSummary(record.Id) })
        {
            (await s.Patient.PostAsJsonAsync(url, new { version = plan.Version, versionNumber = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await otherTherapist.PostAsJsonAsync(url, new { version = plan.Version, versionNumber = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        (await s.Patient.PutAsJsonAsync(TherapyTestSteps.Plan(plan.Id), TherapyTestSteps.Save(plan))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Patient.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(record.Id) + "/corrections", DocumentationTestSteps.Correction(record)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var url in new[] { SharingTestSteps.Plans + "?pageSize=101", SharingTestSteps.Summaries + "?pageNumber=0" })
            (await s.Patient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.GetAsync(SharingTestSteps.Plan(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Playback_uses_published_material_only_and_revocation_stops_new_links_without_deleting_old_objects()
    {
        var s = await factory.SharingScenario();
        var exercise = await (await s.Therapist.UploadVideo(await s.Therapist.NewExercise())).Body<ExerciseDto>();
        var plan = await s.Publish(await s.ActivePlan(exercise));
        var originalItem = plan.Content.Exercises[0];
        var playbackUrl = SharingTestSteps.Playback(plan.Id, originalItem.Id);
        var originalKey = await factory.WithDbAsync(db => db.ExerciseVideos.Where(v => v.Id == exercise.Video!.Id).Select(v => v.StorageKey).SingleAsync());
        var playback = await (await s.Patient.GetAsync(playbackUrl)).Body<ExerciseVideoPlaybackDto>();
        playback.Url.Should().Contain(originalKey!);
        var response = await s.Patient.GetAsync(playbackUrl);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var planJson = await (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).Content.ReadAsStringAsync();
        planJson.Should().NotContain(exercise.Video!.Id.ToString()).And.NotContain("storageKey");
        exercise = await (await s.Therapist.UploadVideo(exercise)).Body<ExerciseDto>();
        var replacement = await s.Therapist.NewExercise("Unshared exercise");
        replacement = await (await s.Therapist.UploadVideo(replacement)).Body<ExerciseDto>();
        var corrected = await (await s.Therapist.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/corrections",
            TherapyTestSteps.Correction(plan) with { Exercises = TherapyTestSteps.Complete(exercise.Id).Exercises })).Body<TherapyPlanDto>();
        (await (await s.Patient.GetAsync(playbackUrl)).Body<ExerciseVideoPlaybackDto>()).Url.Should().Contain(originalKey!);
        (await s.Patient.GetAsync(SharingTestSteps.Playback(plan.Id, corrected.Content.Exercises[0].Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(ExerciseVideoTestSteps.Playback(replacement.Video!.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.Patient.GetAsync(SharingTestSteps.Playback(plan.Id, replacement.Video.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Videos.FailPlayback = true;
        try
        {
            var failure = await s.Patient.GetAsync(playbackUrl);
            failure.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await failure.Content.ReadAsStringAsync()).Should().NotContain("PROVIDER_SECRET").And.NotContain(originalKey!);
        }
        finally { factory.Videos.FailPlayback = false; }
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { corrected.Version })).EnsureSuccessStatusCode();
        var issued = factory.Videos.Signed.Count;
        (await s.Patient.GetAsync(playbackUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Videos.Signed.Count.Should().Be(issued);
        factory.Videos.Objects.Should().ContainKey(originalKey!);
        factory.Videos.Deleted.Should().BeEmpty();
        // Signing cannot invalidate an already issued URL. The API bounds that window to five minutes.
        factory.Videos.Signed.Should().OnlyContain(signed => signed.Expiry == TimeSpan.FromMinutes(5));

        var youtube = await s.Therapist.Youtube(await s.Therapist.NewExercise());
        var youtubePlan = await s.Publish(await s.ActivePlan(youtube));
        var patientYoutube = await s.Read(youtubePlan);
        patientYoutube.Content.Exercises[0].Video!.YoutubeUrl.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        (await s.Patient.GetAsync(SharingTestSteps.Playback(youtubePlan.Id, youtubePlan.Content.Exercises[0].Id)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Completion_and_archival_preserve_existing_grants_but_block_new_publication_and_allow_revocation()
    {
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var record = await s.Publish(await s.ApprovedRecord());
        plan = await s.Therapist.Action(plan, "complete");
        (await s.Read(plan)).VersionNumber.Should().Be(1);
        plan = await s.Publish(plan); // Completed plans may be explicitly shared.
        plan = await s.Therapist.Action(plan, "archive");
        (await s.Read(plan)).VersionNumber.Should().Be(1);
        var archivedPlan = await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), new { plan.Version, versionNumber = 1 });
        (await archivedPlan.ErrorCodeAsync()).Should().Be("plan_not_publishable");
        (await s.Therapist.DeleteAsync($"/api/patients/{s.CardId}")).EnsureSuccessStatusCode();
        (await s.Read(plan)).VersionNumber.Should().Be(1);
        (await s.Read(record)).Summary.Should().Be("PATIENT_SUMMARY");
        var archivedCard = await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishSummary(record.Id), new { record.Version, versionNumber = 1 });
        (await archivedCard.ErrorCodeAsync()).Should().Be("patient_archived");
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { plan.Version })).EnsureSuccessStatusCode();
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokeSummary(record.Id), new { record.Version })).EnsureSuccessStatusCode();
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ending_cooperation_and_relinking_even_the_same_account_never_resurrect_old_grants()
    {
        var s = await factory.SharingScenario();
        var exercise = await (await s.Therapist.UploadVideo(await s.Therapist.NewExercise())).Body<ExerciseDto>();
        var plan = await s.Publish(await s.ActivePlan(exercise));
        var record = await s.Publish(await s.ApprovedRecord());
        var oldEpoch = await factory.WithDbAsync(db => db.Patients.Where(p => p.Id == s.CardId).Select(p => p.AccountLinkVersion).SingleAsync());
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)).EnsureSuccessStatusCode();
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Playback(plan.Id, plan.Content.Exercises[0].Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.ReadPlan(plan.Id)).Publication!.IsAccessible.Should().BeFalse();
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), new { plan.Version, versionNumber = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        var otherRequest = await other.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(otherRequest.Id, s.CardId)).EnsureSuccessStatusCode();
        (await other.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{otherRequest.Id}/end", null)).EnsureSuccessStatusCode();
        var renewed = await s.Patient.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(renewed.Id, s.CardId)).EnsureSuccessStatusCode();
        var newEpoch = await factory.WithDbAsync(db => db.Patients.Where(p => p.Id == s.CardId).Select(p => p.AccountLinkVersion).SingleAsync());
        newEpoch.Should().NotBe(oldEpoch);
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.ReadRecordAsync(record.Id)).Publication!.IsAccessible.Should().BeFalse();
        plan = await s.Publish(await s.Therapist.ReadPlan(plan.Id));
        (await s.Read(plan)).VersionNumber.Should().Be(1);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Soft deletion is different from archival: it always hides content.
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.CardId)).IsDeleted = true;
            return await db.SaveChangesAsync();
        });
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publication_requires_linked_confirmed_account_active_cooperation_and_valid_edit_token()
    {
        var s = await factory.SharingScenario();
        var plan = await s.ActivePlan();
        foreach (var body in new[] { new { version = Guid.Empty, versionNumber = 1 }, new { version = plan.Version, versionNumber = 0 } })
            (await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), new { version = Guid.NewGuid(), versionNumber = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await factory.WithDbAsync(async db =>
        {
            (await db.CooperationRequests.SingleAsync(r => r.Id == s.CooperationId)).Status = PhysioFlow.Domain.Enums.CooperationRequestStatus.Ended;
            return await db.SaveChangesAsync();
        });
        var inactive = await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), new { plan.Version, versionNumber = 1 });
        (await inactive.ErrorCodeAsync()).Should().Be("cooperation_not_active");
        var unlinked = await s.Therapist.NewPlan(await s.Therapist.CreateCardAsync(), TherapyTestSteps.Complete((await s.Therapist.NewExercise()).Id));
        unlinked = await s.Therapist.Action(unlinked, "activate");
        var noAccount = await s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(unlinked.Id), new { unlinked.Version, versionNumber = 1 });
        (await noAccount.ErrorCodeAsync()).Should().Be("patient_account_required");
    }
}
