using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class CooperationRequestConfiguration : IEntityTypeConfiguration<CooperationRequest>
{
    public void Configure(EntityTypeBuilder<CooperationRequest> b)
    {
        b.ToTable("cooperation_requests");

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CardLinkMode).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.EndedBy).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Message).HasMaxLength(CooperationRequest.MessageMaxLength);
        b.Property(x => x.PatientFirstName).HasMaxLength(ApplicationUserConfiguration.NameMaxLength);
        b.Property(x => x.PatientLastName).HasMaxLength(ApplicationUserConfiguration.NameMaxLength);
        b.Property(x => x.PatientEmail).HasMaxLength(256);
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();

        b.HasOne(x => x.Therapist).WithMany().HasForeignKey(x => x.TherapistId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);

        // Najwyżej jedna oczekująca prośba albo aktywna współpraca na parę pacjent-terapeuta, także przy równoczesnych żądaniach.
        b.HasIndex(x => new { x.PatientUserId, x.TherapistId })
            .IsUnique()
            .HasFilter($"status IN ('{CooperationRequestStatus.Pending}', '{CooperationRequestStatus.Accepted}')")
            .HasDatabaseName("ix_cooperation_requests_open_pair");

        b.HasIndex(x => new { x.TherapistId, x.Status });
        b.HasIndex(x => new { x.PatientUserId, x.CreatedAt });

        b.HasQueryFilter(x => !x.Therapist.IsDeleted);
    }
}

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> b)
    {
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();

        // Jedna karta danego konta u jednego terapeuty.
        b.HasIndex(x => new { x.TherapistId, x.UserId })
            .IsUnique()
            .HasFilter("user_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ix_patients_therapist_account");
    }
}
