using System.Globalization;
using System.Text.RegularExpressions;
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

public record GetOwnTherapistReviewQuery(Guid TherapistId) : IRequest<ReviewEligibilityDto>;
public record SaveTherapistReviewCommand(Guid TherapistId, int? Rating, string? Comment, bool IsCreate)
    : IRequest<OwnTherapistReviewDto>;
public record DeleteTherapistReviewCommand(Guid TherapistId) : IRequest;
public record GetPublicTherapistReviewsQuery(Guid TherapistId, int PageNumber = 1, int PageSize = 20)
    : IRequest<PublicTherapistReviewsDto>;

public class SaveTherapistReviewValidator : AbstractValidator<SaveTherapistReviewCommand>
{
    public SaveTherapistReviewValidator()
    {
        RuleFor(x => x.Rating).NotNull().WithErrorCode("review_rating_invalid").InclusiveBetween(1, 5)
            .WithErrorCode("review_rating_invalid").OverridePropertyName("rating");
        RuleFor(x => x.Comment).Cascade(CascadeMode.Stop)
            .MaximumLength(TherapistReview.CommentMaxLength).WithErrorCode("review_comment_too_long")
            .Must(ReviewContentRules.HasNoContactDetails).WithErrorCode("review_contact_details")
            .WithMessage("Do not include contact details in a review.").OverridePropertyName("comment");
    }
}

public static partial class ReviewContentRules
{
    // Detailed medical documentation is checked by the human moderator before approval.
    // Bound regex execution and input size even if this method is called outside validation.
    [GeneratedRegex(@"@|https?://|www\.|\b[\p{L}\d-]+\.(?:pl|com|org|net|eu)\b|(?:\+?\d[\s().-]*){7,}",
        RegexOptions.IgnoreCase, 100)]
    private static partial Regex ContactDetails();

    public static bool HasNoContactDetails(string? comment) => comment is null
        || (comment.Length <= TherapistReview.CommentMaxLength && !ContactDetails().IsMatch(comment));
}

public class GetPublicTherapistReviewsValidator : AbstractValidator<GetPublicTherapistReviewsQuery>
{
    public GetPublicTherapistReviewsValidator()
    {
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50).WithErrorCode("review_page_invalid")
            .OverridePropertyName("pageSize");
        RuleFor(x => x.PageNumber).Must((q, page) => page >= 1 && (long)(page - 1) * q.PageSize <= int.MaxValue)
            .WithErrorCode("review_page_invalid").OverridePropertyName("pageNumber");
    }
}

public class TherapistReviewAccess(IApplicationDbContext db, ICurrentUserService user, IUserAccountService accounts)
{
    public async Task<Guid> RequirePatientAsync(CancellationToken ct)
    {
        var id = FavoriteTherapistAccess.RequirePatient(user);
        if (!await accounts.IsConfirmedPatientAsync(id, ct))
            throw new ForbiddenAccessException("email_confirmation_required", "A confirmed patient account is required.");
        return id;
    }

    public Task<bool> HasCompletedVisitAsync(Guid patientId, Guid therapistId, CancellationToken ct) =>
        db.Appointments.AnyAsync(a => a.TherapistId == therapistId && a.PatientUserId == patientId
            && a.Status == AppointmentStatus.Completed, ct);

    public async Task<string?> IneligibilityAsync(Guid patientId, Guid therapistId, CancellationToken ct)
    {
        return await HasCompletedVisitAsync(patientId, therapistId, ct) ? null : "review_completed_visit_required";
    }
}

public class GetOwnTherapistReviewHandler(IApplicationDbContext db, TherapistReviewAccess access)
    : IRequestHandler<GetOwnTherapistReviewQuery, ReviewEligibilityDto>
{
    public async Task<ReviewEligibilityDto> Handle(GetOwnTherapistReviewQuery request, CancellationToken ct)
    {
        var patientId = await access.RequirePatientAsync(ct);
        var review = await db.TherapistReviews.AsNoTracking()
            .SingleOrDefaultAsync(r => r.PatientUserId == patientId && r.TherapistId == request.TherapistId, ct);
        var reason = review is null ? await access.IneligibilityAsync(patientId, request.TherapistId, ct) : "review_already_exists";
        return new(reason is null, reason, review?.ToOwnDto());
    }
}

