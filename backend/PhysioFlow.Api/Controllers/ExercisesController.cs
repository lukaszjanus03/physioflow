using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapy;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
[Route("api/exercises")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExercisesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<ExerciseDto>), 200)]
    public async Task<IActionResult> List([FromQuery] string? search = null, [FromQuery] string? category = null,
        [FromQuery] bool? archived = false, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await Mediator.Send(new ListExercisesQuery(search, category, archived, pageNumber, pageSize), ct));

    [HttpPost]
    [ProducesResponseType(typeof(ExerciseDto), 201)]
    public async Task<IActionResult> Create([FromBody] ExerciseContent body, CancellationToken ct)
    {
        var exercise = await Mediator.Send(new CreateExerciseCommand(body), ct);
        return CreatedAtAction(nameof(Get), new { id = exercise.Id }, exercise);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await Mediator.Send(new GetExerciseQuery(id), ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> Save(Guid id, [FromBody] SaveExerciseRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new SaveExerciseCommand(id, body), ct));

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> Archive(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new SetExerciseArchiveCommand(id, body.Version, true), ct));

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> Restore(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new SetExerciseArchiveCommand(id, body.Version, false), ct));

    [HttpPost("{id:guid}/video/upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ExerciseVideoFile.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ExerciseVideoFile.MaxRequestBytes)]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> UploadVideo(Guid id, [FromForm] UploadExerciseVideoForm body, CancellationToken ct)
    {
        if (body.File is null)
            throw new FluentValidation.ValidationException([new("file", "A video file is required.")]);
        await using var content = body.File.OpenReadStream();
        return Ok(await Mediator.Send(new UploadExerciseVideoCommand(id, body.Version, content, body.File.Length,
            body.File.FileName, body.File.ContentType), ct));
    }

    [HttpPut("{id:guid}/video/youtube")]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> SetYoutubeVideo(Guid id, [FromBody] SetYoutubeVideoRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new SetYoutubeVideoCommand(id, body), ct));

    [HttpDelete("{id:guid}/video")]
    [ProducesResponseType(typeof(ExerciseDto), 200)]
    public async Task<IActionResult> DetachVideo(Guid id, [FromBody] TherapyVersionRequest body, CancellationToken ct) =>
        Ok(await Mediator.Send(new DetachExerciseVideoCommand(id, body.Version), ct));
}

public class UploadExerciseVideoForm
{
    public Guid Version { get; set; }
    public IFormFile? File { get; set; }
}
