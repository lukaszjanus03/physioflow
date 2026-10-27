using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Muscles.Queries.GetMuscleByRegion;

public record AnatomicalRegionInfoDto(
    string RegionId,
    string Name,
    string Description,
    string Function,
    IReadOnlyList<string> CommonIssues,
    IReadOnlyList<string> ExaminationTips);

public record GetMuscleByRegionQuery(
    string RegionId
) : IRequest<AnatomicalRegionInfoDto>;

public class GetMuscleByRegionQueryValidator
    : AbstractValidator<GetMuscleByRegionQuery>
{
    public GetMuscleByRegionQueryValidator()
    {
        RuleFor(x => x.RegionId)
            .NotEmpty()
            .MaximumLength(100)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Nieprawidłowy identyfikator regionu.");
    }
}

public class GetMuscleByRegionQueryHandler
    : IRequestHandler<GetMuscleByRegionQuery, AnatomicalRegionInfoDto>
{
    private readonly IApplicationDbContext _db;

    public GetMuscleByRegionQueryHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AnatomicalRegionInfoDto> Handle(
        GetMuscleByRegionQuery request,
        CancellationToken ct)
    {
        var region = await _db.AnatomicalRegionInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.RegionId == request.RegionId,
                ct)
            ?? throw new NotFoundException(
                nameof(AnatomicalRegionInfo),
                request.RegionId);

        return new AnatomicalRegionInfoDto(
            region.RegionId,
            region.Name,
            region.Description,
            region.Function,
            region.CommonIssues,
            region.ExaminationTips);
    }
}