using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.BodyMapping.Commands.CreateBodyMap;

/// <summary>
/// Tworzy nowy body map (snapshot) dla pacjenta.
/// Obsługuje dwa scenariusze:
/// 1. Terapeuta (rola Therapist) - tworzy na wizycie dla swojego pacjenta (PatientId wymagany)
/// 2. Pacjent (rola Patient) - tworzy własny snapshot w portalu (PatientId ignorowany, bierzemy z claimów)
/// </summary>
public record CreateBodyMapCommand(
    Guid? PatientId,                              // wymagany dla terapeuty, ignorowany dla pacjenta
    Guid? AppointmentId,                          // opcjonalne powiązanie z wizytą
    string? Notes,
    IReadOnlyList<PainPointInput> PainPoints
) : IRequest<Guid>;

public record PainPointInput(
    BodySide Side,
    decimal X,                    // 0.0 - 1.0 znormalizowane
    decimal Y,
    PainPointSeverity Severity,
    string? Note
);

public class CreateBodyMapCommandValidator : AbstractValidator<CreateBodyMapCommand>
{
    public CreateBodyMapCommandValidator()
    {
        RuleForEach(x => x.PainPoints).ChildRules(pp =>
        {
            pp.RuleFor(p => p.X).InclusiveBetween(0m, 1m).WithMessage("X musi być w zakresie 0.0 - 1.0");
            pp.RuleFor(p => p.Y).InclusiveBetween(0m, 1m).WithMessage("Y musi być w zakresie 0.0 - 1.0");
            pp.RuleFor(p => p.Note).MaximumLength(500);
        });
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateBodyMapCommandHandler : IRequestHandler<CreateBodyMapCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateBodyMapCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreateBodyMapCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new ForbiddenAccessException();

        Guid patientId;
        UserRole createdByRole;

        if (_currentUser.IsInRole("Therapist"))
        {
            // Terapeuta musi podać PatientId i mieć go w swojej kartotece
            if (request.PatientId is null)
                throw new ValidationException("PatientId jest wymagany gdy akcję wykonuje terapeuta.");

            var therapist = await _db.Therapists.FirstOrDefaultAsync(t => t.UserId == userId, ct)
                ?? throw new NotFoundException(nameof(TherapistProfile), userId);

            var patientExists = await _db.Patients
                .AnyAsync(p => p.Id == request.PatientId && p.TherapistId == therapist.Id, ct);
            if (!patientExists)
                throw new NotFoundException(nameof(Patient), request.PatientId);

            patientId = request.PatientId.Value;
            createdByRole = UserRole.Therapist;
        }
        else if (_currentUser.IsInRole("Patient"))
        {
            // Pacjent tworzy własny snapshot - szukamy Patient powiązanego z jego ApplicationUser
            var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId, ct)
                ?? throw new NotFoundException(nameof(Patient), userId);

            patientId = patient.Id;
            createdByRole = UserRole.Patient;
        }
        else
        {
            throw new ForbiddenAccessException("Brak uprawnień do tworzenia body map.");
        }

        var bodyMap = new BodyMap
        {
            PatientId = patientId,
            AppointmentId = request.AppointmentId,
            Notes = request.Notes,
            RecordedAt = DateTime.UtcNow,
            CreatedByRole = createdByRole,
            PainPoints = request.PainPoints.Select(p => new PainPoint
            {
                Side = p.Side,
                X = p.X,
                Y = p.Y,
                Severity = p.Severity,
                Note = p.Note,
            }).ToList(),
        };

        _db.BodyMaps.Add(bodyMap);
        await _db.SaveChangesAsync(ct);

        return bodyMap.Id;
    }
}
