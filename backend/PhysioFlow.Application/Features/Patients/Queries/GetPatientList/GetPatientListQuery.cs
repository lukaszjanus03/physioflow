using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Patients.Queries.GetPatientList;

public record GetPatientListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    bool IncludeArchived = false
) : IRequest<PaginatedList<PatientListItemDto>>;

public class GetPatientListQueryHandler
    : IRequestHandler<GetPatientListQuery, PaginatedList<PatientListItemDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetPatientListQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<PatientListItemDto>> Handle(
        GetPatientListQuery request, CancellationToken ct)
    {
        var therapistUserId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        var therapist = await _db.Therapists
            .FirstOrDefaultAsync(t => t.UserId == therapistUserId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), therapistUserId);

        var query = _db.Patients
            .Where(p => p.TherapistId == therapist.Id && !p.IsDeleted);

        if (!request.IncludeArchived)
            query = query.Where(p => !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.FirstName.ToLower().Contains(search) ||
                p.LastName.ToLower().Contains(search) ||
                (p.Email != null && p.Email.ToLower().Contains(search)) ||
                (p.PhoneNumber != null && p.PhoneNumber.Contains(search)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => new PatientListItemDto(
                p.Id,
                p.FirstName,
                p.LastName,
                p.Email,
                p.PhoneNumber,
                p.IsArchived,
                p.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedList<PatientListItemDto>(items, total, request.PageNumber, request.PageSize);
    }
}
