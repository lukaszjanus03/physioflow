using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Snapshot mapy ciała - może być tworzony przez terapeutę (na wizycie) albo przez pacjenta (samodzielnie w portalu).
/// Rozróżniamy przez CreatedByRole, żeby pokazać w historii kto zaznaczył który snapshot.
/// Przechowuje listę PainPointów jako osobne encje - łatwiej filtrować/raportować per-punkt.
/// </summary>
public class BodyMap : BaseEntity
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }   // opcjonalne powiązanie z wizytą

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    /// <summary>Kto utworzył ten snapshot - terapeuta (Therapist) czy sam pacjent (Patient).</summary>
    public UserRole CreatedByRole { get; set; }

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
