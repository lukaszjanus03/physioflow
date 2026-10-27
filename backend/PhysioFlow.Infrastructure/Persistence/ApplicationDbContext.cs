using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;

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
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<BodyMap> BodyMaps => Set<BodyMap>();
    public DbSet<PainPoint> PainPoints => Set<PainPoint>();

    public DbSet<AnatomicalRegionInfo> AnatomicalRegionInfos =>
    Set<AnatomicalRegionInfo>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseImage> ExerciseImages => Set<ExerciseImage>();
    public DbSet<TherapyPlan> TherapyPlans => Set<TherapyPlan>();
    public DbSet<TherapyPlanExercise> TherapyPlanExercises => Set<TherapyPlanExercise>();
    public DbSet<PatientDiaryEntry> PatientDiaryEntries => Set<PatientDiaryEntry>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

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

        return await base.SaveChangesAsync(cancellationToken);
    }
}
