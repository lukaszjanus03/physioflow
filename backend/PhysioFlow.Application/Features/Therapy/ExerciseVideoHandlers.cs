using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapy;

public record UploadExerciseVideoCommand(Guid ExerciseId, Guid Version, Stream Content, long Length,
    string FileName, string ContentType) : IRequest<ExerciseDto>;
public record SetYoutubeVideoCommand(Guid ExerciseId, SetYoutubeVideoRequest Body) : IRequest<ExerciseDto>;
public record DetachExerciseVideoCommand(Guid ExerciseId, Guid Version) : IRequest<ExerciseDto>;
public record GetExerciseVideoPlaybackQuery(Guid VideoId) : IRequest<ExerciseVideoPlaybackDto>;

public class ExerciseVideoHandlers(IApplicationDbContext db, ICurrentUserService user,
    IExerciseVideoStorage storage, IDateTimeProvider clock) :
    IRequestHandler<UploadExerciseVideoCommand, ExerciseDto>,
    IRequestHandler<SetYoutubeVideoCommand, ExerciseDto>,
    IRequestHandler<DetachExerciseVideoCommand, ExerciseDto>,
    IRequestHandler<GetExerciseVideoPlaybackQuery, ExerciseVideoPlaybackDto>
{
    public static readonly TimeSpan PlaybackLifetime = TimeSpan.FromMinutes(5);

    private async Task<Guid> TherapistId(CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        return (await db.RequireTherapistAsync(user, ct)).Id;
    }

    private async Task<Exercise> Editable(Guid id, Guid version, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        var exercise = await db.Exercises.Include(e => e.Video)
            .SingleOrDefaultAsync(e => e.Id == id && e.TherapistId == owner, ct)
            ?? throw new NotFoundException(nameof(Exercise), id);
        TherapyValidation.Require(version != Guid.Empty, "version", "Edit token is required.");
        if (exercise.ConcurrencyStamp != version)
            throw new ConflictException("concurrent_modification", "Exercise changed. Reload before saving.");
        return exercise;
    }

    public async Task<ExerciseDto> Handle(UploadExerciseVideoCommand request, CancellationToken ct)
    {
        var exercise = await Editable(request.ExerciseId, request.Version, ct);
        var validated = await ExerciseVideoFile.ValidateAsync(request.Content, request.Length,
            request.FileName, request.ContentType, ct);
        await using var content = validated.Content;
        string key;
        try
        {
            key = await storage.UploadAsync(content, "exercise-video" + validated.Extension, validated.ContentType, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new StorageUnavailableException(ex); }

        return await Attach(exercise, new ExerciseVideo
        {
            TherapistId = exercise.TherapistId, Source = ExerciseVideoSource.Upload,
            StorageKey = key, ContentType = validated.ContentType, SizeBytes = request.Length
        }, ct);
    }

    public async Task<ExerciseDto> Handle(SetYoutubeVideoCommand request, CancellationToken ct)
    {
        var exercise = await Editable(request.ExerciseId, request.Body.Version, ct);
        var id = YoutubeVideoUrl.ParseId(request.Body.Url);
        TherapyValidation.Require(id != null, "url", "A supported YouTube video URL with an 11-character video ID is required.");
        return await Attach(exercise, new ExerciseVideo
        {
            TherapistId = exercise.TherapistId, Source = ExerciseVideoSource.YouTube, YoutubeVideoId = id
        }, ct);
    }

    public async Task<ExerciseDto> Handle(DetachExerciseVideoCommand request, CancellationToken ct)
    {
        var exercise = await Editable(request.ExerciseId, request.Version, ct);
        exercise.Video = null;
        exercise.VideoId = null;
        exercise.YoutubeUrl = null;
        exercise.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ExerciseHandlers.ToDto(exercise);
    }

    public async Task<ExerciseVideoPlaybackDto> Handle(GetExerciseVideoPlaybackQuery request, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        var video = await db.ExerciseVideos.AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == request.VideoId && v.TherapistId == owner, ct)
            ?? throw new NotFoundException(nameof(ExerciseVideo), request.VideoId);
        if (video.Source != ExerciseVideoSource.Upload)
            throw new ConflictException("video_not_uploaded", "Use the canonical YouTube URL for this material.");

        var expiresAt = clock.UtcNow.Add(PlaybackLifetime);
        try
        {
            var url = await storage.GetPresignedUrlAsync(video.StorageKey!, PlaybackLifetime, ct);
            return new(url, expiresAt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new StorageUnavailableException(ex); }
    }

    private async Task<ExerciseDto> Attach(Exercise exercise, ExerciseVideo video, CancellationToken ct)
    {
        db.ExerciseVideos.Add(video);
        exercise.Video = video;
        exercise.VideoId = video.Id;
        exercise.YoutubeUrl = video.YoutubeVideoId is { } id ? YoutubeVideoUrl.CanonicalUrl(id) : null;
        exercise.ConcurrencyStamp = Guid.NewGuid();
        // Materials and objects are retained, including when a database commit has an ambiguous outcome.
        // History contains immutable material IDs and must outlive library replacements and detachments.
        await db.SaveChangesAsync(ct);
        return ExerciseHandlers.ToDto(exercise);
    }
}

internal static class ExerciseVideoMapping
{
    public static ExerciseVideoDto? ToDto(ExerciseVideo? video) => video is null ? null :
        new(video.Id, video.Source, video.YoutubeVideoId is { } id ? YoutubeVideoUrl.CanonicalUrl(id) : null,
            video.ContentType, video.SizeBytes);
}
