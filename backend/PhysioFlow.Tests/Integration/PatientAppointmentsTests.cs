using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class CalendarTestClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

public class PatientCalendarFactory : TestWebApplicationFactory
{
    public CalendarTestClock Clock { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider>(Clock);
    }
}

public record PatientCalendarScenario(HttpClient Therapist, Guid TherapistId, HttpClient Patient,
    string Email, Guid CardId, Guid CooperationId, DateOnly Date, DateTime Start);

public static class PatientCalendarSteps
{
    public const string Url = "/api/patient-portal/appointments";

    public static async Task<PatientCalendarScenario> CalendarScenarioAsync(this TestWebApplicationFactory factory)
    {
        var (therapist, therapistId) = await factory.CreatePublishedTherapistAsync();
        var (patient, email) = await factory.CreateConfirmedPatientClientAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var card = await therapist.CreateCardAsync();
        var request = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(request.Id, card)).EnsureSuccessStatusCode();
        var date = CalendarTestSteps.NextMonday();
        var start = (await therapist.GetSlotsOkAsync(date, 60)).Slots[0].StartsAt;
        return new(therapist, therapistId, patient, email, card, request.Id, date, start);
    }

    public static Task<HttpResponseMessage> BookAsync(this PatientCalendarScenario s, DateTime? start = null) =>
        s.Patient.PostAsJsonAsync(Url, new { therapistId = s.TherapistId, startsAt = start ?? s.Start, durationMinutes = 60 });

    public static async Task<PatientAppointmentDto> BookOkAsync(this PatientCalendarScenario s)
    {
        var response = await s.BookAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
    }
}

public class PatientAppointmentsTests : IClassFixture<PatientCalendarFactory>
{
    private readonly PatientCalendarFactory _factory;
    public PatientAppointmentsTests(PatientCalendarFactory factory) => _factory = factory;

    [Fact]
    public async Task Patient_without_cooperation_should_not_access_booking_or_therapist_availability()
    {
        var (_, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var date = CalendarTestSteps.NextMonday();
        var settings = await patient.GetAsync($"{PatientCalendarSteps.Url}/therapists/{therapistId}/settings");
        (await settings.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationNotActive);
        var slots = await patient.GetAsync($"{PatientCalendarSteps.Url}/therapists/{therapistId}/available-slots?date={date:yyyy-MM-dd}&durationMinutes=60");
        (await slots.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationNotActive);
        var booking = await patient.PostAsJsonAsync(PatientCalendarSteps.Url,
            new { therapistId, startsAt = DateTime.UtcNow.AddDays(20), durationMinutes = 60 });
        (await booking.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationNotActive);
    }

    [Fact]
    public async Task Booking_should_reject_ambiguous_time_and_accept_explicit_offset()
    {
        var s = await _factory.CalendarScenarioAsync();
        var invalid = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url,
            new { therapistId = s.TherapistId, startsAt = s.Start.ToString("yyyy-MM-ddTHH:mm:ss"), durationMinutes = 60 });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var explicitOffset = new DateTimeOffset(s.Start).ToOffset(TimeSpan.FromHours(2)).ToString("O");
        var valid = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url,
            new { therapistId = s.TherapistId, startsAt = explicitOffset, durationMinutes = 60 });
        valid.StatusCode.Should().Be(HttpStatusCode.Created);
        (await valid.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!.StartsAt.Should().Be(s.Start);
    }

