using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.DTOs;

public record PatientListItemDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    bool IsArchived,
    DateTime CreatedAt,
    bool IsLinkedToAccount);

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
    DateTime? UpdatedAt,
    bool IsLinkedToAccount);

/// <summary>Własny profil zawodowy terapeuty, także przed publikacją.</summary>
public record TherapistOwnProfileDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? City,
    string? PracticeName,
    string? Bio,
    bool IsPublished,
    IReadOnlyList<string> MissingFieldsForPublication,
    TherapistMarketplaceDto? Marketplace = null,
    string? PhotoUrl = null);

/// <summary>Publiczne dane opublikowanego profilu w katalogu. Bez e-maila konta i danych pacjentów.</summary>
public record TherapistCatalogEntryDto(
    Guid Id,
    string FirstName,
    string LastName,
    string City,
    string PracticeName,
    string? Bio,
    TherapistMarketplaceDto? Marketplace = null,
    string? PhotoUrl = null);

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
    CooperationTherapistDto Therapist,
    DateTime? EndedAt,
    CooperationParty? EndedBy);

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
    Guid? LinkedPatientId,
    DateTime? EndedAt,
    CooperationParty? EndedBy);

public record TimeIntervalDto(TimeOnly Start, TimeOnly End);

public record WeeklyHoursDto(DayOfWeek DayOfWeek, IReadOnlyList<TimeIntervalDto> Intervals);

public record CalendarExceptionDto(
    DateOnly Date,
    bool IsDayOff,
    IReadOnlyList<TimeIntervalDto>? Intervals);

public record TherapistCalendarSettingsDto(
    int BufferMinutes,
    IReadOnlyList<int> AllowedDurations,
    IReadOnlyList<WeeklyHoursDto> WeeklyHours,
    IReadOnlyList<CalendarExceptionDto> Exceptions);

public record AvailableSlotDto(DateTime StartsAt, DateTime EndsAt);

public record AvailableSlotsDto(DateOnly Date, int DurationMinutes, IReadOnlyList<AvailableSlotDto> Slots);

public record AppointmentDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    DateTime StartsAt,
    DateTime EndsAt,
    int DurationMinutes,
    AppointmentStatus Status,
    string? Reason,
    string? Notes,
    string? PatientMessage = null);

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
