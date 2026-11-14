using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class TherapyTestSteps
{
    public static string Plans(Guid patientId) => $"/api/patients/{patientId}/therapy-plans";
    public static string Plan(Guid id) => $"/api/therapy-plans/{id}";
    public static string Exercise(Guid id) => $"/api/exercises/{id}";

    public static async Task<T> Body<T>(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(CooperationTestSteps.Json))!;
    }

    public static async Task<ExerciseDto> NewExercise(this HttpClient client, string name = "Stretch") =>
        await (await client.PostAsJsonAsync("/api/exercises", new ExerciseContent
        {
            Name = name, Instructions = "<script>plain text, never execute</script>", Category = "Back", Notes = "PRIVATE_LIBRARY_NOTE"
        })).Body<ExerciseDto>(HttpStatusCode.Created);

    public static TherapyPlanContent Complete(Guid exerciseId) => new()
    {
        Name = "Recovery", Goal = "PRIVATE_GOAL", StartDate = new(2026, 11, 14), EndDate = new(2026, 12, 14),
        GeneralRecommendations = "PRIVATE_RECOMMENDATIONS",
        Exercises = [new() { ExerciseId = exerciseId, Sets = 3, Repetitions = 10, Frequency = "Twice daily", Notes = "PRIVATE_POSITION_NOTE" }]
    };

    public static SaveTherapyPlanRequest Save(TherapyPlanDto plan) => new()
    {
        Version = plan.Version, Name = plan.Content.Name, Goal = plan.Content.Goal, StartDate = plan.Content.StartDate,
        EndDate = plan.Content.EndDate, GeneralRecommendations = plan.Content.GeneralRecommendations,
        Exercises = plan.Content.Exercises.Select(i => new TherapyPlanItemRequest
        {
            Id = i.Id, ExerciseId = i.ExerciseId, Sets = i.Sets, Repetitions = i.Repetitions,
            DurationSeconds = i.DurationSeconds, Frequency = i.Frequency, Notes = i.Notes
        }).ToList()
    };

    public static CorrectTherapyPlanRequest Correction(TherapyPlanDto plan, string? reason = "Adjusted dose") => new()
    {
        Version = plan.Version, Name = plan.Content.Name, Goal = "Corrected goal", StartDate = plan.Content.StartDate,
        EndDate = plan.Content.EndDate, GeneralRecommendations = plan.Content.GeneralRecommendations,
        Exercises = Save(plan).Exercises, CorrectionReason = reason
    };

    public static async Task<TherapyPlanDto> NewPlan(this HttpClient client, Guid patientId, TherapyPlanContent? content = null) =>
        await (await client.PostAsJsonAsync(Plans(patientId), content ?? new())).Body<TherapyPlanDto>(HttpStatusCode.Created);

    public static async Task<TherapyPlanDto> Action(this HttpClient client, TherapyPlanDto plan, string action) =>
        await (await client.PostAsJsonAsync(Plan(plan.Id) + "/" + action, new { plan.Version })).Body<TherapyPlanDto>();

    public static async Task<TherapyPlanDto> ReadPlan(this HttpClient client, Guid id) =>
        await (await client.GetAsync(Plan(id))).Body<TherapyPlanDto>();

    public static async Task<List<TherapyPlanVersionDto>> History(this HttpClient client, Guid id) =>
        await (await client.GetAsync(Plan(id) + "/versions")).Body<List<TherapyPlanVersionDto>>();
}

