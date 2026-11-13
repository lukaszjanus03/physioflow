using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Therapists;

public record GetReviewModerationQueueQuery(int PageNumber = 1, int PageSize = 20, ReviewStatus Status = ReviewStatus.Pending)
    : IRequest<PaginatedList<ModerationReviewDto>>;
public record ModerateTherapistReviewCommand(Guid Id, Guid? Version, ReviewChangeAction Action, string? Reason)
    : IRequest<ModerationReviewDto>;

public class GetReviewModerationQueueValidator : AbstractValidator<GetReviewModerationQueueQuery>
{
    public GetReviewModerationQueueValidator()
    {
        RuleFor(x => x.Status).IsInEnum().WithErrorCode("review_status_invalid").OverridePropertyName("status");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50).WithErrorCode("review_page_invalid").OverridePropertyName("pageSize");
        RuleFor(x => x.PageNumber).Must((q, page) => page >= 1 && (long)(page - 1) * q.PageSize <= int.MaxValue)
            .WithErrorCode("review_page_invalid").OverridePropertyName("pageNumber");
    }
}

public class ModerateTherapistReviewValidator : AbstractValidator<ModerateTherapistReviewCommand>
{
    public ModerateTherapistReviewValidator()
    {
        RuleFor(x => x.Version).NotNull().WithErrorCode("review_version_required")
            .NotEqual(Guid.Empty).WithErrorCode("review_version_required")
            .OverridePropertyName("version");
        RuleFor(x => x.Action).Must(a => a is ReviewChangeAction.Approved or ReviewChangeAction.Rejected or ReviewChangeAction.Withdrawn)
            .WithErrorCode("review_action_invalid").OverridePropertyName("action");
        RuleFor(x => x.Reason).Must((r, reason) => r.Action == ReviewChangeAction.Approved || !string.IsNullOrWhiteSpace(reason))
            .WithErrorCode("review_reason_required")
            .MaximumLength(TherapistReview.CommentMaxLength).WithErrorCode("review_reason_too_long")
            .OverridePropertyName("reason");
    }
}

public class ReviewModeratorAccess(IApplicationDbContext db, ICurrentUserService user, IUserAccountService accounts)
{
    public async Task<Guid> RequireModeratorAsync(CancellationToken ct)
    {
        if (user.UserId is not { } id || !await accounts.IsReviewModeratorAsync(id, ct))
            throw new ForbiddenAccessException("review_moderation_forbidden", "Review moderation permission is required.");
        return id;
    }

    public async Task EnsureNotOwnProfileAsync(Guid moderatorId, Guid therapistId, CancellationToken ct)
    {
        if (await db.Therapists.IgnoreQueryFilters().AnyAsync(t => t.Id == therapistId && t.UserId == moderatorId, ct))
            throw new ForbiddenAccessException("review_self_moderation_forbidden", "You cannot moderate reviews of your own profile.");
    }
}

public class GetReviewModerationQueueHandler(IApplicationDbContext db, ReviewModeratorAccess access)
    : IRequestHandler<GetReviewModerationQueueQuery, PaginatedList<ModerationReviewDto>>
{
    public async Task<PaginatedList<ModerationReviewDto>> Handle(GetReviewModerationQueueQuery request, CancellationToken ct)
    {
        var moderatorId = await access.RequireModeratorAsync(ct);
        var ownProfiles = db.Therapists.IgnoreQueryFilters().Where(t => t.UserId == moderatorId).Select(t => t.Id);
        var query = db.TherapistReviews.AsNoTracking().Where(r => r.Status == request.Status && r.PatientUserId != moderatorId
            && !ownProfiles.Contains(r.TherapistId));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(rows.Select(r => r.ToModerationDto()).ToArray(), total, request.PageNumber, request.PageSize);
    }
}

public class ModerateTherapistReviewHandler(IApplicationDbContext db, ReviewModeratorAccess access,
    ReviewStatusHistory history, IDateTimeProvider clock)
    : IRequestHandler<ModerateTherapistReviewCommand, ModerationReviewDto>
{
    public async Task<ModerationReviewDto> Handle(ModerateTherapistReviewCommand request, CancellationToken ct)
    {
        var moderatorId = await access.RequireModeratorAsync(ct);
        var review = await db.TherapistReviews.SingleOrDefaultAsync(r => r.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(TherapistReview), request.Id);
        await access.EnsureNotOwnProfileAsync(moderatorId, review.TherapistId, ct);
        if (review.PatientUserId == moderatorId)
            throw new ForbiddenAccessException("review_self_moderation_forbidden", "You cannot moderate your own review.");
        if (review.ConcurrencyStamp != request.Version)
            throw new ConflictException("concurrent_modification", "The review has changed. Refresh before moderating.");
        var expected = request.Action == ReviewChangeAction.Withdrawn ? ReviewStatus.Approved : ReviewStatus.Pending;
        if (review.Status != expected)
            throw new ConflictException("review_status_conflict", "The action is unavailable for the current review status.");

        var previousStatus = review.Status;
        review.Status = request.Action == ReviewChangeAction.Approved ? ReviewStatus.Approved : ReviewStatus.Rejected;
        review.RejectionReason = review.Status == ReviewStatus.Rejected ? request.Reason!.Trim() : null;
        review.DecidedAt = clock.UtcNow;
        review.ConcurrencyStamp = Guid.NewGuid();
        history.Record(review, moderatorId, request.Action, previousStatus, review.Status);
        await db.SaveChangesAsync(ct);
        return review.ToModerationDto();
    }
}

public class ReviewStatusHistory(IApplicationDbContext db, IDateTimeProvider clock)
{
    public void Record(TherapistReview review, Guid actorId, ReviewChangeAction action,
        ReviewStatus? from, ReviewStatus? to) => db.ReviewStatusChanges.Add(new ReviewStatusChange
        {
            ReviewId = review.Id, ActorUserId = actorId, Action = action,
            FromStatus = from, ToStatus = to, OccurredAt = clock.UtcNow
        });
}
