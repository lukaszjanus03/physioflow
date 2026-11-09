using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Api.Json;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class CalendarTestSteps
{
    public const string SettingsUrl = "/api/therapists/me/calendar";
    public const string AppointmentsUrl = "/api/appointments";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(),
            new TimeOnlyJsonConverter(),
            new NullableTimeOnlyJsonConverter(),
        },
    };

    public static DateOnly NextMonday()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(21));
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    public static object OpenWeek(string start = "08:00", string end = "16:00") => new
    {
        bufferMinutes = 0,
        allowedDurations = new[] { 30, 45, 60 },
        weeklyHours = CalendarSchedule.WeekOrder.Select(day => new
        {
            dayOfWeek = day.ToString(),
            intervals = new[] { new { start, end } },
        }),
        exceptions = Array.Empty<object>(),
    };

    public static async Task<TherapistCalendarSettingsDto> PutSettingsOkAsync(this HttpClient therapist, object body)
    {
        var response = await therapist.PutAsJsonAsync(SettingsUrl, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TherapistCalendarSettingsDto>(Json))!;
    }

    public static Task<HttpResponseMessage> GetSlotsAsync(this HttpClient therapist, DateOnly date, int duration) =>
        therapist.GetAsync($"{SettingsUrl}/available-slots?date={date:yyyy-MM-dd}&durationMinutes={duration}");

    public static async Task<AvailableSlotsDto> GetSlotsOkAsync(this HttpClient therapist, DateOnly date, int duration)
    {
        var response = await therapist.GetSlotsAsync(date, duration);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AvailableSlotsDto>(Json))!;
    }

    public static async Task<AppointmentDto> CreateAppointmentOkAsync(
        this HttpClient therapist, Guid patientId, DateTime startsAt, int duration = 60, string? reason = null)
    {
        var response = await therapist.PostAsJsonAsync(AppointmentsUrl, new
        {
            patientId,
            startsAt,
            durationMinutes = duration,
            reason,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AppointmentDto>(Json))!;
    }
}

