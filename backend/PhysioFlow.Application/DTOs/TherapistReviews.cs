using PhysioFlow.Application.Common.Models;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.DTOs;

public record OwnTherapistReviewDto(Guid Id, Guid TherapistId, int Rating, string? Comment,
    ReviewStatus Status, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? DecidedAt,
    string? RejectionReason, Guid Version);

public record ReviewEligibilityDto(bool CanCreate, string? ReasonCode, OwnTherapistReviewDto? Review);

public record PublicTherapistReviewDto(int Rating, string? Comment, DateTime Date,
    string FirstName, string? LastNameInitial);

public record PublicTherapistReviewsDto(PaginatedList<PublicTherapistReviewDto> Reviews,
    double? AverageRating, int ReviewCount);

public record ModerationReviewDto(Guid Id, Guid TherapistId, int Rating, string? Comment,
    ReviewStatus Status, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? DecidedAt,
    string? RejectionReason, Guid Version);
