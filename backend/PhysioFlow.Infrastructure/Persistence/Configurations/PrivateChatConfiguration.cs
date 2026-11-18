using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Infrastructure.Persistence.Configurations;

public class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public const string ContextIndex = "ix_chat_conversations_context";
    public void Configure(EntityTypeBuilder<ChatConversation> b)
    {
        b.ToTable("chat_conversations");
        b.Property(c => c.Version).IsConcurrencyToken();
        b.HasIndex(c => new { c.CooperationId, c.AccountLinkVersion }).IsUnique().HasDatabaseName(ContextIndex);
        b.HasIndex(c => c.PatientUserId);
        b.HasIndex(c => c.TherapistUserId);
        b.HasOne(c => c.Cooperation).WithMany().HasForeignKey(c => c.CooperationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.Patient).WithMany().HasForeignKey(c => c.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(c => !c.Patient.IsDeleted && !c.Cooperation.Therapist.IsDeleted);
    }
}

public class PrivateChatMessageConfiguration : IEntityTypeConfiguration<PrivateChatMessage>
{
    public const string RequestIndex = "ix_private_chat_messages_sender_request";
    public const string SequenceIndex = "ix_private_chat_messages_conversation_sequence";
    public void Configure(EntityTypeBuilder<PrivateChatMessage> b)
    {
        b.ToTable("private_chat_messages", t =>
        {
            t.HasCheckConstraint("ck_private_chat_content", "length(btrim(content)) BETWEEN 1 AND 4000");
            t.HasCheckConstraint("ck_private_chat_sequence", "sequence > 0");
            t.HasCheckConstraint("ck_private_chat_participants", "sender_id <> recipient_id");
        });
        b.Property(m => m.Content).HasMaxLength(PrivateChatMessage.ContentMaxLength).IsRequired();
        b.HasIndex(m => new { m.SenderId, m.RequestId }).IsUnique().HasDatabaseName(RequestIndex);
        b.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique().HasDatabaseName(SequenceIndex);
        b.HasIndex(m => new { m.ConversationId, m.RecipientId, m.Sequence }).HasFilter("read_at IS NULL");
        b.HasOne(m => m.Conversation).WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(m => !m.Conversation.Patient.IsDeleted && !m.Conversation.Cooperation.Therapist.IsDeleted);
    }
}

public class ChatSendQuotaConfiguration : IEntityTypeConfiguration<ChatSendQuota>
{
    public void Configure(EntityTypeBuilder<ChatSendQuota> b)
    {
        b.ToTable("chat_send_quotas");
        b.HasKey(q => q.UserId);
        b.Property(q => q.Version).IsConcurrencyToken();
    }
}
