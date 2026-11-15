using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.DTOs;

public record ExerciseContent
{
    public string? Name { get; init; }
    public string? Instructions { get; init; }
    public string? Category { get; init; }
    public string? Notes { get; init; }
}

public record SaveExerciseRequest : ExerciseContent
{
    public Guid Version { get; init; }
}

public record TherapyVersionRequest(Guid Version);
public record ExerciseDto(Guid Id, string Name, string? Instructions, string? Category, string? Notes,
    bool IsArchived, Guid Version, ExerciseVideoDto? Video = null);

public record ExerciseVideoDto(Guid Id, ExerciseVideoSource Source, string? YoutubeUrl,
    string? ContentType, long? SizeBytes);
public record SetYoutubeVideoRequest(Guid Version, string? Url);
public record ExerciseVideoPlaybackDto(string Url, DateTime ExpiresAt);

public record TherapyPlanContent
{
    public string? Name { get; init; }
    public string? Goal { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? GeneralRecommendations { get; init; }
    public List<TherapyPlanItemRequest> Exercises { get; init; } = [];
}

public record SaveTherapyPlanRequest : TherapyPlanContent
{
    public Guid Version { get; init; }
}

public record CorrectTherapyPlanRequest : SaveTherapyPlanRequest
{
    public string? CorrectionReason { get; init; }
}

// Id identifies an existing position. Null creates a new position from ExerciseId.
// Array order is authoritative; client cannot supply or overwrite snapshot text.
public record TherapyPlanItemRequest
{
    public Guid? Id { get; init; }
    public Guid ExerciseId { get; init; }
    public int? Sets { get; init; }
    public int? Repetitions { get; init; }
    public int? DurationSeconds { get; init; }
    public string? Frequency { get; init; }
    public string? Notes { get; init; }
}

public record TherapyPlanItemDto(Guid Id, Guid ExerciseId, int Order, string Name, string? Instructions,
    int? Sets, int? Repetitions, int? DurationSeconds, string? Frequency, string? Notes, ExerciseVideoDto? Video = null);

public record TherapyPlanSnapshot(string? Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string? GeneralRecommendations, IReadOnlyList<TherapyPlanItemDto> Exercises);

public record TherapyPlanDto(Guid Id, Guid PatientId, TherapyPlanStatus Status, int CurrentVersion,
    Guid Version, DateTime CreatedAt, DateTime? UpdatedAt, TherapyPlanSnapshot Content,
    PatientPublicationDto? Publication = null);

public record TherapyPlanListItemDto(Guid Id, string? Name, TherapyPlanStatus Status, int CurrentVersion,
    DateOnly? StartDate, DateOnly? EndDate, DateTime CreatedAt, DateTime? UpdatedAt,
    PatientPublicationDto? Publication = null);

public record TherapyPlanVersionDto(int VersionNumber, Guid AuthorUserId, DateTime ActivatedAt,
    string? CorrectionReason, TherapyPlanSnapshot Content);
