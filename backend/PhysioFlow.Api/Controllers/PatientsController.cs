using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Patients.Commands.CreatePatient;
using PhysioFlow.Application.Features.Patients.Queries.GetPatientList;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
public class PatientsController : ApiControllerBase
{
    /// <summary>Lista pacjentów zalogowanego terapeuty z paginacją i wyszukiwaniem.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<PatientListItemDto>), 200)]
    public async Task<IActionResult> GetList(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetPatientListQuery(pageNumber, pageSize, search, includeArchived), ct);
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

    /// <summary>Szczegóły pacjenta.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PatientDetailDto), 200)]
    [ProducesResponseType(404)]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        // TODO: GetPatientByIdQuery
        throw new NotImplementedException();
    }

    /// <summary>Archiwizacja pacjenta (soft delete).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        // TODO: ArchivePatientCommand
        throw new NotImplementedException();
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
