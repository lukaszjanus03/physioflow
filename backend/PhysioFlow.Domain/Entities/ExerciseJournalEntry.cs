namespace PhysioFlow.Domain.Entities;

public enum ExerciseCompletionStatus { Completed, Partial, Skipped }

/// <summary>Owned by the reporting account, independently of subsequent patient-card links.</summary>
public class ExerciseJournalEntry
{
    public const int CommentMaxLength = 2000;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientUserId { get; set; }
    public Guid PatientId { get; set; }
    public Guid TherapyPlanId { get; set; }
    public Guid TherapyPlanVersionId { get; set; }
    public int PlanVersionNumber { get; set; }
    // Position IDs belong to immutable version JSON, not to the mutable plan-item table.
    public Guid PlanItemId { get; set; }
    public Guid PublicationLinkVersion { get; set; }
    public DateTime PublishedAt { get; set; }
    public string? PlanName { get; set; }
    public string ExerciseSnapshotJson { get; set; } = null!;
    public DateOnly PerformedOn { get; set; }
    public ExerciseCompletionStatus Status { get; set; }
    public int? ActualSets { get; set; }
    public int? ActualRepetitions { get; set; }
    public int? ActualDurationSeconds { get; set; }
    public int? PainLevel { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid RequestId { get; set; }
    public string OriginalRequestHash { get; set; } = null!;
}
