using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists;

namespace PhysioFlow.Api.Controllers;

[Route("api/patient-accounts/me/favorite-therapists")]
[Authorize(Roles = "Patient")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FavoriteTherapistsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<FavoriteTherapistDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetFavoriteTherapistsQuery(pageNumber, pageSize), ct));

    [HttpPut("{therapistId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(Guid therapistId, CancellationToken ct)
    {
        await Mediator.Send(new AddFavoriteTherapistCommand(therapistId), ct);
        return NoContent();
    }

    [HttpDelete("{therapistId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid therapistId, CancellationToken ct)
    {
        await Mediator.Send(new RemoveFavoriteTherapistCommand(therapistId), ct);
        return NoContent();
    }
}
