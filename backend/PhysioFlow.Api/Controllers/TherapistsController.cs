using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Commands.UpdateMyTherapistProfile;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;
using PhysioFlow.Application.Features.Therapists.Queries.GetTherapistCatalogEntry;
using PhysioFlow.Application.Features.Therapists.Queries.SearchTherapistCatalog;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Features.Therapists.Commands.ProfilePhoto;
using PhysioFlow.Application.Features.Therapists.Queries.GetProfilePhoto;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Api.Controllers;

/// <summary>
/// Profil zawodowy terapeuty (endpointy /me, właściciel z sesji) oraz katalog opublikowanych profili dla pacjentów.
/// </summary>
public class TherapistsController : ApiControllerBase
{
    [HttpGet("specializations")]
    [Authorize(Roles = "Therapist,Patient")]
    [ProducesResponseType(typeof(IReadOnlyList<TherapistSpecializationDto>), StatusCodes.Status200OK)]
    public IActionResult Specializations() => Ok(TherapistSpecializations.All
        .Select(s => new TherapistSpecializationDto(s.Key, s.Value)).ToArray());

    [HttpPut("me/profile/photo")]
    [Authorize(Roles = "Therapist")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(IProfilePhotoProcessor.MaxUploadBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = IProfilePhotoProcessor.MaxUploadBytes + 1)]
    [ProducesResponseType(typeof(TherapistOwnProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadPhoto(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > IProfilePhotoProcessor.MaxUploadBytes)
            throw new FluentValidation.ValidationException(new[] {
                new FluentValidation.Results.ValidationFailure("file", "Prześlij zdjęcie o rozmiarze od 1 bajta do 5 MB.") });
        await using var content = file.OpenReadStream();
        return Ok(await Mediator.Send(new UploadMyProfilePhotoCommand(content), ct));
    }

    [HttpDelete("me/profile/photo")]
    [Authorize(Roles = "Therapist")]
    [ProducesResponseType(typeof(TherapistOwnProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePhoto(CancellationToken ct) =>
        Ok(await Mediator.Send(new DeleteMyProfilePhotoCommand(), ct));

    /// <summary>Published profile photos are public; drafts are accessible only to their owner.</summary>
    [HttpGet("{id:guid}/photo")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken ct)
    {
        var stream = await Mediator.Send(new GetProfilePhotoQuery(id), ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, "image/webp");
    }

    /// <summary>Własny profil zawodowy, także nieopublikowany.</summary>
    [HttpGet("me/profile")]
    [Authorize(Roles = "Therapist")]
    [ProducesResponseType(typeof(TherapistOwnProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMyTherapistProfileQuery(), ct));

    /// <summary>
    /// Zapisuje dane zawodowe i jawnie podany stan publikacji. Publikacja wymaga imienia, nazwiska, miasta i nazwy gabinetu.
    /// </summary>
    [HttpPut("me/profile")]
    [Authorize(Roles = "Therapist")]
    [ProducesResponseType(typeof(TherapistOwnProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateMyProfile(
        [FromBody] UpdateMyTherapistProfileCommand command, CancellationToken ct) =>
        Ok(await Mediator.Send(command, ct));

    /// <summary>Wyszukiwanie opublikowanych profili po imieniu, nazwisku, mieście i nazwie gabinetu.</summary>
    [HttpGet]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(PaginatedList<TherapistCatalogEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = SearchTherapistCatalogQuery.DefaultPageSize,
        CancellationToken ct = default) =>
        Ok(await Mediator.Send(new SearchTherapistCatalogQuery(search, pageNumber, pageSize), ct));

    /// <summary>Szczegóły opublikowanego profilu. Nieopublikowany i nieistniejący dają 404.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(TherapistCatalogEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetTherapistCatalogEntryQuery(id), ct));
}