    [Fact]
    public async Task Full_booking_cancel_flow_should_use_session_card_and_free_therapist_slot()
    {
        var s = await _factory.CalendarScenarioAsync();
        var settings = await s.Patient.GetFromJsonAsync<PatientBookingSettingsDto>(
            $"{PatientCalendarSteps.Url}/therapists/{s.TherapistId}/settings");
        settings!.AllowedDurations.Should().Equal(30, 45, 60);
        settings.TimeZone.Should().Be("Europe/Warsaw");
        var slots = await s.Patient.GetFromJsonAsync<AvailableSlotsDto>(
            $"{PatientCalendarSteps.Url}/therapists/{s.TherapistId}/available-slots?date={s.Date:yyyy-MM-dd}&durationMinutes=60");
        slots!.Slots.Should().Contain(x => x.StartsAt == s.Start);

        // Nawet przesłany dodatkowo patientId nie może nadpisać karty z sesji.
        var foreign = await s.Therapist.CreateCardAsync();
        var response = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
        {
            therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60,
            patientId = foreign, notes = "Nie wolno zapisać", status = "Completed",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var dto = (await response.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
        dto.Status.Should().Be(AppointmentStatus.Scheduled);
        var therapistView = await s.Therapist.GetFromJsonAsync<AppointmentDto>(
            $"{CalendarTestSteps.AppointmentsUrl}/{dto.Id}", CalendarTestSteps.Json);
        therapistView!.PatientId.Should().Be(s.CardId);
        therapistView.Notes.Should().BeNull();
        (await s.Therapist.GetSlotsOkAsync(s.Date, 60)).Slots.Should().NotContain(x => x.StartsAt == s.Start);
        (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{dto.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await s.Therapist.GetSlotsOkAsync(s.Date, 60)).Slots.Should().Contain(x => x.StartsAt == s.Start);
        var summary = await s.Patient.GetFromJsonAsync<PatientAppointmentSummaryDto>($"{PatientCalendarSteps.Url}/summary", CalendarTestSteps.Json);
        summary!.NextAppointment.Should().BeNull();
        var history = await s.Patient.GetFromJsonAsync<List<PatientAppointmentDto>>(
            $"{PatientCalendarSteps.Url}?from={s.Date:yyyy-MM-dd}&to={s.Date:yyyy-MM-dd}", CalendarTestSteps.Json);
        history.Should().ContainSingle(a => a.Id == dto.Id && a.Status == AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Therapist_created_visits_and_history_should_remain_private_after_end_and_relink()
    {
        var s = await _factory.CalendarScenarioAsync();
        var response = await s.Therapist.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl,
            new { patientId = s.CardId, startsAt = s.Start, durationMinutes = 60, notes = "SECRET_INTERNAL", reason = "SECRET_REASON" });
        var a = (await response.Content.ReadFromJsonAsync<AppointmentDto>(CalendarTestSteps.Json))!;
        var (other, _) = await _factory.CreateConfirmedPatientClientAsync();
        var (otherTherapist, _) = await _factory.CreatePublishedTherapistAsync();
        (await other.GetAsync($"{PatientCalendarSteps.Url}/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"{PatientCalendarSteps.Url}/{a.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await s.Patient.GetStringAsync($"{PatientCalendarSteps.Url}/{a.Id}");
        body.Should().NotContain("SECRET").And.NotContain("notes").And.NotContain("patientId");
        var otherSummary = await otherTherapist.GetFromJsonAsync<TherapistAppointmentSummaryDto>(
            "/api/appointments/summary", CalendarTestSteps.Json);
        otherSummary!.UpcomingAppointments.Should().BeEmpty();

        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)).EnsureSuccessStatusCode();
        var denied = await s.BookAsync(s.Start.AddHours(2));
        (await denied.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationNotActive);
        var relink = await other.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(relink.Id, s.CardId)).EnsureSuccessStatusCode();
        (await other.GetAsync($"{PatientCalendarSteps.Url}/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.GetAsync($"{PatientCalendarSteps.Url}/{a.Id}")).EnsureSuccessStatusCode();
        (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task First_link_should_include_visits_created_before_patient_had_account()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var card = await therapist.CreateCardAsync();
        var start = (await therapist.GetSlotsOkAsync(CalendarTestSteps.NextMonday(), 60)).Slots[0].StartsAt;
        var a = await therapist.CreateAppointmentOkAsync(card, start);
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var request = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(request.Id, card)).EnsureSuccessStatusCode();
        (await patient.GetAsync($"{PatientCalendarSteps.Url}/{a.Id}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Booking_should_recheck_archived_card_past_duration_exceptions_and_conflicts()
    {
        var s = await _factory.CalendarScenarioAsync();
        (await s.BookAsync(_factory.Clock.UtcNow.AddMinutes(-1))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var first = await s.BookOkAsync();
        (await (await s.BookAsync()).ErrorCodeAsync()).Should().Be(CalendarErrorCodes.SlotConflict);
        await s.Therapist.PutSettingsOkAsync(new
        {
            bufferMinutes = 15, allowedDurations = new[] { 30 },
            weeklyHours = CalendarSchedule.WeekOrder.Select(day => new
            { dayOfWeek = day.ToString(), intervals = new[] { new { start = "08:00", end = "16:00" } } }),
            exceptions = new[] { new { date = s.Date.ToString("yyyy-MM-dd"), isDayOff = true, intervals = Array.Empty<object>() } },
        });
        (await s.BookAsync(s.Start.AddHours(2))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var closed = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url,
            new { therapistId = s.TherapistId, startsAt = s.Start.AddHours(2), durationMinutes = 30 });
        (await closed.ErrorCodeAsync()).Should().Be(CalendarErrorCodes.SlotUnavailable);
        (await s.Therapist.DeleteAsync($"/api/patients/{s.CardId}")).EnsureSuccessStatusCode();
        (await (await s.BookAsync(s.Start.AddDays(1))).ErrorCodeAsync()).Should().Be(CalendarErrorCodes.CardArchived);
    }

    [Fact]
    public async Task Patient_routes_should_require_patient_role_and_not_allow_therapist_status_actions()
    {
        var s = await _factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        var routes = new[] { "", $"/{a.Id}", "/summary", $"/therapists/{s.TherapistId}/settings",
            $"/therapists/{s.TherapistId}/available-slots?date={s.Date:yyyy-MM-dd}&durationMinutes=60" };
        using var anonymous = _factory.CreateClient();
        foreach (var route in routes)
        {
            (await anonymous.GetAsync(PatientCalendarSteps.Url + route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await s.Therapist.GetAsync(PatientCalendarSteps.Url + route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        foreach (var action in new[] { "complete", "no-show" })
            (await s.Patient.PostAsync($"/api/appointments/{a.Id}/{action}", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var client in new[] { anonymous, s.Therapist })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            (await client.PostAsJsonAsync(PatientCalendarSteps.Url, new { })).StatusCode.Should().Be(expected);
            (await client.PostAsync($"{PatientCalendarSteps.Url}/{a.Id}/cancel", null)).StatusCode.Should().Be(expected);
        }
    }

    [Fact]
    public async Task Summary_should_select_only_own_future_scheduled_visits()
    {
        var s = await _factory.CalendarScenarioAsync();
        var first = await s.BookOkAsync();
        var second = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start.AddHours(2));
        (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{first.Id}/cancel", null)).EnsureSuccessStatusCode();
        var summary = await s.Patient.GetFromJsonAsync<PatientAppointmentSummaryDto>($"{PatientCalendarSteps.Url}/summary", CalendarTestSteps.Json);
        summary!.NextAppointment!.Id.Should().Be(second.Id);
        var therapist = await s.Therapist.GetFromJsonAsync<TherapistAppointmentSummaryDto>("/api/appointments/summary", CalendarTestSteps.Json);
        therapist!.UpcomingAppointments.Should().ContainSingle(x => x.Id == second.Id);
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.Conflict)]
    [InlineData(0, HttpStatusCode.OK)]
    [InlineData(1, HttpStatusCode.OK)]
    public async Task Cancellation_should_use_exact_24_hour_server_boundary(int seconds, HttpStatusCode expected)
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24).AddSeconds(-seconds);
        var response = await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{a.Id}/cancel", null);
        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.Conflict)
        {
            (await response.ErrorCodeAsync()).Should().Be(CalendarErrorCodes.CancellationTooLate);
            (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
        }
    }
}
