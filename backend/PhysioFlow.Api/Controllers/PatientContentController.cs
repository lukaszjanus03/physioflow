using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.PatientSharing;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Patient")]
[Route("api/patient-portal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PatientContentController : ApiControllerBase
{
    [HttpGet("therapy-plans")]
    [ProducesResponseType(typeof(PaginatedList<PatientTherapyPlanListItemDto>), 200)]
    public async Task<IActionResult> Plans([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await Mediator.Send(new ListPatientTherapyPlansQuery(pageNumber, pageSize), ct));

    [HttpGet("therapy-plans/{id:guid}")]
    [ProducesResponseType(typeof(PatientTherapyPlanDto), 200)]
    public async Task<IActionResult> Plan(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetPatientTherapyPlanQuery(id), ct));

    [HttpGet("therapy-plans/{id:guid}/exercises/{itemId:guid}/video/playback")]
    [ProducesResponseType(typeof(ExerciseVideoPlaybackDto), 200)]
    public async Task<IActionResult> Playback(Guid id, Guid itemId, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetPatientPlanVideoPlaybackQuery(id, itemId), ct));

    [HttpGet("visit-summaries")]
    [ProducesResponseType(typeof(PaginatedList<PatientVisitSummaryListItemDto>), 200)]
    public async Task<IActionResult> Summaries([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await Mediator.Send(new ListPatientVisitSummariesQuery(pageNumber, pageSize), ct));

    [HttpGet("visit-summaries/{id:guid}")]
    [ProducesResponseType(typeof(PatientVisitSummaryDto), 200)]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetPatientVisitSummaryQuery(id), ct));
}
