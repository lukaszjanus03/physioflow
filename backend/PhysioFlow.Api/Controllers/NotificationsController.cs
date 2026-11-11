using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Notifications;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Patient,Therapist")]
public class NotificationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<NotificationDto>), 200)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => Ok(await Mediator.Send(new GetNotificationsQuery(pageNumber, pageSize), ct));

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadNotificationCountDto), 200)]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
        => Ok(await Mediator.Send(new GetUnreadNotificationCountQuery(), ct));

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await Mediator.Send(new ReadNotificationCommand(id), ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await Mediator.Send(new ReadAllNotificationsCommand(), ct);
        return NoContent();
    }
}
