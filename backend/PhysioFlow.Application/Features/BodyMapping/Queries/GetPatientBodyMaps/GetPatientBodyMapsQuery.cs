using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.BodyMapping.Queries.GetPatientBodyMaps;

/// <summary>
/// Lista snapshotów body map dla pacjenta, chronologicznie (najnowsze pierwsze).
/// Terapeuta: widzi snapshoty wszystkich swoich pacjentów.
/// Pacjent: widzi tylko własne snapshoty.
/// </summary>
public record GetPatientBodyMapsQuery(Guid PatientId) : IRequest<IReadOnlyList<BodyMapSummaryDto>>;

public class GetPatientBodyMapsQueryHandler
    : IRequestHandler<GetPatientBodyMapsQuery, IReadOnlyList<BodyMapSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetPatientBodyMapsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<BodyMapSummaryDto>> Handle(
        GetPatientBodyMapsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new ForbiddenAccessException();

        // Sprawdzenie autoryzacji - terapeuta tylko swoich pacjentów, pacjent tylko swoje
        if (_currentUser.IsInRole("Therapist"))
        {
            var therapist = await _db.Therapists.FirstOrDefaultAsync(t => t.UserId == userId, ct)
                ?? throw new NotFoundException(nameof(TherapistProfile), userId);

            var owns = await _db.Patients
                .AnyAsync(p => p.Id == request.PatientId && p.TherapistId == therapist.Id, ct);
            if (!owns) throw new ForbiddenAccessException();
        }
        else if (_currentUser.IsInRole("Patient"))
        {
            var ownsSelf = await _db.Patients
                .AnyAsync(p => p.Id == request.PatientId && p.UserId == userId, ct);
            if (!ownsSelf) throw new ForbiddenAccessException();
        }
        else
        {
            throw new ForbiddenAccessException();
        }

        return await _db.BodyMaps
            .Where(m => m.PatientId == request.PatientId)
            .OrderByDescending(m => m.RecordedAt)
            .Select(m => new BodyMapSummaryDto(
                m.Id,
                m.PatientId,
                m.AppointmentId,
                m.RecordedAt,
                m.Notes,
                m.CreatedByRole.ToString(),
                m.PainPoints.Count))
            .ToListAsync(ct);
    }
}
