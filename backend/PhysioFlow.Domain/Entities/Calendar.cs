namespace PhysioFlow.Domain.Entities;

/// <summary>
/// Ustawienia kalendarza terapeuty: bufor po wizycie i dozwolone długości.
/// Godziny pracy i wyjątki dat są w <see cref="AvailabilitySlot"/>.
/// </summary>
public class TherapistCalendarSettings : PhysioFlow.Domain.Common.BaseEntity
{
    public const int MaxBufferMinutes = 60;
    public const int MaxIntervalsPerDay = 8;
    public const int MaxExceptions = 366;
    public const int ReasonMaxLength = 500;
    public const int NotesMaxLength = 2000;
    public const int DefaultBufferMinutes = 0;
    public const string DefaultAllowedDurations = "30,45,60";

    public Guid TherapistId { get; set; }
    public int BufferMinutes { get; set; } = DefaultBufferMinutes;

    /// <summary>Dozwolone długości wizyt, zapisane jako "30,45,60".</summary>
    public string AllowedDurations { get; set; } = DefaultAllowedDurations;

    // Serializuje zapisy kalendarza terapeuty, żeby równoczesne rezerwacje nie weszły w kolizję.
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public TherapistProfile Therapist { get; set; } = null!;
}
