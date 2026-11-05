using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public const int NameMaxLength = 100;

    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.FirstName)
            .HasMaxLength(NameMaxLength);

        builder.Property(x => x.LastName)
            .HasMaxLength(NameMaxLength);
    }
}
