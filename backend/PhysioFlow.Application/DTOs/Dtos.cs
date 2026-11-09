using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.DTOs;

public record PatientListItemDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    bool IsArchived,
    DateTime CreatedAt);

public record PatientDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Pesel,
    DateOnly? DateOfBirth,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? MedicalHistory,
    string? Medications,
    string? GeneralNotes,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Własny profil zawodowy terapeuty, także przed publikacją.</summary>
public record TherapistOwnProfileDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? City,
    string? PracticeName,
    string? Bio,
    bool IsPublished,
    IReadOnlyList<string> MissingFieldsForPublication);

/// <summary>Publiczne dane opublikowanego profilu w katalogu. Bez e-maila konta i danych pacjentów.</summary>
public record TherapistCatalogEntryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string City,
    string PracticeName,
    string? Bio);

/// <summary>Publiczne dane terapeuty widoczne dla pacjenta w prośbach i współpracach.</summary>
public record CooperationTherapistDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? City,
    string? PracticeName);

public record PatientCooperationRequestDto(
    Guid Id,
    CooperationRequestStatus Status,
    string? Message,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    CooperationTherapistDto Therapist);

/// <summary>Aktywna współpraca widziana przez pacjenta. Bez danych karty pacjenta.</summary>
public record PatientCooperationDto(
    Guid RequestId,
    DateTime Since,
    CooperationTherapistDto Therapist);

/// <summary>Dane konta pacjenta podane przy wysyłce prośby; służą terapeucie do weryfikacji tożsamości.</summary>
public record RequestingPatientDto(
    string? FirstName,
    string? LastName,
    string Email);

public record TherapistCooperationRequestDto(
    Guid Id,
    CooperationRequestStatus Status,
    string? Message,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    RequestingPatientDto Patient,
    CardLinkMode? CardLinkMode,
    Guid? LinkedPatientId);

public record AppointmentDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    DateTime StartsAt,
    DateTime EndsAt,
    int DurationMinutes,
    string Status,
    string? Reason,
    string? Notes);

public record BodyMapDto(
    Guid Id,
    Guid PatientId,
    Guid? AppointmentId,
    DateTime RecordedAt,
    string? Notes,
    IReadOnlyList<PainPointDto> PainPoints);

public record BodyMapSummaryDto(
    Guid Id,
    Guid PatientId,
    Guid? AppointmentId,
    DateTime RecordedAt,
    string? Notes,
    string CreatedByRole,       // "Therapist" | "Patient"
    int PainPointsCount);

public record PainPointDto(
    Guid Id,
    string Side,
    decimal X,
    decimal Y,
    string Severity,
    string? Note,
    int? PainScale);
