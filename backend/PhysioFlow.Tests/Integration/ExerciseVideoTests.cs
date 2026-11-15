using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class FakeExerciseVideoStorage : IExerciseVideoStorage
{
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
    public ConcurrentQueue<(string Key, TimeSpan Expiry)> Signed { get; } = new();
    public ConcurrentQueue<string> Deleted { get; } = new();
    public bool FailUploads { get; set; }
    public bool FailPlayback { get; set; }
    public Func<Task>? BeforePlayback { get; set; }

    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        if (FailUploads) throw new InvalidOperationException("PROVIDER_SECRET");
        using var bytes = new MemoryStream();
        await content.CopyToAsync(bytes, ct);
        var key = Guid.NewGuid() + "/" + fileName;
        Objects[key] = bytes.ToArray();
        return key;
    }

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default) =>
        Task.FromResult<Stream>(new MemoryStream(Objects[storageKey]));

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        Deleted.Enqueue(storageKey);
        Objects.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    public async Task<string> GetPresignedUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct = default)
    {
        if (BeforePlayback is { } before) await before();
        if (FailPlayback) throw new InvalidOperationException("PROVIDER_SECRET");
        Signed.Enqueue((storageKey, expiry));
        return $"https://private.r2.test/{storageKey}?signature={Guid.NewGuid():N}";
    }
}

public static class ExerciseVideoTestSteps
{
    public static byte[] Mp4 => [0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0,
        105, 115, 111, 109, 109, 112, 52, 49, 0, 0, 0, 8, 109, 100, 97, 116];
    public static string Video(Guid exerciseId) => TherapyTestSteps.Exercise(exerciseId) + "/video";
    public static string Playback(Guid videoId) => $"/api/exercise-videos/{videoId}/playback";

    public static async Task<HttpResponseMessage> UploadVideo(this HttpClient client, ExerciseDto exercise,
        byte[]? bytes = null, string name = "personal-recording.mp4", string type = "video/mp4")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Mp4);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        form.Add(new StringContent(exercise.Version.ToString()), "version");
        return await client.PostAsync(Video(exercise.Id) + "/upload", form);
    }

    public static async Task<ExerciseDto> Youtube(this HttpClient client, ExerciseDto exercise,
        string url = "https://youtu.be/dQw4w9WgXcQ?si=tracking") =>
        await (await client.PutAsJsonAsync(Video(exercise.Id) + "/youtube", new { exercise.Version, url })).Body<ExerciseDto>();

    public static async Task<HttpResponseMessage> DetachVideo(this HttpClient client, ExerciseDto exercise) =>
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, Video(exercise.Id))
        {
            Content = JsonContent.Create(new { exercise.Version })
        });
}

