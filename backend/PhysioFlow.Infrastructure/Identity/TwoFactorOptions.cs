namespace PhysioFlow.Infrastructure.Identity;

public sealed class TwoFactorOptions
{
    public string Issuer { get; set; } = "PhysioFlow";
    public int ChallengeLifetimeSeconds { get; set; } = 300;
    public int SetupLifetimeSeconds { get; set; } = 600;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutSeconds { get; set; } = 300;
    public int RecoveryCodeCount { get; set; } = 10;
}

public sealed class TwoFactorException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
