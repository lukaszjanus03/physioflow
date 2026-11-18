using PhysioFlow.Application.DTOs;

namespace PhysioFlow.Application.Common.Interfaces;

public interface IChatPublisher
{
    Task MessageCreated(ChatMessageDto message, CancellationToken ct);
    Task MessagesRead(ChatReadDto read, CancellationToken ct);
}
