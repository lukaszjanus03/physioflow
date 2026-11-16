using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.ExerciseJournal;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Patient")]
[Route("api/patient-portal/exercise-journal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExerciseJournalController : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ExerciseJournalEntryDto), 201)]
    [ProducesResponseType(typeof(ExerciseJournalEntryDto), 200)]
    public async Task<IActionResult> Create(CreateExerciseJournalRequest body, CancellationToken ct)
    {
        var result = await Mediator.Send(new CreateExerciseJournalCommand(body), ct);
        return result.Replayed ? Ok(result.Entry) : CreatedAtAction(nameof(Get), new { id = result.Entry.Id }, result.Entry);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<ExerciseJournalEntryDto>), 200)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? therapyPlanId = null, [FromQuery] ExerciseCompletionStatus? status = null,
        CancellationToken ct = default) => Ok(await Mediator.Send(
            new ListExerciseJournalQuery(pageNumber, pageSize, from, to, therapyPlanId, status), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ExerciseJournalEntryDto), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetExerciseJournalQuery(id), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ExerciseJournalEntryDto), 200)]
    public async Task<IActionResult> Update(Guid id, UpdateExerciseJournalRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new UpdateExerciseJournalCommand(id, body), ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] ExerciseJournalVersionRequest body, CancellationToken ct)
    {
        await Mediator.Send(new DeleteExerciseJournalCommand(id, body.Version), ct);
        return NoContent();
    }
}
