using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public sealed class JournalWriteBarrier : SaveChangesInterceptor
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
            && e.Entity is ExerciseJournalEntry or TherapyPlan or Patient))
        {
            if (Interlocked.Increment(ref _arrived) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public sealed class PostgresExerciseJournalFactory : PostgresCalendarFactory
{
    public JournalWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(Writes));
    }
    public async Task<HttpResponseMessage[]> Race(Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        Writes.Enable();
        try { return await Task.WhenAll(first(), second()); }
        finally { Writes.Disable(); }
    }
}

public class PostgresExerciseJournalTests
{
    [PostgresFact]
    public async Task Complete_journal_contract_runs_on_postgres()
    {
        using var factory = new PostgresExerciseJournalFactory();
        var tests = new ExerciseJournalTests(factory);
        await tests.Reports_keep_immutable_version_and_position_snapshots_after_library_and_plan_changes();
        await tests.Only_confirmed_owner_can_report_currently_shared_positions_or_access_own_entries();
        await tests.Revocation_stops_new_reports_but_history_edit_delete_and_retries_remain_owned_by_author();
        await tests.Ending_cooperation_relinking_and_parent_deletion_preserve_author_history_without_transferring_it();
        await tests.Idempotent_retry_never_duplicates_or_overwrites_an_edited_report_and_new_keys_allow_multiple_daily_reports();
        await tests.Stable_pagination_and_inclusive_filters_distinguish_partial_skipped_and_missing_reports();
        await tests.Validation_requires_status_valid_dates_dose_pain_comment_and_edit_tokens();
        await tests.Stale_edit_and_delete_tokens_cannot_overwrite_newer_reports();
    }

    [PostgresFact]
    public async Task Concurrent_retries_have_one_entry_and_key_reuse_with_different_content_is_rejected()
    {
        using var factory = new PostgresExerciseJournalFactory();
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var request = ExerciseJournalTestSteps.Report(plan);
        Task<HttpResponseMessage> Submit(CreateExerciseJournalRequest body) => s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, body);
        var same = await factory.Race(() => Submit(request), () => Submit(request));
        same.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.OK]);
        var reports = await Task.WhenAll(same.Select(r => r.Body<ExerciseJournalEntryDto>(r.StatusCode)));
        reports[0].Id.Should().Be(reports[1].Id);
        (await s.Patient.Journal()).TotalCount.Should().Be(1);

        request = request with { RequestId = Guid.NewGuid() };
        var changed = await factory.Race(() => Submit(request), () => Submit(request with { PainLevel = 8 }));
        changed.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await changed.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("idempotency_key_reused");
        (await s.Patient.Journal()).TotalCount.Should().Be(2);

        // Independent executions are allowed; a concurrent publication fence conflict is safe to retry.
        request = request with { RequestId = Guid.NewGuid() };
        var other = request with { RequestId = Guid.NewGuid() };
        var distinct = await factory.Race(() => Submit(request), () => Submit(other));
        distinct.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await distinct.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
        (await Submit(request)).IsSuccessStatusCode.Should().BeTrue();
        (await Submit(other)).IsSuccessStatusCode.Should().BeTrue();
        (await s.Patient.Journal()).TotalCount.Should().Be(4);
    }

    [PostgresFact]
    public async Task Concurrent_updates_and_delete_cannot_lose_a_newer_version()
    {
        using var factory = new PostgresExerciseJournalFactory();
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(plan)))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        Task<HttpResponseMessage> Edit(int pain) => s.Patient.PutAsJsonAsync(ExerciseJournalTestSteps.Entry(entry.Id),
            ExerciseJournalTestSteps.Edit(entry) with { PainLevel = pain });
        var edits = await factory.Race(() => Edit(3), () => Edit(7));
        edits.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await edits.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
        entry = await edits.Single(r => r.IsSuccessStatusCode).Body<ExerciseJournalEntryDto>();
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
        var delete = await factory.Race(() => Edit(5), () => s.Patient.DeleteReport(entry.Id, entry.Version));
        delete.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        delete.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        if (delete[1].IsSuccessStatusCode)
            (await s.Patient.GetAsync(ExerciseJournalTestSteps.Entry(entry.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        else
            (await s.Patient.ReadReport(entry.Id)).PainLevel.Should().Be(5);
    }

    [PostgresFact]
    public async Task Revocation_or_end_of_cooperation_racing_a_report_has_one_atomic_winner()
    {
        using var factory = new PostgresExerciseJournalFactory();
        foreach (var endCooperation in new[] { false, true })
        {
            var s = await factory.SharingScenario();
            var plan = await s.Publish(await s.ActivePlan());
            var body = ExerciseJournalTestSteps.Report(plan);
            var responses = await factory.Race(
                () => s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, body),
                () => endCooperation
                    ? s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)
                    : s.Therapist.PostAsJsonAsync(SharingTestSteps.RevokePlan(plan.Id), new { plan.Version }));
            responses.Count(r => r.IsSuccessStatusCode).Should().Be(1);
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
            (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
            var entries = await s.Patient.Journal();
            entries.TotalCount.Should().Be(responses[0].IsSuccessStatusCode ? 1 : 0);
            if (responses[1].IsSuccessStatusCode)
                (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, body with { RequestId = Guid.NewGuid() }))
                    .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [PostgresFact]
    public async Task Migration_is_additive_idempotent_and_enforces_numeric_constraints_in_the_database()
    {
        using var factory = new PostgresExerciseJournalFactory();
        var s = await factory.SharingScenario();
        var plan = await s.Publish(await s.ActivePlan());
        plan = await s.Therapist.ReadPlan(plan.Id);
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261115174003_AddPatientContentSharing");
            db.PatientDiaryEntries.Add(new PatientDiaryEntry
            { PatientId = s.CardId, EntryDate = ExerciseJournalTestSteps.WarsawToday, PainLevel = 3, Wellbeing = 5, Notes = "LEGACY_DIARY" });
            await db.SaveChangesAsync();
            await migrator.MigrateAsync();
            var latest = (await db.Database.GetAppliedMigrationsAsync()).Last();
            await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript("20261115174003_AddPatientContentSharing",
                latest, MigrationsSqlGenerationOptions.Idempotent));
            db.Database.HasPendingModelChanges().Should().BeFalse();
            (await db.PatientDiaryEntries.SingleAsync(e => e.PatientId == s.CardId)).Notes.Should().Be("LEGACY_DIARY");
            (await db.ExerciseJournalEntries.CountAsync()).Should().Be(0); // No fabrication from legacy daily diary.
            return 0;
        });
        (await s.Therapist.ReadPlan(plan.Id)).Should().BeEquivalentTo(plan);
        var entry = await (await s.Patient.PostAsJsonAsync(ExerciseJournalTestSteps.Url, ExerciseJournalTestSteps.Report(plan)))
            .Body<ExerciseJournalEntryDto>(HttpStatusCode.Created);
        await factory.WithDbAsync(async db =>
        {
            var updatePain = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_journal_entries SET pain_level = 11 WHERE id = {entry.Id}");
            (await updatePain.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            var updateDose = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_journal_entries SET actual_sets = 0 WHERE id = {entry.Id}");
            (await updateDose.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            var duplicate = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_journal_entries SET status = 'Unknown' WHERE id = {entry.Id}");
            (await duplicate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            return 0;
        });
        (await s.Patient.ReadReport(entry.Id)).Should().BeEquivalentTo(entry);
    }
}
