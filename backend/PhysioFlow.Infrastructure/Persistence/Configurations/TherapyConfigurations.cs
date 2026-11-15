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
        b.HasOne(e => e.Video).WithMany().HasForeignKey(e => e.VideoId).OnDelete(DeleteBehavior.Restrict);
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
        b.HasOne(p => p.PublishedVersion).WithMany().HasForeignKey(p => p.PublishedVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => new { p.PublishedToUserId, p.PublishedAt, p.Id });
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
        b.HasOne(e => e.Video).WithMany().HasForeignKey(e => e.VideoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.Exercise).WithMany(e => e.PlanAssignments).HasForeignKey(e => e.ExerciseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(e => e.TherapyPlan).WithMany(p => p.Exercises).HasForeignKey(e => e.TherapyPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ExerciseVideoConfiguration : IEntityTypeConfiguration<ExerciseVideo>
{
    public void Configure(EntityTypeBuilder<ExerciseVideo> b)
    {
        b.Property(v => v.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(v => v.YoutubeVideoId).HasMaxLength(11);
        b.Property(v => v.StorageKey).HasMaxLength(500);
        b.Property(v => v.ContentType).HasMaxLength(50);
        b.HasOne(v => v.Therapist).WithMany().HasForeignKey(v => v.TherapistId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(v => new { v.TherapistId, v.Id });
        b.HasQueryFilter(v => !v.Therapist.IsDeleted);
        b.ToTable(t => t.HasCheckConstraint("ck_exercise_videos_source",
            "(source = 'YouTube' AND youtube_video_id IS NOT NULL AND storage_key IS NULL AND content_type IS NULL AND size_bytes IS NULL) OR " +
            "(source = 'Upload' AND youtube_video_id IS NULL AND storage_key IS NOT NULL AND content_type IS NOT NULL AND size_bytes IS NOT NULL AND size_bytes > 0 AND size_bytes <= 104857600)"));
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
