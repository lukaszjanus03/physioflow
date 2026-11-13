using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Api.Controllers;

public record ModerateTherapistReviewRequest(Guid? Version, string? Reason = null);

[Route("api/moderation/therapist-reviews")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
// Admin membership is read from Identity by the handlers on every request.
public class ReviewModerationController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<ModerationReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] ReviewStatus status = ReviewStatus.Pending, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetReviewModerationQueueQuery(pageNumber, pageSize, status), ct));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(ModerationReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, ModerateTherapistReviewRequest request, CancellationToken ct) =>
        Ok(await Mediator.Send(new ModerateTherapistReviewCommand(id, request.Version, ReviewChangeAction.Approved, null), ct));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(ModerationReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, ModerateTherapistReviewRequest request, CancellationToken ct) =>
        Ok(await Mediator.Send(new ModerateTherapistReviewCommand(id, request.Version, ReviewChangeAction.Rejected, request.Reason), ct));

    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(typeof(ModerationReviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid id, ModerateTherapistReviewRequest request, CancellationToken ct) =>
        Ok(await Mediator.Send(new ModerateTherapistReviewCommand(id, request.Version, ReviewChangeAction.Withdrawn, request.Reason), ct));
}
