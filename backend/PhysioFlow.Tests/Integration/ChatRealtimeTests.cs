using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using Xunit;

namespace PhysioFlow.Tests.Integration;

// A minimal real SignalR JSON client. It retains frames coalesced in a single WebSocket receive.
public sealed class ChatSocket(WebSocket socket, CancellationToken ct) : IDisposable
{
    private readonly List<JsonElement> _pending = [];
    private readonly StringBuilder _buffer = new();
    private int _invocation;
    public static async Task<ChatSocket> Connect(TestWebApplicationFactory factory, HttpClient user, CancellationToken ct)
    {
        var client = factory.Server.CreateWebSocketClient();
        var token = user.DefaultRequestHeaders.Authorization!.Parameter;
        var socket = await client.ConnectAsync(new Uri("wss://localhost/hubs/chat?access_token=" + Uri.EscapeDataString(token!)), ct);
        var result = new ChatSocket(socket, ct);
        await result.Send(new { protocol = "json", version = 1 });
        await result.Next(j => !j.TryGetProperty("type", out _));
        return result;
    }

    private Task Send(object value) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(value, CooperationTestSteps.Json) + '\u001e')), WebSocketMessageType.Text, true, ct);

    public async Task<JsonElement> Invoke(string method, params object?[] args)
    {
        var id = (++_invocation).ToString();
        await Send(new { type = 1, invocationId = id, target = method, arguments = args });
        return await Next(j => j.TryGetProperty("invocationId", out var v) && v.GetString() == id);
    }

    public Task<JsonElement> Event(string name) => Next(j => j.TryGetProperty("target", out var v) && v.GetString() == name);

    private async Task<JsonElement> Next(Func<JsonElement, bool> match)
    {
        while (true)
        {
            var index = _pending.FindIndex(j => match(j));
            if (index >= 0) { var found = _pending[index]; _pending.RemoveAt(index); return found; }
            var bytes = new byte[16384];
            var received = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), ct);
            received.MessageType.Should().Be(WebSocketMessageType.Text);
            _buffer.Append(Encoding.UTF8.GetString(bytes, 0, received.Count));
            var text = _buffer.ToString();
            var last = text.LastIndexOf('\u001e');
            if (last < 0) continue;
            foreach (var frame in text[..last].Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(frame);
                _pending.Add(json.RootElement.Clone());
            }
            _buffer.Clear().Append(text[(last + 1)..]);
        }
    }

    public void Dispose() => socket.Dispose();
}

public class RecordingChatPublisher(IServiceScopeFactory scopes) : IChatPublisher
{
    public ConcurrentQueue<ChatMessageDto> Messages { get; } = new();
    public ConcurrentQueue<ChatReadDto> Reads { get; } = new();
    public bool Fail { get; set; }
    public async Task MessageCreated(ChatMessageDto message, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        // A separate context can observe the committed row before any notification.
        (await db.PrivateChatMessages.AnyAsync(m => m.Id == message.Id, ct)).Should().BeTrue();
        if (Fail) throw new IOException("PRIVATE_PROVIDER_DIAGNOSTIC");
        Messages.Enqueue(message);
    }
    public Task MessagesRead(ChatReadDto read, CancellationToken ct)
    {
        if (Fail) throw new IOException("PRIVATE_PROVIDER_DIAGNOSTIC");
        Reads.Enqueue(read);
        return Task.CompletedTask;
    }
}

public class ChatPublisherFactory : TestWebApplicationFactory
{
    public RecordingChatPublisher Publisher => Services.GetRequiredService<RecordingChatPublisher>();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<RecordingChatPublisher>();
        services.RemoveAll<IChatPublisher>();
        services.AddSingleton<IChatPublisher>(sp => sp.GetRequiredService<RecordingChatPublisher>());
    }
}

