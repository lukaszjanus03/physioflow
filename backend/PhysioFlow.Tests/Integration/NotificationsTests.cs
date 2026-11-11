using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PhysioFlow.Api.Hubs;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Notifications;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class NotificationFactory : PatientCalendarFactory
{
    public ConcurrentQueue<(string Group, string Event, NotificationDto Dto)> Deliveries { get; } = new();
    public bool FailSignalR { get; set; }
    public Func<NotificationDto, Task>? BeforeDelivery { get; set; }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns((string group) =>
        {
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(async (string name, object?[] args, CancellationToken _) =>
                {
                    var dto = (NotificationDto)args.Single()!;
                    if (BeforeDelivery is not null) await BeforeDelivery(dto);
                    if (FailSignalR) throw new IOException("SignalR unavailable");
                    Deliveries.Enqueue((group, name, dto));
                });
            return proxy.Object;
        });
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        services.RemoveAll<IHubContext<NotificationHub>>();
        services.AddSingleton(hub.Object);
    }
}

public static class NotificationTestSteps
{
    public const string Url = "/api/notifications";
    public static async Task<PaginatedList<NotificationDto>> NotificationsAsync(this HttpClient client,
        int page = 1, int size = 10)
        => (await client.GetFromJsonAsync<PaginatedList<NotificationDto>>($"{Url}?pageNumber={page}&pageSize={size}"))!;
    public static async Task<int> UnreadAsync(this HttpClient client)
        => (await client.GetFromJsonAsync<UnreadNotificationCountDto>($"{Url}/unread-count"))!.Count;

    public static Task<HttpResponseMessage> RescheduleAsync(this PatientCalendarScenario s, Guid id, DateTime start,
        string? notes = null) => s.Therapist.PutAsJsonAsync($"/api/appointments/{id}", new
        { patientId = s.CardId, startsAt = start, durationMinutes = 60, notes });
}

