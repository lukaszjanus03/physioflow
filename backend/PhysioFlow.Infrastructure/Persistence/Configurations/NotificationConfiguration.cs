using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(50).IsRequired();
        b.Property(x => x.Title).HasMaxLength(150).IsRequired();
        b.Property(x => x.Body).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.EventId, x.RecipientId }).IsUnique();
        b.HasIndex(x => new { x.RecipientId, x.CreatedAt, x.Id });
        b.HasIndex(x => new { x.RecipientId, x.ReadAt });
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecipientId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
