using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Chat;

namespace PhysioFlow.Api.Hubs;

[Authorize(Roles = "Patient,Therapist")]
public class ChatHub(ChatService chat, ChatAccess access, ICurrentUserService currentUser) : Hub
{
    public static string UserGroup(Guid id) => $"chat:user:{id:D}";

    public override async Task OnConnectedAsync()
    {
        var id = currentUser.UserId ?? throw new HubException("unauthorized");
        await Safe(async () => { await access.RequireCaller(id, Context.ConnectionAborted); return true; });
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(id));
        await base.OnConnectedAsync();
    }

    public Task<SendChatResult> SendMessage(Guid conversationId, SendChatRequest body) =>
        Safe(() => chat.Send(conversationId, body, Context.ConnectionAborted));

    public Task<ChatReadDto> MarkRead(Guid conversationId, ReadChatRequest body) =>
        Safe(() => chat.Read(conversationId, body.ThroughMessageId, Context.ConnectionAborted));

    public Task<ChatHistoryDto> GetHistory(Guid conversationId, long? before = null, long? after = null, int limit = 50) =>
        Safe(() => chat.History(conversationId, before, after, limit, Context.ConnectionAborted));

    private static async Task<T> Safe<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (ValidationException) { throw new HubException("validation_failed"); }
        catch (NotFoundException) { throw new HubException("not_found"); }
        catch (ForbiddenAccessException) { throw new HubException("forbidden"); }
        catch (UnauthorizedAccessException) { throw new HubException("unauthorized"); }
        catch (ConflictException e) { throw new HubException(e.Code ?? "conflict"); }
        catch (TooManyRequestsException e) { throw new HubException(e.Code); }
        catch (OperationCanceledException) { throw new HubException("operation_cancelled"); }
        catch { throw new HubException("internal_error"); }
    }
}

[Authorize]
public class NotificationHub : Hub
{
    private readonly ICurrentUserService _currentUser;
    public NotificationHub(ICurrentUserService currentUser) => _currentUser = currentUser;
    public override async Task OnConnectedAsync()
    {
        var userId = _currentUser.UserId;
        if (userId.HasValue)
            await Groups.AddToGroupAsync(Context.ConnectionId, userId.Value.ToString());
        await base.OnConnectedAsync();
    }
}
