using System.Globalization;
using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using PhysioFlow.Infrastructure.Persistence;

namespace PhysioFlow.Infrastructure.Email;

public class AppointmentReminderOptions
{
    public const string SectionName = "AppointmentReminders";
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelayMinutes { get; set; } = 5;
}

/// <summary>
/// Rejestr w bazie i stały klucz dostawcy chronią także przed awarią między wysyłką a COMMIT.
/// PostgreSQL blokuje wiersz wizyty przez czas sprawdzenia i wysyłki, serializując zmianę terminu/odwołanie.
/// </summary>
public class AppointmentReminderProcessor(
    ApplicationDbContext db, IDateTimeProvider clock, IEmailService email, IUserAccountService accounts,
    IOptions<AppointmentReminderOptions> options, ILogger<AppointmentReminderProcessor> logger)
{
    public const string Subject = "Przypomnienie o wizycie - PhysioFlow";

    public async Task ProcessDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var until = now.AddHours(24);
        var limit = options.Value.MaxAttempts;
        var ids = await db.Appointments.AsNoTracking().Where(a => a.Status == AppointmentStatus.Scheduled
                && !a.ReminderSent && a.StartsAt > now && a.StartsAt <= until
                && !db.AppointmentReminders.Any(r => r.AppointmentId == a.Id && r.ScheduleId == a.ReminderScheduleId
                    && (r.Status != ReminderDeliveryStatus.Pending || r.Attempts >= limit || r.NextAttemptAt > now)))
            .OrderBy(a => a.StartsAt).Select(a => a.Id).Take(options.Value.BatchSize).ToListAsync(ct);

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await EnsureDeliveryAsync(id, ct);
                    db.ChangeTracker.Clear();
                    await DeliverAsync(id, ct);
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Bez wyjątku/wiadomości: dostawca może umieścić w nich adres lub payload.
                logger.LogWarning("Reminder processing failed for appointment {AppointmentId}; error {ErrorType}", id, ex.GetType().Name);
            }
        }
    }

    private async Task EnsureDeliveryAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Therapist)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (a is null || a.Status != AppointmentStatus.Scheduled) return;
        if (await db.AppointmentReminders.AnyAsync(r => r.AppointmentId == id && r.ScheduleId == a.ReminderScheduleId, ct)) return;

        // Dla konta portalu używamy adresu tego konta, także po zakończeniu współpracy.
        // Nigdy nie używamy danych innego konta aktualnie podłączonego do tej samej karty.
        var recipient = a.PatientUserId is { } owner
            ? (await accounts.FindAsync(owner, ct))?.Email : a.Patient.Email;
        var localWithOffset = TimeZoneInfo.ConvertTime(
            new DateTimeOffset(DateTime.SpecifyKind(a.StartsAt, DateTimeKind.Utc)), WarsawTime.Zone);
        var local = localWithOffset.DateTime;
        static string E(string? text) => HtmlEncoder.Default.Encode(text ?? "");
        var html = $"<h2>Przypomnienie o wizycie</h2><p>Termin: {local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} " +
            $"(Europe/Warsaw, UTC{localWithOffset:zzz}).</p><p>Terapeuta: {E(a.Therapist.FirstName)} {E(a.Therapist.LastName)}</p>" +
            $"<p>Gabinet: {E(a.Therapist.PracticeName)}, {E(a.Therapist.City)}</p><p>Kontakt: {E(a.Therapist.PhoneNumber)}</p>";
        var now = clock.UtcNow;
        db.AppointmentReminders.Add(new AppointmentReminder
        {
            AppointmentId = id, ScheduleId = a.ReminderScheduleId, Recipient = recipient,
            HtmlBody = html, NextAttemptAt = now, AttemptWindowStartedAt = now,
            Status = string.IsNullOrWhiteSpace(recipient) ? ReminderDeliveryStatus.Skipped : ReminderDeliveryStatus.Pending,
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.AppointmentReminders.AnyAsync(r => r.AppointmentId == id && r.ScheduleId == a.ReminderScheduleId, ct)) throw;
        }
    }

    private async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // InMemory służy wyłącznie testom reguł. Blokadę weryfikują testy na PostgreSQL.
        var a = db.Database.IsNpgsql()
            ? (await db.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {id} FOR UPDATE SKIP LOCKED")
                .ToListAsync(ct)).SingleOrDefault()
            : await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (a is null) return;
        var now = clock.UtcNow;
        if (a.Status != AppointmentStatus.Scheduled || a.StartsAt <= now || a.StartsAt > now.AddHours(24) || a.ReminderSent) return;
        var delivery = await db.AppointmentReminders.FirstOrDefaultAsync(r =>
            r.AppointmentId == id && r.ScheduleId == a.ReminderScheduleId, ct);
        if (delivery is null || delivery.Status != ReminderDeliveryStatus.Pending
            || delivery.Attempts >= options.Value.MaxAttempts || delivery.NextAttemptAt > now) return;
        // Zapas przed 24h TTL klucza Resend; nie ponawiamy niepewnej wysyłki poza tym oknem.
        if (now >= delivery.AttemptWindowStartedAt.AddHours(23))
        {
            delivery.Status = ReminderDeliveryStatus.Failed;
        }
        else
        {
            delivery.Attempts++;
            try
            {
                await email.SendIdempotentAsync(delivery.Recipient!, Subject, delivery.HtmlBody,
                    $"appointment-reminder/{a.Id:N}/{a.ReminderScheduleId:N}", ct);
                delivery.Status = ReminderDeliveryStatus.Sent;
                delivery.SentAt = clock.UtcNow;
                a.ReminderSent = true;
                a.ConcurrencyStamp = Guid.NewGuid();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                delivery.Status = delivery.Attempts >= options.Value.MaxAttempts
                    ? ReminderDeliveryStatus.Failed : ReminderDeliveryStatus.Pending;
                delivery.NextAttemptAt = now.AddMinutes(options.Value.RetryDelayMinutes * delivery.Attempts);
                logger.LogWarning("Reminder delivery failed for appointment {AppointmentId}; attempt {Attempt}; error {ErrorType}",
                    id, delivery.Attempts, ex.GetType().Name);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
