using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Commands.EndCooperation;

/// <summary>Pacjent kończy współpracę wskazaną przez id zaakceptowanej prośby.</summary>
public record EndCooperationByPatientCommand(Guid Id) : IRequest<PatientCooperationRequestDto>;

/// <summary>Terapeuta kończy współpracę wskazaną przez id zaakceptowanej prośby.</summary>
public record EndCooperationByTherapistCommand(Guid Id) : IRequest<TherapistCooperationRequestDto>;

public class EndCooperationByPatientCommandHandler
    : IRequestHandler<EndCooperationByPatientCommand, PatientCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICooperationNotifier _notifier;
    private readonly ILogger<EndCooperationByPatientCommandHandler> _logger;

    public EndCooperationByPatientCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ICooperationNotifier notifier,
        ILogger<EndCooperationByPatientCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<PatientCooperationRequestDto> Handle(EndCooperationByPatientCommand request, CancellationToken ct)
    {
        var accountId = _currentUser.RequireUserId();

        var entity = await _db.CooperationRequests
            .Include(r => r.Therapist)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.PatientUserId == accountId, ct)
            ?? throw new NotFoundException(nameof(CooperationRequest), request.Id);

        await CooperationEnding.EndAsync(_db, entity, CooperationParty.Patient, _clock.UtcNow, ct);

        _logger.LogInformation(
            "Cooperation request {RequestId} status Accepted -> Ended by account {AccountId}; card {PatientId} unlinked",
            entity.Id, accountId, entity.PatientId);

        await _notifier.CooperationEndedByPatientAsync(
            entity.Id, entity.Therapist.UserId, entity.PatientFirstName, entity.PatientLastName, ct);

        return entity.ToPatient();
    }
}

public class EndCooperationByTherapistCommandHandler
    : IRequestHandler<EndCooperationByTherapistCommand, TherapistCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICooperationNotifier _notifier;
    private readonly ILogger<EndCooperationByTherapistCommandHandler> _logger;

    public EndCooperationByTherapistCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ICooperationNotifier notifier,
        ILogger<EndCooperationByTherapistCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<TherapistCooperationRequestDto> Handle(EndCooperationByTherapistCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);

        var entity = await _db.CooperationRequests
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.TherapistId == therapist.Id, ct)
            ?? throw new NotFoundException(nameof(CooperationRequest), request.Id);

        await CooperationEnding.EndAsync(_db, entity, CooperationParty.Therapist, _clock.UtcNow, ct);

        _logger.LogInformation(
            "Cooperation request {RequestId} status Accepted -> Ended by therapist {TherapistId}; card {PatientId} unlinked",
            entity.Id, therapist.Id, entity.PatientId);

        await _notifier.CooperationEndedByTherapistAsync(
            entity.Id, entity.PatientUserId, CooperationEnding.TherapistName(therapist), ct);

        return entity.ToTherapist();
    }
}
