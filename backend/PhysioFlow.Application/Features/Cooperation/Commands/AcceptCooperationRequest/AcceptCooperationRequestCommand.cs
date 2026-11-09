using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation.Commands.AcceptCooperationRequest;

/// <summary>Dane nowej karty. Celowo bez pól medycznych - te terapeuta uzupełnia sam w swojej karcie.</summary>
public record NewPatientCardInput(
    string? FirstName,
    string? LastName,
    string? Pesel,
    DateOnly? DateOfBirth,
    string? PhoneNumber,
    string? Email);

public record AcceptCooperationRequestCommand(
    Guid Id,
    bool? IdentityVerified,
    CardLinkMode? CardAction,
    Guid? PatientId,
    NewPatientCardInput? NewPatient
) : IRequest<TherapistCooperationRequestDto>;

public class AcceptCooperationRequestCommandValidator : AbstractValidator<AcceptCooperationRequestCommand>
{
    public AcceptCooperationRequestCommandValidator()
    {
        RuleFor(x => x.IdentityVerified)
            .Equal(true).WithMessage("Potwierdź, że tożsamość pacjenta została zweryfikowana.")
            .OverridePropertyName("identityVerified");

        RuleFor(x => x.CardAction)
            .NotNull().WithMessage("Wybierz: połącz z istniejącą kartą albo utwórz nową.")
            .OverridePropertyName("cardAction");

        RuleFor(x => x.PatientId)
            .NotNull().When(x => x.CardAction == CardLinkMode.LinkExisting)
            .WithMessage("Wskaż kartę pacjenta do połączenia.")
            .OverridePropertyName("patientId");

        RuleFor(x => x.NewPatient)
            .NotNull().When(x => x.CardAction == CardLinkMode.CreateNew)
            .WithMessage("Podaj dane nowej karty.")
            .OverridePropertyName("newPatient");

        When(x => x.CardAction == CardLinkMode.CreateNew && x.NewPatient is not null, () =>
        {
            RuleFor(x => x.NewPatient!.FirstName)
                .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Imię jest wymagane.")
                .MaximumLength(100).WithMessage("Imię może mieć maksymalnie 100 znaków.")
                .OverridePropertyName("newPatient.firstName");
            RuleFor(x => x.NewPatient!.LastName)
                .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Nazwisko jest wymagane.")
                .MaximumLength(100).WithMessage("Nazwisko może mieć maksymalnie 100 znaków.")
                .OverridePropertyName("newPatient.lastName");
            RuleFor(x => x.NewPatient!.Pesel)
                .Matches(@"^\d{11}$").When(x => !string.IsNullOrEmpty(x.NewPatient!.Pesel))
                .WithMessage("PESEL musi mieć dokładnie 11 cyfr.")
                .OverridePropertyName("newPatient.pesel");
            RuleFor(x => x.NewPatient!.Email)
                .EmailAddress().When(x => !string.IsNullOrEmpty(x.NewPatient!.Email))
                .WithMessage("Nieprawidłowy adres e-mail.")
                .OverridePropertyName("newPatient.email");
            RuleFor(x => x.NewPatient!.PhoneNumber)
                .MaximumLength(20).WithMessage("Numer telefonu może mieć maksymalnie 20 znaków.")
                .OverridePropertyName("newPatient.phoneNumber");
        });
    }
}

