namespace PhysioFlow.Application.DTOs;

public record OpenChatRequest(Guid CooperationId);
public record SendChatRequest(Guid RequestId, string? Content)
{
    public override string ToString() => nameof(SendChatRequest);
}
public record ReadChatRequest(Guid ThroughMessageId);
public record ChatMessageDto(Guid Id, Guid ConversationId, long Sequence, Guid SenderId,
    Guid RecipientId, Guid RequestId, string Content, DateTime SentAt, DateTime? ReadAt)
{
    public override string ToString() => nameof(ChatMessageDto);
}
public record ChatConversationDto(Guid Id, Guid CooperationId, Guid PatientUserId, Guid TherapistUserId,
    bool CanSend, string? ReadOnlyReason, ChatMessageDto? LastMessage, int UnreadCount,
    long LastSequence, long ReadThrough, Guid Version);
public record ChatHistoryDto(IReadOnlyList<ChatMessageDto> Items, bool HasMore, long? NextCursor,
    long LastSequence);
public record SendChatResult(ChatMessageDto Message, bool Replayed);
public record ChatReadDto(Guid ConversationId, Guid ReaderId, long ReadThrough, int UnreadCount);
// SignalR carries invalidation hints, never message bodies. REST remains the source of truth.
public record ChatMessageEvent(Guid ConversationId, Guid MessageId, long Sequence);
public record ChatConversationEvent(Guid ConversationId);