public class ExerciseVideoTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Upload_replace_and_detach_preserve_drafts_versions_and_historical_playback()
    {
        var client = await factory.CreateTherapistClientAsync();
        var patient = await client.CreateCardAsync();
        var original = await client.NewExercise();
        original.Video.Should().BeNull();
        var beforeVideo = await client.NewPlan(patient, TherapyTestSteps.Complete(original.Id));
        var first = await (await client.UploadVideo(original)).Body<ExerciseDto>();
        first.Video!.Source.Should().Be(ExerciseVideoSource.Upload);
        first.Video.ContentType.Should().Be("video/mp4");
        first.Video.SizeBytes.Should().Be(ExerciseVideoTestSteps.Mp4.Length);
        first.Video.YoutubeUrl.Should().BeNull();
        first.Version.Should().NotBe(original.Version);
        first.Instructions.Should().Be(original.Instructions);
        (await client.ReadPlan(beforeVideo.Id)).Content.Exercises[0].Video.Should().BeNull();
        var draft = await client.NewPlan(patient, TherapyTestSteps.Complete(first.Id));
        draft.Content.Exercises[0].Video.Should().BeEquivalentTo(first.Video);

        var replacement = await (await client.UploadVideo(first)).Body<ExerciseDto>();
        replacement.Video!.Id.Should().NotBe(first.Video.Id);
        (await client.ReadPlan(draft.Id)).Content.Exercises[0].Video.Should().BeEquivalentTo(first.Video);
        var active = await client.Action(draft, "activate");
        var correction = TherapyTestSteps.Correction(active);
        correction.Exercises.Add(TherapyTestSteps.Complete(first.Id).Exercises[0]);
        var corrected = await (await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", correction)).Body<TherapyPlanDto>();
        corrected.Content.Exercises[0].Video.Should().BeEquivalentTo(first.Video);
        corrected.Content.Exercises[1].Video.Should().BeEquivalentTo(replacement.Video);

        var youtube = await client.Youtube(replacement);
        youtube.Video!.Source.Should().Be(ExerciseVideoSource.YouTube);
        youtube.Video.YoutubeUrl.Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        youtube.Video.ContentType.Should().BeNull();
        youtube.Video.SizeBytes.Should().BeNull();
        var youtubePlan = await client.Action(await client.NewPlan(patient, TherapyTestSteps.Complete(first.Id)), "activate");
        var detached = await (await client.DetachVideo(youtube)).Body<ExerciseDto>();
        detached.Video.Should().BeNull();
        (await client.NewPlan(patient, TherapyTestSteps.Complete(first.Id))).Content.Exercises[0].Video.Should().BeNull();
        (await client.History(youtubePlan.Id))[0].Content.Exercises[0].Video.Should().BeEquivalentTo(youtube.Video);

        // Remove both uploaded positions from the current projection. Only history references them now.
        var removal = TherapyTestSteps.Correction(corrected);
        removal.Exercises.Clear();
        removal.Exercises.Add(TherapyTestSteps.Complete(first.Id).Exercises[0]);
        await (await client.PostAsJsonAsync(TherapyTestSteps.Plan(active.Id) + "/corrections", removal)).Body<TherapyPlanDto>();
        var history = await client.History(active.Id);
        history.Should().HaveCount(3);
        history[0].Content.Exercises[0].Video.Should().BeEquivalentTo(first.Video);
        history[1].Content.Exercises[1].Video.Should().BeEquivalentTo(replacement.Video);
        history[2].Content.Exercises[0].Video.Should().BeNull();

        foreach (var video in new[] { first.Video, replacement.Video })
        {
            var key = await factory.WithDbAsync(db => db.ExerciseVideos.Where(v => v.Id == video.Id).Select(v => v.StorageKey).SingleAsync());
            factory.Videos.Objects.Should().ContainKey(key!);
            factory.Videos.Deleted.Should().NotContain(key!);
            var clock = factory.Services.GetRequiredService<IDateTimeProvider>();
            var earliestExpiry = clock.UtcNow.AddMinutes(5);
            var response = await client.GetAsync(ExerciseVideoTestSteps.Playback(video.Id));
            var latestExpiry = clock.UtcNow.AddMinutes(5);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            var playback = await response.Body<ExerciseVideoPlaybackDto>();
            playback.Url.Should().Contain(key!);
            playback.ExpiresAt.Should().BeOnOrAfter(earliestExpiry).And.BeOnOrBefore(latestExpiry);
            factory.Videos.Signed.Should().Contain(s => s.Key == key && s.Expiry == TimeSpan.FromMinutes(5));
        }
        (await client.GetAsync(ExerciseVideoTestSteps.Playback(youtube.Video.Id))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var listed = await (await client.GetAsync("/api/exercises")).Body<PaginatedList<ExerciseDto>>();
        listed.Items.Single(e => e.Id == original.Id).Video.Should().BeNull();
        var persistedHistory = await factory.WithDbAsync(db => db.TherapyPlanVersions.Where(v => v.TherapyPlanId == active.Id)
            .Select(v => v.ContentJson).ToListAsync());
        persistedHistory.Should().OnlyContain(json => !json.Contains("signature") && !json.Contains("StorageKey"));
    }

    [Fact]
    public async Task Other_therapists_patients_and_anonymous_users_cannot_change_or_play_private_materials()
    {
        var owner = await factory.CreateTherapistClientAsync();
        var other = await factory.CreateTherapistClientAsync();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        var exercise = await (await owner.UploadVideo(await owner.NewExercise())).Body<ExerciseDto>();
        var signedBefore = factory.Videos.Signed.Count;
        var objectsBefore = factory.Videos.Objects.Count;
        foreach (var (client, status) in new[]
        {
            (other, HttpStatusCode.NotFound), (patient, HttpStatusCode.Forbidden),
            (factory.CreateClient(), HttpStatusCode.Unauthorized)
        })
        {
            (await client.UploadVideo(exercise)).StatusCode.Should().Be(status);
            (await client.PutAsJsonAsync(ExerciseVideoTestSteps.Video(exercise.Id) + "/youtube",
                new { exercise.Version, url = "https://youtu.be/dQw4w9WgXcQ" })).StatusCode.Should().Be(status);
            (await client.DetachVideo(exercise)).StatusCode.Should().Be(status);
            (await client.GetAsync(ExerciseVideoTestSteps.Playback(exercise.Video!.Id))).StatusCode.Should().Be(status);
        }
        factory.Videos.Objects.Count.Should().Be(objectsBefore);
        factory.Videos.Signed.Count.Should().Be(signedBefore);
        (await owner.GetAsync(ExerciseVideoTestSteps.Playback(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unchanged = await (await owner.GetAsync(TherapyTestSteps.Exercise(exercise.Id))).Body<ExerciseDto>();
        unchanged.Should().BeEquivalentTo(exercise);

        // User-provided metadata cannot attach another owner's material through library or plan DTOs.
        var forged = await (await other.PostAsJsonAsync("/api/exercises", new
        {
            name = "Other", instructions = "Required", category = "Back", videoId = exercise.Video!.Id,
            video = exercise.Video, youtubeUrl = "https://example.com/private.mp4"
        })).Body<ExerciseDto>(HttpStatusCode.Created);
        forged.Video.Should().BeNull();
        var ownPlan = await (await other.PostAsJsonAsync(TherapyTestSteps.Plans(await other.CreateCardAsync()), new
        {
            exercises = new[] { new { exerciseId = forged.Id, videoId = exercise.Video.Id, video = exercise.Video } }
        })).Body<TherapyPlanDto>(HttpStatusCode.Created);
        ownPlan.Content.Exercises[0].Video.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_uploads_youtube_urls_stale_tokens_and_storage_failures_preserve_current_material()
    {
        var owner = await factory.CreateTherapistClientAsync();
        var original = await owner.NewExercise();
        var attached = await owner.Youtube(original);
        foreach (var (bytes, file, type) in new[]
        {
            (Array.Empty<byte>(), "empty.mp4", "video/mp4"),
            (new byte[] { 1, 2, 3 }, "fake.mp4", "video/mp4"),
            (ExerciseVideoTestSteps.Mp4, "wrong.webm", "video/webm"),
            (ExerciseVideoTestSteps.Mp4, "wrong.mov", "video/quicktime"),
            (ExerciseVideoTestSteps.Mp4, "bad.avi", "video/mp4"),
            (ExerciseVideoTestSteps.Mp4, "wrong.mp4", "application/octet-stream")
        })
            (await owner.UploadVideo(attached, bytes, file, type)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var missing = new MultipartFormDataContent())
        {
            missing.Add(new StringContent(attached.Version.ToString()), "version");
            (await owner.PostAsync(ExerciseVideoTestSteps.Video(attached.Id) + "/upload", missing)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        foreach (var url in new[] { "https://example.com/video.mp4", "https://youtube.com.evil.test/watch?v=dQw4w9WgXcQ", "https://youtube.com/playlist?list=abc" })
            (await owner.PutAsJsonAsync(ExerciseVideoTestSteps.Video(attached.Id) + "/youtube", new { attached.Version, url }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.UploadVideo(original)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.DetachVideo(original)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PutAsJsonAsync(ExerciseVideoTestSteps.Video(attached.Id) + "/youtube",
            new { original.Version, url = "https://youtu.be/dQw4w9WgXcQ" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.DetachVideo(attached with { Version = Guid.Empty })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        factory.Videos.FailUploads = true;
        try
        {
            var failure = await owner.UploadVideo(attached);
            failure.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await failure.Content.ReadAsStringAsync()).Should().NotContain("PROVIDER_SECRET");
            (await (await owner.GetAsync(TherapyTestSteps.Exercise(attached.Id))).Body<ExerciseDto>()).Should().BeEquivalentTo(attached);
        }
        finally { factory.Videos.FailUploads = false; }
        var uploaded = await (await owner.UploadVideo(attached)).Body<ExerciseDto>();
        factory.Videos.FailPlayback = true;
        try
        {
            var failure = await owner.GetAsync(ExerciseVideoTestSteps.Playback(uploaded.Video!.Id));
            failure.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await failure.Content.ReadAsStringAsync()).Should().NotContain("PROVIDER_SECRET");
        }
        finally { factory.Videos.FailPlayback = false; }

        // Editing text through the pre-existing endpoint preserves video and required instructions.
        (await owner.PutAsJsonAsync(TherapyTestSteps.Exercise(uploaded.Id), new SaveExerciseRequest
        {
            Version = uploaded.Version, Name = "Edited", Category = "Back", Instructions = " "
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var edited = await (await owner.PutAsJsonAsync(TherapyTestSteps.Exercise(uploaded.Id), new SaveExerciseRequest
        {
            Version = uploaded.Version, Name = "Edited", Category = "Back", Instructions = "Required instructions"
        })).Body<ExerciseDto>();
        edited.Video.Should().BeEquivalentTo(uploaded.Video);
        var list = await (await owner.GetAsync("/api/exercises")).Body<PaginatedList<ExerciseDto>>();
        list.Items.Single(e => e.Id == edited.Id).Video.Should().BeEquivalentTo(uploaded.Video);
        var raw = JsonSerializer.Serialize(edited);
        raw.Should().NotContain("StorageKey").And.NotContain("signature");
    }
}
