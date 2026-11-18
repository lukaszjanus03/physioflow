using PhysioFlow.Domain.Common;

namespace PhysioFlow.Domain.Entities;

public class ChatConversation : BaseEntity
{
    public Guid CooperationId { get; set; }
    public Guid PatientId { get; set; }
    public Guid AccountLinkVersion { get; set; }
    public Guid PatientUserId { get; set; }
    public Guid TherapistUserId { get; set; }
    public long LastSequence { get; set; }
    public long PatientReadThrough { get; set; }
    public long TherapistReadThrough { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public CooperationRequest Cooperation { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
}

// The old ChatMessage table is deliberately not used: its rows have no verified cooperation context.
public class PrivateChatMessage : BaseEntity
{
    public const int ContentMaxLength = 4000;
    public Guid ConversationId { get; set; }
    public long Sequence { get; set; }
    public Guid SenderId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid RequestId { get; set; }
    public string Content { get; set; } = null!;
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public ChatConversation Conversation { get; set; } = null!;
    public override string ToString() => nameof(PrivateChatMessage);
}

// A database fence shared by REST, the hub, all conversations and all application instances.
public class ChatSendQuota
{
    public Guid UserId { get; set; }
    public DateTime WindowStartedAt { get; set; }
    public int Count { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
