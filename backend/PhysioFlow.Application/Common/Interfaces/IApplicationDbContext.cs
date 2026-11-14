using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<TherapistProfile> Therapists { get; }
    DbSet<FavoriteTherapist> FavoriteTherapists { get; }
    DbSet<TherapistReview> TherapistReviews { get; }
    DbSet<ReviewStatusChange> ReviewStatusChanges { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<AppointmentReminder> AppointmentReminders { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AvailabilitySlot> AvailabilitySlots { get; }
    DbSet<TherapistCalendarSettings> CalendarSettings { get; }
    DbSet<BodyMap> BodyMaps { get; }
    DbSet<PainPoint> PainPoints { get; }

    DbSet<AnatomicalRegionInfo> AnatomicalRegionInfos { get; }
    DbSet<MedicalRecord> MedicalRecords { get; }
    DbSet<MedicalRecordVersion> MedicalRecordVersions { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<Exercise> Exercises { get; }
    DbSet<ExerciseImage> ExerciseImages { get; }
    DbSet<TherapyPlan> TherapyPlans { get; }
    DbSet<TherapyPlanExercise> TherapyPlanExercises { get; }
    DbSet<PatientDiaryEntry> PatientDiaryEntries { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<CooperationRequest> CooperationRequests { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Odczyt kont użytkowników (ASP.NET Identity) bez zależności od Infrastructure.</summary>
public interface IUserAccountService
{
    Task<UserAccountInfo?> FindAsync(Guid userId, CancellationToken ct);

    /// <summary>Id aktywnych kont, do składania z zapytaniami EF (katalog, wysyłka prośby).</summary>
    IQueryable<Guid> ActiveUserIds();
    Task<bool> IsConfirmedPatientAsync(Guid userId, CancellationToken ct);
    Task<bool> IsReviewModeratorAsync(Guid userId, CancellationToken ct);
    IQueryable<ReviewAuthorName> ReviewAuthorNames();
}

public record UserAccountInfo(Guid Id, string? FirstName, string? LastName, string Email);
public record ReviewAuthorName
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
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
