using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

public class Appointment : SoftDeletableEntity
{
    public Guid TherapistId { get; set; }
    public Guid PatientId { get; set; }

    public DateTime StartsAt { get; set; }     // UTC
    public DateTime EndsAt { get; set; }       // UTC
    public AppointmentDuration Duration { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    public string? Reason { get; set; }        // powód wizyty
    public string? Notes { get; set; }         // notatka administracyjna (nie EDM)
    public bool ReminderSent { get; set; }

    public TherapistProfile Therapist { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
    public MedicalRecord? MedicalRecord { get; set; }
}

/// <summary>
/// Slot dostępności terapeuty - powtarzalny wg dnia tygodnia
/// albo jednorazowy override na konkretną datę.
/// </summary>
public class AvailabilitySlot : BaseEntity
{
    public Guid TherapistId { get; set; }

    public DayOfWeek? DayOfWeek { get; set; }    // null = override jednorazowy
    public DateOnly? SpecificDate { get; set; }   // null = rekurencyjny

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BufferMinutes { get; set; } = 0;   // bufor między wizytami
    public bool IsBlocked { get; set; }           // true = przerwa/blok

    public TherapistProfile Therapist { get; set; } = null!;
}