public class TherapyTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Library_supports_create_edit_archive_restore_filters_stable_pagination_and_conflicts()
    {
        var client = await factory.CreateTherapistClientAsync();
        var first = await client.NewExercise();
        var second = await client.NewExercise();
        var edited = await (await client.PutAsJsonAsync(TherapyTestSteps.Exercise(first.Id), new SaveExerciseRequest
        {
            Version = first.Version, Name = "Stretch", Instructions = "NEW_INSTRUCTIONS", Category = "Neck"
        })).Body<ExerciseDto>();
        edited.Version.Should().NotBe(first.Version);
        var stale = await client.PostAsJsonAsync(TherapyTestSteps.Exercise(first.Id) + "/archive", new { first.Version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.ErrorCodeAsync()).Should().Be("concurrent_modification");
        var archived = await (await client.PostAsJsonAsync(TherapyTestSteps.Exercise(first.Id) + "/archive", new { edited.Version })).Body<ExerciseDto>();
        archived.IsArchived.Should().BeTrue();
        var filtered = await (await client.GetAsync("/api/exercises?archived=true&search=new_instructions&category=NECK"))
            .Body<PaginatedList<ExerciseDto>>();
        filtered.Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
        (await (await client.GetAsync("/api/exercises")).Body<PaginatedList<ExerciseDto>>()).Items.Should().ContainSingle().Which.Id.Should().Be(second.Id);
        var restored = await (await client.PostAsJsonAsync(TherapyTestSteps.Exercise(first.Id) + "/restore", new { archived.Version })).Body<ExerciseDto>();
        restored.IsArchived.Should().BeFalse();
        var page1 = await (await client.GetAsync("/api/exercises?pageSize=1")).Body<PaginatedList<ExerciseDto>>();
        var page2 = await (await client.GetAsync("/api/exercises?pageSize=1&pageNumber=2")).Body<PaginatedList<ExerciseDto>>();
        page1.Items.Single().Id.Should().NotBe(page2.Items.Single().Id);
        (await (await client.GetAsync("/api/exercises?pageSize=1")).Body<PaginatedList<ExerciseDto>>()).Items.Should().BeEquivalentTo(page1.Items);
        (await client.GetAsync("/api/exercises?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/exercises?pageSize=100&pageNumber=2147483647")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/exercises", new ExerciseContent())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Draft_saves_activation_correction_completion_and_archive_preserve_immutable_snapshots()
    {
        var client = await factory.CreateTherapistClientAsync();
        var patientId = await client.CreateCardAsync(); // No patient portal account is required.
        var exercise = await client.NewExercise();
        var draft = await client.NewPlan(patientId);
        draft.CurrentVersion.Should().Be(0);
        (await client.History(draft.Id)).Should().BeEmpty();
        (await client.PostAsJsonAsync(TherapyTestSteps.Plan(draft.Id) + "/activate", new { draft.Version })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var complete = TherapyTestSteps.Complete(exercise.Id);
        draft = await (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), new SaveTherapyPlanRequest
        {
            Version = draft.Version, Name = complete.Name, Goal = complete.Goal, StartDate = complete.StartDate,
            EndDate = complete.EndDate, GeneralRecommendations = complete.GeneralRecommendations, Exercises = complete.Exercises
        })).Body<TherapyPlanDto>();
        var saved = await (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), TherapyTestSteps.Save(draft))).Body<TherapyPlanDto>();
        saved.Version.Should().NotBe(draft.Version);
        (await client.History(saved.Id)).Should().BeEmpty();
        var active = await client.Action(saved, "activate");
        active.Status.Should().Be(TherapyPlanStatus.Active);
        active.CurrentVersion.Should().Be(1);
        (await client.PutAsJsonAsync(TherapyTestSteps.Plan(active.Id), TherapyTestSteps.Save(active))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", TherapyTestSteps.Correction(active, " "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var corrected = await (await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", TherapyTestSteps.Correction(active))).Body<TherapyPlanDto>();
        corrected.CurrentVersion.Should().Be(2);
        var history = await client.History(active.Id);
        history.Should().HaveCount(2);
        history[0].Content.Should().BeEquivalentTo(active.Content);
        history[0].CorrectionReason.Should().BeNull();
        history[0].AuthorUserId.Should().NotBeEmpty();
        history[0].ActivatedAt.Should().NotBe(default);
        history[1].Content.Should().BeEquivalentTo(corrected.Content);
        history[1].CorrectionReason.Should().Be("Adjusted dose");
        var completed = await client.Action(corrected, "complete");
        completed.Status.Should().Be(TherapyPlanStatus.Completed);
        (await client.PostAsJsonAsync(TherapyTestSteps.Plan(completed.Id) + "/corrections", TherapyTestSteps.Correction(completed))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var archived = await client.Action(completed, "archive");
        archived.Status.Should().Be(TherapyPlanStatus.Archived);
        (await client.History(archived.Id)).Should().BeEquivalentTo(history);
        (await client.DeleteAsync(TherapyTestSteps.Plan(archived.Id))).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var list = await (await client.GetAsync(TherapyTestSteps.Plans(patientId) + "?status=Archived")).Body<PaginatedList<TherapyPlanListItemDto>>();
        list.Items.Should().ContainSingle().Which.Id.Should().Be(archived.Id);
        (await factory.WithDbAsync(db => db.TherapyPlans.SingleAsync(p => p.Id == active.Id))).IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task Reorder_preserves_position_parameters_and_library_changes_or_archive_do_not_change_snapshots()
    {
        var client = await factory.CreateTherapistClientAsync();
        var patientId = await client.CreateCardAsync();
        var a = await client.NewExercise("A");
        var b = await client.NewExercise("B");
        var content = TherapyTestSteps.Complete(a.Id);
        content.Exercises.Add(new() { ExerciseId = b.Id, Sets = 5, DurationSeconds = 60, Frequency = "Daily" });
        var draft = await client.NewPlan(patientId, content);
        var aPosition = draft.Content.Exercises[0];
        var bPosition = draft.Content.Exercises[1];
        a = await (await client.PutAsJsonAsync(TherapyTestSteps.Exercise(a.Id), new SaveExerciseRequest
        {
            Version = a.Version, Name = "CHANGED_LIBRARY_NAME", Instructions = "CHANGED_LIBRARY_INSTRUCTIONS", Category = "Legs"
        })).Body<ExerciseDto>();
        await (await client.PostAsJsonAsync(TherapyTestSteps.Exercise(a.Id) + "/archive", new { a.Version })).Body<ExerciseDto>();
        var save = TherapyTestSteps.Save(draft);
        save.Exercises.Reverse();
        var reordered = await (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), save)).Body<TherapyPlanDto>();
        reordered.Content.Exercises[0].Should().BeEquivalentTo(bPosition with { Order = 0 });
        reordered.Content.Exercises[1].Should().BeEquivalentTo(aPosition with { Order = 1 });
        reordered.Content.Exercises[1].Instructions.Should().Be("<script>plain text, never execute</script>");
        var active = await client.Action(reordered, "activate");
        var appendArchived = TherapyTestSteps.Correction(active);
        appendArchived.Exercises.Add(new() { ExerciseId = a.Id, Sets = 1, Repetitions = 1, Frequency = "Daily" });
        var rejected = await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", appendArchived);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.ErrorCodeAsync()).Should().Be("exercise_archived");
        (await client.ReadPlan(active.Id)).Content.Should().BeEquivalentTo(active.Content);
        (await client.History(active.Id)).Should().ContainSingle();
        // Omitting a position deletes only the current projection, not its activated history.
        var correction = TherapyTestSteps.Correction(active);
        correction.Exercises.RemoveAt(1);
        var updated = await (await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", correction)).Body<TherapyPlanDto>();
        updated.Content.Exercises.Should().ContainSingle().Which.Id.Should().Be(bPosition.Id);
        (await client.History(active.Id))[0].Content.Should().BeEquivalentTo(active.Content);
    }

    [Fact]
    public async Task Foreign_resources_are_404_and_patient_and_moderator_roles_cannot_access_therapy()
    {
        var owner = await factory.CreateTherapistClientAsync();
        var other = await factory.CreateTherapistClientAsync();
        var patientId = await owner.CreateCardAsync();
        var exercise = await owner.NewExercise();
        var plan = await owner.NewPlan(patientId, TherapyTestSteps.Complete(exercise.Id));
        (await other.GetAsync(TherapyTestSteps.Exercise(exercise.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync(TherapyTestSteps.Exercise(exercise.Id), new SaveExerciseRequest { Version = exercise.Version })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var action in new[] { "archive", "restore" })
            (await other.PostAsJsonAsync(TherapyTestSteps.Exercise(exercise.Id) + "/" + action, new { exercise.Version })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var url in new[] { TherapyTestSteps.Plan(plan.Id), TherapyTestSteps.Plan(plan.Id) + "/versions", TherapyTestSteps.Plans(patientId) })
            (await other.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.NewExercise()).Should().NotBeNull();
        (await other.PostAsJsonAsync(TherapyTestSteps.Plans(patientId), new TherapyPlanContent())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var otherCard = await other.CreateCardAsync();
        (await other.PostAsJsonAsync(TherapyTestSteps.Plans(otherCard), TherapyTestSteps.Complete(exercise.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync(TherapyTestSteps.Plan(plan.Id), TherapyTestSteps.Save(plan))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreach (var action in new[] { "activate", "complete", "archive" })
            (await other.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/" + action, new { plan.Version })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/corrections", TherapyTestSteps.Correction(plan))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (patient, email) = await factory.CreateConfirmedPatientClientAsync();
        await factory.WithDbAsync(async db =>
        {
            var card = await db.Patients.SingleAsync(p => p.Id == patientId);
            card.UserId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            card.DocumentationSharedWithAccount = true;
            return await db.SaveChangesAsync();
        });
        plan = await owner.Action(plan, "activate");
        var moderator = await factory.CreateModeratorAsync();
        foreach (var denied in new[] { patient, moderator })
        {
            (await denied.GetAsync(TherapyTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await denied.GetAsync(TherapyTestSteps.Plan(plan.Id) + "/versions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await denied.GetAsync(TherapyTestSteps.Plans(patientId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await denied.GetAsync("/api/exercises")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await denied.PostAsJsonAsync(TherapyTestSteps.Plans(patientId), new TherapyPlanContent())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        (await factory.CreateClient().GetAsync(TherapyTestSteps.Plan(plan.Id))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Owner_status_history_and_snapshot_fields_are_controlled_by_server()
    {
        var owner = await factory.CreateTherapistClientAsync();
        var card = await owner.CreateCardAsync();
        var forgedOwner = Guid.NewGuid();
        var exercise = await (await owner.PostAsJsonAsync("/api/exercises", new
        {
            name = "Owned", instructions = "Real instruction", category = "Back", therapistId = forgedOwner,
            isArchived = true, version = Guid.NewGuid()
        })).Body<ExerciseDto>(HttpStatusCode.Created);
        exercise.IsArchived.Should().BeFalse();
        (await factory.WithDbAsync(db => db.Exercises.SingleAsync(e => e.Id == exercise.Id))).TherapistId.Should().NotBe(forgedOwner);
        var plan = await (await owner.PostAsJsonAsync(TherapyTestSteps.Plans(card), new
        {
            name = "Owned plan", therapistId = forgedOwner, patientId = Guid.NewGuid(), status = "Active",
            currentVersion = 99, isPublished = true,
            exercises = new[] { new { exerciseId = exercise.Id, name = "FORGED_NAME", instructions = "FORGED_INSTRUCTIONS", order = 99 } }
        })).Body<TherapyPlanDto>(HttpStatusCode.Created);
        plan.PatientId.Should().Be(card);
        plan.Status.Should().Be(TherapyPlanStatus.Draft);
        plan.CurrentVersion.Should().Be(0);
        plan.Content.Exercises.Should().ContainSingle().Which.Name.Should().Be(exercise.Name);
        plan.Content.Exercises[0].Instructions.Should().Be(exercise.Instructions);
        plan.Content.Exercises[0].Order.Should().Be(0);
        (await owner.History(plan.Id)).Should().BeEmpty();
        (await factory.WithDbAsync(db => db.TherapyPlans.SingleAsync(p => p.Id == plan.Id))).IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task Patient_archive_keeps_readable_history_and_blocks_all_plan_writes_until_restore()
    {
        var client = await factory.CreateTherapistClientAsync();
        var card = await client.CreateCardAsync();
        var exercise = await client.NewExercise();
        var draft = await client.NewPlan(card, TherapyTestSteps.Complete(exercise.Id));
        var active = await client.Action(await client.NewPlan(card, TherapyTestSteps.Complete(exercise.Id)), "activate");
        (await client.DeleteAsync($"/api/patients/{card}")).EnsureSuccessStatusCode();
        foreach (var url in new[] { TherapyTestSteps.Plan(active.Id), TherapyTestSteps.Plan(active.Id) + "/versions", TherapyTestSteps.Plans(card) })
        {
            var read = await client.GetAsync(url);
            read.EnsureSuccessStatusCode();
            read.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        var writes = new[]
        {
            await client.PostAsJsonAsync(TherapyTestSteps.Plans(card), new TherapyPlanContent()),
            await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), TherapyTestSteps.Save(draft)),
            await client.PostAsJsonAsync(TherapyTestSteps.Plan(draft.Id) + "/activate", new { draft.Version }),
            await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", TherapyTestSteps.Correction(active)),
            await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/complete", new { active.Version }),
            await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/archive", new { active.Version })
        };
        foreach (var write in writes)
        {
            write.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await write.ErrorCodeAsync()).Should().Be("patient_archived");
        }
        (await client.History(active.Id)).Should().ContainSingle().Which.Content.Should().BeEquivalentTo(active.Content);
        (await client.PostAsJsonAsync($"/api/patients/{card}/unarchive", new { })).EnsureSuccessStatusCode();
        (await client.Action(active, "complete")).Status.Should().Be(TherapyPlanStatus.Completed);
    }

    [Fact]
    public async Task Invalid_positions_dates_and_transitions_are_rejected_without_partial_updates()
    {
        var client = await factory.CreateTherapistClientAsync();
        var card = await client.CreateCardAsync();
        var exercise = await client.NewExercise();
        var draft = await client.NewPlan(card, TherapyTestSteps.Complete(exercise.Id));
        var baseline = TherapyTestSteps.Save(draft);
        var item = baseline.Exercises.Single();
        foreach (var invalid in new[]
        {
            item with { Sets = 0 }, item with { Sets = 101 }, item with { Repetitions = 0 },
            item with { Repetitions = 1001 }, item with { DurationSeconds = 1 },
            item with { Repetitions = null, DurationSeconds = 0 }, item with { Repetitions = null, DurationSeconds = 86401 }
        })
            (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), baseline with { Exercises = [invalid] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), baseline with { Exercises = [item, item] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), baseline with { EndDate = baseline.StartDate!.Value.AddDays(-1) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), baseline with { Exercises = [item with { Id = Guid.NewGuid() }] })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync(TherapyTestSteps.Plan(draft.Id), baseline with { Version = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(TherapyTestSteps.Plan(draft.Id) + "/complete", new { draft.Version })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(TherapyTestSteps.Plan(draft.Id) + "/corrections", TherapyTestSteps.Correction(draft))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.ReadPlan(draft.Id)).Should().BeEquivalentTo(draft,
            options => options.Using<DateTime>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromTicks(10))).WhenTypeIs<DateTime>());
        var archived = await client.Action(draft, "archive");
        (await client.PostAsJsonAsync(TherapyTestSteps.Plan(draft.Id) + "/activate", new { archived.Version })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
