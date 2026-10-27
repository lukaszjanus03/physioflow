using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Persistence.Seeds;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class AnatomicalRegionInfoConfiguration
    : IEntityTypeConfiguration<AnatomicalRegionInfo>
{
    public void Configure(
        EntityTypeBuilder<AnatomicalRegionInfo> builder)
    {
        builder.ToTable("anatomical_region_infos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RegionId)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.RegionId)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.Function)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.CommonIssues)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(x => x.ExaminationTips)
            .HasColumnType("text[]")
            .IsRequired();

        builder.HasData(AnatomicalRegionInfoSeed.Create());
    }
}