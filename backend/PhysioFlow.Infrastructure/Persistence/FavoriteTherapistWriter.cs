using Microsoft.EntityFrameworkCore;
using Npgsql;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Persistence.Configurations;

namespace PhysioFlow.Infrastructure.Persistence;

public class FavoriteTherapistWriter(ApplicationDbContext db) : IFavoriteTherapistWriter
{
    public async Task AddAsync(Guid patientUserId, Guid therapistId, CancellationToken ct)
    {
        if (await db.FavoriteTherapists.AnyAsync(
            f => f.PatientUserId == patientUserId && f.TherapistId == therapistId, ct)) return;

        var favorite = new FavoriteTherapist { PatientUserId = patientUserId, TherapistId = therapistId };
        db.FavoriteTherapists.Add(favorite);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FavoriteTherapistConfiguration.PrimaryKeyName })
        {
            // Another request inserted this exact pair. Other database failures must propagate.
            db.Entry(favorite).State = EntityState.Detached;
        }
    }

    public async Task RemoveAsync(Guid patientUserId, Guid therapistId, CancellationToken ct)
    {
        var query = db.FavoriteTherapists.Where(f => f.PatientUserId == patientUserId && f.TherapistId == therapistId);
        if (db.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(ct);
            return;
        }
        // InMemory test provider does not support ExecuteDelete.
        var favorite = await query.SingleOrDefaultAsync(ct);
        if (favorite is null) return;
        db.FavoriteTherapists.Remove(favorite);
        await db.SaveChangesAsync(ct);
    }
}
