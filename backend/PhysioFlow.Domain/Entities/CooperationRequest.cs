using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Prośba pacjenta o współpracę z terapeutą. Zaakceptowana prośba oznacza aktywną współpracę
/// i wskazuje kartę pacjenta, z którą terapeuta powiązał konto.
/// </summary>
public class CooperationRequest : BaseEntity
{
    public const int MessageMaxLength = 500;

    public Guid PatientUserId { get; set; }     // konto pacjenta (AspNetUsers), nie karta
    public Guid TherapistId { get; set; }
    public CooperationRequestStatus Status { get; set; } = CooperationRequestStatus.Pending;
    public string? Message { get; set; }

    // Dane konta z chwili wysłania, do weryfikacji tożsamości przez terapeutę.
    public string? PatientFirstName { get; set; }
    public string? PatientLastName { get; set; }
    public string PatientEmail { get; set; } = null!;

    public DateTime? DecidedAt { get; set; }
    public DateTime? IdentityVerifiedAt { get; set; }
    public CardLinkMode? CardLinkMode { get; set; }
    public Guid? PatientId { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public TherapistProfile Therapist { get; set; } = null!;
    public Patient? Patient { get; set; }
}
