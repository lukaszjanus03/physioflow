using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

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
                r.Therapist.Id, r.Therapist.FirstName, r.Therapist.LastName, r.Therapist.City, r.Therapist.PracticeName));

    public static readonly Expression<Func<CooperationRequest, TherapistCooperationRequestDto>> ToTherapistDto = r =>
        new TherapistCooperationRequestDto(
            r.Id,
            r.Status,
            r.Message,
            r.CreatedAt,
            r.DecidedAt,
            new RequestingPatientDto(r.PatientFirstName, r.PatientLastName, r.PatientEmail),
            r.CardLinkMode,
            r.PatientId);

    private static readonly Func<CooperationRequest, PatientCooperationRequestDto> ToPatientDtoCompiled = ToPatientDto.Compile();
    private static readonly Func<CooperationRequest, TherapistCooperationRequestDto> ToTherapistDtoCompiled = ToTherapistDto.Compile();

    public static PatientCooperationRequestDto ToPatient(this CooperationRequest r) => ToPatientDtoCompiled(r);
    public static TherapistCooperationRequestDto ToTherapist(this CooperationRequest r) => ToTherapistDtoCompiled(r);
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
