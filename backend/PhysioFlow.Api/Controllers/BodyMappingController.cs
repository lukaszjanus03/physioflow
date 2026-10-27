using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.BodyMapping.Commands.CreateBodyMap;
using PhysioFlow.Application.Features.BodyMapping.Commands.DeleteBodyMap;
using PhysioFlow.Application.Features.BodyMapping.Queries.GetBodyMapById;
using PhysioFlow.Application.Features.BodyMapping.Queries.GetPatientBodyMaps;
using PhysioFlow.Application.Features.BodyMapping.Commands.UpdateBodyMap;

namespace PhysioFlow.Api.Controllers;

/// <summary>
/// Body mapping - interaktywna mapa bólu pacjenta.
/// Dostępne dla terapeutów i pacjentów (pacjent może dodawać własne snapshoty w portalu).
/// </summary>
[Authorize(Roles = "Therapist,Patient")]
public class BodyMappingController : ApiControllerBase
{
    /// <summary>Lista snapshotów body map dla pacjenta, chronologicznie.</summary>
    [HttpGet("patient/{patientId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<BodyMapSummaryDto>), 200)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetByPatient(Guid patientId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPatientBodyMapsQuery(patientId), ct);
        return Ok(result);
    }

    [Authorize(Roles = "Therapist")]
    [HttpPut("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBodyMapRequest request,
        CancellationToken ct)
    {
        await Mediator.Send(
            new UpdateBodyMapCommand(
                id,
                request.Notes,
                request.PainPoints),
            ct);

        return NoContent();
    }

    /// <summary>Pełen snapshot z wszystkimi PainPointami.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BodyMapDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetBodyMapByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Tworzy nowy snapshot body map.</summary>
    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Create([FromBody] CreateBodyMapCommand command, CancellationToken ct)
    {
        var id = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>Usuwa snapshot (pacjent może tylko swoje).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await Mediator.Send(new DeleteBodyMapCommand(id), ct);
        return NoContent();
    }
}

public record UpdateBodyMapRequest(
    string? Notes,
    IReadOnlyList<PainPointInput> PainPoints);