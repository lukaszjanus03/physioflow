using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class TherapistProfileConfiguration : IEntityTypeConfiguration<TherapistProfile>
{
    public void Configure(EntityTypeBuilder<TherapistProfile> builder)
    {
        builder.Property(x => x.City).HasMaxLength(TherapistProfile.CityMaxLength);
        builder.Property(x => x.PracticeName).HasMaxLength(TherapistProfile.PracticeNameMaxLength);
        builder.Property(x => x.Bio).HasMaxLength(TherapistProfile.BioMaxLength);
        builder.Property(x => x.Specializations).IsRequired().HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(x => x.Experience).HasMaxLength(TherapistProfile.DescriptionMaxLength);
        builder.Property(x => x.Education).HasMaxLength(TherapistProfile.DescriptionMaxLength);
        builder.Property(x => x.Street).HasMaxLength(TherapistProfile.StreetMaxLength);
        builder.Property(x => x.BuildingNumber).HasMaxLength(TherapistProfile.BuildingNumberMaxLength);
        builder.Property(x => x.PostalCode).HasMaxLength(TherapistProfile.PostalCodeMaxLength);
        builder.Property(x => x.PublicPhoneNumber).HasMaxLength(TherapistProfile.PublicPhoneMaxLength);
        builder.Property(x => x.PublicEmail).HasMaxLength(TherapistProfile.PublicEmailMaxLength);
        builder.Property(x => x.OrganizationalInfo).HasMaxLength(TherapistProfile.DescriptionMaxLength);
        builder.Property(x => x.FirstVisitPreparation).HasMaxLength(TherapistProfile.DescriptionMaxLength);
        // Optimistic concurrency protects replacements/deletions, including two first uploads.
        builder.Property(x => x.PhotoStorageKey).HasMaxLength(512).IsConcurrencyToken();
    }
}
