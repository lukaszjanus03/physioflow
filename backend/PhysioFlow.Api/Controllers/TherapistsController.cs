using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Commands.UpdateMyTherapistProfile;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;
using PhysioFlow.Application.Features.Therapists.Queries.GetTherapistCatalogEntry;
using PhysioFlow.Application.Features.Therapists.Queries.SearchTherapistCatalog;

namespace PhysioFlow.Api.Controllers;

/// <summary>
/// Profil zawodowy terapeuty (endpointy /me, właściciel z sesji) oraz katalog opublikowanych profili dla pacjentów.
/// </summary>
public class TherapistsController : ApiControllerBase
{
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
