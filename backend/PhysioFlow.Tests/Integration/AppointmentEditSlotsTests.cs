using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class AppointmentEditSlotsTests : IClassFixture<PatientCalendarFactory>
{
    private readonly PatientCalendarFactory _factory;
    public AppointmentEditSlotsTests(PatientCalendarFactory factory) => _factory = factory;

    private static string SlotsUrl(DateOnly date, int duration, Guid? exclude = null) =>
        $"/api/therapists/me/calendar/available-slots?date={date:yyyy-MM-dd}&durationMinutes={duration}"
        + (exclude is null ? "" : $"&excludeAppointmentId={exclude}");

    [Fact]
    public async Task Exclusion_should_remove_own_occupancy_and_buffer_but_keep_other_visits_and_save_checks()
    {
        var s = await _factory.CalendarScenarioAsync();
        await s.Therapist.PutSettingsOkAsync(new
        {
            bufferMinutes = 15, allowedDurations = new[] { 30, 45, 60 },
            weeklyHours = CalendarSchedule.WeekOrder.Select(day => new
            { dayOfWeek = day.ToString(), intervals = new[] { new { start = "08:00", end = "16:00" } } }),
            exceptions = Array.Empty<object>(),
        });
        var a = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start, 30);
        await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start.AddHours(2), 30);
        var unchanged = await s.Therapist.GetFromJsonAsync<AvailableSlotsDto>(SlotsUrl(s.Date, 30));
        unchanged!.Slots.Should().NotContain(x => x.StartsAt == s.Start || x.StartsAt == s.Start.AddMinutes(30));
        var excluded = await s.Therapist.GetFromJsonAsync<AvailableSlotsDto>(SlotsUrl(s.Date, 30, a.Id));
        excluded!.Slots.Should().Contain(x => x.StartsAt == s.Start).And.Contain(x => x.StartsAt == s.Start.AddMinutes(30));
        excluded.Slots.Should().NotContain(x => x.StartsAt == s.Start.AddHours(2)
            || x.StartsAt == s.Start.AddHours(2).AddMinutes(30));
        var longer = await s.Therapist.GetFromJsonAsync<AvailableSlotsDto>(SlotsUrl(s.Date, 60, a.Id));
        longer!.Slots.Should().Contain(x => x.StartsAt == s.Start)
            .And.NotContain(x => x.StartsAt == s.Start.AddHours(1) || x.StartsAt == s.Start.AddHours(2));
        (await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
        { patientId = s.CardId, startsAt = s.Start, durationMinutes = 60 })).EnsureSuccessStatusCode();
        var conflict = await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
        { patientId = s.CardId, startsAt = s.Start.AddHours(2), durationMinutes = 60, excludeAppointmentId = a.Id });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.ErrorCodeAsync()).Should().Be(CalendarErrorCodes.SlotConflict);
        (await s.Therapist.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{a.Id}", CalendarTestSteps.Json))!
            .StartsAt.Should().Be(s.Start);
    }

    [Fact]
    public async Task Foreign_and_missing_exclusions_should_return_404_and_patient_cannot_exclude_visits()
    {
        var s = await _factory.CalendarScenarioAsync();
        var other = await _factory.CreateTherapistClientAsync();
        var a = await s.BookOkAsync();
        foreach (var id in new[] { a.Id, Guid.NewGuid(), Guid.Empty })
            (await other.GetAsync(SlotsUrl(s.Date, 60, id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync(SlotsUrl(s.Date, 60, a.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var patientSlots = await s.Patient.GetFromJsonAsync<AvailableSlotsDto>(
            $"{PatientCalendarSteps.Url}/therapists/{s.TherapistId}/available-slots?date={s.Date:yyyy-MM-dd}&durationMinutes=60&excludeAppointmentId={a.Id}");
        patientSlots!.Slots.Should().NotContain(x => x.StartsAt == s.Start);
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled, false)]
    [InlineData(AppointmentStatus.Completed, true)]
    [InlineData(AppointmentStatus.NoShow, true)]
    [InlineData(AppointmentStatus.Scheduled, true)]
    public async Task Exclusion_should_reject_closed_or_started_appointments(AppointmentStatus status, bool past)
    {
        var s = await _factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Appointments.SingleAsync(x => x.Id == a.Id);
            row.Status = status;
            if (past)
            {
                row.StartsAt = _factory.Clock.UtcNow.AddHours(-2);
                row.EndsAt = _factory.Clock.UtcNow.AddHours(-1);
            }
            return await db.SaveChangesAsync();
        });
        var response = await s.Therapist.GetAsync(SlotsUrl(s.Date, 60, a.Id));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be(CalendarErrorCodes.AppointmentNotEditable);
    }
}
