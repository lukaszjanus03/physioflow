using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Elektroniczna Dokumentacja Medyczna - wpis powiązany z wizytą.
/// New visit content is plain text with immutable approved versions.
/// Legacy HTML, JSON interview forms and pain scale are preserved independently.
/// </summary>
public class MedicalRecord : SoftDeletableEntity
{
    public const int ClinicalTextMaxLength = 10000;
    public const int PatientSummaryMaxLength = 2000;
    public const int CorrectionReasonMaxLength = 1000;

    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid TherapistId { get; set; }

    public string? InterviewForm { get; set; }        // wywiad wstępny (JSON)
    public string? PostVisitNoteHtml { get; set; }    // Tiptap rich text
    public string? PostVisitNoteText { get; set; }    // plain text do search
    public int? PainScale { get; set; }               // 1-10

    // Plain text, independent of legacy notes and patient booking messages.
    public string? Interview { get; set; }
    public string? FunctionalAssessment { get; set; }
    public string? PerformedActions { get; set; }
    public string? Recommendations { get; set; }
    public string? PatientSummary { get; set; }
    public MedicalRecordStatus Status { get; set; } = MedicalRecordStatus.Draft;
    public int CurrentVersion { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public ICollection<MedicalRecordVersion> Versions { get; set; } = new List<MedicalRecordVersion>();

    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}

public enum MedicalRecordStatus { Draft, Approved }

/// <summary>Immutable snapshot of an approval or correction; never exposed to patient accounts.</summary>
public class MedicalRecordVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalRecordId { get; set; }
    public int VersionNumber { get; set; }
    public Guid AuthorUserId { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? CorrectionReason { get; set; }
    public string Interview { get; set; } = null!;
    public string FunctionalAssessment { get; set; } = null!;
    public string PerformedActions { get; set; } = null!;
    public string Recommendations { get; set; } = null!;
    public string? PatientSummary { get; set; }
    public MedicalRecord MedicalRecord { get; set; } = null!;
}

public class Attachment : BaseEntity
{
    public Guid MedicalRecordId { get; set; }

    public string FileName { get; set; } = null!;
    public string StorageKey { get; set; } = null!;   // klucz w Cloudflare R2
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public AttachmentType Type { get; set; }

    public MedicalRecord MedicalRecord { get; set; } = null!;
}
