namespace PhysioFlow.Api.Options;

public class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Publiczny adres aplikacji Next.js, na który prowadzą linki w e-mailach.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
