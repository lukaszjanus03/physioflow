using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Chat;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public sealed class ChatWriteBarrier : SaveChangesInterceptor
{
    private int _arrived;
    private bool _enabled;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Enable() { _arrived = 0; _ready = new(TaskCreationOptions.RunContinuationsAsynchronously); _enabled = true; }
    public void Disable() => _enabled = false;
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_enabled && eventData.Context!.ChangeTracker.Entries().Any(e => e.State is EntityState.Added or EntityState.Modified
                && e.Entity is ChatConversation or PrivateChatMessage or Patient or CooperationRequest))
        {
            if (Interlocked.Increment(ref _arrived) >= 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public sealed class PostgresChatFactory : PostgresCalendarFactory
{
    public ChatWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<PhysioFlow.Infrastructure.Persistence.ApplicationDbContext>(o => o.AddInterceptors(Writes));
    }
    public async Task<HttpResponseMessage[]> Race(Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        Writes.Enable();
        try { return await Task.WhenAll(first(), second()); }
        finally { Writes.Disable(); }
    }
}

public class PostgresChatTests
{
    [PostgresFact]
    public async Task Complete_chat_contract_runs_on_postgresql()
    {
        using var factory = new PostgresChatFactory();
        var tests = new ChatTests(factory);
        await tests.Participants_exchange_persisted_messages_with_server_identity_time_and_private_lists();
        await tests.Cursor_history_is_stable_and_reconnect_recovers_every_missing_message();
        await tests.Read_watermarks_are_monotonic_recipient_owned_and_never_include_newer_messages();
        await tests.Retries_are_idempotent_even_after_cooperation_ends_and_key_reuse_is_rejected();
        await tests.Outsiders_and_admins_cannot_open_read_send_or_mark_other_conversations();
        await tests.Archive_end_and_relink_preserve_only_original_participants_history_and_create_new_contexts();
        await tests.Unlinking_and_relinking_same_account_cannot_resume_an_old_active_context();
        await tests.Validation_rejects_invalid_messages_and_cursors_without_disclosing_content();
    }

    [PostgresFact]
    public async Task Concurrent_retries_and_distinct_sends_never_duplicate_messages_or_sequences()
    {
        using var factory = new PostgresChatFactory();
        var s = await factory.ChatScenario();
        var key = Guid.NewGuid();
        var retries = await factory.Race(() => s.Patient.Send(s.Chat.Id, "Same", key), () => s.Patient.Send(s.Chat.Id, "Same", key));
        retries.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.OK]);
        var values = await Task.WhenAll(retries.Select(r => r.Body<ChatMessageDto>(r.StatusCode)));
        values[0].Id.Should().Be(values[1].Id);
        var distinct = await factory.Race(() => s.Patient.Send(s.Chat.Id, "A"), () => s.Therapist.Send(s.Chat.Id, "B"));
        distinct.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Select(m => m.Sequence).Should().Equal(1, 2, 3);
        key = Guid.NewGuid();
        var changed = await factory.Race(() => s.Patient.Send(s.Chat.Id, "One", key), () => s.Patient.Send(s.Chat.Id, "Two", key));
        changed.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);
        (await changed.Single(r => r.StatusCode == HttpStatusCode.Conflict).ErrorCodeAsync()).Should().Be("idempotency_key_reused");
    }

    [PostgresFact]
    public async Task Concurrent_read_watermarks_only_advance_and_newer_messages_remain_unread()
    {
        using var factory = new PostgresChatFactory();
        var s = await factory.ChatScenario();
        var first = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var second = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var reads = await factory.Race(() => s.Therapist.Read(s.Chat.Id, first.Id), () => s.Therapist.Read(s.Chat.Id, second.Id));
        reads.Should().OnlyContain(r => r.IsSuccessStatusCode);
        (await s.Therapist.Chat(s.Chat.Id)).ReadThrough.Should().Be(2);
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Should().OnlyContain(m => m.ReadAt != null);
        var third = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var race = await factory.Race(() => s.Therapist.Read(s.Chat.Id, third.Id), () => s.Patient.Send(s.Chat.Id));
        race.Should().OnlyContain(r => r.IsSuccessStatusCode);
        var state = await s.Therapist.Chat(s.Chat.Id);
        state.ReadThrough.Should().Be(3);
        state.UnreadCount.Should().Be(1);
    }

    [PostgresFact]
    public async Task Sending_racing_archive_or_end_of_cooperation_cannot_commit_on_a_stale_grant()
    {
        using var factory = new PostgresChatFactory();
        foreach (var end in new[] { false, true })
        {
            var s = await factory.ChatScenario();
            var race = await factory.Race(() => s.Patient.Send(s.Chat.Id), () => end
                ? s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null)
                : s.Therapist.DeleteAsync($"/api/patients/{s.CardId}"));
            race.Count(r => r.IsSuccessStatusCode).Should().Be(1);
            race.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
            var count = (await s.Therapist.ChatHistory(s.Chat.Id)).Items.Count;
            count.Should().Be(race[0].IsSuccessStatusCode ? 1 : 0);
            if (race[1].IsSuccessStatusCode)
                (await s.Patient.Send(s.Chat.Id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [PostgresFact]
    public async Task Concurrent_conversations_share_the_last_available_per_user_send_permit()
    {
        using var factory = new PostgresChatFactory();
        var s = await factory.ChatScenario();
        var other = await factory.ChatScenario(s.Patient);
        factory.Services.GetRequiredService<IOptions<ChatOptions>>().Value.MessagesPerMinute = 1;
        var race = await factory.Race(() => s.Patient.Send(s.Chat.Id), () => s.Patient.Send(other.Chat.Id));
        race.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.TooManyRequests]);
        (await factory.WithDbAsync(db => db.PrivateChatMessages.CountAsync(m => m.SenderId == s.Chat.PatientUserId))).Should().Be(1);
    }

    [PostgresFact]
    public async Task Concurrent_opening_creates_only_one_conversation_per_cooperation_and_account_link()
    {
        using var factory = new PostgresChatFactory();
        var (therapist, therapistId) = await factory.CreatePublishedTherapistAsync();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        var request = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptAsNewCardAsync(request.Id)).EnsureSuccessStatusCode();
        var race = await factory.Race(() => patient.PostAsJsonAsync(ChatTestSteps.Url, new OpenChatRequest(request.Id)),
            () => therapist.PostAsJsonAsync(ChatTestSteps.Url, new OpenChatRequest(request.Id)));
        race.Should().OnlyContain(r => r.IsSuccessStatusCode);
        var chats = await Task.WhenAll(race.Select(r => r.Body<ChatConversationDto>()));
        chats[0].Id.Should().Be(chats[1].Id);
        (await factory.WithDbAsync(db => db.ChatConversations.CountAsync())).Should().Be(1);
    }

    [PostgresFact]
    public async Task Migration_preserves_legacy_unscoped_messages_without_exposing_them_as_private_conversations()
    {
        using var factory = new PostgresChatFactory();
        // No conversations exist yet, so rollback/reapply only affects this new schema in the isolated database.
        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261116205703_AddExerciseJournal");
            db.ChatMessages.Add(new ChatMessage { SenderId = Guid.NewGuid(), RecipientId = Guid.NewGuid(), Content = "Legacy" });
            await db.SaveChangesAsync();
            await migrator.MigrateAsync();
            db.Database.HasPendingModelChanges().Should().BeFalse();
            (await db.ChatMessages.CountAsync()).Should().Be(1);
            (await db.PrivateChatMessages.CountAsync()).Should().Be(0);
            return 0;
        });
        var s = await factory.ChatScenario();
        (await s.Patient.ChatHistory(s.Chat.Id)).Items.Should().BeEmpty();
        var message = await (await s.Patient.Send(s.Chat.Id)).Body<ChatMessageDto>(HttpStatusCode.Created);
        var duplicate = () => factory.WithDbAsync(async db =>
        {
            db.PrivateChatMessages.Add(new PrivateChatMessage
            {
                ConversationId = s.Chat.Id, Sequence = 2, SenderId = message.SenderId, RecipientId = message.RecipientId,
                Content = "Duplicate", RequestId = message.RequestId, SentAt = DateTime.UtcNow
            });
            return await db.SaveChangesAsync();
        });
        await duplicate.Should().ThrowAsync<PhysioFlow.Application.Common.Exceptions.ConflictException>();
    }
}
