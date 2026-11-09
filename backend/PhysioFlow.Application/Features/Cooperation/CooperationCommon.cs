using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Cooperation;

/// <summary>Polityka ponownych próśb. Sekcja konfiguracji "CooperationRequests".</summary>
public class CooperationRequestOptions
{
    public const string SectionName = "CooperationRequests";

    /// <summary>Po odrzuceniu pacjent może ponownie poprosić tego samego terapeutę dopiero po tym czasie.</summary>
    public int RejectionCooldownDays { get; set; } = 7;

    /// <summary>Po wycofaniu własnej prośby kolejną do tego samego terapeuty można wysłać po tym czasie.</summary>
    public int WithdrawalCooldownMinutes { get; set; } = 60;

    /// <summary>Limit wszystkich próśb wysłanych przez jedno konto w ciągu ostatnich 24 godzin.</summary>
    public int MaxRequestsPerDay { get; set; } = 10;
}

public static class CooperationErrorCodes
{
    public const string RequestAlreadyPending = "request_already_pending";
    public const string CooperationAlreadyActive = "cooperation_already_active";
    public const string RequestNotPending = "request_not_pending";
    public const string CooperationNotActive = "cooperation_not_active";
    public const string ResubmissionCooldown = "resubmission_cooldown";
    public const string DailyRequestLimit = "daily_request_limit";
    public const string CardArchived = "card_archived";
    public const string CardAlreadyLinked = "card_already_linked";
    public const string AccountAlreadyLinked = "account_already_linked";
    public const string PeselConflict = "pesel_conflict";
}

internal static class CooperationMapping
{
    public static readonly Expression<Func<CooperationRequest, PatientCooperationRequestDto>> ToPatientDto = r =>
        new PatientCooperationRequestDto(
            r.Id,
            r.Status,
            r.Message,
            r.CreatedAt,
            r.DecidedAt,
            new CooperationTherapistDto(
                r.Therapist.Id, r.Therapist.FirstName, r.Therapist.LastName, r.Therapist.City, r.Therapist.PracticeName),
            r.EndedAt,
            r.EndedBy);

    public static readonly Expression<Func<CooperationRequest, TherapistCooperationRequestDto>> ToTherapistDto = r =>
        new TherapistCooperationRequestDto(
            r.Id,
            r.Status,
            r.Message,
            r.CreatedAt,
            r.DecidedAt,
            new RequestingPatientDto(r.PatientFirstName, r.PatientLastName, r.PatientEmail),
            r.CardLinkMode,
            r.PatientId,
            r.EndedAt,
            r.EndedBy);

    private static readonly Func<CooperationRequest, PatientCooperationRequestDto> ToPatientDtoCompiled = ToPatientDto.Compile();
    private static readonly Func<CooperationRequest, TherapistCooperationRequestDto> ToTherapistDtoCompiled = ToTherapistDto.Compile();

    public static PatientCooperationRequestDto ToPatient(this CooperationRequest r) => ToPatientDtoCompiled(r);
    public static TherapistCooperationRequestDto ToTherapist(this CooperationRequest r) => ToTherapistDtoCompiled(r);
}

internal static class CooperationEnding
{
    /// <summary>
    /// Kończy aktywną współpracę i odłącza konto od karty. Karta z dokumentacją zostaje u terapeuty,
    /// a udostępnianie dokumentacji kontu jest wyłączane.
    /// </summary>
    public static async Task EndAsync(
        IApplicationDbContext db, CooperationRequest entity, CooperationParty endedBy, DateTime now, CancellationToken ct)
    {
        if (entity.Status != CooperationRequestStatus.Accepted)
            throw NotActive();

        entity.Status = CooperationRequestStatus.Ended;
        entity.EndedAt = now;
        entity.EndedBy = endedBy;
        entity.ConcurrencyStamp = Guid.NewGuid();

        // Bez filtra soft delete: konto trzeba odłączyć także od usuniętej karty.
        var card = entity.PatientId is { } cardId
            ? await db.Patients.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == cardId && p.UserId == entity.PatientUserId, ct)
            : null;
        if (card is not null)
        {
            card.UserId = null;
            card.DocumentationSharedWithAccount = false;
            card.ConcurrencyStamp = Guid.NewGuid();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is CooperationRequest))
        {
            throw NotActive();
        }
    }

    public static string TherapistName(TherapistProfile therapist) => $"{therapist.FirstName} {therapist.LastName}".Trim();

    private static ConflictException NotActive() => new(CooperationErrorCodes.CooperationNotActive,
        "Ta współpraca nie jest aktywna.");
}

internal static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUserService currentUser) =>
        currentUser.UserId ?? throw new ForbiddenAccessException();

    public static async Task<TherapistProfile> RequireTherapistAsync(
        this IApplicationDbContext db, ICurrentUserService currentUser, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        return await db.Therapists.FirstOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), userId);
    }
}
