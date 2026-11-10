using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar;

public static class CalendarErrorCodes
{
    public const string SlotUnavailable = "slot_unavailable";
    public const string SlotConflict = "slot_conflict";
    public const string AppointmentInPast = "appointment_in_past";
    public const string InvalidStatusTransition = "invalid_status_transition";
    public const string AppointmentNotEditable = "appointment_not_editable";
    public const string CardArchived = "card_archived";
    public const string CancellationTooLate = "cancellation_too_late";
}

/// <summary>
/// Godziny pracy i wyjątki są w strefie Europe/Warsaw. Wizyty w API i bazie są w UTC (timestamptz).
/// Wolne terminy startują od początku przedziału i dalej co wybraną długość wizyty.
/// </summary>
public static class WarsawTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }

    public static bool TryToUtc(DateOnly date, TimeOnly time, out DateTime utc)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        if (Zone.IsAmbiguousTime(local))
        {
            var offsets = Zone.GetAmbiguousTimeOffsets(local);
            var dstOffset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
            utc = DateTime.SpecifyKind(local - dstOffset, DateTimeKind.Utc);
            return true;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(local, Zone);
        return true;
    }

    public static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly date)
    {
        if (!TryToUtc(date, TimeOnly.MinValue, out var start))
            start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
        var next = date.AddDays(1);
        if (!TryToUtc(next, TimeOnly.MinValue, out var end))
            end = TimeZoneInfo.ConvertTimeToUtc(next.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
        return (start, end);
    }

    public static DateTime RequireUtc(DateTime value, string propertyName)
    {
        if (value.Kind == DateTimeKind.Utc)
            return value;
        if (value.Kind == DateTimeKind.Local)
            return value.ToUniversalTime();
        throw CalendarValidation.Field(propertyName, "Podaj czas w UTC (z końcówką Z albo przesunięciem).");
    }
}

internal static class CalendarValidation
{
    public static ValidationException Field(string property, string message) =>
        new(new[] { new ValidationFailure(property, message) { FormattedMessagePlaceholderValues = new Dictionary<string, object> { ["PropertyName"] = property } } });
}

public static class CalendarDurations
{
    public static readonly int[] Allowed = [30, 45, 60];

