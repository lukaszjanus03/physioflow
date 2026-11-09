using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar.Commands.CreateAppointment;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar.Commands.UpdateAppointment;

public record UpdateAppointmentCommand(
    Guid Id,
    Guid? PatientId,
    DateTime? StartsAt,
    int? DurationMinutes,
    string? Reason,
    string? Notes
) : IRequest<AppointmentDto>;

public class UpdateAppointmentCommandValidator : AbstractValidator<UpdateAppointmentCommand>
{
    public UpdateAppointmentCommandValidator()
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

public class UpdateAppointmentCommandHandler : IRequestHandler<UpdateAppointmentCommand, AppointmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UpdateAppointmentCommandHandler> _logger;

    public UpdateAppointmentCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<UpdateAppointmentCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AppointmentDto> Handle(UpdateAppointmentCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var startsAt = WarsawTime.RequireUtc(request.StartsAt!.Value, "startsAt");
        var now = _clock.UtcNow;

        if (startsAt <= now)
            throw new ConflictException(CalendarErrorCodes.AppointmentInPast, "Nie można przenieść wizyty w przeszłość.");

        var existing = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.TherapistId == therapist.Id, ct)
            ?? throw new NotFoundException(nameof(Appointment), request.Id);

        if (existing.Status != AppointmentStatus.Scheduled || existing.StartsAt <= now)
            throw new ConflictException(CalendarErrorCodes.AppointmentNotEditable,
                "Można edytować tylko przyszłą zaplanowaną wizytę.");

        var patient = await AppointmentScheduling.RequireActiveCardAsync(_db, therapist.Id, request.PatientId!.Value, ct);

        var strategy = _db.Database.CreateExecutionStrategy();
        var updated = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var appointment = await AppointmentScheduling.PlaceAsync(
                _db, therapist.Id, patient, startsAt, request.DurationMinutes!.Value,
                request.Reason, request.Notes, exceptId: existing.Id, now, ct);
            await transaction.CommitAsync(ct);
            return appointment;
        });

        updated.Patient = patient;
        _logger.LogInformation(
            "Appointment {AppointmentId} rescheduled by therapist {TherapistId} for card {PatientId}",
            updated.Id, therapist.Id, patient.Id);
        return updated.ToDto();
    }
}
