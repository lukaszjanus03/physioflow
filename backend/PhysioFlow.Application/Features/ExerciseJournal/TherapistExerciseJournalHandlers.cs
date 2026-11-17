using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.ExerciseJournal;

public record TherapistJournalFilters(DateOnly? From = null, DateOnly? To = null,
    Guid? TherapyPlanId = null, Guid? PlanItemId = null, ExerciseCompletionStatus? Status = null);

public record ListTherapistExerciseJournalQuery(Guid PatientId, TherapistJournalFilters Filters,
    int PageNumber = 1, int PageSize = 20) : IRequest<PaginatedList<ExerciseJournalEntryDto>>;
public record GetTherapistExerciseJournalQuery(Guid PatientId, Guid Id) : IRequest<ExerciseJournalEntryDto>;
public record GetTherapistExerciseJournalSummaryQuery(Guid PatientId, TherapistJournalFilters Filters)
    : IRequest<ExerciseJournalSummaryDto>;

public class TherapistExerciseJournalHandlers(IApplicationDbContext db, ICurrentUserService user,
    IUserAccountService accounts) :
    IRequestHandler<ListTherapistExerciseJournalQuery, PaginatedList<ExerciseJournalEntryDto>>,
    IRequestHandler<GetTherapistExerciseJournalQuery, ExerciseJournalEntryDto>,
    IRequestHandler<GetTherapistExerciseJournalSummaryQuery, ExerciseJournalSummaryDto>
{
    private async Task<IQueryable<ExerciseJournalEntry>> Accessible(Guid patientId, CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        var userId = user.RequireUserId();
        var cards = db.Patients.AsNoTracking().Where(p => p.Id == patientId && !p.IsArchived
            && p.UserId != null && p.Therapist.UserId == userId && !p.Therapist.IsDeleted
            && db.CooperationRequests.Any(r => r.PatientId == p.Id && r.TherapistId == p.TherapistId
                && r.PatientUserId == p.UserId && r.Status == CooperationRequestStatus.Accepted));
        var accountId = await cards.Select(p => p.UserId).SingleOrDefaultAsync(ct);
        if (accountId is null || !await accounts.IsConfirmedPatientAsync(accountId.Value, ct))
            throw new NotFoundException("PatientExerciseJournal", patientId);

        // Recheck the relationship in every data query, including the current link generation.
        // Renewed cooperation never implicitly restores an earlier link's health reports.
        // Do not join mutable plans/library: historical reports use their own saved snapshots.
        return db.ExerciseJournalEntries.AsNoTracking().Where(e => !e.IsDeleted
            && e.PatientUserId == accountId.Value && cards.Any(p => p.Id == e.PatientId
                && p.UserId == e.PatientUserId && p.AccountLinkVersion == e.PublicationLinkVersion));
    }

    private static IQueryable<ExerciseJournalEntry> Filter(IQueryable<ExerciseJournalEntry> query,
        TherapistJournalFilters filters)
    {
        TherapyValidation.Require(!filters.From.HasValue || !filters.To.HasValue || filters.From <= filters.To,
            "to", "End date must be on or after start date.");
        TherapyValidation.Require(!filters.From.HasValue || filters.From.Value >= new DateOnly(1900, 1, 1),
            "from", "Invalid date.");
        TherapyValidation.Require(!filters.To.HasValue || filters.To.Value >= new DateOnly(1900, 1, 1),
            "to", "Invalid date.");
        TherapyValidation.Require(filters.TherapyPlanId != Guid.Empty, "therapyPlanId", "Invalid plan ID.");
        TherapyValidation.Require(filters.PlanItemId != Guid.Empty, "planItemId", "Invalid plan position ID.");
        TherapyValidation.Require(!filters.Status.HasValue || Enum.IsDefined(filters.Status.Value),
            "status", "Invalid completion status.");
        if (filters.From.HasValue) query = query.Where(e => e.PerformedOn >= filters.From.Value);
        if (filters.To.HasValue) query = query.Where(e => e.PerformedOn <= filters.To.Value);
        if (filters.TherapyPlanId.HasValue) query = query.Where(e => e.TherapyPlanId == filters.TherapyPlanId.Value);
        if (filters.PlanItemId.HasValue) query = query.Where(e => e.PlanItemId == filters.PlanItemId.Value);
        if (filters.Status.HasValue) query = query.Where(e => e.Status == filters.Status.Value);
        return query;
    }

    public async Task<PaginatedList<ExerciseJournalEntryDto>> Handle(ListTherapistExerciseJournalQuery request,
        CancellationToken ct)
    {
        var accessible = await Accessible(request.PatientId, ct);
        TherapyValidation.Page(request.PageNumber, request.PageSize);
        var query = Filter(accessible, request.Filters);
        var count = await query.CountAsync(ct);
        var entries = await query.OrderByDescending(e => e.PerformedOn).ThenByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id).Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize).ToListAsync(ct);
        return new(entries.Select(ExerciseJournalHandlers.ToDto).ToList(), count, request.PageNumber, request.PageSize);
    }

    public async Task<ExerciseJournalEntryDto> Handle(GetTherapistExerciseJournalQuery request, CancellationToken ct)
    {
        var query = await Accessible(request.PatientId, ct);
        var entry = await query.SingleOrDefaultAsync(e => e.Id == request.Id, ct)
            ?? throw new NotFoundException("ExerciseJournalEntry", request.Id);
        return ExerciseJournalHandlers.ToDto(entry);
    }

    public async Task<ExerciseJournalSummaryDto> Handle(GetTherapistExerciseJournalSummaryQuery request,
        CancellationToken ct)
    {
        var query = Filter(await Accessible(request.PatientId, ct), request.Filters);
        // Aggregate the complete filtered set in SQL, without loading clinical content or paginating.
        // A skip is a report, but is not a performed exercise. Absence of reports creates no skips.
        return await query.GroupBy(e => 1).Select(g => new ExerciseJournalSummaryDto(
            g.Count(e => e.Status == ExerciseCompletionStatus.Completed),
            g.Count(e => e.Status == ExerciseCompletionStatus.Partial),
            g.Count(e => e.Status == ExerciseCompletionStatus.Skipped),
            g.Max(e => e.Status == ExerciseCompletionStatus.Completed || e.Status == ExerciseCompletionStatus.Partial
                ? (DateOnly?)e.PerformedOn : null))).SingleOrDefaultAsync(ct) ?? new(0, 0, 0, null);
    }
}
