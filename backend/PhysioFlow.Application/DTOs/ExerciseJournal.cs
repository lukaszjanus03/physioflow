using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.DTOs;

public record ExerciseJournalContent
{
    public DateOnly PerformedOn { get; init; }
    public ExerciseCompletionStatus? Status { get; init; }
    public int? ActualSets { get; init; }
    public int? ActualRepetitions { get; init; }
    public int? ActualDurationSeconds { get; init; }
    public int? PainLevel { get; init; }
    public string? Comment { get; init; }
}

public record CreateExerciseJournalRequest : ExerciseJournalContent
{
    public Guid RequestId { get; init; }
    public Guid TherapyPlanId { get; init; }
    public int PlanVersionNumber { get; init; }
    public Guid PlanItemId { get; init; }
}

public record UpdateExerciseJournalRequest : ExerciseJournalContent
{
    public Guid Version { get; init; }
}

public record ExerciseJournalVersionRequest(Guid Version);

// Only patient-visible instructions and dose, never library notes, video storage keys or clinical data.
public record JournalExerciseSnapshot(string Name, string? Instructions, int Order, int? Sets,
    int? Repetitions, int? DurationSeconds, string? Frequency, string? Notes);

public record ExerciseJournalEntryDto(Guid Id, Guid PatientId, Guid TherapyPlanId, Guid TherapyPlanVersionId,
    int PlanVersionNumber, Guid PlanItemId, DateTime PublishedAt, string? PlanName, JournalExerciseSnapshot Exercise,
    DateOnly PerformedOn, ExerciseCompletionStatus Status, int? ActualSets, int? ActualRepetitions,
    int? ActualDurationSeconds, int? PainLevel, string? Comment, DateTime CreatedAt, DateTime? UpdatedAt, Guid Version);

public record CreateExerciseJournalResult(ExerciseJournalEntryDto Entry, bool Replayed);

public record ExerciseJournalSummaryDto(int CompletedCount, int PartialCount, int SkippedCount,
    DateOnly? LastPerformedOn);