public class NotificationsTests
{
    [Fact]
    public async Task Live_hub_should_deliver_notification_to_authenticated_recipient_with_rest_payload()
    {
        using var factory = new PatientCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var other = await factory.CalendarScenarioAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var therapistSocket = await ConnectAsync(factory, s.Therapist, timeout.Token);
        using var patientSocket = await ConnectAsync(factory, s.Patient, timeout.Token);
        using var otherSocket = await ConnectAsync(factory, other.Therapist, timeout.Token);
        var a = await s.BookOkAsync();
        var received = await ReceiveEventAsync(therapistSocket, timeout.Token);
        received.Type.Should().Be(NotificationTypes.AppointmentBooked);
        received.AppointmentId.Should().Be(a.Id);
        received.Should().BeEquivalentTo((await s.Therapist.NotificationsAsync()).Items.Single());
        // The author's next event must be the therapist-created visit, not their own booking.
        var created = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start.AddHours(2));
        var patientEvent = await ReceiveEventAsync(patientSocket, timeout.Token);
        patientEvent.AppointmentId.Should().Be(created.Id);
        patientEvent.Type.Should().Be(NotificationTypes.AppointmentCreated);
        // An unrelated connection's first event must be its own booking, never the first pair's events.
        var otherAppointment = await other.BookOkAsync();
        (await ReceiveEventAsync(otherSocket, timeout.Token)).AppointmentId.Should().Be(otherAppointment.Id);
    }

    private static async Task<WebSocket> ConnectAsync(TestWebApplicationFactory factory, HttpClient user, CancellationToken ct)
    {
        var client = factory.Server.CreateWebSocketClient();
        var token = user.DefaultRequestHeaders.Authorization!.Parameter;
        var socket = await client.ConnectAsync(new Uri("wss://localhost/hubs/notifications?access_token="
            + Uri.EscapeDataString(token!)), ct);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
            WebSocketMessageType.Text, true, ct);
        var buffer = new byte[4096];
        var handshake = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        Encoding.UTF8.GetString(buffer, 0, handshake.Count).Should().Be("{}\u001e");
        return socket;
    }

    private static async Task<NotificationDto> ReceiveEventAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        while (true)
        {
            var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            received.MessageType.Should().Be(WebSocketMessageType.Text);
            text.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            if (!received.EndOfMessage) continue;
            foreach (var frame in text.ToString().Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(frame);
                if (json.RootElement.GetProperty("type").GetInt32() == 6) continue;
                json.RootElement.GetProperty("target").GetString().Should().Be("NotificationCreated");
                return json.RootElement.GetProperty("arguments")[0].Deserialize<NotificationDto>(CalendarTestSteps.Json)!;
            }
            text.Clear();
        }
    }

    [Fact]
    public async Task Booking_and_patient_cancellation_should_notify_only_own_therapist_using_server_data()
    {
        using var factory = new NotificationFactory();
        var s = await factory.CalendarScenarioAsync();
        var other = await factory.CalendarScenarioAsync();
        var response = await s.Patient.PostAsJsonAsync(PatientCalendarSteps.Url, new
        {
            therapistId = s.TherapistId, startsAt = s.Start, durationMinutes = 60,
            recipientId = other.TherapistId, patientMessage = "PRIVATE_MESSAGE_123", notes = "PRIVATE_NOTES_123"
        });
        response.EnsureSuccessStatusCode();
        var appointment = (await response.Content.ReadFromJsonAsync<PatientAppointmentDto>(CalendarTestSteps.Json))!;
        var notification = (await s.Therapist.NotificationsAsync()).Items.Should().ContainSingle().Subject;
        notification.Type.Should().Be(NotificationTypes.AppointmentBooked);
        notification.AppointmentId.Should().Be(appointment.Id);
        notification.ReadAt.Should().BeNull();
        notification.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        (await s.Therapist.GetStringAsync(NotificationTestSteps.Url)).Should().NotContain("PRIVATE").And.NotContain("recipientId");
        (await s.Patient.NotificationsAsync()).Items.Should().BeEmpty();
        (await other.Patient.NotificationsAsync()).Items.Should().BeEmpty();
        (await other.Therapist.NotificationsAsync()).Items.Should().BeEmpty();

        (await s.Patient.PostAsync($"{PatientCalendarSteps.Url}/{appointment.Id}/cancel", null)).EnsureSuccessStatusCode();
        var rows = (await s.Therapist.NotificationsAsync()).Items;
        rows.Should().HaveCount(2).And.Contain(n => n.Type == NotificationTypes.AppointmentCancelled);
        (await s.Patient.UnreadAsync()).Should().Be(0);
        var therapistUser = await factory.WithDbAsync(db => db.Therapists.Where(t => t.Id == s.TherapistId)
            .Select(t => t.UserId).SingleAsync());
        factory.Deliveries.Should().HaveCount(2).And.OnlyContain(d =>
            d.Group == therapistUser.ToString() && d.Event == "NotificationCreated");
        factory.Deliveries.Select(d => d.Dto.Id).Should().BeEquivalentTo(rows.Select(n => n.Id));
    }

    [Fact]
    public async Task Therapist_actions_should_notify_portal_owner_but_not_notes_only_or_failed_operations()
    {
        using var factory = new NotificationFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start, reason: "PRIVATE_REASON");
        (await s.Patient.NotificationsAsync()).Items.Should().ContainSingle(n => n.Type == NotificationTypes.AppointmentCreated);
        (await s.RescheduleAsync(a.Id, s.Start, "PRIVATE_NOTE")).EnsureSuccessStatusCode();
        (await s.Patient.UnreadAsync()).Should().Be(1);
        (await s.RescheduleAsync(a.Id, s.Start.AddHours(2))).EnsureSuccessStatusCode();
        // Repeating the same edit is not a new schedule event.
        (await s.RescheduleAsync(a.Id, s.Start.AddHours(2))).EnsureSuccessStatusCode();
        (await s.Patient.UnreadAsync()).Should().Be(2);
        (await s.RescheduleAsync(a.Id, s.Start.AddYears(-1))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var rows = (await s.Patient.NotificationsAsync()).Items;
        rows.Select(n => n.Type).Should().BeEquivalentTo(new[] { NotificationTypes.AppointmentCreated,
            NotificationTypes.AppointmentRescheduled, NotificationTypes.AppointmentCancelled });
        (await s.Therapist.UnreadAsync()).Should().Be(0);
        (await s.Patient.GetStringAsync(NotificationTestSteps.Url)).Should().NotContain("PRIVATE");
        factory.Deliveries.Should().HaveCount(3);
        var card = await s.Therapist.CreateCardAsync();
        await s.Therapist.CreateAppointmentOkAsync(card, s.Start.AddHours(4));
        factory.Deliveries.Should().HaveCount(3);
    }

    [Fact]
    public async Task Appointment_owner_should_receive_changes_after_cooperation_ends_and_card_is_relinked()
    {
        using var factory = new NotificationFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        var (other, _) = await factory.CreateConfirmedPatientClientAsync();
        var request = await other.SendRequestOkAsync(s.TherapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(request.Id, s.CardId)).EnsureSuccessStatusCode();
        (await s.RescheduleAsync(a.Id, s.Start.AddHours(2))).EnsureSuccessStatusCode();
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await s.Patient.NotificationsAsync()).Items.Should().HaveCount(3);
        (await other.NotificationsAsync()).Items.Should().BeEmpty();
        // The original patient can still cancel a different owned visit after unlinking.
        var original = await factory.CalendarScenarioAsync();
        var booked = await original.BookOkAsync();
        (await original.Patient.PostAsync($"/api/patient-accounts/me/cooperations/{original.CooperationId}/end", null))
            .EnsureSuccessStatusCode();
        (await original.Patient.PostAsync($"{PatientCalendarSteps.Url}/{booked.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await original.Therapist.NotificationsAsync()).Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Reads_counts_and_pagination_should_be_private_durable_and_idempotent()
    {
        using var factory = new NotificationFactory();
        var s = await factory.CalendarScenarioAsync();
        var other = await factory.CalendarScenarioAsync();
        var a = await s.Therapist.CreateAppointmentOkAsync(s.CardId, s.Start);
        factory.Clock.UtcNow = DateTime.UtcNow.AddSeconds(1);
        (await s.RescheduleAsync(a.Id, s.Start.AddHours(2))).EnsureSuccessStatusCode();
        factory.Clock.UtcNow = DateTime.UtcNow.AddSeconds(2);
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
        await other.Therapist.CreateAppointmentOkAsync(other.CardId, other.Start);
        var first = await s.Patient.NotificationsAsync(size: 2);
        var second = await s.Patient.NotificationsAsync(page: 2, size: 2);
        first.TotalCount.Should().Be(3);
        first.TotalPages.Should().Be(2);
        first.HasNext.Should().BeTrue();
        second.HasPrevious.Should().BeTrue();
        first.Items.Concat(second.Items).Should().HaveCount(3).And.OnlyHaveUniqueItems(n => n.Id)
            .And.BeInDescendingOrder(n => n.CreatedAt);
        (await s.Patient.NotificationsAsync(page: 50)).Items.Should().BeEmpty();
        (await s.Patient.UnreadAsync()).Should().Be(3);
        var id = first.Items[0].Id;
        foreach (var client in new[] { other.Patient, other.Therapist, s.Therapist })
            (await client.PatchAsync($"{NotificationTestSteps.Url}/{id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.PatchAsync($"{NotificationTestSteps.Url}/{Guid.NewGuid()}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Patient.PatchAsync($"{NotificationTestSteps.Url}/{id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var readAt = (await s.Patient.NotificationsAsync()).Items.Single(n => n.Id == id).ReadAt;
        readAt.Should().NotBeNull();
        factory.Clock.UtcNow = DateTime.UtcNow.AddSeconds(10);
        (await s.Patient.PatchAsync($"{NotificationTestSteps.Url}/{id}/read", null)).EnsureSuccessStatusCode();
        (await s.Patient.UnreadAsync()).Should().Be(2);
        (await s.Patient.PostAsync($"{NotificationTestSteps.Url}/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await s.Patient.PostAsync($"{NotificationTestSteps.Url}/read-all", null)).EnsureSuccessStatusCode();
        (await s.Patient.UnreadAsync()).Should().Be(0);
        var refreshed = await s.Patient.NotificationsAsync();
        refreshed.Items.Should().OnlyContain(n => n.ReadAt != null);
        refreshed.Items.Single(n => n.Id == id).ReadAt.Should().Be(readAt);
        (await other.Patient.UnreadAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Anonymous_requests_and_invalid_pagination_should_be_rejected()
    {
        using var factory = new NotificationFactory();
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(NotificationTestSteps.Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{NotificationTestSteps.Url}/unread-count")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PatchAsync($"{NotificationTestSteps.Url}/{Guid.NewGuid()}/read", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"{NotificationTestSteps.Url}/read-all", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        foreach (var query in new[] { "pageNumber=0", "pageNumber=-1", "pageNumber=2147483647",
            "pageSize=0", "pageSize=101", "pageSize=-1" })
            (await patient.GetAsync($"{NotificationTestSteps.Url}?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SignalR_failure_should_not_undo_booking_or_notification_and_rest_should_recover_it()
    {
        using var factory = new NotificationFactory { FailSignalR = true };
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        (await s.Therapist.NotificationsAsync()).Items.Should().ContainSingle(n => n.AppointmentId == a.Id);
        (await s.Therapist.UnreadAsync()).Should().Be(1);
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await s.Patient.UnreadAsync()).Should().Be(1);
        factory.Deliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task Staging_the_same_event_twice_should_not_add_duplicate_notification()
    {
        using var factory = new NotificationFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PhysioFlow.Infrastructure.Persistence.ApplicationDbContext>();
        var actor = new Mock<PhysioFlow.Application.Common.Interfaces.ICurrentUserService>();
        actor.SetupGet(u => u.UserId).Returns(await db.Appointments.Where(x => x.Id == a.Id).Select(x => x.PatientUserId).SingleAsync());
        var service = new AppointmentNotifications(db, actor.Object, factory.Clock,
            Mock.Of<PhysioFlow.Application.Common.Interfaces.INotificationPublisher>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AppointmentNotifications>.Instance);
        var appointment = await db.Appointments.SingleAsync(x => x.Id == a.Id);
        (await service.StageAsync(appointment, NotificationTypes.AppointmentBooked, default)).Should().BeNull();
        appointment.ConcurrencyStamp = Guid.NewGuid();
        (await service.StageAsync(appointment, NotificationTypes.AppointmentCancelled, default)).Should().NotBeNull();
        (await service.StageAsync(appointment, NotificationTypes.AppointmentCancelled, default)).Should().BeNull();
        await db.SaveChangesAsync();
        (await db.Notifications.CountAsync()).Should().Be(2);
        // Self-notification is suppressed even when a card links to the therapist's own account.
        actor.SetupGet(u => u.UserId).Returns(await db.Therapists.Where(t => t.Id == s.TherapistId).Select(t => t.UserId).SingleAsync());
        appointment.PatientUserId = actor.Object.UserId;
        appointment.ConcurrencyStamp = Guid.NewGuid();
        (await service.StageAsync(appointment, NotificationTypes.AppointmentCreated, default)).Should().BeNull();
    }
}

public class PostgresNotificationTests
{
    [PostgresFact]
    public async Task Migration_unique_index_and_read_updates_should_work_on_postgres()
    {
        using var factory = new PostgresCalendarFactory();
        var s = await factory.CalendarScenarioAsync();
        var a = await s.BookOkAsync();
        var row = (await s.Therapist.NotificationsAsync()).Items.Single();
        (await s.Therapist.PatchAsync($"{NotificationTestSteps.Url}/{row.Id}/read", null)).EnsureSuccessStatusCode();
        var readAt = (await s.Therapist.NotificationsAsync()).Items.Single().ReadAt;
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => s.Therapist.PatchAsync($"{NotificationTestSteps.Url}/{row.Id}/read", null)));
        (await s.Therapist.UnreadAsync()).Should().Be(0);
        (await s.Therapist.NotificationsAsync()).Items.Single().ReadAt.Should().Be(readAt);
        await factory.WithDbAsync(async db =>
        {
            var original = await db.Notifications.AsNoTracking().SingleAsync();
            db.Notifications.Add(new Notification { RecipientId = original.RecipientId, EventId = original.EventId,
                AppointmentId = a.Id, Type = original.Type, Title = original.Title, Body = original.Body, CreatedAt = DateTime.UtcNow });
            var action = async () => await db.SaveChangesAsync();
            await action.Should().ThrowAsync<DbUpdateException>();
            return 0;
        });
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await s.Patient.PostAsync($"{NotificationTestSteps.Url}/read-all", null)).EnsureSuccessStatusCode();
        (await s.Patient.UnreadAsync()).Should().Be(0);
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261110151220_AddAppointmentPatientMessage");
            await migrator.MigrateAsync();
            (await db.Appointments.CountAsync(x => x.Id == a.Id)).Should().Be(1);
            (await db.Notifications.CountAsync()).Should().Be(0);
            db.Database.HasPendingModelChanges().Should().BeFalse();
            return 0;
        });
    }

    [PostgresFact]
    public async Task Notification_failure_should_roll_back_appointment_and_publish_only_after_commit()
    {
        using var factory = new AtomicNotificationFactory();
        var s = await factory.CalendarScenarioAsync();
        factory.FailNotificationSave = true;
        (await s.BookAsync()).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await factory.WithDbAsync(db => db.Appointments.CountAsync())).Should().Be(0);
        (await factory.WithDbAsync(db => db.Notifications.CountAsync())).Should().Be(0);
        factory.Published.Should().Be(0);
        factory.FailNotificationSave = false;
        var a = await s.BookOkAsync();
        factory.Published.Should().Be(1);
        factory.FailNotificationSave = true;
        (await s.Therapist.PostAsync($"/api/appointments/{a.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await factory.WithDbAsync(db => db.Appointments.Where(x => x.Id == a.Id).Select(x => x.Status).SingleAsync()))
            .Should().Be(PhysioFlow.Domain.Enums.AppointmentStatus.Scheduled);
        (await factory.WithDbAsync(db => db.Notifications.CountAsync())).Should().Be(1);
        factory.Published.Should().Be(1);
    }
}

public class AtomicNotificationFactory : PostgresCalendarFactory
{
    public bool FailNotificationSave { get; set; }
    public int Published { get; private set; }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.ConfigureDbContext<PhysioFlow.Infrastructure.Persistence.ApplicationDbContext>(o =>
            o.AddInterceptors(new NotificationSaveFailure(this)));
        var publisher = new Mock<PhysioFlow.Application.Common.Interfaces.INotificationPublisher>();
        publisher.Setup(p => p.PublishAsync(It.IsAny<Guid>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid recipient, NotificationDto dto, CancellationToken _) =>
            {
                // A separate connection must see both committed rows when live delivery starts.
                var committed = await this.WithDbAsync(db => db.Notifications.AsNoTracking()
                    .AnyAsync(n => n.Id == dto.Id && n.RecipientId == recipient));
                committed.Should().BeTrue();
                (await this.WithDbAsync(db => db.Appointments.AnyAsync(a => a.Id == dto.AppointmentId))).Should().BeTrue();
                Published++;
            });
        services.RemoveAll<PhysioFlow.Application.Common.Interfaces.INotificationPublisher>();
        services.AddSingleton(publisher.Object);
    }

    private sealed class NotificationSaveFailure(AtomicNotificationFactory factory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (factory.FailNotificationSave && eventData.Context!.ChangeTracker.Entries<Notification>()
                .Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Simulated notification storage failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
