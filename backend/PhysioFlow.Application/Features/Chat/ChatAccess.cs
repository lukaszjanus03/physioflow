using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Chat;

public class ChatAccess(IApplicationDbContext db, IUserAccountService accounts)
{
    public async Task RequireCaller(Guid id, CancellationToken ct)
    {
        // Hub connections can outlive their JWT/account permissions. Never trust connection-time checks alone.
        if (!await accounts.ActiveUserIds().AnyAsync(u => u == id, ct)
            || (!await accounts.IsConfirmedPatientAsync(id, ct) && !await accounts.IsActiveTherapistAsync(id, ct)))
            throw new ForbiddenAccessException();
    }

    public IQueryable<ChatConversation> Readable(Guid id) => db.ChatConversations.Where(c =>
        accounts.ActiveUserIds().Contains(id)
        && c.Patient.TherapistId == c.Cooperation.TherapistId
        && c.Cooperation.Therapist.UserId == c.TherapistUserId
        && ((c.TherapistUserId == id)
            || (c.PatientUserId == id &&
                (c.Cooperation.Status == CooperationRequestStatus.Ended
                 || (c.Patient.UserId == id && c.Patient.AccountLinkVersion == c.AccountLinkVersion)))));

    public async Task<bool> CanRead(Guid conversationId, Guid id, CancellationToken ct)
    {
        if (!await Readable(id).AnyAsync(c => c.Id == conversationId, ct)) return false;
        var patient = await db.ChatConversations.Where(c => c.Id == conversationId)
            .Select(c => c.PatientUserId).SingleAsync(ct);
        return patient == id ? await accounts.IsConfirmedPatientAsync(id, ct) : await accounts.IsActiveTherapistAsync(id, ct);
    }

    public async Task<string?> ReadOnlyReason(ChatConversation c, CancellationToken ct)
    {
        if (c.Cooperation.Status != CooperationRequestStatus.Accepted) return "cooperation_ended";
        if (c.Patient.IsArchived) return "patient_archived";
        if (c.Patient.UserId != c.PatientUserId || c.Patient.AccountLinkVersion != c.AccountLinkVersion)
            return "account_link_changed";
        if (!await accounts.IsActiveTherapistAsync(c.TherapistUserId, ct)
            || !await accounts.IsConfirmedPatientAsync(c.PatientUserId, ct)) return "participant_unavailable";
        return null;
    }
}
