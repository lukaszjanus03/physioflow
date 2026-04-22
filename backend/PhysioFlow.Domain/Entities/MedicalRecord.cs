using PhysioFlow.Domain.Common;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Elektroniczna Dokumentacja Medyczna - wpis powiązany z wizytą.
/// Rich text notatek trzymamy jako HTML (Tiptap output) + plain text do wyszukiwania.
/// </summary>
public class MedicalRecord : SoftDeletableEntity
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid TherapistId { get; set; }

    public string? InterviewForm { get; set; }        // wywiad wstępny (JSON)
    public string? PostVisitNoteHtml { get; set; }    // Tiptap rich text
    public string? PostVisitNoteText { get; set; }    // plain text do search
    public int? PainScale { get; set; }               // 1-10

    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
    public TherapistProfile Therapist { get; set; } = null!;
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
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
