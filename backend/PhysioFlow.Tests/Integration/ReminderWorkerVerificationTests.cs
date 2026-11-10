using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Email;
using Xunit;

namespace PhysioFlow.Tests.Integration;

// Początkowo przechwytuje potwierdzenia kont; po przygotowaniu danych przełącza się na prawdziwy pickup.
public sealed class SwitchableEmailService(IEmailService initial) : IEmailService
{
    public IEmailService Target { get; set; } = initial;
    public Task SendAsync(string to, string subject, string html, CancellationToken ct = default) => Target.SendAsync(to, subject, html, ct);
    public Task SendIdempotentAsync(string to, string subject, string html, string key, CancellationToken ct = default) =>
        Target.SendIdempotentAsync(to, subject, html, key, ct);
    public Task SendEmailConfirmationAsync(string to, string name, string link, CancellationToken ct = default) =>
        Target.SendEmailConfirmationAsync(to, name, link, ct);
    public Task SendAppointmentReminderAsync(string to, string name, DateTime date, CancellationToken ct = default) =>
        Target.SendAppointmentReminderAsync(to, name, date, ct);
    public Task SendPatientInviteAsync(string to, string link, string name, CancellationToken ct = default) =>
        Target.SendPatientInviteAsync(to, link, name, ct);
}

public sealed class EnabledReminderWorkerFactory : PostgresCalendarFactory
{
    public SwitchableEmailService Delivery { get; }
    public EnabledReminderWorkerFactory() => Delivery = new(Email);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IEmailService>();
        services.AddSingleton<IEmailService>(Delivery);
        services.PostConfigure<AppointmentReminderOptions>(o => { o.Enabled = true; o.PollIntervalSeconds = 1; });
    }
}

public class ReminderWorkerVerificationTests
{
    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Uruchomione zadanie przypomnień nie zakończyło wysyłki w 20 sekund.");
    }

    [PostgresFact]
    public async Task Enabled_background_worker_should_write_safe_local_email_once_and_follow_reschedule_and_cancel()
    {
        var directory = Path.Combine(Path.GetTempPath(), "physioflow-worker-mail-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var factory = new EnabledReminderWorkerFactory();
            var s = await factory.CalendarScenarioAsync();
            var booked = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
            {
                therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60,
                patientMessage = "PRIVATE_PATIENT_MESSAGE",
            });
            booked.EnsureSuccessStatusCode();
            var a = (await booked.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
            (await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
            { patientId = s.CardId, startsAt = s.Start, durationMinutes = 60, notes = "PRIVATE_NOTES" })).EnsureSuccessStatusCode();
            var cancelled = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start.AddHours(2));
            (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{cancelled.Id}/cancel", null)).EnsureSuccessStatusCode();

            factory.Delivery.Target = new LocalPickupEmailService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Email:PickupDirectory"] = directory }).Build());
            factory.Clock.UtcNow = s.Start.AddHours(-24);
            await WaitForAsync(() => factory.WithDbAsync(db => db.AppointmentReminders.AnyAsync(r =>
                r.AppointmentId == a.Id && r.Status == ReminderDeliveryStatus.Sent)));
            var file = Directory.GetFiles(directory, "*.html").Should().ContainSingle().Subject;
            var html = await File.ReadAllTextAsync(file);
            var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(s.Start), WarsawTime.Zone);
            html.Should().Contain(s.Email).And.Contain(local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture))
                .And.Contain("Europe/Warsaw").And.NotContain("PRIVATE_");
            var first = await factory.WithDbAsync(db => db.AppointmentReminders.AsNoTracking().SingleAsync(r => r.AppointmentId == a.Id));
            first.Recipient.Should().Be(s.Email);

            var moved = s.Start.AddDays(1);
            (await s.Therapist.PutAsJsonAsync($"/api/appointments/{a.Id}", new
            { patientId = s.CardId, startsAt = moved, durationMinutes = 60 })).EnsureSuccessStatusCode();
            factory.Clock.UtcNow = moved.AddHours(-24);
            await WaitForAsync(async () => await factory.WithDbAsync(db => db.AppointmentReminders.CountAsync(r =>
                r.AppointmentId == a.Id && r.Status == ReminderDeliveryStatus.Sent)) == 2);
            // Rzeczywiste kolejne przebiegi hostowanego zadania, bez ręcznego wywołania procesora.
            await Task.Delay(2100);
            Directory.GetFiles(directory, "*.html").Should().HaveCount(2);
            (await factory.WithDbAsync(db => db.AppointmentReminders.AnyAsync(r => r.AppointmentId == cancelled.Id))).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
