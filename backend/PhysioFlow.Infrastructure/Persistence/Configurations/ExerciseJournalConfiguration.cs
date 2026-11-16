using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class ExerciseJournalConfiguration : IEntityTypeConfiguration<ExerciseJournalEntry>
{
    public const string RequestIndex = "ix_exercise_journal_entries_account_request";
    public void Configure(EntityTypeBuilder<ExerciseJournalEntry> b)
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Comment).HasMaxLength(ExerciseJournalEntry.CommentMaxLength);
        b.Property(e => e.PlanName).HasMaxLength(TherapyPlan.NameMaxLength);
        b.Property(e => e.ExerciseSnapshotJson).IsRequired();
        b.Property(e => e.OriginalRequestHash).HasMaxLength(64).IsRequired();
        b.Property(e => e.Version).IsConcurrencyToken();
        // Retain tombstones in this index: a delayed retry must never recreate a deleted report.
        b.HasIndex(e => new { e.PatientUserId, e.RequestId }).IsUnique().HasDatabaseName(RequestIndex);
        b.HasIndex(e => new { e.PatientUserId, e.PerformedOn, e.CreatedAt, e.Id });
        b.HasIndex(e => new { e.PatientUserId, e.TherapyPlanId, e.PerformedOn });
        b.HasOne<Patient>().WithMany().HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TherapyPlanVersion>().WithMany().HasForeignKey(e => e.TherapyPlanVersionId).OnDelete(DeleteBehavior.Restrict);
        // No parent query filter: the author's own history survives revocation, unlink and soft deletion.
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_exercise_journal_status", "status IN ('Completed', 'Partial', 'Skipped')");
            t.HasCheckConstraint("ck_exercise_journal_dose", "(actual_sets IS NULL OR actual_sets BETWEEN 1 AND 100) AND (actual_repetitions IS NULL OR actual_repetitions BETWEEN 1 AND 1000) AND (actual_duration_seconds IS NULL OR actual_duration_seconds BETWEEN 1 AND 86400) AND (actual_repetitions IS NULL OR actual_duration_seconds IS NULL) AND (status <> 'Skipped' OR (actual_sets IS NULL AND actual_repetitions IS NULL AND actual_duration_seconds IS NULL))");
            t.HasCheckConstraint("ck_exercise_journal_pain", "pain_level IS NULL OR pain_level BETWEEN 0 AND 10");
            t.HasCheckConstraint("ck_exercise_journal_date", "performed_on >= DATE '1900-01-01'");
        });
    }
}
