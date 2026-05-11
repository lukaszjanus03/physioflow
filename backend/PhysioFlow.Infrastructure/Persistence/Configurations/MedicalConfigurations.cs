using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> b)
    {
        b.ToTable("appointments");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Duration).HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasOne(x => x.Therapist).WithMany(t => t.Appointments)
            .HasForeignKey(x => x.TherapistId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Patient).WithMany(p => p.Appointments)
            .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TherapistId, x.StartsAt });
        b.HasIndex(x => new { x.PatientId, x.StartsAt });
    }
}

public class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> b)
    {
        b.ToTable("availability_slots");
        b.HasOne(x => x.Therapist).WithMany(t => t.AvailabilitySlots)
            .HasForeignKey(x => x.TherapistId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TherapistId);
    }
}

public class BodyMapConfiguration : IEntityTypeConfiguration<BodyMap>
{
    public void Configure(EntityTypeBuilder<BodyMap> b)
    {
        b.ToTable("body_maps");
        b.Property(x => x.CreatedByRole).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(2000);

        b.HasOne(x => x.Patient).WithMany(p => p.BodyMaps)
            .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Appointment).WithMany()
            .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.PatientId, x.RecordedAt });
    }
}

public class PainPointConfiguration : IEntityTypeConfiguration<PainPoint>
{
    public void Configure(EntityTypeBuilder<PainPoint> b)
    {
        b.ToTable("pain_points");
        b.Property(x => x.Side).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.X).HasPrecision(6, 5);  // 0.00000 - 1.00000
        b.Property(x => x.Y).HasPrecision(6, 5);
        b.Property(x => x.Note).HasMaxLength(500);

        b.HasOne(x => x.BodyMap).WithMany(m => m.PainPoints)
            .HasForeignKey(x => x.BodyMapId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.BodyMapId);
    }
}

public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> b)
    {
        b.ToTable("medical_records");
        b.Property(x => x.PostVisitNoteHtml).HasColumnType("text");
        b.Property(x => x.PostVisitNoteText).HasColumnType("text");
        b.Property(x => x.InterviewForm).HasColumnType("jsonb");
        b.Property(x => x.PainScale);

        b.HasOne(x => x.Patient).WithMany(p => p.MedicalRecords)
            .HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Appointment).WithOne(a => a.MedicalRecord)
            .HasForeignKey<MedicalRecord>(x => x.AppointmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Therapist).WithMany()
            .HasForeignKey(x => x.TherapistId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.PatientId, x.CreatedAt });
    }
}

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("attachments");
        b.Property(x => x.FileName).IsRequired().HasMaxLength(255);
        b.Property(x => x.StorageKey).IsRequired().HasMaxLength(512);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(100);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);

        b.HasOne(x => x.MedicalRecord).WithMany(r => r.Attachments)
            .HasForeignKey(x => x.MedicalRecordId).OnDelete(DeleteBehavior.Cascade);
    }
}
