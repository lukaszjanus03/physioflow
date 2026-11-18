using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Api.Hubs;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Chat;

namespace PhysioFlow.Api.Services;

public class SignalRChatPublisher(IHubContext<ChatHub> hub, IApplicationDbContext db, ChatAccess access) : IChatPublisher
{
    private async Task Deliver(Guid conversationId, string eventName, object payload, CancellationToken ct)
    {
        var c = await db.ChatConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (c is null) return;
        foreach (var id in new[] { c.PatientUserId, c.TherapistUserId })
        {
            // No conversation groups: a long-lived connection never holds a cached cooperation grant.
            if (!await access.CanRead(conversationId, id, ct)) continue;
            await hub.Clients.Group(ChatHub.UserGroup(id)).SendAsync(eventName, payload, ct);
            await hub.Clients.Group(ChatHub.UserGroup(id)).SendAsync("ConversationUpdated", new ChatConversationEvent(conversationId), ct);
        }
    }

    public Task MessageCreated(ChatMessageDto message, CancellationToken ct) => Deliver(message.ConversationId,
        "MessageCreated", new ChatMessageEvent(message.ConversationId, message.Id, message.Sequence), ct);
    public Task MessagesRead(ChatReadDto read, CancellationToken ct) => Deliver(read.ConversationId, "MessagesRead", read, ct);
}
