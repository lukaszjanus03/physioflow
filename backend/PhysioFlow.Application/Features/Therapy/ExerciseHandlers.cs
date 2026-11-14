using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapy;

public record CreateExerciseCommand(ExerciseContent Body) : IRequest<ExerciseDto>;
public record SaveExerciseCommand(Guid Id, SaveExerciseRequest Body) : IRequest<ExerciseDto>;
public record SetExerciseArchiveCommand(Guid Id, Guid Version, bool Archived) : IRequest<ExerciseDto>;
public record GetExerciseQuery(Guid Id) : IRequest<ExerciseDto>;
public record ListExercisesQuery(string? Search = null, string? Category = null, bool? Archived = false,
    int PageNumber = 1, int PageSize = 20) : IRequest<PaginatedList<ExerciseDto>>;

public class ExerciseHandlers(IApplicationDbContext db, ICurrentUserService user) :
    IRequestHandler<CreateExerciseCommand, ExerciseDto>,
    IRequestHandler<SaveExerciseCommand, ExerciseDto>,
    IRequestHandler<SetExerciseArchiveCommand, ExerciseDto>,
    IRequestHandler<GetExerciseQuery, ExerciseDto>,
    IRequestHandler<ListExercisesQuery, PaginatedList<ExerciseDto>>
{
    private async Task<Guid> TherapistId(CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        return (await db.RequireTherapistAsync(user, ct)).Id;
    }

    private async Task<Exercise> Find(Guid id, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        return await db.Exercises.SingleOrDefaultAsync(e => e.Id == id && e.TherapistId == owner, ct)
            ?? throw new NotFoundException(nameof(Exercise), id);
    }

    private static void CheckVersion(Exercise exercise, Guid version)
    {
        TherapyValidation.Require(version != Guid.Empty, "version", "Edit token is required.");
        if (exercise.ConcurrencyStamp != version)
            throw new ConflictException("concurrent_modification", "Exercise changed. Reload before saving.");
    }

    public async Task<ExerciseDto> Handle(CreateExerciseCommand request, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        await new ExerciseContentValidator().ValidateAndThrowAsync(request.Body, ct);
        var exercise = new Exercise { TherapistId = owner };
        Apply(exercise, request.Body);
        db.Exercises.Add(exercise);
        await db.SaveChangesAsync(ct);
        return ToDto(exercise);
    }

    public async Task<ExerciseDto> Handle(SaveExerciseCommand request, CancellationToken ct)
    {
        var exercise = await Find(request.Id, ct);
        CheckVersion(exercise, request.Body.Version);
        await new ExerciseContentValidator().ValidateAndThrowAsync(request.Body, ct);
        Apply(exercise, request.Body);
        exercise.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(exercise);
    }

    public async Task<ExerciseDto> Handle(SetExerciseArchiveCommand request, CancellationToken ct)
    {
        var exercise = await Find(request.Id, ct);
        CheckVersion(exercise, request.Version);
        exercise.IsArchived = request.Archived;
        exercise.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(exercise);
    }

    public async Task<ExerciseDto> Handle(GetExerciseQuery request, CancellationToken ct) => ToDto(await Find(request.Id, ct));

    public async Task<PaginatedList<ExerciseDto>> Handle(ListExercisesQuery request, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        TherapyValidation.Page(request.PageNumber, request.PageSize);
        TherapyValidation.Require(request.Search?.Length is not > 200, "search", "Maximum search length is 200.");
        TherapyValidation.Require(request.Category?.Length is not > Exercise.CategoryMaxLength, "category", "Maximum category length is 100.");
        var query = db.Exercises.AsNoTracking().Where(e => e.TherapistId == owner);
        if (request.Archived.HasValue) query = query.Where(e => e.IsArchived == request.Archived.Value);
        if (TherapyValidation.Clean(request.Category) is { } category)
            query = query.Where(e => e.Category != null && e.Category.ToLower() == category.ToLower());
        if (TherapyValidation.Clean(request.Search) is { } search)
        {
            var term = search.ToLowerInvariant();
            query = query.Where(e => e.Name.ToLower().Contains(term)
                || (e.Instructions != null && e.Instructions.ToLower().Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.Name).ThenBy(e => e.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(items.Select(ToDto).ToList(), total, request.PageNumber, request.PageSize);
    }

    private static void Apply(Exercise exercise, ExerciseContent content)
    {
        exercise.Name = content.Name!.Trim();
        exercise.Instructions = content.Instructions!.Trim();
        exercise.Category = content.Category!.Trim();
        exercise.Notes = TherapyValidation.Clean(content.Notes);
    }

    private static ExerciseDto ToDto(Exercise exercise) => new(exercise.Id, exercise.Name, exercise.Instructions,
        exercise.Category, exercise.Notes, exercise.IsArchived, exercise.ConcurrencyStamp);
}
