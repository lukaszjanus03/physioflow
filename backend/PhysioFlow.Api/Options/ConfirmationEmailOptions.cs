namespace PhysioFlow.Api.Options;

public class ConfirmationEmailOptions
{
    public const string SectionName = "ConfirmationEmail";

    /// <summary>Minimalny odstęp między wiadomościami potwierdzającymi do jednego konta.</summary>
    public int CooldownSeconds { get; set; } = 60;
}
