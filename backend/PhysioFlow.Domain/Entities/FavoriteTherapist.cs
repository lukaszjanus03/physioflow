namespace PhysioFlow.Domain.Entities;

/// <summary>An account bookmark, independent of patient cards, cooperation and appointments.</summary>
public class FavoriteTherapist
{
    public Guid PatientUserId { get; set; }
    public Guid TherapistId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
