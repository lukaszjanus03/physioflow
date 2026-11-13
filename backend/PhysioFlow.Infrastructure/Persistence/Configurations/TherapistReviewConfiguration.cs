using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class TherapistReviewConfiguration : IEntityTypeConfiguration<TherapistReview>
{
    public const string UniquePairIndex = "ux_therapist_reviews_patient_therapist";

    public void Configure(EntityTypeBuilder<TherapistReview> b)
    {
        b.ToTable("therapist_reviews", t =>
        {
            t.HasCheckConstraint("ck_therapist_reviews_rating", "rating BETWEEN 1 AND 5");
            t.HasCheckConstraint("ck_therapist_reviews_status", "status IN (1, 2, 3)");
        });
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.PatientUserId, x.TherapistId }).IsUnique().HasDatabaseName(UniquePairIndex);
        b.HasIndex(x => new { x.TherapistId, x.Status, x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.Status, x.CreatedAt, x.Id });
        b.Property(x => x.Comment).HasMaxLength(TherapistReview.CommentMaxLength);
        b.Property(x => x.RejectionReason).HasMaxLength(TherapistReview.CommentMaxLength);
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PatientUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TherapistProfile>().WithMany(x => x.Reviews).HasForeignKey(x => x.TherapistId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReviewStatusChangeConfiguration : IEntityTypeConfiguration<ReviewStatusChange>
{
    public void Configure(EntityTypeBuilder<ReviewStatusChange> b)
    {
        b.ToTable("review_status_changes");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ReviewId, x.OccurredAt });
    }
}
