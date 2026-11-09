using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhysioFlow.Api.Options;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation.Commands.AcceptCooperationRequest;
using PhysioFlow.Application.Features.Cooperation.Commands.RejectCooperationRequest;
using PhysioFlow.Application.Features.Cooperation.Commands.SendCooperationRequest;
using PhysioFlow.Application.Features.Cooperation.Commands.WithdrawCooperationRequest;
using PhysioFlow.Application.Features.Cooperation.Queries.GetMyCooperationRequests;
using PhysioFlow.Application.Features.Cooperation.Queries.GetMyCooperations;
using PhysioFlow.Application.Features.Cooperation.Queries.GetTherapistCooperationRequests;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Api.Controllers;

public record SendCooperationRequestBody(Guid? TherapistId, string? Message);

public record AcceptCooperationRequestBody(
    bool? IdentityVerified,
    CardLinkMode? CardAction,
    Guid? PatientId,
    NewPatientCardInput? NewPatient);

/// <summary>Prośby o współpracę i współprace zalogowanego pacjenta. Nadawcą jest zawsze konto z sesji.</summary>
[Route("api/patient-accounts/me")]
[Authorize(Roles = "Patient")]
public class PatientCooperationController : ApiControllerBase
{
    [HttpGet("cooperation-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<PatientCooperationRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyRequests([FromQuery] CooperationRequestStatus? status, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMyCooperationRequestsQuery(status), ct));

    [HttpPost("cooperation-requests")]
    [EnableRateLimiting(RateLimitPolicies.CooperationRequest)]
    [ProducesResponseType(typeof(PatientCooperationRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Send([FromBody] SendCooperationRequestBody body, CancellationToken ct)
    {
        var result = await Mediator.Send(new SendCooperationRequestCommand(body.TherapistId, body.Message), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("cooperation-requests/{id:guid}/withdraw")]
    [ProducesResponseType(typeof(PatientCooperationRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new WithdrawCooperationRequestCommand(id), ct));

    [HttpGet("cooperations")]
    [ProducesResponseType(typeof(IReadOnlyList<PatientCooperationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyCooperations(CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMyCooperationsQuery(), ct));
}

/// <summary>Prośby o współpracę skierowane do zalogowanego terapeuty.</summary>
[Route("api/therapists/me/cooperation-requests")]
[Authorize(Roles = "Therapist")]
public class TherapistCooperationRequestsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<TherapistCooperationRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] CooperationRequestStatus? status = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetTherapistCooperationRequestsQuery(status, pageNumber, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TherapistCooperationRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetTherapistCooperationRequestByIdQuery(id), ct));

    /// <summary>
    /// Akceptuje prośbę po weryfikacji tożsamości i w jednej transakcji łączy konto z istniejącą kartą albo tworzy nową.
    /// Nie udostępnia pacjentowi dokumentacji.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType(typeof(TherapistCooperationRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(Guid id, [FromBody] AcceptCooperationRequestBody body, CancellationToken ct) =>
        Ok(await Mediator.Send(new AcceptCooperationRequestCommand(
            id, body.IdentityVerified, body.CardAction, body.PatientId, body.NewPatient), ct));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(TherapistCooperationRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new RejectCooperationRequestCommand(id), ct));
}
