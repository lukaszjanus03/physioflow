using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Application.Features.Therapy;

namespace PhysioFlow.Application.Features.Analytics;

internal record AnalyticsDateRange(DateOnly From, DateOnly To)
{
    public const string TimeZone = "Europe/Warsaw";
    public const int MaxDays = 366;

    public static AnalyticsDateRange Resolve(DateOnly? from, DateOnly? to, IDateTimeProvider clock)
    {
        TherapyValidation.Require(from.HasValue == to.HasValue, "from",
            "Provide both from and to, or omit both for the current week in Europe/Warsaw.");
        if (!from.HasValue)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, WarsawTime.Zone));
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            from = monday;
            to = monday.AddDays(6);
        }

        var start = from.Value;
        var end = to!.Value;
        TherapyValidation.Require(start >= new DateOnly(1900, 1, 1), "from", "Earliest date is 1900-01-01.");
        TherapyValidation.Require(end >= new DateOnly(1900, 1, 1) && end < DateOnly.MaxValue,
            "to", "End date must be between 1900-01-01 and 9999-12-30.");
        TherapyValidation.Require(end >= start, "to", "End date must be on or after start date.");
        TherapyValidation.Require(end.DayNumber - start.DayNumber < MaxDays, "to",
            $"The range must not exceed {MaxDays} days, including both endpoints.");
        return new(start, end);
    }
}
