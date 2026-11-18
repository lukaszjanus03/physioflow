using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.MedicalRecords;
using PhysioFlow.Application.Features.PatientSharing;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
[Route("api/medical-records")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MedicalRecordsController : ApiControllerBase
{
    [HttpPost("{id:guid}/patient-summary/publish")]
    [ProducesResponseType(typeof(MedicalRecordDto), 200)]
    public async Task<IActionResult> PublishSummary(Guid id, [FromBody] PublishPatientContentRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new PublishVisitSummaryCommand(id, body), ct));

    [HttpPost("{id:guid}/patient-summary/revoke")]
    [ProducesResponseType(typeof(MedicalRecordDto), 200)]
    public async Task<IActionResult> RevokeSummary(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new RevokeVisitSummaryCommand(id, body.Version), ct));

    [HttpGet("/api/patients/{patientId:guid}/medical-records")]
    [ProducesResponseType(typeof(PaginatedList<MedicalRecordListItemDto>), 200)]
    public async Task<IActionResult> List(Guid patientId, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new ListMedicalRecordsQuery(patientId, pageNumber, pageSize), ct));

    [HttpGet("/api/patients/{patientId:guid}/medical-records/export")]
    [ProducesResponseType(typeof(PaginatedList<MedicalRecordExportItemDto>), 200)]
    public async Task<IActionResult> Export(Guid patientId, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new ExportMedicalRecordsQuery(patientId, from, to, pageNumber, pageSize), ct));

    [HttpPost("/api/patients/{patientId:guid}/medical-records")]
    [ProducesResponseType(typeof(MedicalRecordDto), 201)]
    public async Task<IActionResult> Create(Guid patientId, [FromBody] CreateMedicalRecordRequest body, CancellationToken ct)
    {
        var record = await Mediator.Send(new CreateMedicalRecordCommand(patientId, body), ct);
        return CreatedAtAction(nameof(Get), new { id = record.Id }, record);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MedicalRecordDto), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMedicalRecordQuery(id), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(MedicalRecordDto), 200)]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveMedicalRecordRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new SaveMedicalRecordCommand(id, body), ct));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(MedicalRecordDto), 200)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveMedicalRecordRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new ApproveMedicalRecordCommand(id, body.Version), ct));

    [HttpPost("{id:guid}/corrections")]
    [ProducesResponseType(typeof(MedicalRecordDto), 200)]
    public async Task<IActionResult> Correct(Guid id, [FromBody] CorrectMedicalRecordRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new CorrectMedicalRecordCommand(id, body), ct));

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<MedicalRecordVersionDto>), 200)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMedicalRecordHistoryQuery(id), ct));
}