public class SaveTherapistReviewHandler(IApplicationDbContext db, TherapistReviewAccess access,
    ReviewStatusHistory history) : IRequestHandler<SaveTherapistReviewCommand, OwnTherapistReviewDto>
{
    public async Task<OwnTherapistReviewDto> Handle(SaveTherapistReviewCommand request, CancellationToken ct)
    {
        var patientId = await access.RequirePatientAsync(ct);
        var review = await db.TherapistReviews
            .SingleOrDefaultAsync(r => r.PatientUserId == patientId && r.TherapistId == request.TherapistId, ct);
        if (request.IsCreate && review is not null)
            throw new ConflictException("review_already_exists", "An opinion for this therapist already exists.");
        if (!request.IsCreate && review is null)
            throw new NotFoundException(nameof(TherapistReview), request.TherapistId);

        var reason = await access.IneligibilityAsync(patientId, request.TherapistId, ct);
        if (reason is not null)
            throw new ConflictException(reason, "A completed visit with this therapist is required.");

        var previousStatus = review?.Status;
        if (review is null)
        {
            review = new TherapistReview { PatientUserId = patientId, TherapistId = request.TherapistId };
            db.TherapistReviews.Add(review);
        }
        review.Rating = request.Rating!.Value;
        review.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        review.Status = ReviewStatus.Pending;
        review.DecidedAt = null;
        review.RejectionReason = null;
        review.ConcurrencyStamp = Guid.NewGuid();
        history.Record(review, patientId, request.IsCreate ? ReviewChangeAction.Created : ReviewChangeAction.Edited,
            previousStatus, ReviewStatus.Pending);
        await db.SaveChangesAsync(ct);
        return review.ToOwnDto();
    }
}

public class DeleteTherapistReviewHandler(IApplicationDbContext db, TherapistReviewAccess access,
    ReviewStatusHistory history) : IRequestHandler<DeleteTherapistReviewCommand>
{
    public async Task Handle(DeleteTherapistReviewCommand request, CancellationToken ct)
    {
        var patientId = await access.RequirePatientAsync(ct);
        var review = await db.TherapistReviews
            .SingleOrDefaultAsync(r => r.PatientUserId == patientId && r.TherapistId == request.TherapistId, ct);
        if (review is null) return;
        history.Record(review, patientId, ReviewChangeAction.Deleted, review.Status, null);
        db.TherapistReviews.Remove(review);
        await db.SaveChangesAsync(ct);
    }
}

public class GetPublicTherapistReviewsHandler(IApplicationDbContext db, IUserAccountService accounts)
    : IRequestHandler<GetPublicTherapistReviewsQuery, PublicTherapistReviewsDto>
{
    public async Task<PublicTherapistReviewsDto> Handle(GetPublicTherapistReviewsQuery request, CancellationToken ct)
    {
        if (!await db.Therapists.PubliclyAvailable(accounts).AnyAsync(t => t.Id == request.TherapistId, ct))
            throw new NotFoundException(nameof(TherapistProfile), request.TherapistId);
        var reviews = db.TherapistReviews.AsNoTracking()
            .Where(r => r.TherapistId == request.TherapistId && r.Status == ReviewStatus.Approved);
        // One aggregate query gives a consistent count/average without persisted counters.
        var summary = await reviews.GroupBy(r => r.TherapistId)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double?)r.Rating) }).SingleOrDefaultAsync(ct);
        var page = await (from r in reviews
                          join author in accounts.ReviewAuthorNames() on r.PatientUserId equals author.Id
                          orderby r.CreatedAt descending, r.Id
                          select new { r.Rating, r.Comment, Date = r.UpdatedAt ?? r.CreatedAt, author.FirstName, author.LastName })
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var items = page.Select(r => new PublicTherapistReviewDto(r.Rating, r.Comment, r.Date,
            r.FirstName?.Trim() ?? "", string.IsNullOrWhiteSpace(r.LastName) ? null
                : StringInfo.GetNextTextElement(r.LastName.Trim()) + ".")).ToArray();
        return new(new PaginatedList<PublicTherapistReviewDto>(items, summary?.Count ?? 0,
            request.PageNumber, request.PageSize), summary?.Average, summary?.Count ?? 0);
    }
}

internal static class TherapistReviewMapping
{
    public static OwnTherapistReviewDto ToOwnDto(this TherapistReview r) => new(r.Id, r.TherapistId,
        r.Rating, r.Comment, r.Status, r.CreatedAt, r.UpdatedAt, r.DecidedAt, r.RejectionReason, r.ConcurrencyStamp);

    public static ModerationReviewDto ToModerationDto(this TherapistReview r) => new(r.Id, r.TherapistId,
        r.Rating, r.Comment, r.Status, r.CreatedAt, r.UpdatedAt, r.DecidedAt, r.RejectionReason, r.ConcurrencyStamp);
}