    public static IReadOnlyList<int> Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<int>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var n) ? n : 0)
            .Where(n => Allowed.Contains(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    public static string Format(IEnumerable<int> values) =>
        string.Join(',', values.Distinct().OrderBy(n => n));

    public static AppointmentDuration ToEnum(int minutes) => minutes switch
    {
        30 => AppointmentDuration.Thirty,
        45 => AppointmentDuration.FortyFive,
        60 => AppointmentDuration.Sixty,
        _ => throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Nieobsługiwana długość wizyty."),
    };
}

public static class CalendarSchedule
{
    public static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static IReadOnlyList<TimeIntervalDto> SortAndRequireNoOverlap(
        IReadOnlyList<TimeIntervalDto>? intervals, string field)
    {
        var list = (intervals ?? []).ToList();
        if (list.Count > TherapistCalendarSettings.MaxIntervalsPerDay)
            throw CalendarValidation.Field(field, $"Jeden dzień może mieć maksymalnie {TherapistCalendarSettings.MaxIntervalsPerDay} przedziałów.");

        foreach (var interval in list)
        {
            if (interval.End <= interval.Start)
                throw CalendarValidation.Field(field, "Godzina zakończenia przedziału musi być późniejsza niż godzina rozpoczęcia.");
        }

        list.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].Start < list[i - 1].End)
                throw CalendarValidation.Field(field, "Przedziały godzinowe w jednym dniu nie mogą się nakładać.");
        }

        return list;
    }

    public static IReadOnlyList<TimeIntervalDto> IntervalsFor(
        IReadOnlyList<AvailabilitySlot> slots, DateOnly date)
    {
        var exceptions = slots.Where(s => s.SpecificDate == date).ToList();
        if (exceptions.Count > 0)
        {
            if (exceptions.Any(s => s.IsBlocked))
                return [];
            return exceptions
                .Select(s => new TimeIntervalDto(s.StartTime, s.EndTime))
                .OrderBy(i => i.Start)
                .ToList();
        }

        var weekday = date.DayOfWeek;
        return slots
            .Where(s => s.SpecificDate is null && s.DayOfWeek == weekday && !s.IsBlocked)
            .Select(s => new TimeIntervalDto(s.StartTime, s.EndTime))
            .OrderBy(i => i.Start)
            .ToList();
    }

    public static IReadOnlyList<(DateTime Start, DateTime End)> ToUtcWindows(
        DateOnly date, IReadOnlyList<TimeIntervalDto> intervals)
    {
        var windows = new List<(DateTime Start, DateTime End)>();
        foreach (var interval in intervals)
        {
            if (!WarsawTime.TryToUtc(date, interval.Start, out var startUtc))
                continue;
            if (!WarsawTime.TryToUtc(date, interval.End, out var endUtc) || endUtc <= startUtc)
                continue;
            windows.Add((startUtc, endUtc));
        }

        return windows;
    }

    /// <summary>
    /// Wolne terminy: start od początku przedziału UTC, krok równy długości wizyty.
    /// Wizyta wraz z buforem musi zmieścić się w przedziale. Zajętość to [startsAt, endsAt + buffer).
    /// </summary>
    public static IReadOnlyList<AvailableSlotDto> GenerateSlots(
        DateOnly date,
        int durationMinutes,
        int bufferMinutes,
        IReadOnlyList<TimeIntervalDto> intervals,
        IReadOnlyList<(DateTime Start, DateTime End, int BufferMinutes)> occupied,
        DateTime utcNow)
    {
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var buffer = TimeSpan.FromMinutes(bufferMinutes);
        var slots = new List<AvailableSlotDto>();

        foreach (var (windowStart, windowEnd) in ToUtcWindows(date, intervals))
        {
            for (var start = windowStart; start + duration + buffer <= windowEnd; start += duration)
            {
                if (start <= utcNow)
                    continue;
                var end = start + duration;
                if (Overlaps(start, end, bufferMinutes, occupied))
                    continue;
                slots.Add(new AvailableSlotDto(start, end));
            }
        }

        return slots;
    }

    public static bool FitsAvailability(
        DateTime startUtc,
        int durationMinutes,
        int bufferMinutes,
        IReadOnlyList<TimeIntervalDto> intervals,
        DateOnly date)
    {
        var endWithBuffer = startUtc + TimeSpan.FromMinutes(durationMinutes + bufferMinutes);
        return ToUtcWindows(date, intervals)
            .Any(window => startUtc >= window.Start && endWithBuffer <= window.End);
    }

    public static bool Overlaps(
        DateTime start,
        DateTime end,
        int bufferMinutes,
        IReadOnlyList<(DateTime Start, DateTime End, int BufferMinutes)> occupied)
    {
        var until = end + TimeSpan.FromMinutes(bufferMinutes);
        foreach (var other in occupied)
        {
            var otherUntil = other.End + TimeSpan.FromMinutes(other.BufferMinutes);
            if (start < otherUntil && other.Start < until)
                return true;
        }

        return false;
    }
}

internal static class CalendarMapping
{
    public static AppointmentDto ToDto(this Appointment appointment) => new(
        appointment.Id,
        appointment.PatientId,
        $"{appointment.Patient.FirstName} {appointment.Patient.LastName}".Trim(),
        DateTime.SpecifyKind(appointment.StartsAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(appointment.EndsAt, DateTimeKind.Utc),
        (int)appointment.Duration,
        appointment.Status,
        appointment.Reason,
        appointment.Notes);
}

internal static class CalendarAccess
{
    public static async Task<TherapistCalendarSettings> GetOrCreateSettingsAsync(
        IApplicationDbContext db, Guid therapistId, CancellationToken ct)
    {
        var settings = await db.CalendarSettings.FirstOrDefaultAsync(s => s.TherapistId == therapistId, ct);
        if (settings is not null)
            return settings;

        settings = new TherapistCalendarSettings { TherapistId = therapistId };
        db.CalendarSettings.Add(settings);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.CalendarSettings.Remove(settings);
            settings = await db.CalendarSettings.FirstAsync(s => s.TherapistId == therapistId, ct);
        }

        return settings;
    }
}
