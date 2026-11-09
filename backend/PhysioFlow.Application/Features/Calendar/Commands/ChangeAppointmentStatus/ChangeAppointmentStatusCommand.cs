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

namespace PhysioFlow.Application.Features.Calendar.Commands.ChangeAppointmentStatus;

public record ChangeAppointmentStatusCommand(Guid Id, AppointmentStatus Status) : IRequest<AppointmentDto>;

public class ChangeAppointmentStatusCommandValidator : AbstractValidator<ChangeAppointmentStatusCommand>
{
    public ChangeAppointmentStatusCommandValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => s is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow)
            .WithMessage("Dozwolone statusy to Cancelled, Completed i NoShow.")
            .OverridePropertyName("status");
    }
}

public class ChangeAppointmentStatusCommandHandler
    : IRequestHandler<ChangeAppointmentStatusCommand, AppointmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ChangeAppointmentStatusCommandHandler> _logger;

    public ChangeAppointmentStatusCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<ChangeAppointmentStatusCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<AppointmentDto> Handle(ChangeAppointmentStatusCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.TherapistId == therapist.Id, ct)
            ?? throw new NotFoundException(nameof(Appointment), request.Id);

        if (appointment.Status != AppointmentStatus.Scheduled)
            throw new ConflictException(CalendarErrorCodes.InvalidStatusTransition,
                "Można zmienić status tylko zaplanowanej wizyty.");

        var now = _clock.UtcNow;
        if (request.Status == AppointmentStatus.Cancelled)
        {
            if (appointment.StartsAt <= now)
                throw new ConflictException(CalendarErrorCodes.InvalidStatusTransition,
                    "Można odwołać tylko przyszłą wizytę.");
        }
        else if (appointment.StartsAt > now)
        {
            throw new ConflictException(CalendarErrorCodes.InvalidStatusTransition,
                "Nie można oznaczyć przyszłej wizyty jako odbytej albo nieobecności.");
        }

        var settings = await CalendarAccess.GetOrCreateSettingsAsync(_db, therapist.Id, ct);
        var from = appointment.Status;
        appointment.Status = request.Status;
        appointment.ConcurrencyStamp = Guid.NewGuid();
        settings.ConcurrencyStamp = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(CalendarErrorCodes.InvalidStatusTransition,
                "Status wizyty zmienił się w międzyczasie. Odśwież widok.");
        }

        _logger.LogInformation(
            "Appointment {AppointmentId} status {From} -> {To} by therapist {TherapistId}",
            appointment.Id, from, appointment.Status, therapist.Id);

        return appointment.ToDto();
    }
}
