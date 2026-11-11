using PhysioFlow.Application.DTOs;

namespace PhysioFlow.Application.Common.Interfaces;

public interface INotificationPublisher
{
    Task PublishAsync(Guid recipientId, NotificationDto notification, CancellationToken ct);
}
