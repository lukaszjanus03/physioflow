using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Chat;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public record ChatScenario(HttpClient Therapist, HttpClient Patient, Guid CooperationId, Guid CardId, ChatConversationDto Chat);

public static class ChatTestSteps
{
    public const string Url = "/api/chat/conversations";
    public static string Conversation(Guid id) => $"{Url}/{id}";
    public static string Messages(Guid id) => Conversation(id) + "/messages";
    public static Task<HttpResponseMessage> Send(this HttpClient client, Guid id, string content = "Hello", Guid? key = null) =>
        client.PostAsJsonAsync(Messages(id), new SendChatRequest(key ?? Guid.NewGuid(), content));
    public static Task<HttpResponseMessage> Read(this HttpClient client, Guid id, Guid through) =>
        client.PostAsJsonAsync(Conversation(id) + "/read", new ReadChatRequest(through));
    public static async Task<ChatHistoryDto> ChatHistory(this HttpClient client, Guid id, string query = "") =>
        await (await client.GetAsync(Messages(id) + query)).Body<ChatHistoryDto>();
    public static async Task<ChatConversationDto> Chat(this HttpClient client, Guid id) =>
        await (await client.GetAsync(Conversation(id))).Body<ChatConversationDto>();
    public static async Task<PaginatedList<ChatConversationDto>> Chats(this HttpClient client) =>
        await (await client.GetAsync(Url)).Body<PaginatedList<ChatConversationDto>>();
    public static async Task<ChatScenario> ChatScenario(this TestWebApplicationFactory factory, HttpClient? patient = null)
    {
        var (therapist, therapistId) = await factory.CreatePublishedTherapistAsync();
        patient ??= (await factory.CreateConfirmedPatientClientAsync()).Client;
        var card = await therapist.CreateCardAsync();
        var cooperation = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(cooperation.Id, card)).EnsureSuccessStatusCode();
        var chat = await (await patient.PostAsJsonAsync(Url, new OpenChatRequest(cooperation.Id))).Body<ChatConversationDto>();
        return new(therapist, patient, cooperation.Id, card, chat);
    }
}

public sealed class ChatClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
}

public class ChatLimitFactory : TestWebApplicationFactory
{
    public ChatClock Clock { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.Configure<ChatOptions>(o => o.MessagesPerMinute = 2);
        services.RemoveAll<IDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider>(Clock);
    }
}

