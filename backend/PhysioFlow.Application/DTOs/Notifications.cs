namespace PhysioFlow.Application.DTOs;

public record NotificationDto(Guid Id, string Type, string Title, string Body,
    Guid AppointmentId, DateTime CreatedAt, DateTime? ReadAt);

public record UnreadNotificationCountDto(int Count);
