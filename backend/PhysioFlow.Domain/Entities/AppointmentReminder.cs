using PhysioFlow.Domain.Common;

namespace PhysioFlow.Domain.Entities;

public enum ReminderDeliveryStatus { Pending, Sent, Skipped, Failed }

public class AppointmentReminder : BaseEntity
{
    public Guid AppointmentId { get; set; }
    public Guid ScheduleId { get; set; }
    public ReminderDeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime AttemptWindowStartedAt { get; set; }
    public DateTime? SentAt { get; set; }
    // Zamrożony payload: ponowienia z tym samym kluczem muszą mieć tę samą treść.
    public string? Recipient { get; set; }
    public string HtmlBody { get; set; } = "";
    public Appointment Appointment { get; set; } = null!;
}
