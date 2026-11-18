using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Chat;

namespace PhysioFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Patient,Therapist")]
[Route("api/chat/conversations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ChatController(ChatService chat) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ChatConversationDto), 200)]
    public async Task<IActionResult> Open(OpenChatRequest body, CancellationToken ct) => Ok(await chat.Open(body.CooperationId, ct));

    [HttpGet]
    [ProducesResponseType(typeof(PhysioFlow.Application.Common.Models.PaginatedList<ChatConversationDto>), 200)]
    public async Task<IActionResult> List([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => Ok(await chat.List(pageNumber, pageSize, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ChatConversationDto), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await chat.Get(id, ct));

    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(typeof(ChatHistoryDto), 200)]
    public async Task<IActionResult> History(Guid id, [FromQuery] long? before = null, [FromQuery] long? after = null,
        [FromQuery] int limit = 50, CancellationToken ct = default) => Ok(await chat.History(id, before, after, limit, ct));

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(ChatMessageDto), 201)]
    [ProducesResponseType(typeof(ChatMessageDto), 200)]
    public async Task<IActionResult> Send(Guid id, SendChatRequest body, CancellationToken ct)
    {
        var result = await chat.Send(id, body, ct);
        return StatusCode(result.Replayed ? 200 : 201, result.Message);
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(typeof(ChatReadDto), 200)]
    public async Task<IActionResult> Read(Guid id, ReadChatRequest body, CancellationToken ct) =>
        Ok(await chat.Read(id, body.ThroughMessageId, ct));
}
