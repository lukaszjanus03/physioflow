using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Patients.Commands.UpdatePatient;

public record UpdatePatientCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string? Pesel,
    DateOnly? DateOfBirth,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? MedicalHistory,
    string? Medications,
    string? GeneralNotes
) : IRequest<Unit>;

public class UpdatePatientCommandValidator : AbstractValidator<UpdatePatientCommand>
{
    public UpdatePatientCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Pesel)
            .Matches(@"^\d{11}$").When(x => !string.IsNullOrEmpty(x.Pesel))
            .WithMessage("PESEL musi mieć dokładnie 11 cyfr.");
        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.PhoneNumber).MaximumLength(20);
    }
}

public class UpdatePatientCommandHandler : IRequestHandler<UpdatePatientCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdatePatientCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UpdatePatientCommand request, CancellationToken ct)
    {
        var therapistUserId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        var therapist = await _db.Therapists
            .FirstOrDefaultAsync(t => t.UserId == therapistUserId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), therapistUserId);

        var patient = await _db.Patients
            .FirstOrDefaultAsync(p =>
                p.Id == request.Id
                && p.TherapistId == therapist.Id
                && !p.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(Patient), request.Id);

        if (!string.IsNullOrEmpty(request.Pesel) && request.Pesel != patient.Pesel)
        {
            var conflict = await _db.Patients.AnyAsync(p =>
                p.TherapistId == therapist.Id
                && p.Id != patient.Id
                && p.Pesel == request.Pesel
                && !p.IsArchived
                && !p.IsDeleted, ct);
            if (conflict)
                throw new ConflictException($"Pacjent z PESEL {request.Pesel} już istnieje.");
        }

        patient.FirstName = request.FirstName;
        patient.LastName = request.LastName;
        patient.Pesel = request.Pesel;
        patient.DateOfBirth = request.DateOfBirth;
        patient.Email = request.Email;
        patient.PhoneNumber = request.PhoneNumber;
        patient.Address = request.Address;
        patient.MedicalHistory = request.MedicalHistory;
        patient.Medications = request.Medications;
        patient.GeneralNotes = request.GeneralNotes;
        patient.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
