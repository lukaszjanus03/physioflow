using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PHYSIOFLOW_TEST_POSTGRES")))
            Skip = "Ustaw PHYSIOFLOW_TEST_POSTGRES, aby uruchomić testy PostgreSQL (wymagane CREATEDB).";
    }
}

/// <summary>Każda fabryka tworzy i usuwa wyłącznie własną, losową bazę testową.</summary>
public class PostgresCalendarFactory : PatientCalendarFactory
{
    private readonly string _adminConnection;
    private readonly string _databaseName = "physioflow_calendar_test_" + Guid.NewGuid().ToString("N");
    private readonly string _connection;
    private bool _deleted;

    public PostgresCalendarFactory()
    {
        _adminConnection = Environment.GetEnvironmentVariable("PHYSIOFLOW_TEST_POSTGRES")!;
        using (var admin = new NpgsqlConnection(_adminConnection))
        {
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            create.ExecuteNonQuery();
        }
        _connection = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connection).UseSnakeCaseNamingConvention().Options);
        db.Database.Migrate();
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.RemoveAll<ApplicationDbContext>();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(_connection, n => n.EnableRetryOnFailure(3))
            .UseSnakeCaseNamingConvention());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _deleted) return;
        _deleted = true;
        // Nazwa pochodzi wyłącznie ze stałego prefiksu i Guid; nigdy z connection stringa użytkownika.
        if (!_databaseName.StartsWith("physioflow_calendar_test_", StringComparison.Ordinal)
            || !Guid.TryParseExact(_databaseName["physioflow_calendar_test_".Length..], "N", out _))
            throw new InvalidOperationException("Nieprawidłowa nazwa izolowanej bazy testowej.");
        using var ownPool = new NpgsqlConnection(_connection);
        NpgsqlConnection.ClearPool(ownPool);
        using var admin = new NpgsqlConnection(_adminConnection);
        admin.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        drop.ExecuteNonQuery();
    }
}

public class PostgresCalendarTests
{
    [PostgresFact]
    public async Task Patient_message_migration_should_preserve_existing_visits_and_store_optional_message()
    {
        using var factory = new PostgresCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var existing = await s.BookOkAsync();
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261110120608_AddPatientAppointmentsAndReminders");
            await migrator.MigrateAsync();
            return 0;
        });
        var old = await factory.WithDbAsync(db => db.Appointments.AsNoTracking().SingleAsync(x => x.Id == existing.Id));
        old.PatientMessage.Should().BeNull();
        var booked = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
        {
            therapistId = s.TherapistId, startsAt = s.Start.AddHours(2), durationMinutes = 60,
            patientMessage = "  Wiadomość zapisana na PostgreSQL.  ",
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created, await booked.Content.ReadAsStringAsync());
        var result = await booked.Content.ReadFromJsonAsync<PhysioFlow.Application.DTOs.PatientAppointmentDto>(CalendarTestSteps.Json);
        (await factory.WithDbAsync(db => db.Appointments.AsNoTracking().SingleAsync(x => x.Id == result!.Id)))
            .PatientMessage.Should().Be("Wiadomość zapisana na PostgreSQL.");
    }

    [PostgresFact]
    public async Task Two_patients_should_not_book_same_therapist_slot()
    {
        using var factory = new PostgresCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        var otherCard = await s.Therapist.CreateCardAsync();
        var request = await other.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(request.Id, otherCard)).EnsureSuccessStatusCode();
        var responses = await Task.WhenAll(s.BookAsync(), other.PostAsJsonAsync(PatientCalendarSteps.Url,
            new { therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60 }));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [PostgresFact]
    public async Task Concurrent_patient_and_therapist_bookings_should_have_only_one_winner()
    {
        using var factory = new PostgresCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var results = await Task.WhenAll(s.BookAsync(), s.Therapist.PostAsJsonAsync("/api/appointments", new
            { patientId = s.CardId, startsAt = s.Start, durationMinutes = 60 }));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await factory.WithDbAsync(db => db.Appointments.CountAsync(a => a.TherapistId == s.TherapistId)))
            .Should().Be(1);
    }

    [PostgresFact]
    public async Task Parallel_reminder_workers_should_send_once_and_respect_persisted_delivery()
    {
        using var factory = new PostgresCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = s.Start.AddHours(-24);
        await Task.WhenAll(factory.ProcessRemindersAsync(), factory.ProcessRemindersAsync());
        await factory.ProcessRemindersAsync();
        factory.Email.SentTo(s.Email).Count(x => x.Subject == PhysioFlow.Infrastructure.Email.AppointmentReminderProcessor.Subject)
            .Should().Be(1);
        (await factory.WithDbAsync(db => db.AppointmentReminders.CountAsync(r => r.AppointmentId == a.Id)))
            .Should().Be(1);
    }

    [PostgresFact]
    public async Task Migration_should_backfill_existing_appointments_and_preserve_ended_cooperation_history()
    {
        using var factory = new PostgresCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        factory.Clock.UtcNow = DateTime.UtcNow.AddMinutes(1);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261109155650_AddTherapistCalendar");
            await migrator.MigrateAsync();
            return 0;
        });
        var stored = await factory.WithDbAsync(db => db.Appointments.AsNoTracking().SingleAsync(x => x.Id == a.Id));
        stored.PatientUserId.Should().NotBeNull();
        stored.ReminderScheduleId.Should().NotBe(Guid.Empty);
        (await s.Patient.GetAsync($"{PatientCalendarSteps.Url}/{a.Id}")).EnsureSuccessStatusCode();
        (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
    }
}
