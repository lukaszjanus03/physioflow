using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar.Commands.CreateAppointment;

public record CreateAppointmentCommand(
    Guid? PatientId,
    DateTime? StartsAt,
    int? DurationMinutes,
    string? Reason,
    string? Notes
) : IRequest<AppointmentDto>;

public class CreateAppointmentCommandValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentCommandValidator()
    {
        RuleFor(x => x.PatientId)
            .NotNull().WithMessage("Wskaż kartę pacjenta.")
            .OverridePropertyName("patientId");

        RuleFor(x => x.StartsAt)
            .NotNull().WithMessage("Podaj początek wizyty.")
            .OverridePropertyName("startsAt");

        RuleFor(x => x.DurationMinutes)
            .NotNull().WithMessage("Podaj długość wizyty.")
            .Must(v => v is null || CalendarDurations.Allowed.Contains(v.Value))
            .WithMessage("Dopuszczalne długości to wyłącznie 30, 45 i 60 minut.")
            .OverridePropertyName("durationMinutes");

        RuleFor(x => x.Reason)
            .MaximumLength(TherapistCalendarSettings.ReasonMaxLength)
            .OverridePropertyName("reason");

        RuleFor(x => x.Notes)
            .MaximumLength(TherapistCalendarSettings.NotesMaxLength)
            .OverridePropertyName("notes");
    }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, AppointmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CreateAppointmentCommandHandler> _logger;

    public CreateAppointmentCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<CreateAppointmentCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AppointmentDto> Handle(CreateAppointmentCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var startsAt = WarsawTime.RequireUtc(request.StartsAt!.Value, "startsAt");
        var duration = request.DurationMinutes!.Value;
        var now = _clock.UtcNow;

        if (startsAt <= now)
            throw new ConflictException(CalendarErrorCodes.AppointmentInPast, "Nie można utworzyć wizyty w przeszłości.");

        var patient = await AppointmentScheduling.RequireActiveCardAsync(_db, therapist.Id, request.PatientId!.Value, ct);

        var strategy = _db.Database.CreateExecutionStrategy();
        var created = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var appointment = await AppointmentScheduling.PlaceAsync(
                _db, therapist.Id, patient, startsAt, duration, request.Reason, request.Notes, exceptId: null, now, ct);
            await transaction.CommitAsync(ct);
            return appointment;
        });

        created.Patient = patient;
        _logger.LogInformation(
            "Appointment {AppointmentId} scheduled by therapist {TherapistId} for card {PatientId} at {StartsAt:o}",
            created.Id, therapist.Id, patient.Id, created.StartsAt);
        return created.ToDto();
    }
}

internal static class AppointmentScheduling
{
    public static async Task<Patient> RequireActiveCardAsync(
        IApplicationDbContext db, Guid therapistId, Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == patientId && p.TherapistId == therapistId && !p.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Patient), patientId);

        if (patient.IsArchived)
            throw new ConflictException(CalendarErrorCodes.CardArchived,
                "Nie można umówić wizyty na kartę w archiwum.");

        return patient;
    }

    public static async Task<Appointment> PlaceAsync(
        IApplicationDbContext db,
        Guid therapistId,
        Patient patient,
        DateTime startsAt,
        int durationMinutes,
        string? reason,
        string? notes,
        Guid? exceptId,
        DateTime now,
        CancellationToken ct,
        string? patientMessage = null)
    {
        var settings = await CalendarAccess.GetOrCreateSettingsAsync(db, therapistId, ct);
        var allowed = CalendarDurations.Parse(settings.AllowedDurations);
        if (!allowed.Contains(durationMinutes))
            throw CalendarValidation.Field("durationMinutes", "Ta długość wizyty nie jest włączona w Twoich ustawieniach kalendarza.");

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startsAt, DateTimeKind.Utc), WarsawTime.Zone);
        var date = DateOnly.FromDateTime(local);
        var slots = await db.AvailabilitySlots.Where(s => s.TherapistId == therapistId).ToListAsync(ct);
        var intervals = CalendarSchedule.IntervalsFor(slots, date);
        var buffer = settings.BufferMinutes;
        var endsAt = startsAt.AddMinutes(durationMinutes);

        if (!CalendarSchedule.FitsAvailability(startsAt, durationMinutes, buffer, intervals, date))
            throw new ConflictException(CalendarErrorCodes.SlotUnavailable,
                "Ten termin nie mieści się w Twojej dostępności (wliczając bufor po wizycie).");

        var occupiedRows = await db.Appointments
            .Where(a => a.TherapistId == therapistId
                        && a.Status == AppointmentStatus.Scheduled
                        && (exceptId == null || a.Id != exceptId)
                        && a.StartsAt < endsAt.AddMinutes(buffer + 60)
                        && a.EndsAt > startsAt.AddMinutes(-180))
            .Select(a => new { a.StartsAt, a.EndsAt, a.BufferMinutes })
            .ToListAsync(ct);

        var occupied = occupiedRows.Select(a => (a.StartsAt, a.EndsAt, a.BufferMinutes)).ToList();
        if (CalendarSchedule.Overlaps(startsAt, endsAt, buffer, occupied))
            throw new ConflictException(CalendarErrorCodes.SlotConflict,
                "Ten termin koliduje z inną zaplanowaną wizytą.");

        settings.ConcurrencyStamp = Guid.NewGuid();
        patient.ConcurrencyStamp = Guid.NewGuid();

        Appointment appointment;
        if (exceptId is { } id)
        {
            appointment = await db.Appointments.FirstAsync(a => a.Id == id, ct);
            if (appointment.Status != AppointmentStatus.Scheduled)
                throw new ConflictException(CalendarErrorCodes.AppointmentNotEditable,
                    "Można edytować tylko przyszłą zaplanowaną wizytę.");
            if (appointment.StartsAt != startsAt || appointment.PatientId != patient.Id)
            {
                appointment.ReminderScheduleId = Guid.NewGuid();
                appointment.ReminderSent = false;
            }
            if (appointment.PatientId != patient.Id)
                appointment.PatientUserId = patient.UserId;
            appointment.PatientId = patient.Id;
            appointment.StartsAt = startsAt;
            appointment.EndsAt = endsAt;
            appointment.Duration = CalendarDurations.ToEnum(durationMinutes);
            appointment.BufferMinutes = buffer;
            appointment.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            appointment.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            appointment.ConcurrencyStamp = Guid.NewGuid();
        }
        else
        {
            appointment = new Appointment
            {
                TherapistId = therapistId,
                PatientId = patient.Id,
                PatientUserId = patient.UserId,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Duration = CalendarDurations.ToEnum(durationMinutes),
                BufferMinutes = buffer,
                Status = AppointmentStatus.Scheduled,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                PatientMessage = string.IsNullOrWhiteSpace(patientMessage) ? null : patientMessage.Trim(),
            };
            db.Appointments.Add(appointment);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is CooperationRequest || e.Entity is Patient))
        {
            throw new ConflictException(CooperationErrorCodes.CooperationNotActive,
                "Współpraca lub karta zmieniła się w międzyczasie. Odśwież widok.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(CalendarErrorCodes.SlotConflict,
                "Ten termin został w międzyczasie zajęty. Odśwież wolne godziny i wybierz inny.");
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(CalendarErrorCodes.SlotConflict,
                "Ten termin koliduje z inną zaplanowaną wizytą.");
        }

        return appointment;
    }
}
