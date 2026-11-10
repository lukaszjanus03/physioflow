using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Application.Features.Calendar.Commands.ChangeAppointmentStatus;
using PhysioFlow.Application.Features.Calendar.Commands.CreateAppointment;
using PhysioFlow.Application.Features.Calendar.Commands.UpdateAppointment;
using PhysioFlow.Application.Features.Calendar.Commands.UpdateMyCalendarSettings;
using PhysioFlow.Application.Features.Calendar.Queries.GetAppointments;
using PhysioFlow.Application.Features.Calendar.Queries.GetAvailableSlots;
using PhysioFlow.Application.Features.Calendar.Queries.GetMyCalendarSettings;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Api.Controllers;

public record CreateAppointmentBody(
    Guid? PatientId,
    DateTime? StartsAt,
    int? DurationMinutes,
    string? Reason,
    string? Notes);

public record UpdateAppointmentBody(
    Guid? PatientId,
    DateTime? StartsAt,
    int? DurationMinutes,
    string? Reason,
    string? Notes);

/// <summary>
/// Ustawienia kalendarza zalogowanego terapeuty i wolne terminy.
/// Godziny pracy są w Europe/Warsaw; wizyty w API są w UTC.
/// </summary>
[Route("api/therapists/me/calendar")]
[Authorize(Roles = "Therapist")]
public class TherapistCalendarController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(TherapistCalendarSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMyCalendarSettingsQuery(), ct));

    /// <summary>
    /// Zastępuje godziny tygodniowe, wyjątki dat, bufor i dozwolone długości.
    /// Nie przesuwa ani nie usuwa istniejących wizyt.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(TherapistCalendarSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateMyCalendarSettingsCommand command, CancellationToken ct) =>
        Ok(await Mediator.Send(command, ct));

    /// <summary>
    /// Wolne terminy na datę (dzień kalendarzowy w Warszawie).
    /// Krok startu = wybrana długość wizyty, od początku przedziału dostępności.
    /// excludeAppointmentId pomija własną, przyszłą zaplanowaną wizytę podczas edycji.
    /// </summary>
    [HttpGet("available-slots")]
    [ProducesResponseType(typeof(AvailableSlotsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] DateOnly? date,
        [FromQuery] int? durationMinutes,
        [FromQuery] Guid? excludeAppointmentId,
        CancellationToken ct) =>
        Ok(await Mediator.Send(new GetAvailableSlotsQuery(date, durationMinutes, excludeAppointmentId), ct));
}

[Authorize(Roles = "Therapist")]
public class AppointmentsController : ApiControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(TherapistAppointmentSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        Ok(await Mediator.Send(new GetTherapistAppointmentSummaryQuery(), ct));

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRange(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        Ok(await Mediator.Send(new GetAppointmentsQuery(from, to), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetAppointmentByIdQuery(id), ct));

    [HttpPost]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentBody body, CancellationToken ct)
    {
        var created = await Mediator.Send(
            new CreateAppointmentCommand(body.PatientId, body.StartsAt, body.DurationMinutes, body.Reason, body.Notes),
            ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAppointmentBody body, CancellationToken ct) =>
        Ok(await Mediator.Send(new UpdateAppointmentCommand(
            id, body.PatientId, body.StartsAt, body.DurationMinutes, body.Reason, body.Notes), ct));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new ChangeAppointmentStatusCommand(id, AppointmentStatus.Cancelled), ct));

    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new ChangeAppointmentStatusCommand(id, AppointmentStatus.Completed), ct));

    [HttpPost("{id:guid}/no-show")]
    [ProducesResponseType(typeof(AppointmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkNoShow(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new ChangeAppointmentStatusCommand(id, AppointmentStatus.NoShow), ct));
}
