namespace PhysioFlow.Application.Common.Interfaces;

public interface IFavoriteTherapistWriter
{
    /// <summary>Idempotent, including concurrent inserts for the same pair.</summary>
    Task AddAsync(Guid patientUserId, Guid therapistId, CancellationToken ct);
    Task RemoveAsync(Guid patientUserId, Guid therapistId, CancellationToken ct);
}
