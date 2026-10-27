using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.BodyMapping.Queries.GetBodyMapById;

/// <summary>
/// Pełen snapshot body mapa z wszystkimi PainPointami - do wyświetlenia edytora albo porównania.
/// </summary>
public record GetBodyMapByIdQuery(Guid Id) : IRequest<BodyMapDto>;

public class GetBodyMapByIdQueryHandler : IRequestHandler<GetBodyMapByIdQuery, BodyMapDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetBodyMapByIdQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<BodyMapDto> Handle(GetBodyMapByIdQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new ForbiddenAccessException();

        var bodyMap = await _db.BodyMaps
            .Include(m => m.PainPoints)
            .Include(m => m.Patient)
            .FirstOrDefaultAsync(m => m.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(BodyMap), request.Id);

        // Autoryzacja: terapeuta tylko swoich pacjentów, pacjent tylko siebie
        if (_currentUser.IsInRole("Therapist"))
        {
            var therapist = await _db.Therapists.FirstOrDefaultAsync(t => t.UserId == userId, ct)
                ?? throw new NotFoundException(nameof(TherapistProfile), userId);
            if (bodyMap.Patient.TherapistId != therapist.Id)
                throw new ForbiddenAccessException();
        }
        else if (_currentUser.IsInRole("Patient"))
        {
            if (bodyMap.Patient.UserId != userId)
                throw new ForbiddenAccessException();
        }
        else
        {
            throw new ForbiddenAccessException();
        }

        return new BodyMapDto(
            bodyMap.Id,
            bodyMap.PatientId,
            bodyMap.AppointmentId,
            bodyMap.RecordedAt,
            bodyMap.Notes,
            bodyMap.PainPoints
                .Select(p => new PainPointDto(
                    p.Id,
                    p.Side.ToString(),
                    p.X,
                    p.Y,
                    p.Severity.ToString(),
                    p.Note,
                    p.PainScale))
                .ToList());
    }
}
