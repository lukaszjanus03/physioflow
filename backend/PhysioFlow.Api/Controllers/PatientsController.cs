using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Patients.Commands.ArchivePatient;
using PhysioFlow.Application.Features.Patients.Commands.CreatePatient;
using PhysioFlow.Application.Features.Patients.Commands.UnarchivePatient;
using PhysioFlow.Application.Features.Patients.Commands.UpdatePatient;
using PhysioFlow.Application.Features.Patients.Queries.GetPatientById;
using PhysioFlow.Application.Features.Patients.Queries.GetPatientList;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
public class PatientsController : ApiControllerBase
{
    /// <summary>Lista pacjentow zalogowanego terapeuty z paginacja i wyszukiwaniem.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<PatientListItemDto>), 200)]
    public async Task<IActionResult> GetList(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool includeArchived = false,
        [FromQuery] bool onlyArchived = false,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetPatientListQuery(pageNumber, pageSize, search, includeArchived, onlyArchived), ct);
        return Ok(result);
    }

    /// <summary>Utworzenie nowego pacjenta.</summary>
    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePatientCommand command, CancellationToken ct)
    {
        var id = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>Szczegoly pacjenta.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PatientDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPatientByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>Aktualizacja danych pacjenta.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdatePatientCommand command, CancellationToken ct)
    {
        if (id != command.Id)
            return BadRequest(new { error = "ID w URL nie zgadza sie z ID w body." });

        await Mediator.Send(command, ct);
        return NoContent();
    }

    /// <summary>Archiwizacja pacjenta (ustawia IsArchived = true).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await Mediator.Send(new ArchivePatientCommand(id), ct);
        return NoContent();
    }

    /// <summary>Przywrocenie pacjenta z archiwum (ustawia IsArchived = false).</summary>
    [HttpPost("{id:guid}/unarchive")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        await Mediator.Send(new UnarchivePatientCommand(id), ct);
        return NoContent();
    }

    /// <summary>Eksport karty pacjenta do PDF.</summary>
    [HttpGet("{id:guid}/export-pdf")]
    [ProducesResponseType(200)]
    public Task<IActionResult> ExportPdf(Guid id, CancellationToken ct)
    {
        // TODO: ExportPatientPdfQuery -> IPdfGenerationService
        throw new NotImplementedException();
    }
}
