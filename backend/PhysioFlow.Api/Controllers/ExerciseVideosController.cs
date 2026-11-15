using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapy;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
[Route("api/exercise-videos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExerciseVideosController : ApiControllerBase
{
    [HttpGet("{id:guid}/playback")]
    [ProducesResponseType(typeof(ExerciseVideoPlaybackDto), 200)]
    public async Task<IActionResult> Playback(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetExerciseVideoPlaybackQuery(id), ct));
}
