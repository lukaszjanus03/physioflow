using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.DTOs;

public record AppointmentTherapistDto(
    Guid Id, string FirstName, string LastName, string? PracticeName, string? City, string? PhoneNumber);

// Celowo bez patientId, powodu wizyty i wewnętrznych notatek terapeuty.
public record PatientAppointmentDto(
    Guid Id, AppointmentTherapistDto Therapist, DateTime StartsAt, DateTime EndsAt,
    int DurationMinutes, AppointmentStatus Status, bool CanCancel, DateTime CancellationDeadline);

public record PatientBookingSettingsDto(Guid TherapistId, string TimeZone, IReadOnlyList<int> AllowedDurations);
public record PatientAppointmentSummaryDto(PatientAppointmentDto? NextAppointment);
public record TherapistAppointmentSummaryDto(IReadOnlyList<AppointmentDto> UpcomingAppointments);
