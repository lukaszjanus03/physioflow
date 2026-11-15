using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhysioFlow.Application.DTOs;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PostgresExerciseVideoTests
{
    [PostgresFact]
    public async Task Video_contract_preserves_history_and_privacy_on_postgres()
    {
        using var factory = new PostgresTherapyFactory();
        var tests = new ExerciseVideoTests(factory);
        await tests.Upload_replace_and_detach_preserve_drafts_versions_and_historical_playback();
        await tests.Other_therapists_patients_and_anonymous_users_cannot_change_or_play_private_materials();
        await tests.Invalid_uploads_youtube_urls_stale_tokens_and_storage_failures_preserve_current_material();
        await factory.WithDbAsync(db =>
        {
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return Task.FromResult(0);
        });
    }

    [PostgresFact]
    public async Task Concurrent_upload_and_youtube_replacement_commit_one_material_atomically()
    {
        using var factory = new PostgresTherapyFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var exercise = await owner.Youtube(await owner.NewExercise());
        factory.Writes.Enable();
        var responses = await Task.WhenAll(owner.UploadVideo(exercise),
            owner.PutAsJsonAsync(ExerciseVideoTestSteps.Video(exercise.Id) + "/youtube",
                new { exercise.Version, url = "https://youtu.be/abcdefghijk" }));
        factory.Writes.Disable();
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await responses.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("concurrent_modification");
        var winner = await responses.Single(r => r.IsSuccessStatusCode).Body<ExerciseDto>();
        var saved = await (await owner.GetAsync(TherapyTestSteps.Exercise(exercise.Id))).Body<ExerciseDto>();
        saved.Should().BeEquivalentTo(winner);
        await factory.WithDbAsync(async db =>
        {
            var therapist = await db.Exercises.Where(e => e.Id == exercise.Id).Select(e => e.TherapistId).SingleAsync();
            var materials = await db.ExerciseVideos.Where(v => v.TherapistId == therapist).ToListAsync();
            materials.Should().HaveCount(2);
            materials.Should().Contain(v => v.Id == exercise.Video!.Id);
            materials.Should().Contain(v => v.Id == winner.Video!.Id);
            return 0;
        });
    }

    [PostgresFact]
    public async Task Material_used_only_in_history_rejects_EF_and_SQL_mutation_and_delete()
    {
        using var factory = new PostgresTherapyFactory();
        var client = await factory.CreateTherapistClientAsync();
        var exercise = await (await client.UploadVideo(await client.NewExercise())).Body<ExerciseDto>();
        var plan = await client.Action(await client.NewPlan(await client.CreateCardAsync(), TherapyTestSteps.Complete(exercise.Id)), "activate");
        await (await client.DetachVideo(exercise)).Body<ExerciseDto>();
        var correction = TherapyTestSteps.Correction(plan);
        correction.Exercises.Clear();
        correction.Exercises.Add(TherapyTestSteps.Complete(exercise.Id).Exercises[0]);
        await (await client.PostAsJsonAsync(TherapyTestSteps.Plan(plan.Id) + "/corrections", correction)).Body<TherapyPlanDto>();
        var videoId = exercise.Video!.Id;
        var efMutation = () => factory.WithDbAsync(async db =>
        {
            (await db.ExerciseVideos.SingleAsync(v => v.Id == videoId)).StorageKey = "changed";
            return await db.SaveChangesAsync();
        });
        await efMutation.Should().ThrowAsync<InvalidOperationException>();
        var efDelete = () => factory.WithDbAsync(async db =>
        {
            db.ExerciseVideos.Remove(await db.ExerciseVideos.SingleAsync(v => v.Id == videoId));
            return await db.SaveChangesAsync();
        });
        await efDelete.Should().ThrowAsync<InvalidOperationException>();
        var sqlMutation = () => factory.WithDbAsync(db => db.ExerciseVideos.Where(v => v.Id == videoId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StorageKey, "changed")));
        await sqlMutation.Should().ThrowAsync<Npgsql.PostgresException>();
        var sqlDelete = () => factory.WithDbAsync(db => db.ExerciseVideos.Where(v => v.Id == videoId).ExecuteDeleteAsync());
        await sqlDelete.Should().ThrowAsync<Npgsql.PostgresException>();
        (await client.History(plan.Id))[0].Content.Exercises[0].Video.Should().BeEquivalentTo(exercise.Video);
        (await client.GetAsync(ExerciseVideoTestSteps.Playback(videoId))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [PostgresFact]
    public async Task Migration_imports_supported_legacy_library_links_and_preserves_plans_and_JSON_without_video()
    {
        using var factory = new PostgresTherapyFactory();
        var client = await factory.CreateTherapistClientAsync();
        var exercise = await client.NewExercise();
        var plan = await client.NewPlan(await client.CreateCardAsync(), TherapyTestSteps.Complete(exercise.Id));
        var oldJson = JsonSerializer.Serialize(plan.Content, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        oldJson.Should().NotContain("Video");
        await factory.WithDbAsync(async db =>
        {
            var author = await db.Exercises.Where(e => e.Id == exercise.Id).Select(e => e.Therapist.UserId).SingleAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261114150019_AddTherapyLibraryAndPlanVersions");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE exercises SET youtube_url = 'https://youtu.be/dQw4w9WgXcQ?si=tracking' WHERE id = {exercise.Id};
                INSERT INTO therapy_plan_versions (id, therapy_plan_id, version_number, author_user_id, activated_at, content_json)
                VALUES ({Guid.NewGuid()}, {plan.Id}, 1, {author}, {DateTime.UtcNow}, {oldJson});
                """);
            await migrator.MigrateAsync();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = migrator.GenerateScript("20261114150019_AddTherapyLibraryAndPlanVersions",
                "20261115142243_AddExerciseVideoMaterials", MigrationsSqlGenerationOptions.Idempotent);
            await command.ExecuteNonQueryAsync();
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return 0;
        });
        var imported = await (await client.GetAsync(TherapyTestSteps.Exercise(exercise.Id))).Body<ExerciseDto>();
        imported.Video!.YoutubeUrl.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        imported.Instructions.Should().Be(exercise.Instructions);
        (await client.ReadPlan(plan.Id)).Content.Exercises[0].Video.Should().BeNull();
        (await client.History(plan.Id)).Should().ContainSingle().Which.Content.Exercises[0].Video.Should().BeNull();
        (await client.NewPlan(plan.PatientId, TherapyTestSteps.Complete(exercise.Id))).Content.Exercises[0].Video.Should().BeEquivalentTo(imported.Video);
    }
}
