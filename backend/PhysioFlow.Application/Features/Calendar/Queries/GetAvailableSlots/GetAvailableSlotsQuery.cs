using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar.Queries.GetAvailableSlots;

public record GetAvailableSlotsQuery(DateOnly? Date, int? DurationMinutes) : IRequest<AvailableSlotsDto>;

public class GetAvailableSlotsQueryValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    public GetAvailableSlotsQueryValidator()
    {
        RuleFor(x => x.Date)
            .NotNull().WithMessage("Podaj datę.")
            .OverridePropertyName("date");

        RuleFor(x => x.DurationMinutes)
            .NotNull().WithMessage("Podaj długość wizyty.")
            .Must(v => v is null || CalendarDurations.Allowed.Contains(v.Value))
            .WithMessage("Dopuszczalne długości to wyłącznie 30, 45 i 60 minut.")
            .OverridePropertyName("durationMinutes");
    }
}

public class GetAvailableSlotsQueryHandler : IRequestHandler<GetAvailableSlotsQuery, AvailableSlotsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetAvailableSlotsQueryHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<AvailableSlotsDto> Handle(GetAvailableSlotsQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var date = request.Date!.Value;
        var duration = request.DurationMinutes!.Value;

        var settings = await CalendarAccess.GetOrCreateSettingsAsync(_db, therapist.Id, ct);
        var allowed = CalendarDurations.Parse(settings.AllowedDurations);
        if (!allowed.Contains(duration))
            throw CalendarValidation.Field("durationMinutes", "Ta długość wizyty nie jest włączona w Twoich ustawieniach kalendarza.");

        var slots = await _db.AvailabilitySlots.Where(s => s.TherapistId == therapist.Id).ToListAsync(ct);
        var intervals = CalendarSchedule.IntervalsFor(slots, date);
        var (dayStart, dayEnd) = WarsawTime.DayRangeUtc(date);

        var occupiedRows = await _db.Appointments
            .Where(a => a.TherapistId == therapist.Id
                        && a.Status == AppointmentStatus.Scheduled
                        && a.StartsAt < dayEnd
                        && a.EndsAt > dayStart)
            .Select(a => new { a.StartsAt, a.EndsAt, a.BufferMinutes })
            .ToListAsync(ct);

        var occupied = occupiedRows
            .Select(a => (a.StartsAt, a.EndsAt, a.BufferMinutes))
            .ToList();

        var generated = CalendarSchedule.GenerateSlots(
            date, duration, settings.BufferMinutes, intervals, occupied, _clock.UtcNow);

        return new AvailableSlotsDto(date, duration, generated);
    }
}
