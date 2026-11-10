using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar;

internal static class CalendarAvailability
{
    public static async Task<AvailableSlotsDto> GetAsync(IApplicationDbContext db, Guid therapistId,
        DateOnly date, int duration, DateTime now, CancellationToken ct)
    {
        var settings = await CalendarAccess.GetOrCreateSettingsAsync(db, therapistId, ct);
        if (!CalendarDurations.Parse(settings.AllowedDurations).Contains(duration))
            throw CalendarValidation.Field("durationMinutes", "Terapeuta nie udostępnia tej długości wizyty.");
        var availability = await db.AvailabilitySlots.Where(s => s.TherapistId == therapistId).ToListAsync(ct);
        var (start, end) = WarsawTime.DayRangeUtc(date);
        var bufferStart = start.AddMinutes(-60);
        var occupied = await db.Appointments.Where(a => a.TherapistId == therapistId
                && a.Status == AppointmentStatus.Scheduled && a.StartsAt < end && a.EndsAt > bufferStart)
            .Select(a => new { a.StartsAt, a.EndsAt, a.BufferMinutes }).ToListAsync(ct);
        return new(date, duration, CalendarSchedule.GenerateSlots(date, duration, settings.BufferMinutes,
            CalendarSchedule.IntervalsFor(availability, date),
            occupied.Select(a => (a.StartsAt, a.EndsAt, a.BufferMinutes)).ToList(), now));
    }
}
