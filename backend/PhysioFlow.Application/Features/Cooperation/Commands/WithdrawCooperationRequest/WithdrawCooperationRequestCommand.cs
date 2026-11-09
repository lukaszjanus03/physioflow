using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Commands.WithdrawCooperationRequest;

public record WithdrawCooperationRequestCommand(Guid Id) : IRequest<PatientCooperationRequestDto>;

public class WithdrawCooperationRequestCommandHandler
    : IRequestHandler<WithdrawCooperationRequestCommand, PatientCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<WithdrawCooperationRequestCommandHandler> _logger;

    public WithdrawCooperationRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<WithdrawCooperationRequestCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PatientCooperationRequestDto> Handle(WithdrawCooperationRequestCommand request, CancellationToken ct)
    {
        var accountId = _currentUser.RequireUserId();

        // Cudza prośba daje 404, żeby nie zdradzać jej istnienia.
        var entity = await _db.CooperationRequests
            .Include(r => r.Therapist)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.PatientUserId == accountId, ct)
            ?? throw new NotFoundException(nameof(CooperationRequest), request.Id);

        if (entity.Status != CooperationRequestStatus.Pending)
            throw NotPending();

        entity.Status = CooperationRequestStatus.Withdrawn;
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
            "Cooperation request {RequestId} status Pending -> Withdrawn by account {AccountId}", entity.Id, accountId);

        return entity.ToPatient();
    }

    private static ConflictException NotPending() => new(CooperationErrorCodes.RequestNotPending,
        "Można wycofać tylko oczekującą prośbę.");
}
