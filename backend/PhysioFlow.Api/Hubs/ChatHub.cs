using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Api.Hubs;

/// <summary>
/// SignalR hub - kanały per-user (grupa o nazwie = userId).
/// Klient front-endu łączy się po JWT i dostaje prywatne wiadomości.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly ICurrentUserService _currentUser;

    public ChatHub(ICurrentUserService currentUser) => _currentUser = currentUser;

    public override async Task OnConnectedAsync()
    {
        var userId = _currentUser.UserId;
        if (userId.HasValue)
            await Groups.AddToGroupAsync(Context.ConnectionId, userId.Value.ToString());
        await base.OnConnectedAsync();
    }

    /// <summary>Klient wysyła wiadomość -> serwer zapisuje w DB (TODO) i forwarduje do odbiorcy.</summary>
    public async Task SendMessage(Guid recipientId, string content)
    {
        var senderId = _currentUser.UserId
            ?? throw new HubException("Unauthorized");

        // TODO: zapis do DB przez command MediatR (SendChatMessageCommand)
        await Clients.Group(recipientId.ToString()).SendAsync("ReceiveMessage", new
        {
            SenderId = senderId,
            Content = content,
            SentAt = DateTime.UtcNow
        });
    }

    /// <summary>Powiadomienie "X pisze..."</summary>
    public async Task SendTypingIndicator(Guid recipientId)
    {
        var senderId = _currentUser.UserId;
        if (senderId.HasValue)
            await Clients.Group(recipientId.ToString())
                .SendAsync("UserTyping", senderId.Value);
    }
}

/// <summary>Hub do powiadomień ogólnych (nowa wizyta, zmiana planu etc.)</summary>
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
