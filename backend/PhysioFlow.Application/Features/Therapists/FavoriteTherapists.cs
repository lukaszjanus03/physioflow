using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;
using PhysioFlow.Application.Features.Therapists.Queries.SearchTherapistCatalog;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists;

public record GetFavoriteTherapistsQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<FavoriteTherapistDto>>;
public record AddFavoriteTherapistCommand(Guid TherapistId) : IRequest;
public record RemoveFavoriteTherapistCommand(Guid TherapistId) : IRequest;

public class GetFavoriteTherapistsQueryValidator : AbstractValidator<GetFavoriteTherapistsQuery>
{
    public GetFavoriteTherapistsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1)
            .WithMessage("Numer strony musi być większy lub równy 1.")
            .Must((q, page) => page < 1 || (long)(page - 1) * q.PageSize <= int.MaxValue)
            .WithMessage("Numer strony jest za duży.")
            .OverridePropertyName("pageNumber");
        RuleFor(x => x.PageSize).InclusiveBetween(1, SearchTherapistCatalogQuery.MaxPageSize)
            .WithMessage($"Rozmiar strony musi mieścić się w zakresie 1-{SearchTherapistCatalogQuery.MaxPageSize}.")
            .OverridePropertyName("pageSize");
    }
}

public class GetFavoriteTherapistsHandler(IApplicationDbContext db, ICurrentUserService user, IUserAccountService accounts)
    : IRequestHandler<GetFavoriteTherapistsQuery, PaginatedList<FavoriteTherapistDto>>
{
    public async Task<PaginatedList<FavoriteTherapistDto>> Handle(GetFavoriteTherapistsQuery request, CancellationToken ct)
    {
        var accountId = FavoriteTherapistAccess.RequirePatient(user);
        var query = db.FavoriteTherapists.AsNoTracking().Where(f => f.PatientUserId == accountId);
        var total = await query.CountAsync(ct);
        var bookmarks = await query.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.TherapistId)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);

        // Only fetch public DTOs for this page, never load hidden profiles into the response.
        var ids = bookmarks.Select(f => f.TherapistId).ToArray();
        var profiles = await db.Therapists.PubliclyAvailable(accounts).Where(t => ids.Contains(t.Id))
            .Select(TherapistProfileMapping.CatalogProjection).ToDictionaryAsync(t => t.Id, ct);
        var items = bookmarks.Select(f => new FavoriteTherapistDto(f.TherapistId, f.CreatedAt,
            profiles.ContainsKey(f.TherapistId), profiles.GetValueOrDefault(f.TherapistId))).ToArray();
        return new(items, total, request.PageNumber, request.PageSize);
    }
}

public class AddFavoriteTherapistHandler(IApplicationDbContext db, ICurrentUserService user,
    IUserAccountService accounts, IFavoriteTherapistWriter writer) : IRequestHandler<AddFavoriteTherapistCommand>
{
    public async Task Handle(AddFavoriteTherapistCommand request, CancellationToken ct)
    {
        var accountId = FavoriteTherapistAccess.RequirePatient(user);
        if (!await db.Therapists.PubliclyAvailable(accounts).AnyAsync(t => t.Id == request.TherapistId, ct))
            throw new NotFoundException(nameof(TherapistProfile), request.TherapistId);
        await writer.AddAsync(accountId, request.TherapistId, ct);
    }
}

public class RemoveFavoriteTherapistHandler(ICurrentUserService user, IFavoriteTherapistWriter writer)
    : IRequestHandler<RemoveFavoriteTherapistCommand>
{
    public Task Handle(RemoveFavoriteTherapistCommand request, CancellationToken ct) =>
        writer.RemoveAsync(FavoriteTherapistAccess.RequirePatient(user), request.TherapistId, ct);
}

internal static class FavoriteTherapistAccess
{
    public static Guid RequirePatient(ICurrentUserService user) =>
        user.IsInRole("Patient") && user.UserId is { } id ? id : throw new ForbiddenAccessException();
}
