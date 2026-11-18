using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Chat;

public class ChatOptions
{
    public int MessagesPerMinute { get; set; } = 30;
}

public static class ChatValidation
{
    public static void Require(bool condition, string field, string message)
    {
        if (!condition) throw new ValidationException([new ValidationFailure(field, message)]);
    }

    public static string Content(SendChatRequest body)
    {
        Require(body.RequestId != Guid.Empty, "requestId", "A nonempty request ID is required.");
        Require(!string.IsNullOrWhiteSpace(body.Content), "content", "Message cannot be empty.");
        Require(body.Content!.Length <= PrivateChatMessage.ContentMaxLength, "content", "Message exceeds 4000 characters.");
        Require(!body.Content.Contains('\0'), "content", "Message contains an unsupported character.");
        return body.Content.Trim();
    }
}

public class ChatService(IApplicationDbContext db, ICurrentUserService user, IUserAccountService accounts,
    ChatAccess access, IDateTimeProvider clock, IOptions<ChatOptions> options, IChatPublisher publisher,
    ILogger<ChatService> logger)
{
    // PostgreSQL stores microseconds; return exactly the same timestamp on first send and every replay.
    private DateTime Now()
    {
        var now = clock.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    private async Task<Guid> Caller(CancellationToken ct)
    {
        var id = user.UserId ?? throw new UnauthorizedAccessException();
        if (!user.IsInRole("Patient") && !user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        await access.RequireCaller(id, ct);
        return id;
    }

    private async Task<ChatConversation> Conversation(Guid id, Guid actor, CancellationToken ct)
    {
        var c = await access.Readable(actor).Include(c => c.Patient).Include(c => c.Cooperation)
            .ThenInclude(r => r.Therapist).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("ChatConversation", id);
        if (actor == c.PatientUserId && !await accounts.IsConfirmedPatientAsync(actor, ct))
            throw new NotFoundException("ChatConversation", id);
        if (actor == c.TherapistUserId && !await accounts.IsActiveTherapistAsync(actor, ct))
            throw new NotFoundException("ChatConversation", id);
        return c;
    }

    private async Task<T> Write<T>(Func<Task<T>> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException
                || ex is ConflictException { Code: "chat_write_race" })
            {
                // SaveChanges is atomic. Reload all permission, sequence, read and quota fences before retrying.
                db.ChangeTracker.Clear();
                if (attempt >= 3) throw new ConflictException("concurrent_modification", "Chat changed. Retry the operation.");
            }
        }
    }

    private static void Fence(ChatConversation c)
    {
        c.Version = Guid.NewGuid();
        c.Patient.ConcurrencyStamp = Guid.NewGuid();
        c.Cooperation.ConcurrencyStamp = Guid.NewGuid();
    }

    public async Task<ChatConversationDto> Open(Guid cooperationId, CancellationToken ct)
    {
        ChatValidation.Require(cooperationId != Guid.Empty, "cooperationId", "A cooperation ID is required.");
        var actor = await Caller(ct);
        var id = await Write(async () =>
        {
            var r = await db.CooperationRequests.Include(r => r.Patient).Include(r => r.Therapist)
                .SingleOrDefaultAsync(r => r.Id == cooperationId
                    && (r.PatientUserId == actor || r.Therapist.UserId == actor), ct)
                ?? throw new NotFoundException("Cooperation", cooperationId);
            if (r.Status != CooperationRequestStatus.Accepted || r.Patient is null
                || r.Patient.IsDeleted || r.Patient.UserId != r.PatientUserId
                || r.Patient.TherapistId != r.TherapistId || r.PatientUserId == r.Therapist.UserId)
                throw new ConflictException("chat_not_active", "An active linked cooperation is required.");
            var previous = await db.ChatConversations.SingleOrDefaultAsync(c => c.CooperationId == r.Id
                && c.AccountLinkVersion == r.Patient.AccountLinkVersion, ct);
            if (previous is not null) return previous.Id;
            var c = new ChatConversation
            {
                CooperationId = r.Id, PatientId = r.Patient.Id, AccountLinkVersion = r.Patient.AccountLinkVersion,
                PatientUserId = r.PatientUserId, TherapistUserId = r.Therapist.UserId,
                Cooperation = r, Patient = r.Patient
            };
            if (await access.ReadOnlyReason(c, ct) is not null)
                throw new ConflictException("chat_not_active", "An active linked cooperation is required.");
            db.ChatConversations.Add(c);
            Fence(c);
            await db.SaveChangesAsync(ct);
            return c.Id;
        }, ct);
        return await Get(id, ct);
    }

    public async Task<ChatConversationDto> Get(Guid id, CancellationToken ct)
    {
        var actor = await Caller(ct);
        return await ToDto(await Conversation(id, actor, ct), actor, ct);
    }

    public async Task<PaginatedList<ChatConversationDto>> List(int pageNumber, int pageSize, CancellationToken ct)
    {
        var actor = await Caller(ct);
        ChatValidation.Require(pageNumber > 0 && pageNumber <= 1_000_000, "pageNumber", "Invalid page number.");
        ChatValidation.Require(pageSize is >= 1 and <= 100, "pageSize", "Page size must be between 1 and 100.");
        var query = access.Readable(actor);
        if (!await accounts.IsActiveTherapistAsync(actor, ct)) query = query.Where(c => c.TherapistUserId != actor);
        if (!await accounts.IsConfirmedPatientAsync(actor, ct)) query = query.Where(c => c.PatientUserId != actor);
        var total = await query.CountAsync(ct);
        var rows = await query.AsNoTracking().Include(c => c.Patient).Include(c => c.Cooperation)
            .ThenInclude(r => r.Therapist)
            .OrderByDescending(c => db.PrivateChatMessages.Where(m => m.ConversationId == c.Id)
                .Select(m => (DateTime?)m.SentAt).Max() ?? c.CreatedAt)
            .ThenBy(c => c.Id).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<ChatConversationDto>();
        foreach (var c in rows) items.Add(await ToDto(c, actor, ct));
        return new(items, total, pageNumber, pageSize);
    }

    private IQueryable<PrivateChatMessage> Messages(Guid id, Guid actor) => db.PrivateChatMessages
        .AsNoTracking().Where(m => m.ConversationId == id && access.Readable(actor).Any(c => c.Id == m.ConversationId));

    public async Task<ChatHistoryDto> History(Guid id, long? before, long? after, int limit, CancellationToken ct)
    {
        var actor = await Caller(ct);
        await Conversation(id, actor, ct);
        ChatValidation.Require(limit is >= 1 and <= 100, "limit", "Limit must be between 1 and 100.");
        ChatValidation.Require(before is null || before > 0, "before", "Invalid cursor.");
        ChatValidation.Require(after is null || after >= 0, "after", "Invalid cursor.");
        ChatValidation.Require(before is null || after is null, "cursor", "Use before or after, never both.");
        var query = Messages(id, actor);
        if (before.HasValue) query = query.Where(m => m.Sequence < before.Value);
        if (after.HasValue) query = query.Where(m => m.Sequence > after.Value);
        var rows = await (after.HasValue ? query.OrderBy(m => m.Sequence) : query.OrderByDescending(m => m.Sequence))
            .Take(limit + 1).ToListAsync(ct);
        var more = rows.Count > limit;
        var selected = rows.Take(limit).ToList();
        long? next = more ? selected.Last().Sequence : null;
        var last = await access.Readable(actor).Where(c => c.Id == id).Select(c => c.LastSequence).SingleAsync(ct);
        return new(selected.OrderBy(m => m.Sequence).Select(ToDto).ToList(), more, next, last);
    }

    public async Task<SendChatResult> Send(Guid id, SendChatRequest body, CancellationToken ct)
    {
        var actor = await Caller(ct);
        var content = ChatValidation.Content(body);
        var result = await Write(async () =>
        {
            var c = await Conversation(id, actor, ct);
            var previous = await db.PrivateChatMessages.AsNoTracking()
                .SingleOrDefaultAsync(m => m.SenderId == actor && m.RequestId == body.RequestId, ct);
            if (previous is not null)
            {
                if (previous.ConversationId != id || previous.Content != content)
                    throw new ConflictException("idempotency_key_reused", "Use a new request ID for a different message.");
                return new SendChatResult(ToDto(previous), true);
            }
            if (await access.ReadOnlyReason(c, ct) is { } reason)
                throw new ConflictException(reason, "This conversation is read-only.");
            var now = Now();
            var window = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            var quota = await db.ChatSendQuotas.SingleOrDefaultAsync(q => q.UserId == actor, ct);
            if (quota is null)
            {
                quota = new() { UserId = actor, WindowStartedAt = window };
                db.ChatSendQuotas.Add(quota);
            }
            if (quota.WindowStartedAt != window) { quota.WindowStartedAt = window; quota.Count = 0; }
            if (quota.Count >= options.Value.MessagesPerMinute)
                throw new TooManyRequestsException("chat_rate_limit", "Message limit reached. Retry in the next minute.");
            quota.Count++;
            quota.Version = Guid.NewGuid();
            var m = new PrivateChatMessage
            {
                ConversationId = id, Sequence = ++c.LastSequence, SenderId = actor,
                RecipientId = actor == c.PatientUserId ? c.TherapistUserId : c.PatientUserId,
                RequestId = body.RequestId, Content = content, SentAt = now
            };
            db.PrivateChatMessages.Add(m);
            Fence(c);
            await db.SaveChangesAsync(ct);
            return new SendChatResult(ToDto(m), false);
        }, ct);
        if (!result.Replayed) await Publish(() => publisher.MessageCreated(result.Message, CancellationToken.None));
        return result;
    }

    public async Task<ChatReadDto> Read(Guid id, Guid throughMessageId, CancellationToken ct)
    {
        var actor = await Caller(ct);
        ChatValidation.Require(throughMessageId != Guid.Empty, "throughMessageId", "A received message ID is required.");
        var result = await Write(async () =>
        {
            var c = await Conversation(id, actor, ct);
            // Only an actually received message in this conversation can advance the cursor.
            var through = await Messages(id, actor).SingleOrDefaultAsync(m => m.Id == throughMessageId
                && m.RecipientId == actor, ct) ?? throw new NotFoundException("ReceivedChatMessage", throughMessageId);
            var current = actor == c.PatientUserId ? c.PatientReadThrough : c.TherapistReadThrough;
            if (through.Sequence > current)
            {
                var unread = await db.PrivateChatMessages.Where(m => m.ConversationId == id
                    && m.RecipientId == actor && m.Sequence <= through.Sequence && m.ReadAt == null).ToListAsync(ct);
                var readAt = Now();
                foreach (var m in unread) m.ReadAt = readAt;
                if (actor == c.PatientUserId) c.PatientReadThrough = through.Sequence;
                else c.TherapistReadThrough = through.Sequence;
                Fence(c);
                await db.SaveChangesAsync(ct);
                current = through.Sequence;
            }
            var count = await Messages(id, actor).CountAsync(m => m.RecipientId == actor && m.ReadAt == null, ct);
            return new ChatReadDto(id, actor, current, count);
        }, ct);
        await Publish(() => publisher.MessagesRead(result, CancellationToken.None));
        return result;
    }

    private async Task Publish(Func<Task> send)
    {
        try { await send(); }
        catch (Exception ex)
        {
            // A durable write is successful even when real-time delivery fails. No exception payload/content in logs.
            logger.LogWarning("Chat real-time delivery failed ({ErrorType}); synchronize through REST", ex.GetType().Name);
        }
    }

    private async Task<ChatConversationDto> ToDto(ChatConversation c, Guid actor, CancellationToken ct)
    {
        var reason = await access.ReadOnlyReason(c, ct);
        var last = await Messages(c.Id, actor).OrderByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);
        var unread = await Messages(c.Id, actor).CountAsync(m => m.RecipientId == actor && m.ReadAt == null, ct);
        return new(c.Id, c.CooperationId, c.PatientUserId, c.TherapistUserId, reason is null, reason,
            last is null ? null : ToDto(last), unread, c.LastSequence,
            actor == c.PatientUserId ? c.PatientReadThrough : c.TherapistReadThrough, c.Version);
    }

    public static ChatMessageDto ToDto(PrivateChatMessage m) => new(m.Id, m.ConversationId, m.Sequence,
        m.SenderId, m.RecipientId, m.RequestId, m.Content, m.SentAt, m.ReadAt);
}
