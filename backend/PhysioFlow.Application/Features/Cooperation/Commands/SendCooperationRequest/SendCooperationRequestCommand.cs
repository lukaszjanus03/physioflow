using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Commands.SendCooperationRequest;

// Nadawca pochodzi z sesji, więc komenda nie ma pola konta.
public record SendCooperationRequestCommand(Guid? TherapistId, string? Message) : IRequest<PatientCooperationRequestDto>;

public class SendCooperationRequestCommandValidator : AbstractValidator<SendCooperationRequestCommand>
{
    public SendCooperationRequestCommandValidator()
    {
        RuleFor(x => x.TherapistId)
            .NotNull().WithMessage("Wybierz terapeutę.")
            .OverridePropertyName("therapistId");

        RuleFor(x => x.Message)
            .Must(v => v is null || v.Trim().Length <= CooperationRequest.MessageMaxLength)
            .WithMessage($"Wiadomość może mieć maksymalnie {CooperationRequest.MessageMaxLength} znaków.")
            .OverridePropertyName("message");
    }
}

public class SendCooperationRequestCommandHandler
    : IRequestHandler<SendCooperationRequestCommand, PatientCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IUserAccountService _accounts;
    private readonly IDateTimeProvider _clock;
    private readonly CooperationRequestOptions _options;
    private readonly ILogger<SendCooperationRequestCommandHandler> _logger;

    public SendCooperationRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IUserAccountService accounts,
        IDateTimeProvider clock,
        IOptions<CooperationRequestOptions> options,
        ILogger<SendCooperationRequestCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _accounts = accounts;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PatientCooperationRequestDto> Handle(SendCooperationRequestCommand request, CancellationToken ct)
    {
        var accountId = _currentUser.RequireUserId();
        var now = _clock.UtcNow;

        var therapist = await _db.Therapists
            .FirstOrDefaultAsync(t => t.Id == request.TherapistId && t.IsPublished, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), request.TherapistId!);

        var pairRequests = _db.CooperationRequests
            .Where(r => r.PatientUserId == accountId && r.TherapistId == therapist.Id);

        if (await pairRequests.AnyAsync(r => r.Status == CooperationRequestStatus.Pending, ct))
            throw new ConflictException(CooperationErrorCodes.RequestAlreadyPending,
                "Masz już oczekującą prośbę do tego terapeuty.");

        if (await pairRequests.AnyAsync(r => r.Status == CooperationRequestStatus.Accepted, ct)
            || await _db.Patients.AnyAsync(p => p.TherapistId == therapist.Id && p.UserId == accountId, ct))
            throw new ConflictException(CooperationErrorCodes.CooperationAlreadyActive,
                "Współpracujesz już z tym terapeutą.");

        var lastClosed = await pairRequests
            .Where(r => r.Status == CooperationRequestStatus.Rejected || r.Status == CooperationRequestStatus.Withdrawn)
            .OrderByDescending(r => r.DecidedAt)
            .Select(r => new { r.Status, r.DecidedAt })
            .FirstOrDefaultAsync(ct);

        if (lastClosed?.DecidedAt is { } decidedAt)
        {
            var allowedFrom = lastClosed.Status == CooperationRequestStatus.Rejected
                ? decidedAt.AddDays(_options.RejectionCooldownDays)
                : decidedAt.AddMinutes(_options.WithdrawalCooldownMinutes);
            if (allowedFrom > now)
                throw new TooManyRequestsException(CooperationErrorCodes.ResubmissionCooldown,
                    $"Kolejną prośbę do tego terapeuty możesz wysłać po {allowedFrom:yyyy-MM-dd HH:mm} UTC.");
        }

        var sentToday = await _db.CooperationRequests
            .CountAsync(r => r.PatientUserId == accountId && r.CreatedAt > now.AddDays(-1), ct);
        if (sentToday >= _options.MaxRequestsPerDay)
            throw new TooManyRequestsException(CooperationErrorCodes.DailyRequestLimit,
                "Wysłano zbyt wiele próśb w ciągu ostatniej doby. Spróbuj ponownie później.");

        var account = await _accounts.FindAsync(accountId, ct)
            ?? throw new ForbiddenAccessException();

        var entity = new CooperationRequest
        {
            PatientUserId = accountId,
            TherapistId = therapist.Id,
            Therapist = therapist,
            Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            PatientFirstName = account.FirstName,
            PatientLastName = account.LastName,
            PatientEmail = account.Email,
        };
        _db.CooperationRequests.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unikalny indeks na otwartą parę: równoległe żądanie zdążyło zapisać prośbę.
            throw new ConflictException(CooperationErrorCodes.RequestAlreadyPending,
                "Masz już oczekującą prośbę do tego terapeuty.");
        }

        _logger.LogInformation(
            "Cooperation request {RequestId} sent by account {AccountId} to therapist {TherapistId}",
            entity.Id, accountId, therapist.Id);

        return entity.ToPatient();
    }
}
