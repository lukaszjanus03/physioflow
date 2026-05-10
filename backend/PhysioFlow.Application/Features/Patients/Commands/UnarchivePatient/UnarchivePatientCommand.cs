using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Patients.Commands.UnarchivePatient;

public record UnarchivePatientCommand(Guid Id) : IRequest<Unit>;

public class UnarchivePatientCommandHandler : IRequestHandler<UnarchivePatientCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UnarchivePatientCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UnarchivePatientCommand request, CancellationToken ct)
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

        if (!patient.IsArchived)
            return Unit.Value;

        patient.IsArchived = false;
        await _db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
