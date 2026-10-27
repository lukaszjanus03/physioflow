using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Features.Muscles.Queries.GetMuscleByRegion;

namespace PhysioFlow.Api.Controllers;

/// <summary>
/// Informacje o regionach anatomicznych mapy ciała.
/// </summary>
[Authorize(Roles = "Therapist,Patient")]
public class MusclesController : ApiControllerBase
{
    /// <summary>
    /// Pobiera informacje dla identyfikatora regionu anatomicznego.
    /// </summary>
    [HttpGet("by-region/{regionId}")]
    [ProducesResponseType(typeof(AnatomicalRegionInfoDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetByRegion(
        string regionId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new GetMuscleByRegionQuery(regionId),
            ct);

        return Ok(result);
    }
}