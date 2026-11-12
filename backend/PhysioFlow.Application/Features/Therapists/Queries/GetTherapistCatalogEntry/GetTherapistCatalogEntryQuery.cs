using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;

namespace PhysioFlow.Application.Features.Therapists.Queries.GetTherapistCatalogEntry;

public record GetTherapistCatalogEntryQuery(Guid Id) : IRequest<TherapistCatalogEntryDto>;

public class GetTherapistCatalogEntryQueryHandler
    : IRequestHandler<GetTherapistCatalogEntryQuery, TherapistCatalogEntryDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserAccountService _accounts;

    public GetTherapistCatalogEntryQueryHandler(IApplicationDbContext db, IUserAccountService accounts)
    {
        _db = db;
        _accounts = accounts;
    }

    // Nieopublikowany albo nieaktywny profil daje ten sam 404 co nieistniejący, żeby nie zdradzać jego istnienia.
    public async Task<TherapistCatalogEntryDto> Handle(GetTherapistCatalogEntryQuery request, CancellationToken ct)
    {
        return await _db.Therapists
            .PubliclyAvailable(_accounts)
            .Where(t => t.Id == request.Id)
            .Select(TherapistProfileMapping.CatalogProjection)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), request.Id);
    }
}
