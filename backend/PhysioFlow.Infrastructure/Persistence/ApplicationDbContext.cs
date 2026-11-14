using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;
using Npgsql;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Infrastructure.Persistence.Configurations;

namespace PhysioFlow.Infrastructure.Persistence;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    private readonly ICurrentUserService? _currentUser;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUser = null) : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<TherapistProfile> Therapists => Set<TherapistProfile>();
    public DbSet<FavoriteTherapist> FavoriteTherapists => Set<FavoriteTherapist>();
    public DbSet<TherapistReview> TherapistReviews => Set<TherapistReview>();
    public DbSet<ReviewStatusChange> ReviewStatusChanges => Set<ReviewStatusChange>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<TherapistCalendarSettings> CalendarSettings => Set<TherapistCalendarSettings>();
    public DbSet<BodyMap> BodyMaps => Set<BodyMap>();
    public DbSet<PainPoint> PainPoints => Set<PainPoint>();

    public DbSet<AnatomicalRegionInfo> AnatomicalRegionInfos =>
    Set<AnatomicalRegionInfo>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalRecordVersion> MedicalRecordVersions => Set<MedicalRecordVersion>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseImage> ExerciseImages => Set<ExerciseImage>();
    public DbSet<TherapyPlan> TherapyPlans => Set<TherapyPlan>();
    public DbSet<TherapyPlanExercise> TherapyPlanExercises => Set<TherapyPlanExercise>();
    public DbSet<PatientDiaryEntry> PatientDiaryEntries => Set<PatientDiaryEntry>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<CooperationRequest> CooperationRequests => Set<CooperationRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // --- Global query filter: soft delete na wszystkich SoftDeletableEntity
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(SoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(SoftDeletableEntity.IsDeleted));
                var notDeleted = System.Linq.Expressions.Expression.Not(property);
                var lambda = System.Linq.Expressions.Expression.Lambda(notDeleted, parameter);
                builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        // --- Query filtry dla "dzieci" soft-deletable rodziców.
        // EF ostrzega gdy dziecko (required relationship) nie ma matching query filtra - przy JOIN'ach
        // może to prowadzić do sytuacji że dziecko wraca w queries mimo że rodzic jest "usunięty".
        // Rozwiązanie: filter po FK z lookupem na IsDeleted rodzica.
        builder.Entity<Attachment>()
            .HasQueryFilter(a => !a.MedicalRecord.IsDeleted);
        builder.Entity<AvailabilitySlot>()
            .HasQueryFilter(s => !s.Therapist.IsDeleted);
        builder.Entity<TherapistCalendarSettings>()
            .HasQueryFilter(s => !s.Therapist.IsDeleted);
        builder.Entity<BodyMap>()
            .HasQueryFilter(m => !m.Patient.IsDeleted);
        builder.Entity<ExerciseImage>()
            .HasQueryFilter(i => !i.Exercise.IsDeleted);
        builder.Entity<PatientDiaryEntry>()
            .HasQueryFilter(e => !e.Patient.IsDeleted);
        builder.Entity<TherapyPlanExercise>()
            .HasQueryFilter(e => !e.Exercise.IsDeleted && !e.TherapyPlan.IsDeleted);
        builder.Entity<PainPoint>()
            .HasQueryFilter(p => !p.BodyMap.Patient.IsDeleted);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<MedicalRecordVersion>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Approved documentation versions are immutable.");

        // Audit fields - auto uzupełnianie
        var now = DateTime.UtcNow;
        var userId = _currentUser?.UserId;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedById = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedById = userId;
                    break;
            }
        }

        // Soft delete - zamień Delete na Update IsDeleted = true
        foreach (var entry in ChangeTracker.Entries<SoftDeletableEntity>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = now;
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: TherapistReviewConfiguration.UniquePairIndex })
        {
            throw new ConflictException("review_already_exists", "An opinion for this therapist already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_medical_records_appointment_id" })
        {
            throw new ConflictException("medical_record_already_exists", "Documentation for this appointment already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: MedicalRecordVersionConfiguration.UniqueVersionIndex })
        {
            throw new ConflictException("concurrent_modification", "Documentation changed. Reload it before saving.");
        }
    }
}
