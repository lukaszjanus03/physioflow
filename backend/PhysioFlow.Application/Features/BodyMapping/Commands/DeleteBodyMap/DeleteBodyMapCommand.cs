using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.BodyMapping.Commands.DeleteBodyMap;

/// <summary>
/// Usunięcie snapshotu body map. Nie jest soft-delete (BodyMap : BaseEntity, nie SoftDeletable) -
/// kasujemy wraz z PainPoint'ami (OnDelete.Cascade w konfiguracji EF).
/// </summary>
public record DeleteBodyMapCommand(Guid Id) : IRequest;

public class DeleteBodyMapCommandHandler : IRequestHandler<DeleteBodyMapCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DeleteBodyMapCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteBodyMapCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new ForbiddenAccessException();

        var bodyMap = await _db.BodyMaps
            .Include(m => m.Patient)
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(BodyMap), request.Id);

        if (_currentUser.IsInRole("Therapist"))
        {
            var therapist = await _db.Therapists.FirstOrDefaultAsync(t => t.UserId == userId, ct)
                ?? throw new NotFoundException(nameof(TherapistProfile), userId);
            if (bodyMap.Patient.TherapistId != therapist.Id)
                throw new ForbiddenAccessException();
        }
        else if (_currentUser.IsInRole("Patient"))
        {
            // Pacjent może usunąć TYLKO swój własny snapshot (nie ten zrobiony przez terapeutę)
            if (bodyMap.Patient.UserId != userId)
                throw new ForbiddenAccessException();
            if (bodyMap.CreatedByRole != Domain.Enums.UserRole.Patient)
                throw new ForbiddenAccessException("Pacjent nie może usunąć snapshotu zrobionego przez terapeutę.");
        }
        else
        {
            throw new ForbiddenAccessException();
        }

        _db.BodyMaps.Remove(bodyMap);
        await _db.SaveChangesAsync(ct);
    }
}
