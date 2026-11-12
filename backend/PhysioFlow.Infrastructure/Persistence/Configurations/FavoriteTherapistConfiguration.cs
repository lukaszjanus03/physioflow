using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class FavoriteTherapistConfiguration : IEntityTypeConfiguration<FavoriteTherapist>
{
    public const string PrimaryKeyName = "pk_favorite_therapists";

    public void Configure(EntityTypeBuilder<FavoriteTherapist> b)
    {
        b.ToTable("favorite_therapists");
        b.HasKey(x => new { x.PatientUserId, x.TherapistId }).HasName(PrimaryKeyName);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PatientUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<TherapistProfile>().WithMany().HasForeignKey(x => x.TherapistId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PatientUserId, x.CreatedAt, x.TherapistId });
        // No filter/navigation to Therapist: bookmarks survive soft deletion and hiding.
    }
}
