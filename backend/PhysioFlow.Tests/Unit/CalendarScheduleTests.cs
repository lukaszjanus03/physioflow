using FluentAssertions;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class CalendarScheduleTests
{
    [Fact]
    public void Visit_plus_buffer_must_fit_inside_the_interval()
    {
        var date = new DateOnly(2027, 6, 14);
        var intervals = new[] { new TimeIntervalDto(new TimeOnly(8, 0), new TimeOnly(10, 0)) };
        var now = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var slots = CalendarSchedule.GenerateSlots(date, durationMinutes: 60, bufferMinutes: 15, intervals, [], now);

        slots.Should().HaveCount(1);
        WarsawTime.TryToUtc(date, new TimeOnly(8, 0), out var eight).Should().BeTrue();
        slots[0].StartsAt.Should().Be(eight);
        slots[0].EndsAt.Should().Be(eight.AddHours(1));
    }

    [Fact]
    public void Occupied_visit_blocks_its_duration_and_buffer()
    {
        var date = new DateOnly(2027, 6, 14);
        var intervals = new[] { new TimeIntervalDto(new TimeOnly(8, 0), new TimeOnly(12, 0)) };
        WarsawTime.TryToUtc(date, new TimeOnly(8, 0), out var eight).Should().BeTrue();
        var occupied = new[] { (eight, eight.AddMinutes(60), 15) };
        var now = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var slots = CalendarSchedule.GenerateSlots(date, 60, 15, intervals, occupied, now);

        // Krok = długość wizyty od 08:00. Bufor 15 min po 08:00-09:00 blokuje 09:00, następny start to 10:00.
        slots.Should().ContainSingle();
        slots[0].StartsAt.Should().Be(eight.AddHours(2));
    }

    [Fact]
    public void Warsaw_winter_and_summer_clocks_convert_to_different_utc()
    {
        var winter = new DateOnly(2027, 1, 11);
        var summer = new DateOnly(2027, 6, 14);
        WarsawTime.TryToUtc(winter, new TimeOnly(8, 0), out var winterUtc).Should().BeTrue();
        WarsawTime.TryToUtc(summer, new TimeOnly(8, 0), out var summerUtc).Should().BeTrue();

        winterUtc.Should().Be(new DateTime(2027, 1, 11, 7, 0, 0, DateTimeKind.Utc));
        summerUtc.Should().Be(new DateTime(2027, 6, 14, 6, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Spring_dst_skips_the_missing_local_hour()
    {
        var date = new DateOnly(2026, 3, 29);
        var intervals = new[] { new TimeIntervalDto(new TimeOnly(1, 0), new TimeOnly(4, 0)) };
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var slots = CalendarSchedule.GenerateSlots(date, 60, 0, intervals, [], now);

        slots.Should().HaveCount(2);
        slots[0].StartsAt.Should().Be(new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc));
        slots[1].StartsAt.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Overlapping_intervals_are_rejected()
    {
        var act = () => CalendarSchedule.SortAndRequireNoOverlap(
            [
                new TimeIntervalDto(new TimeOnly(8, 0), new TimeOnly(12, 0)),
                new TimeIntervalDto(new TimeOnly(11, 0), new TimeOnly(15, 0)),
            ],
            "weeklyHours[0].intervals");

        act.Should().Throw<FluentValidation.ValidationException>()
            .Which.Errors.Should().Contain(e => e.PropertyName == "weeklyHours[0].intervals");
    }

    [Fact]
    public void Touching_intervals_are_allowed()
    {
        var intervals = CalendarSchedule.SortAndRequireNoOverlap(
            [
                new TimeIntervalDto(new TimeOnly(12, 0), new TimeOnly(16, 0)),
                new TimeIntervalDto(new TimeOnly(8, 0), new TimeOnly(12, 0)),
            ],
            "intervals");

        intervals.Select(i => i.Start.Hour).Should().Equal(8, 12);
    }
}
