using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhysioFlow.Api.Options;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists;

namespace PhysioFlow.Api.Controllers;

public record SaveTherapistReviewRequest(int? Rating, string? Comment = null);

[Route("api/patient-accounts/me/therapist-reviews")]
[Authorize(Roles = "Patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TherapistReviewsController : ApiControllerBase
{
    [HttpGet("{therapistId:guid}")]
    [ProducesResponseType(typeof(ReviewEligibilityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwn(Guid therapistId, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetOwnTherapistReviewQuery(therapistId), ct));

    [HttpPost("{therapistId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.TherapistReviewWrite)]
    [ProducesResponseType(typeof(OwnTherapistReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(Guid therapistId, SaveTherapistReviewRequest request, CancellationToken ct)
    {
        var review = await Mediator.Send(new SaveTherapistReviewCommand(therapistId, request.Rating, request.Comment, true), ct);
        return CreatedAtAction(nameof(GetOwn), new { therapistId }, review);
    }

    [HttpPut("{therapistId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.TherapistReviewWrite)]
    [ProducesResponseType(typeof(OwnTherapistReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Update(Guid therapistId, SaveTherapistReviewRequest request, CancellationToken ct) =>
        Ok(await Mediator.Send(new SaveTherapistReviewCommand(therapistId, request.Rating, request.Comment, false), ct));

    [HttpDelete("{therapistId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid therapistId, CancellationToken ct)
    {
        await Mediator.Send(new DeleteTherapistReviewCommand(therapistId), ct);
        return NoContent();
    }
}

[Route("api/therapists/{therapistId:guid}/reviews")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicTherapistCatalog)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PublicTherapistReviewsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PublicTherapistReviewsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid therapistId, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetPublicTherapistReviewsQuery(therapistId, pageNumber, pageSize), ct));
}
