using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Patients.Commands.CreatePatient;

public record CreatePatientCommand(
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
) : IRequest<Guid>;

public class CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>
{
    public CreatePatientCommandValidator()
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

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Guid>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreatePatientCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreatePatientCommand request, CancellationToken ct)
    {
        var therapistUserId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        var therapist = await _db.Therapists
            .FirstOrDefaultAsync(t => t.UserId == therapistUserId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), therapistUserId);

        // PESEL unikalny per terapeuta
        if (!string.IsNullOrEmpty(request.Pesel))
        {
            var exists = await _db.Patients
                .AnyAsync(p => p.TherapistId == therapist.Id
                            && p.Pesel == request.Pesel
                            && !p.IsDeleted, ct);
            if (exists)
                throw new ConflictException($"Pacjent z PESEL {request.Pesel} już istnieje.");
        }

        var patient = new Patient
        {
            TherapistId = therapist.Id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Pesel = request.Pesel,
            DateOfBirth = request.DateOfBirth,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            MedicalHistory = request.MedicalHistory,
            Medications = request.Medications,
            GeneralNotes = request.GeneralNotes
        };

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(ct);

        return patient.Id;
    }
}
