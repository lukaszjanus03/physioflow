using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.ExerciseJournal;

internal static class TherapistJournalAccess
{
    public static async Task<IQueryable<ExerciseJournalEntry>> Accessible(IApplicationDbContext db,
        ICurrentUserService user, IUserAccountService accounts, Guid patientId, CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        var userId = user.RequireUserId();
        var cards = db.Patients.AsNoTracking().Where(p => p.Id == patientId && !p.IsArchived
            && p.UserId != null && p.Therapist.UserId == userId && !p.Therapist.IsDeleted
            && db.CooperationRequests.Any(r => r.PatientId == p.Id && r.TherapistId == p.TherapistId
                && r.PatientUserId == p.UserId && r.Status == CooperationRequestStatus.Accepted));
        var accountId = await cards.Select(p => p.UserId).SingleOrDefaultAsync(ct);
        if (accountId is null || !await accounts.IsConfirmedPatientAsync(accountId.Value, ct))
            throw new NotFoundException("PatientExerciseJournal", patientId);

        // Recheck the relationship in every data query, including the current link generation.
        // Renewed cooperation never implicitly restores an earlier link's health reports.
        // Do not join mutable plans/library: historical reports use their own saved snapshots.
        return db.ExerciseJournalEntries.AsNoTracking().Where(e => !e.IsDeleted
            && e.PatientUserId == accountId.Value && cards.Any(p => p.Id == e.PatientId
                && p.UserId == e.PatientUserId && p.AccountLinkVersion == e.PublicationLinkVersion));
    }
}
