using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Queries.SearchTherapistCatalog;

public record SearchTherapistCatalogQuery(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = SearchTherapistCatalogQuery.DefaultPageSize,
    string? City = null,
    string? Specialization = null
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
            .Must((q, page) => page < 1 || (long)(page - 1) * q.PageSize <= int.MaxValue)
            .WithMessage("Numer strony jest za duży.")
            .OverridePropertyName("pageNumber");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, SearchTherapistCatalogQuery.MaxPageSize)
            .WithMessage($"Rozmiar strony musi mieścić się w zakresie 1-{SearchTherapistCatalogQuery.MaxPageSize}.")
            .OverridePropertyName("pageSize");

        RuleFor(x => x.Search)
            .MaximumLength(SearchTherapistCatalogQuery.SearchMaxLength)
            .WithMessage($"Wyszukiwana fraza może mieć maksymalnie {SearchTherapistCatalogQuery.SearchMaxLength} znaków.")
            .OverridePropertyName("search");

        RuleFor(x => x.City).MaximumLength(TherapistProfile.CityMaxLength)
            .WithMessage($"Miejscowość może mieć maksymalnie {TherapistProfile.CityMaxLength} znaków.")
            .OverridePropertyName("city");
        RuleFor(x => x.Specialization)
            .Must(v => string.IsNullOrWhiteSpace(v) || TherapistSpecializations.All.ContainsKey(v.Trim()))
            .WithMessage("Wybierz kod specjalizacji ze słownika.")
            .MaximumLength(50)
            .WithMessage("Kod specjalizacji może mieć maksymalnie 50 znaków.")
            .OverridePropertyName("specialization");
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
        var query = _db.Therapists.PubliclyAvailable(_accounts);

        if (!string.IsNullOrWhiteSpace(request.City))
        {
            var city = request.City.Trim().ToLowerInvariant();
            query = query.Where(t => t.City!.ToLower() == city);
        }
        if (!string.IsNullOrWhiteSpace(request.Specialization))
        {
            var specialization = request.Specialization.Trim();
            query = query.Where(t => t.Specializations.Contains(specialization));
        }

        // Każde słowo musi pasować do któregoś pola, więc "anna kraków" zawęża wyniki zamiast je poszerzać.
        var terms = (request.Search ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
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
