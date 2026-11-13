namespace PhysioFlow.Api.Options;

/// <summary>Limit żądań z jednego adresu IP. Rejestrowane jako named options, nazwa = nazwa polityki.</summary>
public class FixedWindowRateLimitOptions
{
    public int PermitLimit { get; set; } = 5;

    public int WindowMinutes { get; set; } = 15;
}

public static class RateLimitPolicies
{
    public const string PatientRegistration = "patient-registration";
    public const string EmailConfirmation = "email-confirmation";
    public const string ConfirmationResend = "confirmation-resend";
    public const string CooperationRequest = "cooperation-request";
    public const string PublicTherapistCatalog = "public-therapist-catalog";
    public const string TherapistReviewWrite = "therapist-review-write";

    /// <summary>Nazwa polityki -> sekcja konfiguracji z jej limitami.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConfigSections = new Dictionary<string, string>
    {
        [PatientRegistration] = "RateLimiting:PatientRegistration",
        [EmailConfirmation] = "RateLimiting:EmailConfirmation",
        [ConfirmationResend] = "RateLimiting:ConfirmationResend",
        [CooperationRequest] = "RateLimiting:CooperationRequest",
        [PublicTherapistCatalog] = "RateLimiting:PublicTherapistCatalog",
        [TherapistReviewWrite] = "RateLimiting:TherapistReviewWrite",
    };
}
