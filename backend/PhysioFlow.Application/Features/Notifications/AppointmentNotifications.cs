using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Notifications;

public static class NotificationTypes
{
    public const string AppointmentBooked = "AppointmentBooked";
    public const string AppointmentCreated = "AppointmentCreated";
    public const string AppointmentRescheduled = "AppointmentRescheduled";
    public const string AppointmentCancelled = "AppointmentCancelled";
}

public class AppointmentNotifications(IApplicationDbContext db, ICurrentUserService user,
    IDateTimeProvider clock, INotificationPublisher publisher, ILogger<AppointmentNotifications> logger)
{
    // Caller must save this row in the same transaction as the appointment.
    public async Task<Notification?> StageAsync(Appointment appointment, string type, CancellationToken ct)
    {
        var actor = user.RequireUserId();
        var therapistUserId = await db.Therapists.IgnoreQueryFilters()
            .Where(t => t.Id == appointment.TherapistId).Select(t => t.UserId).SingleAsync(ct);
        // Use the appointment's persisted owner, never the card's current linkage.
        var recipient = actor == therapistUserId ? appointment.PatientUserId : therapistUserId;
        if (recipient is null || recipient == actor) return null;

        var eventId = appointment.ConcurrencyStamp;
        var existing = db.Notifications.Local.FirstOrDefault(n => n.EventId == eventId && n.RecipientId == recipient)
            ?? await db.Notifications.FirstOrDefaultAsync(n => n.EventId == eventId && n.RecipientId == recipient, ct);
        if (existing is not null) return null;

        var (title, body) = type switch
        {
            NotificationTypes.AppointmentBooked => ("Nowa rezerwacja wizyty", "Pacjent zarezerwował wizytę."),
            NotificationTypes.AppointmentCreated => ("Nowa wizyta", "Terapeuta umówił wizytę."),
            NotificationTypes.AppointmentRescheduled => ("Zmiana terminu wizyty", "Terapeuta zmienił termin wizyty."),
            NotificationTypes.AppointmentCancelled => ("Odwołana wizyta", "Wizyta została odwołana."),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var notification = new Notification
        {
            RecipientId = recipient.Value, EventId = eventId, Type = type, Title = title, Body = body,
            AppointmentId = appointment.Id, CreatedAt = clock.UtcNow
        };
        db.Notifications.Add(notification);
        return notification;
    }

    // Call only after commit. REST remains authoritative if delivery fails or nobody is connected.
    public async Task PublishAsync(Notification? notification)
    {
        if (notification is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await publisher.PublishAsync(notification.RecipientId, notification.ToDto(), timeout.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live delivery failed for notification {NotificationId}; available via REST", notification.Id);
        }
    }
}

public static class NotificationMapping
{
    public static NotificationDto ToDto(this Notification n) => new(n.Id, n.Type, n.Title, n.Body,
        n.AppointmentId, DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
        n.ReadAt is { } readAt ? DateTime.SpecifyKind(readAt, DateTimeKind.Utc) : null);
}
