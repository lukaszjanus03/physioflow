using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Aplikacyjny user rozszerza ASP.NET Identity IdentityUser w warstwie Infrastructure.
/// Tu mamy tylko profil domenowy.
/// </summary>
public class TherapistProfile : SoftDeletableEntity
{
    public const int NameMaxLength = 100;
    public const int CityMaxLength = 100;
    public const int PracticeNameMaxLength = 150;
    public const int BioMaxLength = 500;
    public const int DescriptionMaxLength = 2000;
    public const int StreetMaxLength = 150;
    public const int BuildingNumberMaxLength = 20;
    public const int PostalCodeMaxLength = 6;
    public const int PublicPhoneMaxLength = 30;
    public const int PublicEmailMaxLength = 254;

    // Nazwisko wpisywane przy automatycznym tworzeniu brakujących profili; nie nadaje się do katalogu.
    public const string PlaceholderLastName = "(uzupełnij)";

    public Guid UserId { get; set; }        // FK do AspNetUsers
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? LicenseNumber { get; set; }
    public string? Specialization { get; set; }
    public string? PhoneNumber { get; set; }

    // Dane katalogu terapeutów
    public string? City { get; set; }
    public string? PracticeName { get; set; }
    public string? Bio { get; set; }
    public bool IsPublished { get; set; }

    // Marketplace fields are independent of legacy Specialization/PhoneNumber and Identity contact data.
    public string[] Specializations { get; set; } = [];
    public string? Experience { get; set; }
    public string? Education { get; set; }
    public string? Street { get; set; }
    public string? BuildingNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? PublicPhoneNumber { get; set; }
    public string? PublicEmail { get; set; }
    public string? OrganizationalInfo { get; set; }
    public string? FirstVisitPreparation { get; set; }
    public string? PhotoStorageKey { get; set; }

    /// <summary>Pola wymagane do publikacji w katalogu, których brakuje (nazwy jak w API).</summary>
    public IReadOnlyList<string> MissingCatalogFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(FirstName)) missing.Add("firstName");
        if (string.IsNullOrWhiteSpace(LastName) || LastName == PlaceholderLastName) missing.Add("lastName");
        if (string.IsNullOrWhiteSpace(City)) missing.Add("city");
        if (string.IsNullOrWhiteSpace(PracticeName)) missing.Add("practiceName");
        return missing;
    }

    // Nawigacje
    public ICollection<Patient> Patients { get; set; } = new List<Patient>();
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<TherapistReview> Reviews { get; set; } = new List<TherapistReview>();
    public ICollection<Exercise> Exercises { get; set; } = new List<Exercise>();
    public ICollection<AvailabilitySlot> AvailabilitySlots { get; set; } = new List<AvailabilitySlot>();
    public TherapistCalendarSettings? CalendarSettings { get; set; }
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

    // Powiązanie z kontem (UserId) nie udostępnia dokumentacji; portal pacjenta wymaga tej zgody osobno.
    public bool DocumentationSharedWithAccount { get; set; }

    // Chroni przed równoczesnym powiązaniem jednej karty z dwoma kontami.
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    // Nawigacje
    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<BodyMap> BodyMaps { get; set; } = new List<BodyMap>();
    public ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();
    public ICollection<TherapyPlan> TherapyPlans { get; set; } = new List<TherapyPlan>();
    public ICollection<PatientDiaryEntry> DiaryEntries { get; set; } = new List<PatientDiaryEntry>();
}
