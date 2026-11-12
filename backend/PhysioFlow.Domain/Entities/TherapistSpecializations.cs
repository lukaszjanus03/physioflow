namespace PhysioFlow.Domain.Entities;

/// <summary>Stable marketplace codes; labels may change without changing stored profiles.</summary>
public static class TherapistSpecializations
{
    public static IReadOnlyDictionary<string, string> All { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["orthopedic"] = "Fizjoterapia ortopedyczna",
                ["neurological"] = "Fizjoterapia neurologiczna",
                ["sports"] = "Fizjoterapia sportowa",
                ["pediatric"] = "Fizjoterapia pediatryczna",
                ["urogynecological"] = "Fizjoterapia uroginekologiczna",
                ["geriatric"] = "Fizjoterapia geriatryczna",
                ["respiratory"] = "Fizjoterapia oddechowa",
                ["manual-therapy"] = "Terapia manualna",
            });
}
