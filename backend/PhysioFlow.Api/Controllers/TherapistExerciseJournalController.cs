using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.ExerciseJournal;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
[Route("api/patients/{patientId:guid}/exercise-journal")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TherapistExerciseJournalController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<ExerciseJournalEntryDto>), 200)]
    public async Task<IActionResult> List(Guid patientId, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? therapyPlanId = null, [FromQuery] Guid? planItemId = null,
        [FromQuery] ExerciseCompletionStatus? status = null, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new ListTherapistExerciseJournalQuery(patientId,
            new(from, to, therapyPlanId, planItemId, status), pageNumber, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ExerciseJournalEntryDto), 200)]
    public async Task<IActionResult> Get(Guid patientId, Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetTherapistExerciseJournalQuery(patientId, id), ct));

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ExerciseJournalSummaryDto), 200)]
    public async Task<IActionResult> Summary(Guid patientId, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] Guid? therapyPlanId = null,
        [FromQuery] Guid? planItemId = null, [FromQuery] ExerciseCompletionStatus? status = null,
        CancellationToken ct = default) => Ok(await Mediator.Send(new GetTherapistExerciseJournalSummaryQuery(
            patientId, new(from, to, therapyPlanId, planItemId, status)), ct));
}
