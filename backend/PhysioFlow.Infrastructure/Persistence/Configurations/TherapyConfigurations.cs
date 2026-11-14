using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> b)
    {
        b.Property(e => e.Category).HasMaxLength(Exercise.CategoryMaxLength);
        b.Property(e => e.Notes).HasMaxLength(Exercise.NotesMaxLength);
        b.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        b.HasIndex(e => new { e.TherapistId, e.IsArchived, e.Name, e.Id });
        b.HasOne(e => e.Therapist).WithMany(t => t.Exercises).HasForeignKey(e => e.TherapistId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TherapyPlanConfiguration : IEntityTypeConfiguration<TherapyPlan>
{
    public void Configure(EntityTypeBuilder<TherapyPlan> b)
    {
        b.Property(p => p.Goal).HasMaxLength(TherapyPlan.TextMaxLength);
        b.Property(p => p.GeneralRecommendations).HasMaxLength(TherapyPlan.TextMaxLength);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TherapyPlanStatus.Draft);
        b.Property(p => p.ConcurrencyStamp).IsConcurrencyToken();
        b.HasIndex(p => new { p.TherapistId, p.PatientId, p.CreatedAt, p.Id });
        b.HasOne(p => p.Patient).WithMany(p => p.TherapyPlans).HasForeignKey(p => p.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Therapist).WithMany().HasForeignKey(p => p.TherapistId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TherapyPlanExerciseConfiguration : IEntityTypeConfiguration<TherapyPlanExercise>
{
    public void Configure(EntityTypeBuilder<TherapyPlanExercise> b)
    {
        b.Property(e => e.Frequency).HasMaxLength(200);
        b.HasOne(e => e.Exercise).WithMany(e => e.PlanAssignments).HasForeignKey(e => e.ExerciseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.TherapyPlan).WithMany(p => p.Exercises).HasForeignKey(e => e.TherapyPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TherapyPlanVersionConfiguration : IEntityTypeConfiguration<TherapyPlanVersion>
{
    public const string UniqueVersionIndex = "ix_therapy_plan_versions_plan_version";
    public void Configure(EntityTypeBuilder<TherapyPlanVersion> b)
    {
        b.HasKey(v => v.Id);
        b.Property(v => v.CorrectionReason).HasMaxLength(TherapyPlan.CorrectionReasonMaxLength);
        b.Property(v => v.ContentJson).IsRequired();
        b.HasIndex(v => new { v.TherapyPlanId, v.VersionNumber }).IsUnique().HasDatabaseName(UniqueVersionIndex);
        b.HasOne(v => v.TherapyPlan).WithMany(p => p.Versions).HasForeignKey(v => v.TherapyPlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(v => !v.TherapyPlan.IsDeleted);
    }
}
