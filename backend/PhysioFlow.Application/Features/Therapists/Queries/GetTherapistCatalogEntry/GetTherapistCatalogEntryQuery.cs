using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Queries.GetTherapistCatalogEntry;

public record GetTherapistCatalogEntryQuery(Guid Id) : IRequest<TherapistCatalogEntryDto>;

public class GetTherapistCatalogEntryQueryHandler
    : IRequestHandler<GetTherapistCatalogEntryQuery, TherapistCatalogEntryDto>
{
    private readonly IApplicationDbContext _db;

    public GetTherapistCatalogEntryQueryHandler(IApplicationDbContext db) => _db = db;

    // Nieopublikowany profil daje ten sam 404 co nieistniejący, żeby nie zdradzać jego istnienia.
    public async Task<TherapistCatalogEntryDto> Handle(GetTherapistCatalogEntryQuery request, CancellationToken ct)
    {
        return await _db.Therapists
            .AsNoTracking()
            .Where(t => t.Id == request.Id && t.IsPublished && t.City != null && t.PracticeName != null)
            .Select(t => new TherapistCatalogEntryDto(
                t.Id,
                t.FirstName,
                t.LastName,
                t.City!,
                t.PracticeName!,
                t.Bio))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), request.Id);
    }
}
