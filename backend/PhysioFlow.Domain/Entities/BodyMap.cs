using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Snapshot mapy ciała z konkretnej wizyty. Przechowuje listę PainPointów.
/// Wariant alternatywny: trzymać to jako JSON w kolumnie (jsonb w Postgresie).
/// Wybrałem relacyjnie bo łatwiej filtrować/raportować per-punkt.
/// </summary>
public class BodyMap : BaseEntity
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }   // opcjonalne powiązanie z wizytą

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public ICollection<PainPoint> PainPoints { get; set; } = new List<PainPoint>();
}

public class PainPoint : BaseEntity
{
    public Guid BodyMapId { get; set; }

    public BodySide Side { get; set; }           // przód / tył
    public decimal X { get; set; }                // 0.0 - 1.0 (znormalizowane)
    public decimal Y { get; set; }                // 0.0 - 1.0
    public PainPointSeverity Severity { get; set; }
    public string? Note { get; set; }

    public BodyMap BodyMap { get; set; } = null!;
}
