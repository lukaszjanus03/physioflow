using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Features.BodyMapping.Commands.CreateBodyMap;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.BodyMapping.Commands.UpdateBodyMap;

public record UpdateBodyMapCommand(
    Guid Id,
    string? Notes,
    IReadOnlyList<PainPointInput> PainPoints
) : IRequest;

public class UpdateBodyMapCommandValidator
    : AbstractValidator<UpdateBodyMapCommand>
{
    public UpdateBodyMapCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Notes).MaximumLength(2000);

        RuleFor(x => x.PainPoints).NotNull();

        RuleForEach(x => x.PainPoints).ChildRules(pp =>
        {
            pp.RuleFor(p => p.Side)
                .IsInEnum()
                .WithMessage("Nieprawidłowa strona ciała.");

            pp.RuleFor(p => p.X)
                .InclusiveBetween(0m, 1m)
                .WithMessage("X musi być w zakresie 0.0 - 1.0");

            pp.RuleFor(p => p.Y)
                .InclusiveBetween(0m, 1m)
                .WithMessage("Y musi być w zakresie 0.0 - 1.0");

            pp.RuleFor(p => p.Severity)
                .IsInEnum()
                .WithMessage("Nieprawidłowy typ bólu.");

            pp.RuleFor(p => p.PainScale)
                .Must(value => !value.HasValue ||
                               (value.Value >= 1 && value.Value <= 10))
                .WithMessage("Skala bólu musi być w zakresie od 1 do 10.");

            pp.RuleFor(p => p.Note)
                .MaximumLength(500);
        });
    }
}

public class UpdateBodyMapCommandHandler
    : IRequestHandler<UpdateBodyMapCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateBodyMapCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(
        UpdateBodyMapCommand request,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        if (!_currentUser.IsInRole("Therapist"))
        {
            throw new ForbiddenAccessException(
                "Tylko terapeuta może edytować mapę bólu.");
        }

        var therapist = await _db.Therapists
            .FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw new NotFoundException(
                nameof(TherapistProfile), userId);

        var bodyMap = await _db.BodyMaps
            .Include(m => m.Patient)
            .Include(m => m.PainPoints)
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new NotFoundException(
                nameof(BodyMap), request.Id);

        if (bodyMap.Patient.TherapistId != therapist.Id)
        {
            throw new ForbiddenAccessException();
        }

        // Zastępujemy cały zestaw punktów danymi z żądania.
        var oldPoints = bodyMap.PainPoints.ToList();

        _db.PainPoints.RemoveRange(oldPoints);
        bodyMap.PainPoints.Clear();

        foreach (var point in request.PainPoints)
        {
            var newPoint = new PainPoint
            {
                BodyMapId = bodyMap.Id,
                Side = point.Side,
                X = point.X,
                Y = point.Y,
                Severity = point.Severity,
                PainScale = point.PainScale,
                Note = point.Note,
            };

            bodyMap.PainPoints.Add(newPoint);
            _db.PainPoints.Add(newPoint);
        }

        bodyMap.Notes = request.Notes;

        await _db.SaveChangesAsync(ct);
    }
}