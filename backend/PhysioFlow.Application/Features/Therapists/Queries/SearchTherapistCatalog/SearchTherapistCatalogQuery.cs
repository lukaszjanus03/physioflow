using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;

namespace PhysioFlow.Application.Features.Therapists.Queries.SearchTherapistCatalog;

public record SearchTherapistCatalogQuery(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = SearchTherapistCatalogQuery.DefaultPageSize
) : IRequest<PaginatedList<TherapistCatalogEntryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int SearchMaxLength = 100;
}

public class SearchTherapistCatalogQueryValidator : AbstractValidator<SearchTherapistCatalogQuery>
{
    public SearchTherapistCatalogQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Numer strony musi być większy lub równy 1.")
            .OverridePropertyName("pageNumber");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, SearchTherapistCatalogQuery.MaxPageSize)
            .WithMessage($"Rozmiar strony musi mieścić się w zakresie 1-{SearchTherapistCatalogQuery.MaxPageSize}.")
            .OverridePropertyName("pageSize");

        RuleFor(x => x.Search)
            .MaximumLength(SearchTherapistCatalogQuery.SearchMaxLength)
            .WithMessage($"Wyszukiwana fraza może mieć maksymalnie {SearchTherapistCatalogQuery.SearchMaxLength} znaków.")
            .OverridePropertyName("search");
    }
}

public class SearchTherapistCatalogQueryHandler
    : IRequestHandler<SearchTherapistCatalogQuery, PaginatedList<TherapistCatalogEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserAccountService _accounts;

    public SearchTherapistCatalogQueryHandler(IApplicationDbContext db, IUserAccountService accounts)
    {
        _db = db;
        _accounts = accounts;
    }

    public async Task<PaginatedList<TherapistCatalogEntryDto>> Handle(
        SearchTherapistCatalogQuery request, CancellationToken ct)
    {
        var query = _db.Therapists
            .AsNoTracking()
            .Where(t => t.IsPublished && t.City != null && t.PracticeName != null
                        && _accounts.ActiveUserIds().Contains(t.UserId));

        // Każde słowo musi pasować do któregoś pola, więc "anna kraków" zawęża wyniki zamiast je poszerzać.
        var terms = (request.Search ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLower())
            .Distinct();

        foreach (var term in terms)
        {
            query = query.Where(t =>
                t.FirstName.ToLower().Contains(term) ||
                t.LastName.ToLower().Contains(term) ||
                t.City!.ToLower().Contains(term) ||
                t.PracticeName!.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(t => t.LastName).ThenBy(t => t.FirstName).ThenBy(t => t.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(TherapistProfileMapping.CatalogProjection)
            .ToListAsync(ct);

        return new PaginatedList<TherapistCatalogEntryDto>(items, total, request.PageNumber, request.PageSize);
    }
}
