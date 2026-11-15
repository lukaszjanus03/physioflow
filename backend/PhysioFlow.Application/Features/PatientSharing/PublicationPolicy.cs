using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.PatientSharing;

internal static class PublicationPolicy
{
    public static void CheckToken(Guid actual, Guid expected)
    {
        TherapyValidation.Require(expected != Guid.Empty, "version", "Edit token is required.");
        if (actual != expected)
            throw new ConflictException("concurrent_modification", "Content or publication changed. Reload before saving.");
    }

    public static async Task RequirePublishablePatient(IApplicationDbContext db, IUserAccountService accounts,
        Patient patient, CancellationToken ct)
    {
        if (patient.IsArchived)
            throw new ConflictException("patient_archived", "Restore the patient card before publishing content.");
        if (patient.UserId is not { } accountId || !await accounts.IsConfirmedPatientAsync(accountId, ct))
            throw new ConflictException("patient_account_required", "A confirmed patient account must be linked to the card.");
        if (!await db.CooperationRequests.AnyAsync(r => r.PatientId == patient.Id
            && r.TherapistId == patient.TherapistId && r.PatientUserId == accountId
            && r.Status == CooperationRequestStatus.Accepted, ct))
            throw new ConflictException("cooperation_not_active", "Publishing requires active cooperation with the linked account.");
        // A concurrent unlink/end/archive must conflict with the entire publication transaction.
        patient.ConcurrencyStamp = Guid.NewGuid();
    }

    public static void Publish(IPatientPublication publication, Patient patient, Guid versionId, DateTime now)
    {
        publication.PublishedVersionId = versionId;
        publication.PublishedToUserId = patient.UserId!.Value;
        publication.PublishedForLinkVersion = patient.AccountLinkVersion;
        publication.PublishedAt = now;
    }

    public static void Revoke(IPatientPublication publication)
    {
        publication.PublishedVersionId = null;
        publication.PublishedToUserId = null;
        publication.PublishedForLinkVersion = null;
        publication.PublishedAt = null;
    }

    public static PatientPublicationDto? ToDto(IPatientPublication publication, int? number, Patient patient) =>
        number is null || publication.PublishedAt is null || publication.PublishedToUserId is null ? null :
        new(number.Value, publication.PublishedToUserId.Value, publication.PublishedAt.Value,
            !patient.IsDeleted && patient.UserId == publication.PublishedToUserId
            && patient.AccountLinkVersion == publication.PublishedForLinkVersion);
}
