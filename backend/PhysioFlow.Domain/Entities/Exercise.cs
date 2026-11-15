using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

public class Exercise : SoftDeletableEntity
{
    public const int NameMaxLength = 200;
    public const int InstructionsMaxLength = 10000;
    public const int CategoryMaxLength = 100;
    public const int NotesMaxLength = 2000;
    public Guid TherapistId { get; set; }          // właściciel (biblioteka prywatna)

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public MuscleGroup MuscleGroup { get; set; }
    public string? YoutubeUrl { get; set; }
    public Guid? VideoId { get; set; }
    public ExerciseVideo? Video { get; set; }
    public string? ThumbnailStorageKey { get; set; }  // R2
    public string? Instructions { get; set; }
    public string? Category { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<ExerciseImage> Images { get; set; } = new List<ExerciseImage>();
    public ICollection<TherapyPlanExercise> PlanAssignments { get; set; } = new List<TherapyPlanExercise>();
}

public class ExerciseImage : BaseEntity
{
    public Guid ExerciseId { get; set; }
    public string StorageKey { get; set; } = null!;
    public int Order { get; set; }

    public Exercise Exercise { get; set; } = null!;
}

public class TherapyPlan : SoftDeletableEntity
{
    public const int NameMaxLength = 200;
    public const int TextMaxLength = 10000;
    public const int CorrectionReasonMaxLength = 1000;
    public const int MaxExercises = 100;
    public Guid PatientId { get; set; }
    public Guid TherapistId { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsPublished { get; set; }            // widoczny w portalu pacjenta
    public string? Goal { get; set; }
    public string? GeneralRecommendations { get; set; }
    public TherapyPlanStatus Status { get; set; } = TherapyPlanStatus.Draft;
    public int CurrentVersion { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public ICollection<TherapyPlanVersion> Versions { get; set; } = new List<TherapyPlanVersion>();

    public Patient Patient { get; set; } = null!;
    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<TherapyPlanExercise> Exercises { get; set; } = new List<TherapyPlanExercise>();
}

public class TherapyPlanExercise : BaseEntity
{
    public Guid TherapyPlanId { get; set; }
    public Guid ExerciseId { get; set; }

    public string ExerciseName { get; set; } = null!;
    public string? Instructions { get; set; }
    public Guid? VideoId { get; set; }
    public ExerciseVideo? Video { get; set; }
    public int? Sets { get; set; }
    public int? Repetitions { get; set; }
    public int? DurationSeconds { get; set; }
    public int? RestSeconds { get; set; }
    public int Order { get; set; }
    public string? Notes { get; set; }
    public string? Frequency { get; set; }

    public TherapyPlan TherapyPlan { get; set; } = null!;
    public Exercise Exercise { get; set; } = null!;
}

public enum TherapyPlanStatus { Draft, Active, Completed, Archived }

/// <summary>Immutable activated content, including ordered exercise snapshots.</summary>
public class TherapyPlanVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TherapyPlanId { get; set; }
    public int VersionNumber { get; set; }
    public Guid AuthorUserId { get; set; }
    public DateTime ActivatedAt { get; set; }
    public string? CorrectionReason { get; set; }
    public string ContentJson { get; set; } = null!;
    public TherapyPlan TherapyPlan { get; set; } = null!;
}
