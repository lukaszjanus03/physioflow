using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Analytics;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AnalyticsController : ApiControllerBase
{
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardStatsDto), 200)]
    public async Task<IActionResult> Dashboard([FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetDashboardStatsQuery(from, to), ct));

    [HttpGet("patients/{patientId:guid}/pain-trend")]
    [ProducesResponseType(typeof(PatientPainTrendDto), 200)]
    public async Task<IActionResult> PainTrend(Guid patientId, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetPatientPainTrendQuery(patientId, from, to), ct));
}
