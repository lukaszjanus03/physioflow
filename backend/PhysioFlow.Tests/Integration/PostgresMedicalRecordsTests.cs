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

public class DocumentationWriteBarrier : SaveChangesInterceptor
{
    private int _arrived;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public EntityState? Operation { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Operation is { } operation && eventData.Context!.ChangeTracker.Entries<MedicalRecord>().Any(e => e.State == operation))
        {
            if (Interlocked.Increment(ref _arrived) == 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public class PostgresDocumentationFactory : PostgresCalendarFactory
{
    public DocumentationWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(Writes));
    }
}

public class PostgresMedicalRecordsTests
{
    [PostgresFact]
    public async Task Concurrent_creates_should_commit_one_document_and_return_a_stable_duplicate_error()
    {
        using var factory = new PostgresDocumentationFactory();
        var s = await factory.DocumentationScenarioAsync();
        factory.Writes.Operation = EntityState.Added;
        var responses = await Task.WhenAll(
            s.Therapist.PostAsJsonAsync(s.ListUrl, DocumentationTestSteps.CompleteContent(s.AppointmentId)),
            s.Therapist.PostAsJsonAsync(s.ListUrl, DocumentationTestSteps.CompleteContent(s.AppointmentId)));
        factory.Writes.Operation = null;
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("medical_record_already_exists");
        (await factory.WithDbAsync(db => db.MedicalRecords.CountAsync(r => r.AppointmentId == s.AppointmentId))).Should().Be(1);
    }

    [PostgresFact]
    public async Task Concurrent_draft_saves_should_reject_the_loser_without_losing_the_winning_content()
    {
        using var factory = new PostgresDocumentationFactory();
        var s = await factory.DocumentationScenarioAsync();
        var draft = await s.CreateRecordAsync();
        var url = DocumentationTestSteps.RecordUrl(draft.Id);
        factory.Writes.Operation = EntityState.Modified;
        var responses = await Task.WhenAll(
            s.Therapist.PutAsJsonAsync(url, new SaveMedicalRecordRequest { Version = draft.Version, Interview = "FIRST_EDIT" }),
            s.Therapist.PutAsJsonAsync(url, new SaveMedicalRecordRequest { Version = draft.Version, Interview = "SECOND_EDIT" }));
        factory.Writes.Operation = null;
        await AssertOneWinner(responses);
        var winner = (await responses.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<MedicalRecordDto>(CooperationTestSteps.Json))!;
        var stored = await s.Therapist.ReadRecordAsync(draft.Id);
        // PostgreSQL timestamps have microsecond precision; .NET responses can include 100 ns ticks.
        stored.Should().BeEquivalentTo(winner, options => options.Excluding(r => r.UpdatedAt));
        stored.UpdatedAt!.Value.Should().BeCloseTo(winner.UpdatedAt!.Value, TimeSpan.FromTicks(10));
        (await s.Therapist.HistoryAsync(draft.Id)).Should().BeEmpty();
    }

    [PostgresFact]
    public async Task Concurrent_draft_save_and_approval_should_snapshot_only_the_approved_content()
    {
        using var factory = new PostgresDocumentationFactory();
        var s = await factory.DocumentationScenarioAsync();
        var draft = await s.CreateRecordAsync();
        var url = DocumentationTestSteps.RecordUrl(draft.Id);
        factory.Writes.Operation = EntityState.Modified;
        var responses = await Task.WhenAll(
            s.Therapist.PutAsJsonAsync(url, new SaveMedicalRecordRequest { Version = draft.Version, Interview = "NEW_DRAFT" }),
            s.Therapist.PostAsJsonAsync(url + "/approve", new { draft.Version }));
        factory.Writes.Operation = null;
        await AssertOneWinner(responses);
        var current = await s.Therapist.ReadRecordAsync(draft.Id);
        var history = await s.Therapist.HistoryAsync(draft.Id);
        if (current.Status == MedicalRecordStatus.Approved)
        {
            history.Should().ContainSingle().Which.Content.Should().BeEquivalentTo(draft.Content);
            current.Content.Should().BeEquivalentTo(draft.Content);
        }
        else
        {
            current.Content.Interview.Should().Be("NEW_DRAFT");
            history.Should().BeEmpty();
        }
    }

    [PostgresFact]
    public async Task Concurrent_corrections_should_commit_one_new_snapshot_and_keep_the_original()
    {
        using var factory = new PostgresDocumentationFactory();
        var s = await factory.DocumentationScenarioAsync();
        var approved = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        var url = DocumentationTestSteps.RecordUrl(approved.Id) + "/corrections";
        factory.Writes.Operation = EntityState.Modified;
        var responses = await Task.WhenAll(
            s.Therapist.PostAsJsonAsync(url, DocumentationTestSteps.Correction(approved) with { Interview = "FIRST_CORRECTION" }),
            s.Therapist.PostAsJsonAsync(url, DocumentationTestSteps.Correction(approved) with { Interview = "SECOND_CORRECTION" }));
        factory.Writes.Operation = null;
        await AssertOneWinner(responses);
        var latest = await s.Therapist.ReadRecordAsync(approved.Id);
        latest.CurrentVersion.Should().Be(2);
        var history = await s.Therapist.HistoryAsync(approved.Id);
        history.Should().HaveCount(2);
        history[0].Content.Should().BeEquivalentTo(approved.Content);
        history[1].Content.Should().BeEquivalentTo(latest.Content);
        (await s.Therapist.GetAsync(s.ListUrl)).EnsureSuccessStatusCode();
    }

