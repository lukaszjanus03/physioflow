using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Queries.GetMyCooperations;

/// <summary>Aktywne współprace zalogowanego pacjenta. Zwraca tylko dane terapeuty, bez danych karty.</summary>
public record GetMyCooperationsQuery : IRequest<IReadOnlyList<PatientCooperationDto>>;

public class GetMyCooperationsQueryHandler
    : IRequestHandler<GetMyCooperationsQuery, IReadOnlyList<PatientCooperationDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMyCooperationsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<PatientCooperationDto>> Handle(GetMyCooperationsQuery request, CancellationToken ct)
    {
        var accountId = _currentUser.RequireUserId();

        return await _db.CooperationRequests
            .AsNoTracking()
            .Where(r => r.PatientUserId == accountId && r.Status == CooperationRequestStatus.Accepted)
            .OrderBy(r => r.Therapist.LastName).ThenBy(r => r.Therapist.FirstName).ThenBy(r => r.Id)
            .Select(r => new PatientCooperationDto(
                r.Id,
                r.DecidedAt!.Value,
                new CooperationTherapistDto(
                    r.Therapist.Id, r.Therapist.FirstName, r.Therapist.LastName, r.Therapist.City, r.Therapist.PracticeName)))
            .ToListAsync(ct);
    }
}
