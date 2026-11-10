using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;

namespace PhysioFlow.Api.Controllers;

public record BookPatientAppointmentBody(Guid? TherapistId, DateTime? StartsAt, int? DurationMinutes,
    string? PatientMessage = null);

[Route("api/patient-portal/appointments")]
[Authorize(Roles = "Patient")]
public class PatientAppointmentsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PatientAppointmentDto>), 200)]
    public async Task<IActionResult> GetRange([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await Mediator.Send(new GetPatientAppointmentsQuery(from, to), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PatientAppointmentDto), 200)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await Mediator.Send(new GetPatientAppointmentQuery(id), ct));

    [HttpGet("summary")]
    [ProducesResponseType(typeof(PatientAppointmentSummaryDto), 200)]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(await Mediator.Send(new GetPatientAppointmentSummaryQuery(), ct));

    [HttpGet("therapists/{therapistId:guid}/settings")]
    [ProducesResponseType(typeof(PatientBookingSettingsDto), 200)]
    public async Task<IActionResult> Settings(Guid therapistId, CancellationToken ct)
        => Ok(await Mediator.Send(new GetPatientBookingSettingsQuery(therapistId), ct));

    [HttpGet("therapists/{therapistId:guid}/available-slots")]
    [ProducesResponseType(typeof(AvailableSlotsDto), 200)]
    public async Task<IActionResult> Slots(Guid therapistId, [FromQuery] DateOnly? date,
        [FromQuery] int? durationMinutes, CancellationToken ct)
        => Ok(await Mediator.Send(new GetPatientAvailableSlotsQuery(therapistId, date, durationMinutes), ct));

    [HttpPost]
    [ProducesResponseType(typeof(PatientAppointmentDto), 201)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Book(BookPatientAppointmentBody body, CancellationToken ct)
    {
        var result = await Mediator.Send(new BookPatientAppointmentCommand(
            body.TherapistId, body.StartsAt, body.DurationMinutes, body.PatientMessage), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(PatientAppointmentDto), 200)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(await Mediator.Send(new CancelPatientAppointmentCommand(id), ct));
}
