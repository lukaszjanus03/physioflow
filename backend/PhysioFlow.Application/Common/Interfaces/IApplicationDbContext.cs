using Microsoft.EntityFrameworkCore;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TherapistProfile> Therapists { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<AvailabilitySlot> AvailabilitySlots { get; }
    DbSet<BodyMap> BodyMaps { get; }
    DbSet<PainPoint> PainPoints { get; }
    DbSet<MedicalRecord> MedicalRecords { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Exercise> Exercises { get; }
    DbSet<ExerciseImage> ExerciseImages { get; }
    DbSet<TherapyPlan> TherapyPlans { get; }
    DbSet<TherapyPlanExercise> TherapyPlanExercises { get; }
    DbSet<PatientDiaryEntry> PatientDiaryEntries { get; }
    DbSet<ChatMessage> ChatMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Zwraca informacje o aktualnie zalogowanym userze.
/// Implementacja w Infrastructure (z HttpContext + JWT claims).
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