    [PostgresFact]
    public async Task Migration_should_preserve_legacy_records_patients_visits_and_pain_maps_and_allow_editing_old_drafts()
    {
        using var factory = new PostgresDocumentationFactory();
        var s = await factory.DocumentationScenarioAsync();
        var legacyId = Guid.NewGuid();
        var mapId = await factory.WithDbAsync(async db =>
        {
            var map = new BodyMap { PatientId = s.PatientId, AppointmentId = s.AppointmentId,
                Notes = "Existing pain map", CreatedByRole = UserRole.Therapist,
                PainPoints = [new PainPoint { Side = BodySide.Front, X = .5m, Y = .5m, Note = "Existing pain point" }] };
            db.BodyMaps.Add(map);
            await db.SaveChangesAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261113205321_AddTherapistReviews");
            var legacyJson = "{\"legacy\":true}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO medical_records (id, patient_id, appointment_id, therapist_id, interview_form,
                    post_visit_note_html, post_visit_note_text, pain_scale, created_at, is_deleted)
                VALUES ({legacyId}, {s.PatientId}, {s.AppointmentId}, {s.TherapistId}, {legacyJson}::jsonb,
                    '<p>Legacy note</p>', 'Legacy note', 7, {DateTime.UtcNow}, false)
                """);
            await migrator.MigrateAsync();
            var idempotent = migrator.GenerateScript("20261113205321_AddTherapistReviews",
                "20261114124842_AddVisitDocumentationVersions", MigrationsSqlGenerationOptions.Idempotent);
            // Deployment scripts must also parse and be safe to rerun after application.
            await db.Database.ExecuteSqlRawAsync(idempotent);
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return map.Id;
        });
        var legacy = await factory.WithDbAsync(db => db.MedicalRecords.AsNoTracking().SingleAsync(r => r.Id == legacyId));
        legacy.Status.Should().Be(MedicalRecordStatus.Draft);
        legacy.CurrentVersion.Should().Be(0);
        legacy.ConcurrencyStamp.Should().NotBeEmpty();
        legacy.PostVisitNoteHtml.Should().Be("<p>Legacy note</p>");
        legacy.PostVisitNoteText.Should().Be("Legacy note");
        legacy.InterviewForm.Should().Contain("legacy");
        legacy.PainScale.Should().Be(7);
        legacy.PatientSummary.Should().BeNull();
        (await factory.WithDbAsync(db => db.BodyMaps.Include(m => m.PainPoints).SingleAsync(m => m.Id == mapId)))
            .PainPoints.Should().ContainSingle(p => p.Note == "Existing pain point");
        (await factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == s.AppointmentId))).Status.Should().Be(AppointmentStatus.Completed);
        (await s.Therapist.GetAsync($"/api/patients/{s.PatientId}")).EnsureSuccessStatusCode();
        (await s.Therapist.PutAsJsonAsync(DocumentationTestSteps.RecordUrl(legacyId), new SaveMedicalRecordRequest
            { Version = legacy.ConcurrencyStamp, Interview = "New explicit content" })).EnsureSuccessStatusCode();
        (await factory.WithDbAsync(db => db.MedicalRecords.SingleAsync(r => r.Id == legacyId))).PostVisitNoteText.Should().Be("Legacy note");
    }

    [PostgresFact]
    public async Task Saved_versions_should_reject_mutation_and_hard_deletion_and_prevent_removing_the_record()
    {
        using var factory = new PostgresDocumentationFactory();
        var s = await factory.DocumentationScenarioAsync();
        var approved = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        var mutate = () => factory.WithDbAsync(async db =>
        {
            (await db.MedicalRecordVersions.SingleAsync(v => v.MedicalRecordId == approved.Id)).Interview = "overwrite";
            return await db.SaveChangesAsync();
        });
        await mutate.Should().ThrowAsync<InvalidOperationException>();
        var removeVersion = () => factory.WithDbAsync(async db =>
        {
            db.MedicalRecordVersions.Remove(await db.MedicalRecordVersions.SingleAsync(v => v.MedicalRecordId == approved.Id));
            return await db.SaveChangesAsync();
        });
        await removeVersion.Should().ThrowAsync<InvalidOperationException>();
        var removeRecord = () => factory.WithDbAsync(db => db.MedicalRecords.Where(r => r.Id == approved.Id).ExecuteDeleteAsync());
        await removeRecord.Should().ThrowAsync<Npgsql.PostgresException>();
        (await s.Therapist.HistoryAsync(approved.Id)).Should().ContainSingle().Which.Content.Should().BeEquivalentTo(approved.Content);
    }

    private static async Task AssertOneWinner(HttpResponseMessage[] responses)
    {
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
    }
}
