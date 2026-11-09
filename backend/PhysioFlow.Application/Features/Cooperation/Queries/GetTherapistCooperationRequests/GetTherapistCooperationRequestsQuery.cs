using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Queries.GetTherapistCooperationRequests;

public record GetTherapistCooperationRequestsQuery(
    CooperationRequestStatus? Status = null,
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<PaginatedList<TherapistCooperationRequestDto>>
{
    public const int MaxPageSize = 50;
}

public class GetTherapistCooperationRequestsQueryValidator : AbstractValidator<GetTherapistCooperationRequestsQuery>
{
    public GetTherapistCooperationRequestsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Numer strony musi być większy lub równy 1.")
            .OverridePropertyName("pageNumber");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, GetTherapistCooperationRequestsQuery.MaxPageSize)
            .WithMessage($"Rozmiar strony musi mieścić się w zakresie 1-{GetTherapistCooperationRequestsQuery.MaxPageSize}.")
            .OverridePropertyName("pageSize");
    }
}

public class GetTherapistCooperationRequestsQueryHandler
    : IRequestHandler<GetTherapistCooperationRequestsQuery, PaginatedList<TherapistCooperationRequestDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetTherapistCooperationRequestsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PaginatedList<TherapistCooperationRequestDto>> Handle(
        GetTherapistCooperationRequestsQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);

        var query = _db.CooperationRequests.AsNoTracking().Where(r => r.TherapistId == therapist.Id);
        if (request.Status is { } status)
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(CooperationMapping.ToTherapistDto)
            .ToListAsync(ct);

        return new PaginatedList<TherapistCooperationRequestDto>(items, total, request.PageNumber, request.PageSize);
    }
}

public record GetTherapistCooperationRequestByIdQuery(Guid Id) : IRequest<TherapistCooperationRequestDto>;

public class GetTherapistCooperationRequestByIdQueryHandler
    : IRequestHandler<GetTherapistCooperationRequestByIdQuery, TherapistCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetTherapistCooperationRequestByIdQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<TherapistCooperationRequestDto> Handle(
        GetTherapistCooperationRequestByIdQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);

        return await _db.CooperationRequests
            .AsNoTracking()
            .Where(r => r.Id == request.Id && r.TherapistId == therapist.Id)
            .Select(CooperationMapping.ToTherapistDto)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(CooperationRequest), request.Id);
    }
}
