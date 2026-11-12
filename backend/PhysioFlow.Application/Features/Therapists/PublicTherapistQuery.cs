using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists;

public static class PublicTherapistQuery
{
    // Soft-deleted profiles are excluded by the existing global query filter.
    public static IQueryable<TherapistProfile> PubliclyAvailable(
        this IQueryable<TherapistProfile> profiles, IUserAccountService accounts) =>
        profiles.AsNoTracking().Where(t => t.IsPublished && t.City != null && t.PracticeName != null
            && accounts.ActiveUserIds().Contains(t.UserId));
}