public class AcceptCooperationRequestCommandHandler
    : IRequestHandler<AcceptCooperationRequestCommand, TherapistCooperationRequestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AcceptCooperationRequestCommandHandler> _logger;

    public AcceptCooperationRequestCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        ILogger<AcceptCooperationRequestCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TherapistCooperationRequestDto> Handle(AcceptCooperationRequestCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);

        // Strategia z ponawianiem (Npgsql) wymaga, żeby transakcja była w całości wewnątrz ExecuteAsync.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            var entity = await _db.CooperationRequests
                .FirstOrDefaultAsync(r => r.Id == request.Id && r.TherapistId == therapist.Id, ct)
                ?? throw new NotFoundException(nameof(CooperationRequest), request.Id);

            if (entity.Status != CooperationRequestStatus.Pending)
                throw NotPending();

            if (await _db.Patients.AnyAsync(p => p.TherapistId == therapist.Id && p.UserId == entity.PatientUserId, ct))
                throw new ConflictException(CooperationErrorCodes.AccountAlreadyLinked,
                    "To konto jest już powiązane z inną Twoją kartą pacjenta.");

            Patient? existingCard = null;
            if (request.CardAction == CardLinkMode.LinkExisting)
            {
                existingCard = await _db.Patients
                    .FirstOrDefaultAsync(p => p.Id == request.PatientId && p.TherapistId == therapist.Id, ct)
                    ?? throw new NotFoundException(nameof(Patient), request.PatientId!);
                if (existingCard.IsArchived)
                    throw new ConflictException(CooperationErrorCodes.CardArchived,
                        "Nie można połączyć konta z kartą w archiwum.");
                if (existingCard.UserId is not null)
                    throw new ConflictException(CooperationErrorCodes.CardAlreadyLinked,
                        "Ta karta jest już powiązana z innym kontem.");
            }
            else
            {
                var pesel = request.NewPatient!.Pesel;
                if (!string.IsNullOrEmpty(pesel) && await _db.Patients.AnyAsync(p =>
                        p.TherapistId == therapist.Id && p.Pesel == pesel && !p.IsArchived, ct))
                    throw new ConflictException(CooperationErrorCodes.PeselConflict,
                        "Masz już aktywną kartę pacjenta z tym numerem PESEL. Połącz konto z istniejącą kartą.");
            }

            // Najpierw zajmujemy prośbę: przy równoczesnej akceptacji drugi zapis nie przejdzie
            // sprawdzenia ConcurrencyStamp, zanim powstanie jakakolwiek karta.
            var now = _clock.UtcNow;
            entity.Status = CooperationRequestStatus.Accepted;
            entity.DecidedAt = now;
            entity.IdentityVerifiedAt = now;
            entity.CardLinkMode = request.CardAction;
            entity.ConcurrencyStamp = Guid.NewGuid();
            await SaveAsync(ct);

            Patient card;
            if (existingCard is not null)
            {
                card = existingCard;
                card.UserId = entity.PatientUserId;
                card.ConcurrencyStamp = Guid.NewGuid();
            }
            else
            {
                var input = request.NewPatient!;
                card = new Patient
                {
                    TherapistId = therapist.Id,
                    UserId = entity.PatientUserId,
                    FirstName = input.FirstName!.Trim(),
                    LastName = input.LastName!.Trim(),
                    Pesel = string.IsNullOrWhiteSpace(input.Pesel) ? null : input.Pesel,
                    DateOfBirth = input.DateOfBirth,
                    PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim(),
                    Email = string.IsNullOrWhiteSpace(input.Email) ? entity.PatientEmail : input.Email.Trim(),
                };
                _db.Patients.Add(card);
            }

            entity.PatientId = card.Id;
            await SaveAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Cooperation request {RequestId} status Pending -> Accepted by therapist {TherapistId}; " +
                "identity verified; card {PatientId} linked to account {AccountId} ({CardLinkMode})",
                entity.Id, therapist.Id, card.Id, entity.PatientUserId, entity.CardLinkMode);

            return entity.ToTherapist();
        });
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is Patient))
        {
            throw new ConflictException(CooperationErrorCodes.CardAlreadyLinked,
                "Ta karta została w międzyczasie powiązana z innym kontem.");
        }
        catch (DbUpdateConcurrencyException)
        {
            throw NotPending();
        }
        catch (DbUpdateException)
        {
            // Unikalny indeks: jedna karta danego konta u jednego terapeuty.
            throw new ConflictException(CooperationErrorCodes.AccountAlreadyLinked,
                "To konto jest już powiązane z inną Twoją kartą pacjenta.");
        }
    }

    private static ConflictException NotPending() => new(CooperationErrorCodes.RequestNotPending,
        "Ta prośba została już rozpatrzona albo wycofana.");
}
