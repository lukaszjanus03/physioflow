namespace PhysioFlow.Api.Options;

public class PatientRegistrationRateLimitOptions
{
    public const string SectionName = "RateLimiting:PatientRegistration";
    public const string PolicyName = "patient-registration";

    /// <summary>Liczba prób rejestracji z jednego adresu IP w oknie czasowym.</summary>
    public int PermitLimit { get; set; } = 5;

    public int WindowMinutes { get; set; } = 15;
}
