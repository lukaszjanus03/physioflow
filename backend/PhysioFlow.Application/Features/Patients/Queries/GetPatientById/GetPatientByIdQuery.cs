using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Patients.Queries.GetPatientById;

public record GetPatientByIdQuery(Guid Id) : IRequest<PatientDetailDto>;

public class GetPatientByIdQueryHandler : IRequestHandler<GetPatientByIdQuery, PatientDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetPatientByIdQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PatientDetailDto> Handle(GetPatientByIdQuery request, CancellationToken ct)
    {
        var therapistUserId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        var therapist = await _db.Therapists
            .FirstOrDefaultAsync(t => t.UserId == therapistUserId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), therapistUserId);

        // Pacjent musi należeć do tego terapeuty - chroni przed enumeracją cudzych ID.
        var patient = await _db.Patients
            .Where(p => p.Id == request.Id && p.TherapistId == therapist.Id && !p.IsDeleted)
            .Select(p => new PatientDetailDto(
                p.Id,
                p.FirstName,
                p.LastName,
                p.Pesel,
                p.DateOfBirth,
                p.Email,
                p.PhoneNumber,
                p.Address,
                p.MedicalHistory,
                p.Medications,
                p.GeneralNotes,
                p.IsArchived,
                p.CreatedAt,
                p.UpdatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Patient), request.Id);

        return patient;
    }
}