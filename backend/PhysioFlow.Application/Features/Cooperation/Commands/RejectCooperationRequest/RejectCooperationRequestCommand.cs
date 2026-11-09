using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Commands.RejectCooperationRequest;

public record RejectCooperationRequestCommand(Guid Id) : IRequest<TherapistCooperationRequestDto>;

public class RejectCooperationRequestCommandHandler
    : IRequestHandler<RejectCooperationRequestCommand, TherapistCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RejectCooperationRequestCommandHandler> _logger;

    public RejectCooperationRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<RejectCooperationRequestCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TherapistCooperationRequestDto> Handle(RejectCooperationRequestCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);

        var entity = await _db.CooperationRequests
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.TherapistId == therapist.Id, ct)
            ?? throw new NotFoundException(nameof(CooperationRequest), request.Id);

        if (entity.Status != CooperationRequestStatus.Pending)
            throw NotPending();

        entity.Status = CooperationRequestStatus.Rejected;
        entity.DecidedAt = _clock.UtcNow;
        entity.ConcurrencyStamp = Guid.NewGuid();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw NotPending();
        }

        _logger.LogInformation(
            "Cooperation request {RequestId} status Pending -> Rejected by therapist {TherapistId}", entity.Id, therapist.Id);

        return entity.ToTherapist();
    }

    private static ConflictException NotPending() => new(CooperationErrorCodes.RequestNotPending,
        "Ta prośba została już rozpatrzona albo wycofana.");
}