public class CalendarSettingsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public CalendarSettingsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Therapist_should_read_and_update_only_own_settings()
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var other = await _factory.CreateTherapistClientAsync();
        var defaults = await owner.GetFromJsonAsync<TherapistCalendarSettingsDto>(CalendarTestSteps.SettingsUrl, CalendarTestSteps.Json);

        defaults!.BufferMinutes.Should().Be(0);
        defaults.AllowedDurations.Should().Equal(30, 45, 60);
        defaults.WeeklyHours.Should().HaveCount(7);
        defaults.WeeklyHours.Should().OnlyContain(d => d.Intervals.Count == 0);

        var saved = await owner.PutSettingsOkAsync(new
        {
            bufferMinutes = 10,
            allowedDurations = new[] { 30, 60 },
            weeklyHours = new object[]
            {
                new { dayOfWeek = "Monday", intervals = new[] { new { start = "08:00", end = "12:00" }, new { start = "13:00", end = "16:00" } } },
                new { dayOfWeek = "Sunday", intervals = Array.Empty<object>() },
            },
            exceptions = new object[]
            {
                new { date = "2027-12-24", isDayOff = true, intervals = (object?)null },
                new { date = "2027-12-27", isDayOff = false, intervals = new[] { new { start = "10:00", end = "14:00" } } },
            },
        });

        saved.BufferMinutes.Should().Be(10);
        saved.AllowedDurations.Should().Equal(30, 60);
        saved.WeeklyHours.Single(d => d.DayOfWeek == DayOfWeek.Monday).Intervals.Should().HaveCount(2);
        saved.Exceptions.Should().HaveCount(2);
        saved.Exceptions.Single(e => e.Date == new DateOnly(2027, 12, 24)).IsDayOff.Should().BeTrue();

        var otherSettings = await other.GetFromJsonAsync<TherapistCalendarSettingsDto>(
            CalendarTestSteps.SettingsUrl, CalendarTestSteps.Json);
        otherSettings!.WeeklyHours.Should().OnlyContain(d => d.Intervals.Count == 0);
        otherSettings.Exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Settings_validation_should_reject_overlap_empty_durations_and_invalid_buffer()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var overlap = await therapist.PutAsJsonAsync(CalendarTestSteps.SettingsUrl, new
        {
            bufferMinutes = 0,
            allowedDurations = new[] { 30 },
            weeklyHours = new[]
            {
                new { dayOfWeek = "Monday", intervals = new[] { new { start = "08:00", end = "12:00" }, new { start = "11:00", end = "15:00" } } },
            },
            exceptions = Array.Empty<object>(),
        });
        overlap.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var durations = await therapist.PutAsJsonAsync(CalendarTestSteps.SettingsUrl, new
        {
            bufferMinutes = 0,
            allowedDurations = Array.Empty<int>(),
            weeklyHours = Array.Empty<object>(),
            exceptions = Array.Empty<object>(),
        });
        durations.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var buffer = await therapist.PutAsJsonAsync(CalendarTestSteps.SettingsUrl, new
        {
            bufferMinutes = 61,
            allowedDurations = new[] { 30 },
            weeklyHours = Array.Empty<object>(),
            exceptions = Array.Empty<object>(),
        });
        buffer.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_settings_should_not_move_existing_appointments()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var monday = CalendarTestSteps.NextMonday();
        var cardId = await therapist.CreateCardAsync();
        var slots = await therapist.GetSlotsOkAsync(monday, 60);
        var created = await therapist.CreateAppointmentOkAsync(cardId, slots.Slots[0].StartsAt);

        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek("10:00", "14:00"));

        var stored = await therapist.GetFromJsonAsync<AppointmentDto>(
            $"{CalendarTestSteps.AppointmentsUrl}/{created.Id}", CalendarTestSteps.Json);
        stored!.StartsAt.Should().Be(created.StartsAt);
        stored.Status.Should().Be(AppointmentStatus.Scheduled);
    }
}

public class CalendarSlotsAndAppointmentsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public CalendarSlotsAndAppointmentsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Available_slots_should_respect_hours_exceptions_duration_and_buffer()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        var monday = CalendarTestSteps.NextMonday();
        await therapist.PutSettingsOkAsync(new
        {
            bufferMinutes = 15,
            allowedDurations = new[] { 45, 60 },
            weeklyHours = new[]
            {
                new { dayOfWeek = "Monday", intervals = new[] { new { start = "08:00", end = "12:00" } } },
                new { dayOfWeek = "Tuesday", intervals = new[] { new { start = "08:00", end = "12:00" } } },
            },
            exceptions = new[]
            {
                new { date = monday.AddDays(1).ToString("yyyy-MM-dd"), isDayOff = true, intervals = Array.Empty<object>() },
            },
        });

        var mondaySlots = await therapist.GetSlotsOkAsync(monday, 60);
        mondaySlots.Slots.Should().NotBeEmpty();
        mondaySlots.Slots.Should().OnlyContain(s => s.EndsAt.Subtract(s.StartsAt) == TimeSpan.FromMinutes(60));

        var tuesday = await therapist.GetSlotsOkAsync(monday.AddDays(1), 60);
        tuesday.Slots.Should().BeEmpty();

        var forbidden = await therapist.GetSlotsAsync(monday, 30);
        forbidden.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var noHours = await therapist.GetSlotsOkAsync(monday.AddDays(2), 60);
        noHours.Slots.Should().BeEmpty();
    }

    [Fact]
    public async Task Therapist_should_create_edit_cancel_and_free_the_slot()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var monday = CalendarTestSteps.NextMonday();
        var cardId = await therapist.CreateCardAsync();
        var otherCard = await therapist.CreateCardAsync();
        var slots = await therapist.GetSlotsOkAsync(monday, 60);
        slots.Slots.Count.Should().BeGreaterThan(1);

        var created = await therapist.CreateAppointmentOkAsync(cardId, slots.Slots[0].StartsAt, 60, "Kontrola");
        created.PatientId.Should().Be(cardId);
        created.Status.Should().Be(AppointmentStatus.Scheduled);
        created.Reason.Should().Be("Kontrola");

        var afterCreate = await therapist.GetSlotsOkAsync(monday, 60);
        afterCreate.Slots.Select(s => s.StartsAt).Should().NotContain(created.StartsAt);

        var moved = await therapist.PutAsJsonAsync($"{CalendarTestSteps.AppointmentsUrl}/{created.Id}", new
        {
            patientId = otherCard,
            startsAt = slots.Slots[1].StartsAt,
            durationMinutes = 60,
            reason = "Przeniesiona",
        });
        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await moved.Content.ReadFromJsonAsync<AppointmentDto>(CalendarTestSteps.Json);
        updated!.PatientId.Should().Be(otherCard);
        updated.StartsAt.Should().Be(slots.Slots[1].StartsAt);

        var afterMove = await therapist.GetSlotsOkAsync(monday, 60);
        afterMove.Slots.Select(s => s.StartsAt).Should().Contain(slots.Slots[0].StartsAt);
        afterMove.Slots.Select(s => s.StartsAt).Should().NotContain(slots.Slots[1].StartsAt);

        var cancelled = await therapist.PostAsync($"{CalendarTestSteps.AppointmentsUrl}/{created.Id}/cancel", null);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledDto = await cancelled.Content.ReadFromJsonAsync<AppointmentDto>(CalendarTestSteps.Json);
        cancelledDto!.Status.Should().Be(AppointmentStatus.Cancelled);

        var afterCancel = await therapist.GetSlotsOkAsync(monday, 60);
        afterCancel.Slots.Select(s => s.StartsAt).Should().Contain(slots.Slots[1].StartsAt);

        var list = await therapist.GetFromJsonAsync<List<AppointmentDto>>(
            $"{CalendarTestSteps.AppointmentsUrl}?from={monday:yyyy-MM-dd}&to={monday:yyyy-MM-dd}",
            CalendarTestSteps.Json);
        list.Should().ContainSingle(a => a.Id == created.Id && a.Status == AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Booking_rules_should_reject_past_wrong_duration_foreign_and_archived_cards()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        var other = await _factory.CreateTherapistClientAsync();
        await therapist.PutSettingsOkAsync(new
        {
            bufferMinutes = 0,
            allowedDurations = new[] { 30 },
            weeklyHours = new[]
            {
                new { dayOfWeek = "Monday", intervals = new[] { new { start = "08:00", end = "16:00" } } },
            },
            exceptions = Array.Empty<object>(),
        });
        var monday = CalendarTestSteps.NextMonday();
        var ownCard = await therapist.CreateCardAsync();
        var foreignCard = await other.CreateCardAsync();
        var archivedId = await therapist.CreateCardAsync();
        (await therapist.DeleteAsync($"/api/patients/{archivedId}")).EnsureSuccessStatusCode();
        var slots = await therapist.GetSlotsOkAsync(monday, 30);

        var past = await therapist.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl, new
        {
            patientId = ownCard,
            startsAt = new DateTime(2020, 1, 6, 8, 0, 0, DateTimeKind.Utc),
            durationMinutes = 30,
        });
        past.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await past.ErrorCodeAsync()).Should().Be(CalendarErrorCodes.AppointmentInPast);

        var duration = await therapist.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl, new
        {
            patientId = ownCard,
            startsAt = slots.Slots[0].StartsAt,
            durationMinutes = 60,
        });
        duration.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var foreign = await therapist.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl, new
        {
            patientId = foreignCard,
            startsAt = slots.Slots[0].StartsAt,
            durationMinutes = 30,
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var archived = await therapist.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl, new
        {
            patientId = archivedId,
            startsAt = slots.Slots[0].StartsAt,
            durationMinutes = 30,
        });
        archived.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await archived.ErrorCodeAsync()).Should().Be(CalendarErrorCodes.CardArchived);
    }

    [Fact]
    public async Task Status_transitions_and_therapist_isolation_should_hold()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        var other = await _factory.CreateTherapistClientAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var monday = CalendarTestSteps.NextMonday();
        var cardId = await therapist.CreateCardAsync();
        var slots = await therapist.GetSlotsOkAsync(monday, 60);
        var future = await therapist.CreateAppointmentOkAsync(cardId, slots.Slots[0].StartsAt);

        (await therapist.PostAsync($"{CalendarTestSteps.AppointmentsUrl}/{future.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await therapist.PostAsync($"{CalendarTestSteps.AppointmentsUrl}/{future.Id}/no-show", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await other.GetAsync($"{CalendarTestSteps.AppointmentsUrl}/{future.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"{CalendarTestSteps.AppointmentsUrl}/{future.Id}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var pastId = Guid.NewGuid();
        await _factory.WithDbAsync(async db =>
        {
            var therapistId = await db.Appointments.Where(a => a.Id == future.Id).Select(a => a.TherapistId).SingleAsync();
            db.Appointments.Add(new Appointment
            {
                Id = pastId,
                TherapistId = therapistId,
                PatientId = cardId,
                StartsAt = DateTime.UtcNow.AddDays(-2),
                EndsAt = DateTime.UtcNow.AddDays(-2).AddHours(1),
                Duration = AppointmentDuration.Sixty,
                Status = AppointmentStatus.Scheduled,
            });
            return await db.SaveChangesAsync();
        });

        var completed = await therapist.PostAsync($"{CalendarTestSteps.AppointmentsUrl}/{pastId}/complete", null);
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await completed.Content.ReadFromJsonAsync<AppointmentDto>(CalendarTestSteps.Json))!
            .Status.Should().Be(AppointmentStatus.Completed);

        (await therapist.PostAsync($"{CalendarTestSteps.AppointmentsUrl}/{pastId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Parallel_booking_of_the_same_slot_should_keep_one_winner()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var monday = CalendarTestSteps.NextMonday();
        var cardA = await therapist.CreateCardAsync();
        var cardB = await therapist.CreateCardAsync();
        var start = (await therapist.GetSlotsOkAsync(monday, 60)).Slots[0].StartsAt;

        var second = _factory.CreateClient();
        second.DefaultRequestHeaders.Authorization = therapist.DefaultRequestHeaders.Authorization;

        var results = await Task.WhenAll(
            therapist.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl, new { patientId = cardA, startsAt = start, durationMinutes = 60 }),
            second.PostAsJsonAsync(CalendarTestSteps.AppointmentsUrl, new { patientId = cardB, startsAt = start, durationMinutes = 60 }));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await results.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync())
            .Should().Be(CalendarErrorCodes.SlotConflict);
    }

    [Fact]
    public async Task Ending_cooperation_should_keep_existing_appointments()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        await therapist.PutSettingsOkAsync(CalendarTestSteps.OpenWeek());
        var cardId = await therapist.CreateCardAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(sent.Id, cardId)).EnsureSuccessStatusCode();

        var monday = CalendarTestSteps.NextMonday();
        var start = (await therapist.GetSlotsOkAsync(monday, 60)).Slots[0].StartsAt;
        var appointment = await therapist.CreateAppointmentOkAsync(cardId, start);

        (await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}/end", null))
            .EnsureSuccessStatusCode();

        var stored = await therapist.GetFromJsonAsync<AppointmentDto>(
            $"{CalendarTestSteps.AppointmentsUrl}/{appointment.Id}", CalendarTestSteps.Json);
        stored!.Id.Should().Be(appointment.Id);
        stored.Status.Should().Be(AppointmentStatus.Scheduled);
    }
}
