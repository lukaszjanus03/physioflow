using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Calendar.Queries.GetMyCalendarSettings;

public record GetMyCalendarSettingsQuery : IRequest<TherapistCalendarSettingsDto>;

public class GetMyCalendarSettingsQueryHandler
    : IRequestHandler<GetMyCalendarSettingsQuery, TherapistCalendarSettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMyCalendarSettingsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<TherapistCalendarSettingsDto> Handle(GetMyCalendarSettingsQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var settings = await CalendarAccess.GetOrCreateSettingsAsync(_db, therapist.Id, ct);
        var slots = await _db.AvailabilitySlots.Where(s => s.TherapistId == therapist.Id).ToListAsync(ct);
        return CalendarSettingsMapper.ToDto(settings, slots);
    }
}

internal static class CalendarSettingsMapper
{
    public static TherapistCalendarSettingsDto ToDto(
        TherapistCalendarSettings settings, IReadOnlyList<AvailabilitySlot> slots)
    {
        var weekly = CalendarSchedule.WeekOrder.Select(day => new WeeklyHoursDto(
            day,
            slots.Where(s => s.SpecificDate is null && s.DayOfWeek == day && !s.IsBlocked)
                .OrderBy(s => s.StartTime)
                .Select(s => new TimeIntervalDto(s.StartTime, s.EndTime))
                .ToList()))
            .ToList();

        var exceptions = slots
            .Where(s => s.SpecificDate is not null)
            .GroupBy(s => s.SpecificDate!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayOff = g.Any(s => s.IsBlocked);
                return new CalendarExceptionDto(
                    g.Key,
                    dayOff,
                    dayOff
                        ? []
                        : g.OrderBy(s => s.StartTime).Select(s => new TimeIntervalDto(s.StartTime, s.EndTime)).ToList());
            })
            .ToList();

        return new TherapistCalendarSettingsDto(
            settings.BufferMinutes,
            CalendarDurations.Parse(settings.AllowedDurations),
            weekly,
            exceptions);
    }
}
