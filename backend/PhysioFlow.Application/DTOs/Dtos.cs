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
