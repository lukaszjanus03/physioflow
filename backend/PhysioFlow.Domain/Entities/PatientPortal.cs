using PhysioFlow.Domain.Common;

namespace PhysioFlow.Domain.Entities;

public class PatientDiaryEntry : BaseEntity
{
    public Guid PatientId { get; set; }

    public DateOnly EntryDate { get; set; }
    public int PainLevel { get; set; }         // 1-10
    public int Wellbeing { get; set; }         // 1-10
    public string? Notes { get; set; }
    public string? CompletedExercises { get; set; }  // JSON: lista ID ćwiczeń

    public Patient Patient { get; set; } = null!;
}

public class ChatMessage : BaseEntity
{
    public Guid SenderId { get; set; }         // user id (terapeuta lub pacjent)
    public Guid RecipientId { get; set; }

    public string Content { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
