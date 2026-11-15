using PhysioFlow.Domain.Common;

namespace PhysioFlow.Domain.Entities;

public enum ExerciseVideoSource { Upload, YouTube }

/// <summary>Immutable material. Replacing or detaching a library video only changes Exercise.VideoId.</summary>
public class ExerciseVideo : BaseEntity
{
    public Guid TherapistId { get; set; }
    public TherapistProfile Therapist { get; set; } = null!;
    public ExerciseVideoSource Source { get; set; }
    public string? YoutubeVideoId { get; set; }
    public string? StorageKey { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
}
