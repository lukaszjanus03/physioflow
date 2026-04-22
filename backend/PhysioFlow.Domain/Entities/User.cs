using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Aplikacyjny user rozszerza ASP.NET Identity IdentityUser w warstwie Infrastructure.
/// Tu mamy tylko profil domenowy.
/// </summary>
public class TherapistProfile : SoftDeletableEntity
{
    public Guid UserId { get; set; }        // FK do AspNetUsers
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? LicenseNumber { get; set; }
    public string? Specialization { get; set; }
    public string? PhoneNumber { get; set; }

    // Nawigacje
    public ICollection<Patient> Patients { get; set; } = new List<Patient>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<Exercise> Exercises { get; set; } = new List<Exercise>();
    public ICollection<AvailabilitySlot> AvailabilitySlots { get; set; } = new List<AvailabilitySlot>();
}

public class Patient : SoftDeletableEntity
{
    public Guid? UserId { get; set; }       // opcjonalny - pacjent może nie mieć konta w portalu
    public Guid TherapistId { get; set; }

    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Pesel { get; set; }      // 11 znaków, walidacja w aplikacji
    public DateOnly? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }

    public string? MedicalHistory { get; set; }   // historia schorzeń
    public string? Medications { get; set; }      // aktualne leki
    public string? GeneralNotes { get; set; }     // notatki ogólne
    public bool IsArchived { get; set; }

    // Nawigacje
    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<BodyMap> BodyMaps { get; set; } = new List<BodyMap>();
    public ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();
    public ICollection<TherapyPlan> TherapyPlans { get; set; } = new List<TherapyPlan>();
    public ICollection<PatientDiaryEntry> DiaryEntries { get; set; } = new List<PatientDiaryEntry>();
}
