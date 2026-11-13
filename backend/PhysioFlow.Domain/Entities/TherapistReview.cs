using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

public class TherapistReview : BaseEntity
{
    public const int CommentMaxLength = 1000;
    public Guid PatientUserId { get; set; }
    public Guid TherapistId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public DateTime? DecidedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

// Independent of the review FK so deleting a review preserves its status history.
// Content and rejection reasons must never be copied into this record.
public class ReviewStatusChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReviewId { get; set; }
    public Guid ActorUserId { get; set; }
    public ReviewChangeAction Action { get; set; }
    public ReviewStatus? FromStatus { get; set; }
    public ReviewStatus? ToStatus { get; set; }
    public DateTime OccurredAt { get; set; }
}
