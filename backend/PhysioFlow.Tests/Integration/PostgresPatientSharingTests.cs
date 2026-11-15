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
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class SharingWriteBarrier : SaveChangesInterceptor
{
    private int _arrived;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Enabled { get; private set; }
    public void Enable()
    {
        _arrived = 0;
        _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Enabled = true;
    }
    public void Disable() => Enabled = false;
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && eventData.Context!.ChangeTracker.Entries().Any(e => e.State == EntityState.Modified
            && e.Entity is TherapyPlan or MedicalRecord or Patient or CooperationRequest))
        {
            if (Interlocked.Increment(ref _arrived) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public class PostgresSharingFactory : PostgresCalendarFactory
{
    public SharingWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<PhysioFlow.Infrastructure.Persistence.ApplicationDbContext>(o => o.AddInterceptors(Writes));
    }
}

public class PostgresPatientSharingTests
{
    [PostgresFact]
    public async Task Complete_patient_sharing_contract_runs_on_postgres()
    {
        using var factory = new PostgresSharingFactory();
        var tests = new PatientSharingTests(factory);
        await tests.Plans_require_explicit_publication_of_a_snapshot_and_corrections_do_not_replace_it();
        await tests.Visit_summaries_publish_only_nonempty_approved_text_and_keep_previous_content_during_corrections();
        await tests.Roles_and_resource_ids_never_allow_foreign_access_or_patient_writes();
        await tests.Playback_uses_published_material_only_and_revocation_stops_new_links_without_deleting_old_objects();
        await tests.Completion_and_archival_preserve_existing_grants_but_block_new_publication_and_allow_revocation();
        await tests.Ending_cooperation_and_relinking_even_the_same_account_never_resurrect_old_grants();
        await tests.Publication_requires_linked_confirmed_account_active_cooperation_and_valid_edit_token();
        await tests.Revocation_during_signing_does_not_return_the_in_flight_link();
    }

    [PostgresFact]
    public async Task Concurrent_publication_revocation_and_corrections_have_one_atomic_winner()
    {
        using var factory = new PostgresSharingFactory();
        var s = await factory.SharingScenario();
        var plan = await s.ActivePlan();
        async Task<HttpResponseMessage[]> Race(Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
        {
            factory.Writes.Enable();
            try
            {
                var responses = await Task.WhenAll(first(), second());
                responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
                (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
                return responses;
            }
            finally { factory.Writes.Disable(); }
        }
        Task<HttpResponseMessage> PublishPlan() => s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), new { plan.Version, versionNumber = 1 });
        Task<HttpResponseMessage> RevokePlan() => s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { plan.Version });
        await Race(PublishPlan, PublishPlan);
        plan = await s.Therapist.ReadPlan(plan.Id);
        plan.Publication!.VersionNumber.Should().Be(1);
        await Race(RevokePlan, RevokePlan);
        plan = await s.Therapist.ReadPlan(plan.Id);
        plan.Publication.Should().BeNull();
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        plan = await s.Publish(plan);
        await Race(PublishPlan, RevokePlan);
        plan = await s.Therapist.ReadPlan(plan.Id);
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should()
            .Be(plan.Publication is null ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        await Race(PublishPlan, () => s.Therapist.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/corrections", TherapyTestSteps.Correction(plan)));
        var saved = await s.Therapist.ReadPlan(plan.Id);
        (await s.Therapist.History(plan.Id)).Should().HaveCount(saved.CurrentVersion);
        saved.Publication?.VersionNumber.Should().Be(1);

        var record = await s.ApprovedRecord();
        Task<HttpResponseMessage> PublishSummary() => s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishSummary(record.Id), new { record.Version, versionNumber = 1 });
        Task<HttpResponseMessage> RevokeSummary() => s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokeSummary(record.Id), new { record.Version });
        await Race(PublishSummary, PublishSummary);
        record = await s.Therapist.ReadRecordAsync(record.Id);
        record.Publication!.VersionNumber.Should().Be(1);
        await Race(RevokeSummary, RevokeSummary);
        record = await s.Therapist.ReadRecordAsync(record.Id);
        record.Publication.Should().BeNull();
        record = await s.Publish(record);
        await Race(PublishSummary, RevokeSummary);
        record = await s.Therapist.ReadRecordAsync(record.Id);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should()
            .Be(record.Publication is null ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        await Race(PublishSummary, () => s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(record.Id) + "/corrections",
            DocumentationTestSteps.Correction(record) with { PatientSummary = "RACING_CORRECTION" }));
        var storedRecord = await s.Therapist.ReadRecordAsync(record.Id);
        (await s.Therapist.HistoryAsync(record.Id)).Should().HaveCount(storedRecord.CurrentVersion);
        if (storedRecord.Publication is not null)
            (await s.Read(storedRecord)).Summary.Should().Be("PATIENT_SUMMARY");
    }

    [PostgresFact]
    public async Task Publication_racing_with_end_of_cooperation_or_card_archive_never_commits_a_stale_grant()
    {
        using var factory = new PostgresSharingFactory();
        foreach (var endCooperation in new[] { true, false })
        {
            var s = await factory.SharingScenario();
            var plan = await s.ActivePlan();
            factory.Writes.Enable();
            HttpResponseMessage[] results;
            try
            {
                results = await Task.WhenAll(
                    s.Therapist.PostAsJsonAsync(SharingTestSteps.PublishPlan(plan.Id), new { plan.Version, versionNumber = 1 }),
                    endCooperation
                        ? s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)
                        : s.Therapist.DeleteAsync($"/api/patients/{s.CardId}"));
            }
            finally { factory.Writes.Disable(); }
            results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
            results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
            var stored = await s.Therapist.ReadPlan(plan.Id);
            var card = await factory.WithDbAsync(db => db.Patients.SingleAsync(p => p.Id == s.CardId));
            if (results[0].IsSuccessStatusCode)
            {
                stored.Publication!.IsAccessible.Should().BeTrue();
                card.UserId.Should().NotBeNull();
                card.IsArchived.Should().BeFalse();
            }
            else
            {
                stored.Publication.Should().BeNull();
                (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
                if (endCooperation) card.UserId.Should().BeNull();
                else card.IsArchived.Should().BeTrue();
            }
        }
    }

    [PostgresFact]
    public async Task Migration_preserves_existing_data_and_raw_SQL_relink_does_not_restore_publications()
    {
        using var factory = new PostgresSharingFactory();
        var s = await factory.SharingScenario();
        var exercise = await (await s.Therapist.UploadVideo(await s.Therapist.NewExercise())).Body<ExerciseDto>();
        var plan = await s.ActivePlan(exercise);
        var record = await s.ApprovedRecord();
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261115142243_AddExerciseVideoMaterials");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE therapy_plans SET is_published = true WHERE id = {plan.Id}");
            await migrator.MigrateAsync();
            // The migration is safe in the idempotent deployment script as well.
            await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript("20261115142243_AddExerciseVideoMaterials",
                "20261115174003_AddPatientContentSharing", MigrationsSqlGenerationOptions.Idempotent));
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return 0;
        });
        (await s.Therapist.ReadPlan(plan.Id)).Content.Should().BeEquivalentTo(plan.Content);
        (await s.Therapist.ReadRecordAsync(record.Id)).Content.Should().BeEquivalentTo(record.Content);
        (await s.Therapist.History(plan.Id)).Should().ContainSingle();
        (await s.Therapist.HistoryAsync(record.Id)).Should().ContainSingle();
        (await factory.WithDbAsync(db => db.TherapyPlans.SingleAsync(p => p.Id == plan.Id))).IsPublished.Should().BeTrue();
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        plan = await s.Publish(await s.Therapist.ReadPlan(plan.Id));
        record = await s.Publish(await s.Therapist.ReadRecordAsync(record.Id));
        var original = await factory.WithDbAsync(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == s.CardId));
        await factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE patients SET user_id = NULL WHERE id = {s.CardId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE patients SET user_id = {original.UserId} WHERE id = {s.CardId}");
            return 0;
        });
        var relinked = await factory.WithDbAsync(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == s.CardId));
        relinked.AccountLinkVersion.Should().NotBe(original.AccountLinkVersion);
        relinked.ConcurrencyStamp.Should().NotBe(original.ConcurrencyStamp);
        (await s.Patient.GetAsync(SharingTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Summary(record.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SharingTestSteps.Playback(plan.Id, plan.Content.Exercises[0].Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Unpublishing does not destroy immutable history or material objects.
        (await s.Therapist.History(plan.Id))[0].Content.Exercises[0].Video!.Id.Should().Be(exercise.Video!.Id);
    }
}
