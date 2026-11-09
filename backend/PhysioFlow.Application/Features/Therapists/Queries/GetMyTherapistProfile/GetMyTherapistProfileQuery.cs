using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;

public record GetMyTherapistProfileQuery : IRequest<TherapistOwnProfileDto>;

public class GetMyTherapistProfileQueryHandler : IRequestHandler<GetMyTherapistProfileQuery, TherapistOwnProfileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMyTherapistProfileQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<TherapistOwnProfileDto> Handle(GetMyTherapistProfileQuery request, CancellationToken ct)
    {
        var therapistUserId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        var profile = await _db.Therapists
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == therapistUserId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), therapistUserId);

        return profile.ToOwnProfileDto();
    }
}

public static class TherapistProfileMapping
{
    public static TherapistOwnProfileDto ToOwnProfileDto(this TherapistProfile p) => new(
        p.Id,
        p.FirstName,
        p.LastName,
        p.City,
        p.PracticeName,
        p.Bio,
        p.IsPublished,
        p.MissingCatalogFields());
}
