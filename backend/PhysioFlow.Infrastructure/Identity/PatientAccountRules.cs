namespace PhysioFlow.Infrastructure.Identity;

public static class PatientAccountRules
{
    public const string PatientRole = "Patient";

    /// <summary>
    /// Wymóg potwierdzenia adresu dotyczy tylko pacjentów; konta terapeutów nie mają potwierdzonych adresów.
    /// </summary>
    public static bool RequiresEmailConfirmation(ApplicationUser user, IEnumerable<string> roles) =>
        !user.EmailConfirmed && roles.Contains(PatientRole);
}