public class ChatRealtimeTests
{
    [Fact]
    public async Task Events_follow_commits_replays_do_not_duplicate_events_and_signalr_outages_leave_api_operational()
    {
        using var factory = new ChatPublisherFactory();
        var s = await factory.ChatScenario();
        var key = Guid.NewGuid();
        var sent = await (await s.Patient.Send(s.Chat.Id, "PRIVATE_CHAT_BODY", key)).Body<ChatMessageDto>(HttpStatusCode.Created);
        factory.Publisher.Messages.Should().ContainSingle().Which.Id.Should().Be(sent.Id);
        (await s.Patient.Send(s.Chat.Id, "PRIVATE_CHAT_BODY", key)).EnsureSuccessStatusCode();
        factory.Publisher.Messages.Should().ContainSingle();
        factory.Publisher.Fail = true;
        var fallback = await (await s.Patient.Send(s.Chat.Id, "Persist despite outage")).Body<ChatMessageDto>(HttpStatusCode.Created);
        (await s.Therapist.ChatHistory(s.Chat.Id)).Items.Should().HaveCount(2);
        (await s.Therapist.Read(s.Chat.Id, fallback.Id)).EnsureSuccessStatusCode();
        (await s.Therapist.Chat(s.Chat.Id)).UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Live_hub_delivers_private_hints_and_read_events_and_rechecks_open_connections()
    {
        using var factory = new TestWebApplicationFactory();
        var s = await factory.ChatScenario();
        var outsider = await factory.ChatScenario();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var patient = await ChatSocket.Connect(factory, s.Patient, timeout.Token);
        using var therapist = await ChatSocket.Connect(factory, s.Therapist, timeout.Token);
        using var stranger = await ChatSocket.Connect(factory, outsider.Patient, timeout.Token);
        var key = Guid.NewGuid();
        var completion = await patient.Invoke("SendMessage", s.Chat.Id, new SendChatRequest(key, "PRIVATE_LIVE_BODY"));
        var result = completion.GetProperty("result").Deserialize<SendChatResult>(CooperationTestSteps.Json)!;
        result.Replayed.Should().BeFalse();
        var pushed = await therapist.Event("MessageCreated");
        pushed.GetRawText().Should().NotContain("PRIVATE_LIVE_BODY");
        var hint = pushed.GetProperty("arguments")[0].Deserialize<ChatMessageEvent>(CooperationTestSteps.Json)!;
        hint.MessageId.Should().Be(result.Message.Id);
        (await therapist.Event("ConversationUpdated")).GetProperty("arguments")[0].GetProperty("conversationId").GetGuid().Should().Be(s.Chat.Id);
        (await s.Therapist.Chat(s.Chat.Id)).UnreadCount.Should().Be(1);
        var read = await therapist.Invoke("MarkRead", s.Chat.Id, new ReadChatRequest(result.Message.Id));
        read.GetProperty("result").GetProperty("readThrough").GetInt64().Should().Be(1);
        (await patient.Event("MessagesRead")).GetProperty("arguments")[0].GetProperty("readerId").GetGuid().Should().Be(s.Chat.TherapistUserId);
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Single().ReadAt.Should().NotBeNull();
        (await stranger.Invoke("SendMessage", s.Chat.Id, new SendChatRequest(Guid.NewGuid(), "Intrusion")))
            .GetProperty("error").GetString().Should().Contain("not_found");
        (await stranger.Invoke("MarkRead", s.Chat.Id, new ReadChatRequest(result.Message.Id)))
            .GetProperty("error").GetString().Should().Contain("not_found");
        (await stranger.Invoke("GetHistory", s.Chat.Id, null, null, 50))
            .GetProperty("error").GetString().Should().Contain("not_found");
        // The stranger's first message event must concern their own pair, never the other conversation.
        (await outsider.Therapist.Send(outsider.Chat.Id)).EnsureSuccessStatusCode();
        (await stranger.Event("MessageCreated")).GetProperty("arguments")[0].GetProperty("conversationId").GetGuid().Should().Be(outsider.Chat.Id);
        (await s.Therapist.DeleteAsync($"/api/patients/{s.CardId}")).EnsureSuccessStatusCode();
        (await patient.Invoke("SendMessage", s.Chat.Id, new SendChatRequest(Guid.NewGuid(), "Archived")))
            .GetProperty("error").GetString().Should().Contain("patient_archived");
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.CardId)).UserId = null;
            return await db.SaveChangesAsync();
        });
        (await patient.Invoke("GetHistory", s.Chat.Id, null, null, 50))
            .GetProperty("error").GetString().Should().Contain("not_found");
        await factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == s.Chat.TherapistUserId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        (await therapist.Invoke("GetHistory", s.Chat.Id, null, null, 50))
            .GetProperty("error").GetString().Should().Contain("forbidden");
    }

    [Fact]
    public async Task Rest_and_hub_share_the_same_durable_send_limit()
    {
        using var factory = new ChatLimitFactory();
        var s = await factory.ChatScenario();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var socket = await ChatSocket.Connect(factory, s.Patient, timeout.Token);
        var key = Guid.NewGuid();
        (await socket.Invoke("SendMessage", s.Chat.Id, new SendChatRequest(key, "One"))).TryGetProperty("result", out _).Should().BeTrue();
        (await s.Patient.Send(s.Chat.Id, "Two")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await socket.Invoke("SendMessage", s.Chat.Id, new SendChatRequest(Guid.NewGuid(), "Three")))
            .GetProperty("error").GetString().Should().Contain("chat_rate_limit");
        (await s.Patient.Send(s.Chat.Id, "One", key)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
