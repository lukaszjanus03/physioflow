using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.DTOs;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class AppointmentPatientMessageTests : IClassFixture<PatientCalendarFactory>
{
    private readonly PatientCalendarFactory _factory;
    public AppointmentPatientMessageTests(PatientCalendarFactory factory) => _factory = factory;

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, null, null)]
    [InlineData(true, "", null)]
    [InlineData(true, " \t\r\n ", null)]
    [InlineData(true, "  Ból barku. \r\n", "Ból barku.")]
    public async Task Booking_should_normalize_optional_message_and_return_it_to_patient_and_therapist(
        bool includeMessage, string? message, string? expected)
    {
        var s = await _factory.CalendarScenarioAsync();
        var body = new Dictionary<string, object?>
        {
            ["therapistId"] = s.TherapistId, ["startsAt"] = s.Start, ["durationMinutes"] = 60,
        };
        if (includeMessage) body["patientMessage"] = message;
        var response = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
        created.PatientMessage.Should().Be(expected);
        var patient = await s.Patient.GetFromJsonAsync<PatientAppointmentDto>(
            $"{PatientCalendarSteps.Url}/{created.Id}", CalendarTestSteps.Json);
        patient!.PatientMessage.Should().Be(expected);
        var therapist = await s.Therapist.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{created.Id}", CalendarTestSteps.Json);
        therapist!.PatientMessage.Should().Be(expected);
        therapist.Reason.Should().BeNull();
        therapist.Notes.Should().BeNull();
        (await _factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == created.Id)))
            .PatientMessage.Should().Be(expected);
    }

    [Theory]
    [InlineData(500, HttpStatusCode.Created)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task Message_limit_should_apply_after_trimming_and_failed_validation_should_not_reserve_slot(
        int length, HttpStatusCode expected)
    {
        var s = await _factory.CalendarScenarioAsync();
        var response = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
        { therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60, patientMessage = "  " + new string('a', length) + "\n" });
        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.BadRequest)
        {
            (await response.Content.ReadAsStringAsync()).Should().Contain("patientMessage").And.NotContain(new string('a', length));
            (await s.Therapist.GetSlotsOkAsync(s.Date, 60)).Slots.Should().Contain(x => x.StartsAt == s.Start);
            (await _factory.WithDbAsync(db => db.Appointments.CountAsync(a => a.TherapistId == s.TherapistId))).Should().Be(0);
        }
    }

    [Fact]
    public async Task Message_should_be_isolated_and_preserved_during_therapist_edit()
    {
        var s = await _factory.CalendarScenarioAsync();
        var other = await _factory.CalendarScenarioAsync();
        const string message = "Wiadomość oryginalnego pacjenta.";
        var response = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
        { therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60, patientMessage = message });
        response.EnsureSuccessStatusCode();
        var a = (await response.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
        (await other.Patient.GetAsync($"{PatientCalendarSteps.Url}/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Therapist.GetAsync($"/api/appointments/{a.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Patient.GetStringAsync($"{PatientCalendarSteps.Url}?from={s.Date:yyyy-MM-dd}&to={s.Date:yyyy-MM-dd}"))
            .Should().NotContain(message).And.NotContain(a.Id.ToString());

        var edit = await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
        {
            patientId = s.CardId, startsAt = s.Start.AddHours(2), durationMinutes = 30,
            reason = "Powód terapeuty", notes = "PRIVATE_NOTE", patientMessage = "Próba nadpisania",
        });
        edit.EnsureSuccessStatusCode();
        (await edit.Content.ReadFromJsonAsync<AppointmentDto>(CalendarTestSteps.Json))!.PatientMessage.Should().Be(message);
        var patient = await s.Patient.GetFromJsonAsync<PatientAppointmentDto>($"{PatientCalendarSteps.Url}/{a.Id}", CalendarTestSteps.Json);
        patient!.PatientMessage.Should().Be(message);
        patient.StartsAt.Should().Be(s.Start.AddHours(2));
        (await s.Patient.GetStringAsync($"{PatientCalendarSteps.Url}/{a.Id}")).Should().NotContain("PRIVATE_NOTE");
        var range = await s.Therapist.GetFromJsonAsync<List<AppointmentDto>>(
            $"/api/appointments?from={s.Date:yyyy-MM-dd}&to={s.Date:yyyy-MM-dd}", CalendarTestSteps.Json);
        range!.Single(x => x.Id == a.Id).PatientMessage.Should().Be(message);

        // Zmiana karty nie może udostępnić wiadomości poprzedniego pacjenta nowemu kontu.
        var card = await s.Therapist.CreateCardAsync();
        var cooperation = await other.Patient.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(cooperation.Id, card)).EnsureSuccessStatusCode();
        (await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
        { patientId = card, startsAt = s.Start.AddHours(3), durationMinutes = 30 })).EnsureSuccessStatusCode();
        var newOwner = await other.Patient.GetFromJsonAsync<PatientAppointmentDto>(
            $"{PatientCalendarSteps.Url}/{a.Id}", CalendarTestSteps.Json);
        newOwner!.PatientMessage.Should().BeNull();
        (await s.Therapist.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{a.Id}", CalendarTestSteps.Json))!
            .PatientMessage.Should().Be(message);
    }
}
