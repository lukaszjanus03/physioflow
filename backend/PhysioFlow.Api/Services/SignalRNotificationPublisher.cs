using Microsoft.AspNetCore.SignalR;
using PhysioFlow.Api.Hubs;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;

namespace PhysioFlow.Api.Services;

public class SignalRNotificationPublisher(IHubContext<NotificationHub> hub) : INotificationPublisher
{
    public Task PublishAsync(Guid recipientId, NotificationDto notification, CancellationToken ct)
        => hub.Clients.Group(recipientId.ToString()).SendAsync("NotificationCreated", notification, ct);
}
