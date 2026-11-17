using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.ExerciseJournal;

public record CreateExerciseJournalCommand(CreateExerciseJournalRequest Body) : IRequest<CreateExerciseJournalResult>;
public record UpdateExerciseJournalCommand(Guid Id, UpdateExerciseJournalRequest Body) : IRequest<ExerciseJournalEntryDto>;
public record DeleteExerciseJournalCommand(Guid Id, Guid Version) : IRequest;
public record GetExerciseJournalQuery(Guid Id) : IRequest<ExerciseJournalEntryDto>;
public record ListExerciseJournalQuery(int PageNumber = 1, int PageSize = 20, DateOnly? From = null,
    DateOnly? To = null, Guid? TherapyPlanId = null, ExerciseCompletionStatus? Status = null)
    : IRequest<PaginatedList<ExerciseJournalEntryDto>>;

public class ExerciseJournalHandlers(IApplicationDbContext db, ICurrentUserService user,
    IUserAccountService accounts, IDateTimeProvider clock) :
    IRequestHandler<CreateExerciseJournalCommand, CreateExerciseJournalResult>,
    IRequestHandler<UpdateExerciseJournalCommand, ExerciseJournalEntryDto>,
    IRequestHandler<DeleteExerciseJournalCommand>,
    IRequestHandler<GetExerciseJournalQuery, ExerciseJournalEntryDto>,
    IRequestHandler<ListExerciseJournalQuery, PaginatedList<ExerciseJournalEntryDto>>
{
    private async Task<Guid> AccountId(CancellationToken ct)
    {
        if (!user.IsInRole("Patient")) throw new ForbiddenAccessException();
        var id = user.RequireUserId();
        if (!await accounts.IsConfirmedPatientAsync(id, ct)) throw new ForbiddenAccessException();
        return id;
    }

    private IQueryable<ExerciseJournalEntry> Own(Guid account) => db.ExerciseJournalEntries
        .Where(e => e.PatientUserId == account && !e.IsDeleted);

    private async Task<ExerciseJournalEntry> Entry(Guid id, Guid account, CancellationToken ct) =>
        await Own(account).SingleOrDefaultAsync(e => e.Id == id, ct)
        ?? throw new NotFoundException("ExerciseJournalEntry", id);

    private async Task Validate(ExerciseJournalContent body, CancellationToken ct) =>
        await new ExerciseJournalContentValidator(clock).ValidateAndThrowAsync(body, ct);

    private static void Token(Guid actual, Guid expected)
    {
        TherapyValidation.Require(expected != Guid.Empty, "version", "Edit token is required.");
        if (actual != expected) throw new ConflictException("concurrent_modification", "Report changed. Reload before saving.");
    }

    private static void PublicationDate(DateOnly date, DateTime publishedAt) =>
        TherapyValidation.Require(date >= JournalDates.Date(publishedAt), "performedOn",
            "Performance date cannot precede this publication date in Europe/Warsaw.");

    private static void Apply(ExerciseJournalEntry e, ExerciseJournalContent body)
    {
        e.PerformedOn = body.PerformedOn;
        e.Status = body.Status!.Value;
        e.ActualSets = body.ActualSets;
        e.ActualRepetitions = body.ActualRepetitions;
        e.ActualDurationSeconds = body.ActualDurationSeconds;
        e.PainLevel = body.PainLevel;
        e.Comment = TherapyValidation.Clean(body.Comment);
    }

    private async Task<CreateExerciseJournalResult?> Replay(Guid account, Guid requestId, string hash, CancellationToken ct)
    {
        var previous = await db.ExerciseJournalEntries.AsNoTracking()
            .SingleOrDefaultAsync(e => e.PatientUserId == account && e.RequestId == requestId, ct);
        if (previous is null) return null;
        if (previous.OriginalRequestHash != hash)
            throw new ConflictException("idempotency_key_reused", "Use a new request ID for a different report.");
        if (previous.IsDeleted)
            throw new ConflictException("journal_entry_deleted", "This report was deleted and cannot be recreated by retrying.");
        return new(ToDto(previous), true);
    }

    public async Task<CreateExerciseJournalResult> Handle(CreateExerciseJournalCommand request, CancellationToken ct)
    {
        var account = await AccountId(ct);
        var body = request.Body;
        await Validate(body, ct);
        TherapyValidation.Require(body.RequestId != Guid.Empty, "requestId", "A request ID is required.");
        TherapyValidation.Require(body.TherapyPlanId != Guid.Empty, "therapyPlanId", "A plan ID is required.");
        TherapyValidation.Require(body.PlanItemId != Guid.Empty, "planItemId", "A plan position ID is required.");
        TherapyValidation.Require(body.PlanVersionNumber > 0, "planVersionNumber", "A published version number is required.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(body with { Comment = TherapyValidation.Clean(body.Comment) }))));
        // Replays acknowledge an existing write even after revocation; they never add or restore data.
        if (await Replay(account, body.RequestId, hash, ct) is { } replay) return replay;

        var plan = await db.TherapyPlans.Include(p => p.Patient).Include(p => p.PublishedVersion)
            .SingleOrDefaultAsync(p => p.Id == body.TherapyPlanId && p.PublishedToUserId == account
                && p.Patient.UserId == account && p.PublishedForLinkVersion == p.Patient.AccountLinkVersion
                && p.PublishedAt != null && !p.Patient.IsDeleted && !p.Therapist.IsDeleted
                && p.Patient.TherapistId == p.TherapistId && p.PublishedVersion != null
                && p.PublishedVersion.TherapyPlanId == p.Id
                && p.PublishedVersion.VersionNumber == body.PlanVersionNumber, ct)
            ?? throw new NotFoundException("SharedTherapyPlan", body.TherapyPlanId);
        var snapshot = JsonSerializer.Deserialize<TherapyPlanSnapshot>(plan.PublishedVersion!.ContentJson)!;
        var item = snapshot.Exercises.SingleOrDefault(e => e.Id == body.PlanItemId)
            ?? throw new NotFoundException("SharedPlanPosition", body.PlanItemId);
        PublicationDate(body.PerformedOn, plan.PublishedAt!.Value);
        var entry = new ExerciseJournalEntry
        {
            PatientUserId = account, PatientId = plan.PatientId, TherapyPlanId = plan.Id,
            TherapyPlanVersionId = plan.PublishedVersion.Id, PlanVersionNumber = plan.PublishedVersion.VersionNumber,
            PlanItemId = item.Id, PublicationLinkVersion = plan.PublishedForLinkVersion!.Value,
            PublishedAt = plan.PublishedAt.Value, PlanName = snapshot.Name,
            ExerciseSnapshotJson = JsonSerializer.Serialize(new JournalExerciseSnapshot(item.Name, item.Instructions,
                item.Order, item.Sets, item.Repetitions, item.DurationSeconds, item.Frequency, item.Notes)),
            CreatedAt = JournalDates.Timestamp(clock), RequestId = body.RequestId, OriginalRequestHash = hash
        };
        Apply(entry, body);
        db.ExerciseJournalEntries.Add(entry);
        // Save the report and both access fences atomically. A simultaneous revoke/relink must win
        // before this write or conflict with it, never silently accept a report on a stale grant.
        plan.ConcurrencyStamp = Guid.NewGuid();
        plan.Patient.ConcurrencyStamp = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException
            || ex is ConflictException { Code: "journal_request_exists" })
        {
            // SaveChanges rolled back its transaction. A concurrent identical submission may have won.
            if (await Replay(account, body.RequestId, hash, ct) is { } winner) return winner;
            throw new ConflictException("concurrent_modification", "Publication or patient link changed. Reload before retrying.");
        }
        return new(ToDto(entry), false);
    }

    public async Task<ExerciseJournalEntryDto> Handle(UpdateExerciseJournalCommand request, CancellationToken ct)
    {
        var entry = await Entry(request.Id, await AccountId(ct), ct);
        Token(entry.Version, request.Body.Version);
        await Validate(request.Body, ct);
        PublicationDate(request.Body.PerformedOn, entry.PublishedAt);
        Apply(entry, request.Body);
        entry.UpdatedAt = JournalDates.Timestamp(clock);
        entry.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    public async Task Handle(DeleteExerciseJournalCommand request, CancellationToken ct)
    {
        var entry = await Entry(request.Id, await AccountId(ct), ct);
        Token(entry.Version, request.Version);
        entry.IsDeleted = true;
        entry.DeletedAt = JournalDates.Timestamp(clock);
        entry.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    public async Task<ExerciseJournalEntryDto> Handle(GetExerciseJournalQuery request, CancellationToken ct) =>
        ToDto(await Entry(request.Id, await AccountId(ct), ct));

    public async Task<PaginatedList<ExerciseJournalEntryDto>> Handle(ListExerciseJournalQuery request, CancellationToken ct)
    {
        var account = await AccountId(ct);
        TherapyValidation.Page(request.PageNumber, request.PageSize);
        TherapyValidation.Require(!request.From.HasValue || !request.To.HasValue || request.From <= request.To,
            "to", "End date must be on or after start date.");
        TherapyValidation.Require(!request.From.HasValue || request.From.Value >= new DateOnly(1900, 1, 1), "from", "Invalid date.");
        TherapyValidation.Require(!request.To.HasValue || request.To.Value >= new DateOnly(1900, 1, 1), "to", "Invalid date.");
        TherapyValidation.Require(!request.Status.HasValue || Enum.IsDefined(request.Status.Value), "status", "Invalid completion status.");
        TherapyValidation.Require(request.TherapyPlanId != Guid.Empty, "therapyPlanId", "Invalid plan ID.");
        var query = Own(account).AsNoTracking();
        if (request.From.HasValue) query = query.Where(e => e.PerformedOn >= request.From.Value);
        if (request.To.HasValue) query = query.Where(e => e.PerformedOn <= request.To.Value);
        if (request.TherapyPlanId.HasValue) query = query.Where(e => e.TherapyPlanId == request.TherapyPlanId.Value);
        if (request.Status.HasValue) query = query.Where(e => e.Status == request.Status.Value);
        var count = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(e => e.PerformedOn).ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(entries.Select(ToDto).ToList(), count, request.PageNumber, request.PageSize);
    }

    internal static ExerciseJournalEntryDto ToDto(ExerciseJournalEntry e) => new(e.Id, e.PatientId, e.TherapyPlanId,
        e.TherapyPlanVersionId, e.PlanVersionNumber, e.PlanItemId, e.PublishedAt, e.PlanName,
        JsonSerializer.Deserialize<JournalExerciseSnapshot>(e.ExerciseSnapshotJson)!, e.PerformedOn, e.Status,
        e.ActualSets, e.ActualRepetitions, e.ActualDurationSeconds, e.PainLevel, e.Comment, e.CreatedAt, e.UpdatedAt, e.Version);
}
