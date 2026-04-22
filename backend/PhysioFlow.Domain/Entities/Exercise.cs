using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

public class Exercise : SoftDeletableEntity
{
    public Guid TherapistId { get; set; }          // właściciel (biblioteka prywatna)

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public MuscleGroup MuscleGroup { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? ThumbnailStorageKey { get; set; }  // R2

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
    public Guid PatientId { get; set; }
    public Guid TherapistId { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsPublished { get; set; }            // widoczny w portalu pacjenta

    public Patient Patient { get; set; } = null!;
    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<TherapyPlanExercise> Exercises { get; set; } = new List<TherapyPlanExercise>();
}

public class TherapyPlanExercise : BaseEntity
{
    public Guid TherapyPlanId { get; set; }
    public Guid ExerciseId { get; set; }

    public int Sets { get; set; }
    public int Repetitions { get; set; }
    public int? DurationSeconds { get; set; }
    public int? RestSeconds { get; set; }
    public int Order { get; set; }
    public string? Notes { get; set; }

    public TherapyPlan TherapyPlan { get; set; } = null!;
    public Exercise Exercise { get; set; } = null!;
}
