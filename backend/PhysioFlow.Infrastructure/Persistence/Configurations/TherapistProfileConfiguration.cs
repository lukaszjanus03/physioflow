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
    }
}
