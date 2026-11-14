using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
[Route("api/therapy-plans")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TherapyPlansController : ApiControllerBase
{
    [HttpGet("/api/patients/{patientId:guid}/therapy-plans")]
    [ProducesResponseType(typeof(PaginatedList<TherapyPlanListItemDto>), 200)]
    public async Task<IActionResult> List(Guid patientId, [FromQuery] TherapyPlanStatus? status = null,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new ListTherapyPlansQuery(patientId, status, pageNumber, pageSize), ct));

    [HttpPost("/api/patients/{patientId:guid}/therapy-plans")]
    [ProducesResponseType(typeof(TherapyPlanDto), 201)]
    public async Task<IActionResult> Create(Guid patientId, [FromBody] TherapyPlanContent body, CancellationToken ct)
    {
        var plan = await Mediator.Send(new CreateTherapyPlanCommand(patientId, body), ct);
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, plan);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TherapyPlanDto), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await Mediator.Send(new GetTherapyPlanQuery(id), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(TherapyPlanDto), 200)]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveTherapyPlanRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new SaveTherapyPlanCommand(id, body), ct));

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(TherapyPlanDto), 200)]
    public async Task<IActionResult> Activate(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new ActivateTherapyPlanCommand(id, body.Version), ct));

    [HttpPost("{id:guid}/corrections")]
    [ProducesResponseType(typeof(TherapyPlanDto), 200)]
    public async Task<IActionResult> Correct(Guid id, [FromBody] CorrectTherapyPlanRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new CorrectTherapyPlanCommand(id, body), ct));

    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(TherapyPlanDto), 200)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new TransitionTherapyPlanCommand(id, body.Version, TherapyPlanStatus.Completed), ct));

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(TherapyPlanDto), 200)]
    public async Task<IActionResult> Archive(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new TransitionTherapyPlanCommand(id, body.Version, TherapyPlanStatus.Archived), ct));

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<TherapyPlanVersionDto>), 200)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetTherapyPlanHistoryQuery(id), ct));
}
