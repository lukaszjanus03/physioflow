using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Queries.GetMyCooperationRequests;

public record GetMyCooperationRequestsQuery(CooperationRequestStatus? Status = null)
    : IRequest<IReadOnlyList<PatientCooperationRequestDto>>;

public class GetMyCooperationRequestsQueryHandler
    : IRequestHandler<GetMyCooperationRequestsQuery, IReadOnlyList<PatientCooperationRequestDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetMyCooperationRequestsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<PatientCooperationRequestDto>> Handle(
        GetMyCooperationRequestsQuery request, CancellationToken ct)
    {
        var accountId = _currentUser.RequireUserId();

        var query = _db.CooperationRequests.AsNoTracking().Where(r => r.PatientUserId == accountId);
        if (request.Status is { } status)
            query = query.Where(r => r.Status == status);

        return await query
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(CooperationMapping.ToPatientDto)
            .ToListAsync(ct);
    }
}
