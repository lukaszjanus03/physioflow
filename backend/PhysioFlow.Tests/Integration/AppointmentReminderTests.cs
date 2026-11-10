using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using PhysioFlow.Infrastructure.Email;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class ReminderTestSteps
{
    public static async Task ProcessRemindersAsync(this TestWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppointmentReminderProcessor>().ProcessDueAsync();
    }
}

public class AppointmentReminderTests
{
    [Theory]
    [InlineData(0, "UTC+02:00")]
    [InlineData(1, "UTC+01:00")]
    public async Task Reminder_should_preserve_offset_for_both_occurrences_of_clock_change_hour(int utcHour, string offset)
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        var start = new DateTime(2027, 10, 31, utcHour, 30, 0, DateTimeKind.Utc);
        await factory.WithDbAsync(async db =>
        {
            var appointment = await db.Appointments.SingleAsync(x => x.Id == a.Id);
            appointment.StartsAt = start;
            appointment.EndsAt = start.AddHours(1);
            return await db.SaveChangesAsync();
        });
        factory.Clock.UtcNow = start.AddHours(-24);
        await factory.ProcessRemindersAsync();
        var reminder = factory.Email.SentTo(s.Email).Single(x => x.Subject == AppointmentReminderProcessor.Subject);
        reminder.HtmlBody.Should().Contain("31.10.2027 02:30").And.Contain(offset);
    }

    [Fact]
    public async Task Provider_idempotency_should_protect_retry_after_lost_database_acknowledgement()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        await factory.ProcessRemindersAsync();
        // Symulacja: dostawca przyjął wiadomość, ale aplikacja utraciła zapis potwierdzenia.
        await factory.WithDbAsync(async db =>
        {
            var appointment = await db.Appointments.SingleAsync(x => x.Id == a.Id);
            var delivery = await db.AppointmentReminders.SingleAsync(x => x.AppointmentId == a.Id);
            appointment.ReminderSent = false;
            delivery.Status = ReminderDeliveryStatus.Pending;
            delivery.SentAt = null;
            return await db.SaveChangesAsync();
        });
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Count(x => x.Subject == AppointmentReminderProcessor.Subject).Should().Be(1);
        (await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(x => x.AppointmentId == a.Id)))
            .Status.Should().Be(ReminderDeliveryStatus.Sent);
    }

    [Fact]
    public async Task Reminder_should_wait_until_due_survive_new_scope_and_contain_only_safe_warsaw_details()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var booked = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
        {
            therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60,
            patientMessage = "PRIVATE_PATIENT_MESSAGE",
        });
        booked.EnsureSuccessStatusCode();
        var a = (await booked.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
        (await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
        {
            patientId = s.CardId, startsAt = s.Start, durationMinutes = 60,
            reason = "PRIVATE_REASON", notes = "PRIVATE_THERAPIST_NOTES",
        })).EnsureSuccessStatusCode();
        var before = factory.Email.SentTo(s.Email).Count;
        factory.Clock.UtcNow = s.Start.AddHours(-25);
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Should().HaveCount(before);
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        await factory.ProcessRemindersAsync();
        await factory.ProcessRemindersAsync();
        var reminders = factory.Email.SentTo(s.Email).Where(x => x.Subject == AppointmentReminderProcessor.Subject).ToList();
        reminders.Should().ContainSingle();
        reminders[0].HtmlBody.Should().Contain("Europe/Warsaw").And.Contain("Gabinet Ruch")
            .And.Contain("Krak").And.NotContain("PESEL").And.NotContain("notes").And.NotContain("PRIVATE_");
        var stored = await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.AppointmentId == a.Id));
        stored.Status.Should().Be(ReminderDeliveryStatus.Sent);
        stored.Attempts.Should().Be(1);
        (await factory.WithDbAsync(db => db.Appointments.SingleAsync(x => x.Id == a.Id))).ReminderSent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_failure_should_retry_with_limit_or_stop_on_cancellation(bool cancel)
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        factory.Email.SimulateProviderFailure = true;
        await factory.ProcessRemindersAsync();
        var first = await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.AppointmentId == a.Id));
        first.Attempts.Should().Be(1);
        first.Status.Should().Be(ReminderDeliveryStatus.Pending);
        await factory.ProcessRemindersAsync();
        (await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.Id == first.Id))).Attempts.Should().Be(1);
        if (cancel)
        {
            (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
            factory.Email.SimulateProviderFailure = false;
        }
        for (var i = 0; i < 4; i++)
        {
            factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(20);
            await factory.ProcessRemindersAsync();
        }
        var last = await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.Id == first.Id));
        last.Attempts.Should().Be(cancel ? 1 : 3);
        if (!cancel) last.Status.Should().Be(ReminderDeliveryStatus.Failed);
        factory.Email.SentTo(s.Email).Should().NotContain(x => x.Subject == AppointmentReminderProcessor.Subject);
    }

    [Fact]
    public async Task Successful_retry_should_keep_frozen_payload_and_clear_failure()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        factory.Email.SimulateProviderFailure = true;
        await factory.ProcessRemindersAsync();
        var first = await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.AppointmentId == a.Id));
        factory.Email.SimulateProviderFailure = false;
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(5);
        await factory.ProcessRemindersAsync();
        var sent = await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.Id == first.Id));
        sent.Status.Should().Be(ReminderDeliveryStatus.Sent);
        sent.Attempts.Should().Be(2);
        sent.HtmlBody.Should().Be(first.HtmlBody);
    }

    [Fact]
    public async Task Rescheduling_should_replace_due_time_and_allow_one_reminder_for_new_schedule()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        await factory.ProcessRemindersAsync();
        var movedStart = s.Start.AddDays(1);
        var response = await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}",
            new { patientId = s.CardId, startsAt = movedStart, durationMinutes = 60 });
        response.EnsureSuccessStatusCode();
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Count(x => x.Subject == AppointmentReminderProcessor.Subject).Should().Be(1);
        factory.Clock.UtcNow = movedStart.AddHours(-24);
        await factory.ProcessRemindersAsync();
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Count(x => x.Subject == AppointmentReminderProcessor.Subject).Should().Be(2);
        (await factory.WithDbAsync(db => db.AppointmentReminders.CountAsync(r => r.AppointmentId == a.Id))).Should().Be(2);
    }

    [Fact]
    public async Task Short_notice_visits_should_send_once_and_missing_email_should_not_block_next_visit()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var withoutEmail = await s.Therapist.CreateCardAsync();
        var skipped = await s.Therapist.CreateAppointmentOkAsync(withoutEmail, s.Start);
        await factory.WithDbAsync(async db =>
        {
            var card = await db.Patients.SingleAsync(p => p.Id == withoutEmail);
            card.Email = null;
            return await db.SaveChangesAsync();
        });
        factory.Clock.UtcNow = s.Start.AddHours(-2);
        var booked = await s.BookAsync(s.Start.AddHours(1));
        booked.EnsureSuccessStatusCode();
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Count(x => x.Subject == AppointmentReminderProcessor.Subject).Should().Be(1);
        (await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.AppointmentId == skipped.Id)))
            .Status.Should().Be(ReminderDeliveryStatus.Skipped);
    }

    [Fact]
    public async Task Expired_retry_window_and_past_visits_should_not_send()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        factory.Email.SimulateProviderFailure = true;
        await factory.ProcessRemindersAsync();
        factory.Email.SimulateProviderFailure = false;
        factory.Clock.UtcNow = s.Start.AddMinutes(-30);
        await factory.ProcessRemindersAsync();
        (await factory.WithDbAsync(db => db.AppointmentReminders.SingleAsync(r => r.AppointmentId == a.Id)))
            .Status.Should().Be(ReminderDeliveryStatus.Failed);
        factory.Clock.UtcNow = s.Start.AddHours(1);
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Should().NotContain(x => x.Subject == AppointmentReminderProcessor.Subject);
    }
}