public class ChatTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Participants_exchange_persisted_messages_with_server_identity_time_and_private_lists()
    {
        var s = await factory.ChatScenario();
        var first = await (await s.Patient.PostAsJsonAsync(ChatTestSteps.Messages(s.Chat.Id), new
        {
            requestId = Guid.NewGuid(), content = "  Private message  ", senderId = Guid.NewGuid(),
            recipientId = Guid.NewGuid(), sentAt = DateTime.UtcNow.AddYears(-1), readAt = DateTime.UtcNow
        })).Body<ChatMessageDto>(HttpStatusCode.Created);
        first.SenderId.Should().Be(s.Chat.PatientUserId);
        first.RecipientId.Should().Be(s.Chat.TherapistUserId);
        first.SentAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        first.ReadAt.Should().BeNull();
        first.Content.Should().Be("Private message");
        first.Sequence.Should().Be(1);
        var second = await (await s.Therapist.Send(s.Chat.Id, "Reply")).Body<ChatMessageDto>(HttpStatusCode.Created);
        second.Sequence.Should().Be(2);
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Should().BeEquivalentTo([first, second], o => o.WithStrictOrdering());
        (await s.Patient.Chats()).Items.Single(c => c.Id == s.Chat.Id).UnreadCount.Should().Be(1);
        var therapistChat = (await s.Therapist.Chats()).Items.Single(c => c.Id == s.Chat.Id);
        therapistChat.LastMessage.Should().BeEquivalentTo(second);
        therapistChat.UnreadCount.Should().Be(1);
        var reopened = await (await s.Therapist.PostAsJsonAsync(ChatTestSteps.Url, new OpenChatRequest(s.CooperationId)))
            .Body<ChatConversationDto>();
        reopened.Id.Should().Be(s.Chat.Id);
        (await factory.WithDbAsync(db => db.PrivateChatMessages.CountAsync(m => m.ConversationId == s.Chat.Id))).Should().Be(2);
        (await s.Patient.GetAsync(ChatTestSteps.Messages(s.Chat.Id))).Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Cursor_history_is_stable_and_reconnect_recovers_every_missing_message()
    {
        var s = await factory.ChatScenario();
        var sent = new List<ChatMessageDto>();
        for (var i = 0; i < 5; i++) sent.Add(await (await s.Patient.Send(s.Chat.Id, "Message " + i)).Body<ChatMessageDto>(HttpStatusCode.Created));
        var latest = await s.Patient.ChatHistory(s.Chat.Id, "?limit=2");
        latest.Items.Select(m => m.Sequence).Should().Equal(4, 5);
        latest.NextCursor.Should().Be(4);
        latest.HasMore.Should().BeTrue();
        var newMessage = await (await s.Therapist.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var older = await s.Patient.ChatHistory(s.Chat.Id, "?limit=2&before=" + latest.NextCursor);
        older.Items.Select(m => m.Sequence).Should().Equal(2, 3);
        var oldest = await s.Patient.ChatHistory(s.Chat.Id, "?limit=2&before=" + older.NextCursor);
        oldest.Items.Select(m => m.Sequence).Should().Equal(1);
        oldest.HasMore.Should().BeFalse();
        var missed = await s.Patient.ChatHistory(s.Chat.Id, "?limit=2&after=2");
        missed.Items.Select(m => m.Sequence).Should().Equal(3, 4);
        missed.NextCursor.Should().Be(4);
        var rest = await s.Patient.ChatHistory(s.Chat.Id, "?limit=2&after=" + missed.NextCursor);
        rest.Items.Select(m => m.Sequence).Should().Equal(5, 6);
        rest.HasMore.Should().BeFalse();
        (await s.Patient.ChatHistory(s.Chat.Id, "?after=6")).Items.Should().BeEmpty();
        (await s.Patient.Chat(s.Chat.Id)).ReadThrough.Should().Be(0);
    }

    [Fact]
    public async Task Read_watermarks_are_monotonic_recipient_owned_and_never_include_newer_messages()
    {
        var s = await factory.ChatScenario();
        var first = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var second = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        (await s.Therapist.ChatHistory(s.Chat.Id)).Items.Should().OnlyContain(m => m.ReadAt == null);
        var read = await (await s.Therapist.Read(s.Chat.Id, first.Id)).Body<ChatReadDto>();
        read.ReadThrough.Should().Be(1);
        read.UnreadCount.Should().Be(1);
        var history = await s.Patient.ChatHistory(s.Chat.Id);
        history.Items[0].ReadAt.Should().NotBeNull();
        history.Items[1].ReadAt.Should().BeNull();
        (await s.Therapist.Read(s.Chat.Id, second.Id)).EnsureSuccessStatusCode();
        var newer = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        read = await (await s.Therapist.Read(s.Chat.Id, first.Id)).Body<ChatReadDto>();
        read.ReadThrough.Should().Be(2);
        read.UnreadCount.Should().Be(1);
        (await s.Patient.Read(s.Chat.Id, newer.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var other = await factory.ChatScenario();
        (await other.Therapist.Read(other.Chat.Id, newer.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.Chat(s.Chat.Id)).UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task Retries_are_idempotent_even_after_cooperation_ends_and_key_reuse_is_rejected()
    {
        var s = await factory.ChatScenario();
        var key = Guid.NewGuid();
        var first = await (await s.Patient.Send(s.Chat.Id, "Original", key)).Body<ChatMessageDto>(HttpStatusCode.Created);
        (await (await s.Patient.Send(s.Chat.Id, "  Original  ", key)).Body<ChatMessageDto>()).Should().BeEquivalentTo(first);
        var changed = await s.Patient.Send(s.Chat.Id, "Different", key);
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await changed.ErrorCodeAsync()).Should().Be("idempotency_key_reused");
        var other = await factory.ChatScenario(s.Patient);
        (await s.Patient.Send(other.Chat.Id, "Original", key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)).EnsureSuccessStatusCode();
        (await (await s.Patient.Send(s.Chat.Id, "Original", key)).Body<ChatMessageDto>()).Should().BeEquivalentTo(first);
        (await s.Patient.Send(s.Chat.Id, "New message")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Outsiders_and_admins_cannot_open_read_send_or_mark_other_conversations()
    {
        var s = await factory.ChatScenario();
        var message = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var (stranger, _) = await factory.CreateConfirmedPatientClientAsync();
        var admin = await factory.CreateModeratorAsync();
        var otherTherapist = await factory.CreateTherapistClientAsync();
        foreach (var client in new[] { stranger, admin, otherTherapist })
        {
            (await client.GetAsync(ChatTestSteps.Conversation(s.Chat.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.GetAsync(ChatTestSteps.Messages(s.Chat.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Send(s.Chat.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Read(s.Chat.Id, message.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostAsJsonAsync(ChatTestSteps.Url, new OpenChatRequest(s.CooperationId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.Chats()).Items.Should().BeEmpty();
        }
        (await factory.CreateClient().GetAsync(ChatTestSteps.Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Archive_end_and_relink_preserve_only_original_participants_history_and_create_new_contexts()
    {
        var s = await factory.ChatScenario();
        var old = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        (await s.Therapist.DeleteAsync($"/api/patients/{s.CardId}")).EnsureSuccessStatusCode();
        (await s.Patient.Chat(s.Chat.Id)).ReadOnlyReason.Should().Be("patient_archived");
        (await s.Patient.Send(s.Chat.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Therapist.Read(s.Chat.Id, old.Id)).EnsureSuccessStatusCode();
        (await s.Therapist.PostAsync($"/api/patients/{s.CardId}/unarchive", null)).EnsureSuccessStatusCode();
        (await s.Patient.Chat(s.Chat.Id)).CanSend.Should().BeTrue();
        (await s.Patient.PostAsync($"{CooperationTestSteps.PatientCooperationsUrl}/{s.CooperationId}/end", null)).EnsureSuccessStatusCode();
        (await s.Patient.Chat(s.Chat.Id)).ReadOnlyReason.Should().Be("cooperation_ended");
        var therapistId = await factory.WithDbAsync(db => db.Patients.Where(p => p.Id == s.CardId).Select(p => p.TherapistId).SingleAsync());
        var nextCooperation = await s.Patient.SendRequestOkAsync(therapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(nextCooperation.Id, s.CardId)).EnsureSuccessStatusCode();
        var next = await (await s.Patient.PostAsJsonAsync(ChatTestSteps.Url, new OpenChatRequest(nextCooperation.Id))).Body<ChatConversationDto>();
        next.Id.Should().NotBe(s.Chat.Id);
        (await s.Patient.ChatHistory(next.Id)).Items.Should().BeEmpty();
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Should().ContainSingle();
        (await s.Patient.Send(s.Chat.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{nextCooperation.Id}/end", null)).EnsureSuccessStatusCode();
        var (replacement, _) = await factory.CreateConfirmedPatientClientAsync();
        var replacementRequest = await replacement.SendRequestOkAsync(therapistId);
        (await s.Therapist.AcceptWithExistingCardAsync(replacementRequest.Id, s.CardId)).EnsureSuccessStatusCode();
        (await replacement.GetAsync(ChatTestSteps.Messages(s.Chat.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await replacement.GetAsync(ChatTestSteps.Messages(next.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await replacement.Chats()).Items.Should().BeEmpty();
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Unlinking_and_relinking_same_account_cannot_resume_an_old_active_context()
    {
        var s = await factory.ChatScenario();
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.CardId)).UserId = null;
            return await db.SaveChangesAsync();
        });
        (await s.Patient.GetAsync(ChatTestSteps.Conversation(s.Chat.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.CardId)).UserId = s.Chat.PatientUserId;
            return await db.SaveChangesAsync();
        });
        (await s.Patient.GetAsync(ChatTestSteps.Messages(s.Chat.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.Chat(s.Chat.Id)).ReadOnlyReason.Should().Be("account_link_changed");
        (await s.Therapist.Send(s.Chat.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var next = await (await s.Patient.PostAsJsonAsync(ChatTestSteps.Url, new OpenChatRequest(s.CooperationId))).Body<ChatConversationDto>();
        next.Id.Should().NotBe(s.Chat.Id);
    }

    [Fact]
    public async Task Validation_rejects_invalid_messages_and_cursors_without_disclosing_content()
    {
        var s = await factory.ChatScenario();
        foreach (var text in new string?[] { null, "", " \r\n\t", "Unsupported\0character", new('x', 4001) })
        {
            var response = await s.Patient.PostAsJsonAsync(ChatTestSteps.Messages(s.Chat.Id), new SendChatRequest(Guid.NewGuid(), text));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.ErrorCodeAsync()).Should().Be("validation_failed");
        }
        (await s.Patient.Send(s.Chat.Id, "text", Guid.Empty)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var query in new[] { "?before=0", "?after=-1", "?before=1&after=0", "?limit=0", "?limit=101" })
            (await s.Patient.GetAsync(ChatTestSteps.Messages(s.Chat.Id) + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.Read(s.Chat.Id, Guid.Empty)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.GetAsync(ChatTestSteps.Url + "?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Patient.Send(s.Chat.Id, new string('x', 4000))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Durable_per_user_limit_is_shared_between_conversations_and_replays_do_not_consume_it()
    {
        using var limited = new ChatLimitFactory();
        var s = await limited.ChatScenario();
        var other = await limited.ChatScenario(s.Patient);
        var key = Guid.NewGuid();
        (await s.Patient.Send(s.Chat.Id, "One", key)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await s.Patient.Send(other.Chat.Id, "Two")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await s.Patient.Send(s.Chat.Id, "One", key)).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await s.Patient.Send(other.Chat.Id, "Three");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.ErrorCodeAsync()).Should().Be("chat_rate_limit");
        (await s.Therapist.Send(s.Chat.Id)).StatusCode.Should().Be(HttpStatusCode.Created);
        limited.Clock.UtcNow = limited.Clock.UtcNow.AddMinutes(1);
        (await s.Patient.Send(other.Chat.Id, "Three")).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
