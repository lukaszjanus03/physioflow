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

public class TherapyWriteBarrier : SaveChangesInterceptor
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
            && e.Entity is TherapyPlan or Exercise))
        {
            if (Interlocked.Increment(ref _arrived) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public class PostgresTherapyFactory : PostgresCalendarFactory
{
    public TherapyWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<PhysioFlow.Infrastructure.Persistence.ApplicationDbContext>(o => o.AddInterceptors(Writes));
    }
}

public class PostgresTherapyTests
{
    [PostgresFact]
    public async Task Complete_therapy_contract_should_work_on_postgres_including_reorders_and_position_removal()
    {
        using var factory = new PostgresTherapyFactory();
        var tests = new TherapyTests(factory);
        await tests.Library_supports_create_edit_archive_restore_filters_stable_pagination_and_conflicts();
        await tests.Draft_saves_activation_correction_completion_and_archive_preserve_immutable_snapshots();
        await tests.Reorder_preserves_position_parameters_and_library_changes_or_archive_do_not_change_snapshots();
        await tests.Foreign_resources_are_404_and_patient_and_moderator_roles_cannot_access_therapy();
        await tests.Owner_status_history_and_snapshot_fields_are_controlled_by_server();
        await tests.Patient_archive_keeps_readable_history_and_blocks_all_plan_writes_until_restore();
        await tests.Invalid_positions_dates_and_transitions_are_rejected_without_partial_updates();
    }

    [PostgresFact]
    public async Task Concurrent_saves_activation_and_corrections_commit_only_one_winner_atomically()
    {
        using var factory = new PostgresTherapyFactory();
        var client = await factory.CreateTherapistClientAsync();
        var card = await client.CreateCardAsync();
        var exercise = await client.NewExercise();
        var draft = await client.NewPlan(card, TherapyTestSteps.Complete(exercise.Id));
        var save = TherapyTestSteps.Save(draft);
        factory.Writes.Enable();
        var responses = await Task.WhenAll(
            client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), save with { Goal = "FIRST", Exercises = [save.Exercises[0] with { Sets = 4 }] }),
            client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), save with { Goal = "SECOND", Exercises = [save.Exercises[0] with { Sets = 5 }] }));
        factory.Writes.Disable();
        var winner = await OneWinner<TherapyPlanDto>(responses);
        var stored = await client.ReadPlan(draft.Id);
        stored.Content.Should().BeEquivalentTo(winner.Content);
        stored.Version.Should().Be(winner.Version);
        (await client.History(draft.Id)).Should().BeEmpty();

        factory.Writes.Enable();
        responses = await Task.WhenAll(
            client.PutAsJsonAsync(TherapyTestSteps.Plan(stored.Id), TherapyTestSteps.Save(stored) with { Goal = "RACING_SAVE" }),
            client.PostAsJsonAsync(TherapyTestSteps.Plan(stored.Id) + "/activate", new { stored.Version }));
        factory.Writes.Disable();
        await OneWinner<TherapyPlanDto>(responses);
        var latest = await client.ReadPlan(stored.Id);
        if (latest.Status == TherapyPlanStatus.Draft)
        {
            (await client.History(latest.Id)).Should().BeEmpty();
            latest = await client.Action(latest, "activate");
        }
        else
        {
            latest.Content.Should().BeEquivalentTo(stored.Content);
            (await client.History(latest.Id)).Should().ContainSingle().Which.Content.Should().BeEquivalentTo(stored.Content);
        }
        var original = latest;
        factory.Writes.Enable();
        responses = await Task.WhenAll(
            client.PostAsJsonAsync(TherapyTestSteps.Plan(latest.Id) + "/corrections", TherapyTestSteps.Correction(latest) with { Goal = "FIRST_CORRECTION" }),
            client.PostAsJsonAsync(TherapyTestSteps.Plan(latest.Id) + "/corrections", TherapyTestSteps.Correction(latest) with { Goal = "SECOND_CORRECTION" }));
        factory.Writes.Disable();
        winner = await OneWinner<TherapyPlanDto>(responses);
        latest = await client.ReadPlan(latest.Id);
        latest.Content.Should().BeEquivalentTo(winner.Content);
        latest.CurrentVersion.Should().Be(2);
        var history = await client.History(latest.Id);
        history.Should().HaveCount(2);
        history[0].Content.Should().BeEquivalentTo(original.Content);
        history[1].Content.Should().BeEquivalentTo(winner.Content);
    }

    [PostgresFact]
    public async Task Concurrent_library_edits_return_conflict_without_silent_overwrite()
    {
        using var factory = new PostgresTherapyFactory();
        var client = await factory.CreateTherapistClientAsync();
        var exercise = await client.NewExercise();
        var body = new SaveExerciseRequest { Version = exercise.Version, Name = exercise.Name, Instructions = "First", Category = "Back" };
        factory.Writes.Enable();
        var responses = await Task.WhenAll(client.PutAsJsonAsync(TherapyTestSteps.Exercise(exercise.Id), body),
            client.PutAsJsonAsync(TherapyTestSteps.Exercise(exercise.Id), body with { Instructions = "Second" }));
        factory.Writes.Disable();
        var winner = await OneWinner<ExerciseDto>(responses);
        (await (await client.GetAsync(TherapyTestSteps.Exercise(exercise.Id))).Body<ExerciseDto>()).Should().BeEquivalentTo(winner);
    }

    [PostgresFact]
    public async Task Activated_history_rejects_EF_and_SQL_mutations_and_hard_delete()
    {
        using var factory = new PostgresTherapyFactory();
        var client = await factory.CreateTherapistClientAsync();
        var card = await client.CreateCardAsync();
        var exercise = await client.NewExercise();
        var plan = await client.Action(await client.NewPlan(card, TherapyTestSteps.Complete(exercise.Id)), "activate");
        var efMutation = () => factory.WithDbAsync(async db =>
        {
            (await db.TherapyPlanVersions.SingleAsync(v => v.TherapyPlanId == plan.Id)).ContentJson = "{}";
            return await db.SaveChangesAsync();
        });
        await efMutation.Should().ThrowAsync<InvalidOperationException>();
        var sqlMutation = () => factory.WithDbAsync(db => db.TherapyPlanVersions.Where(v => v.TherapyPlanId == plan.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ContentJson, "{}")));
        await sqlMutation.Should().ThrowAsync<Npgsql.PostgresException>();
        var deleteVersion = () => factory.WithDbAsync(db => db.TherapyPlanVersions.Where(v => v.TherapyPlanId == plan.Id).ExecuteDeleteAsync());
        await deleteVersion.Should().ThrowAsync<Npgsql.PostgresException>();
        var deletePlan = () => factory.WithDbAsync(db => db.TherapyPlans.Where(p => p.Id == plan.Id).ExecuteDeleteAsync());
        await deletePlan.Should().ThrowAsync<Npgsql.PostgresException>();
        (await client.History(plan.Id)).Should().ContainSingle().Which.Content.Should().BeEquivalentTo(plan.Content);
    }

    [PostgresFact]
    public async Task Migration_preserves_legacy_therapy_media_parameters_and_existing_medical_data()
    {
        using var factory = new PostgresTherapyFactory();
        var scenario = await factory.DocumentationScenarioAsync();
        var medical = await scenario.ApproveRecordAsync(await scenario.CreateRecordAsync());
        var exerciseId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var description = new string('d', 11000); // Migration must preserve even old values beyond new API limits.
        var notes = new string('n', 2100);
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261114124842_AddVisitDocumentationVersions");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO exercises (id, therapist_id, name, description, muscle_group, youtube_url, thumbnail_storage_key, created_at, is_deleted)
                VALUES ({exerciseId}, {scenario.TherapistId}, 'Legacy exercise', {description}, 3, 'https://example.test/video', 'old-key', {DateTime.UtcNow}, false);
                INSERT INTO exercise_images (id, exercise_id, storage_key, "order", created_at)
                VALUES ({Guid.NewGuid()}, {exerciseId}, 'old-image', 0, {DateTime.UtcNow});
                INSERT INTO therapy_plans (id, patient_id, therapist_id, name, description, start_date, end_date, is_published, created_at, is_deleted)
                VALUES ({planId}, {scenario.PatientId}, {scenario.TherapistId}, 'Legacy plan', 'Legacy description', '2026-11-01', '2026-12-01', true, {DateTime.UtcNow}, false);
                INSERT INTO therapy_plan_exercises (id, therapy_plan_id, exercise_id, sets, repetitions, duration_seconds, rest_seconds, "order", notes, created_at)
                VALUES ({itemId}, {planId}, {exerciseId}, 3, 10, 20, 30, 7, {notes}, {DateTime.UtcNow});
                """);
            await migrator.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync(migrator.GenerateScript("20261114124842_AddVisitDocumentationVersions",
                "20261114150019_AddTherapyLibraryAndPlanVersions", MigrationsSqlGenerationOptions.Idempotent));
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return 0;
        });
        await factory.WithDbAsync(async db =>
        {
            var exercise = await db.Exercises.Include(e => e.Images).SingleAsync(e => e.Id == exerciseId);
            exercise.Description.Should().Be(description);
            exercise.Instructions.Should().Be(description);
            exercise.Category.Should().Be("Back");
            exercise.ThumbnailStorageKey.Should().Be("old-key");
            exercise.Images.Should().ContainSingle().Which.StorageKey.Should().Be("old-image");
            exercise.ConcurrencyStamp.Should().NotBeEmpty();
            var legacy = await db.TherapyPlans.Include(p => p.Exercises).SingleAsync(p => p.Id == planId);
            legacy.IsPublished.Should().BeTrue();
            legacy.Description.Should().Be("Legacy description");
            legacy.Status.Should().Be(TherapyPlanStatus.Draft);
            legacy.ConcurrencyStamp.Should().NotBeEmpty();
            var item = legacy.Exercises.Should().ContainSingle().Subject;
            item.Id.Should().Be(itemId);
            item.Order.Should().Be(0);
            item.ExerciseName.Should().Be("Legacy exercise");
            item.Instructions.Should().Be(description);
            item.Notes.Should().Be(notes);
            item.Sets.Should().Be(3);
            item.Repetitions.Should().Be(10);
            item.DurationSeconds.Should().Be(20);
            item.RestSeconds.Should().Be(30);
            (await db.Appointments.SingleAsync(a => a.Id == scenario.AppointmentId)).PatientId.Should().Be(scenario.PatientId);
            return 0;
        });
        (await scenario.Therapist.HistoryAsync(medical.Id)).Should().ContainSingle().Which.Content.Should().BeEquivalentTo(medical.Content);
    }

    private static async Task<T> OneWinner<T>(HttpResponseMessage[] responses)
    {
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
        return await responses.Single(r => r.IsSuccessStatusCode).Body<T>();
    }
}
