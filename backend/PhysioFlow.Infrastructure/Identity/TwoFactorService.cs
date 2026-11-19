using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PhysioFlow.Infrastructure.Services;

namespace PhysioFlow.Infrastructure.Identity;

public record TwoFactorStatus(bool Enabled, bool SetupPending, int RecoveryCodesRemaining, bool ShouldPromptTwoFactor);
public record TwoFactorSetup(string SharedKey, string AuthenticatorUri, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "TwoFactorSetup [REDACTED]";
}
public record TwoFactorChallenge(bool RequiresTwoFactor, string ChallengeToken, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "TwoFactorChallenge [REDACTED]";
}
public record AuthTokens(string AccessToken, string RefreshToken, DateTime ExpiresAt, bool ShouldPromptTwoFactor)
{
    public override string ToString() => "AuthTokens [REDACTED]";
}

/// <summary>
/// All proofs, challenges, token issuance and recovery consumption are committed in one Identity
/// update. Identity's concurrency stamp prevents two requests from consuming the same proof.
/// No secrets or request DTOs may be logged by callers.
/// </summary>
public sealed class TwoFactorService(
    UserManager<ApplicationUser> users, IDataProtectionProvider protection, IOptions<TwoFactorOptions> options,
    TimeProvider clock, IJwtTokenService jwt, IConfiguration configuration)
{
    private readonly TwoFactorOptions _options = options.Value;
    private DateTimeOffset Now => clock.GetUtcNow();

    private sealed class State
    {
        public string? Secret { get; set; }
        public string? PendingSecret { get; set; }
        public DateTimeOffset? SetupExpiresAt { get; set; }
        public long LastUsedStep { get; set; } = -1;
        public string[] RecoveryHashes { get; set; } = [];
        public string? ChallengeHash { get; set; }
        public DateTimeOffset? ChallengeExpiresAt { get; set; }
        public string? ChallengeSecurityStamp { get; set; }
        public int FailedAttempts { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
    }

    private IDataProtector Protector(ApplicationUser user) =>
        protection.CreateProtector("PhysioFlow.TwoFactor.v1", user.Id.ToString());

    private State Read(ApplicationUser user) => user.ProtectedTwoFactorState is null ? new State()
        : JsonSerializer.Deserialize<State>(Protector(user).Unprotect(user.ProtectedTwoFactorState))
            ?? throw new InvalidOperationException("Invalid protected 2FA state.");

    private async Task Save(ApplicationUser user, State state)
    {
        user.ProtectedTwoFactorState = Protector(user).Protect(JsonSerializer.Serialize(state));
        await SaveUser(user);
    }

    private async Task SaveUser(ApplicationUser user)
    {
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new TwoFactorException(409, "concurrent_modification");
    }

    public static bool ShouldPrompt(ApplicationUser user, IEnumerable<string> roles) =>
        user.EmailConfirmed && !user.TwoFactorEnabled && !user.TwoFactorPromptHandled
        && roles.Any(r => r is "Patient" or "Therapist");

    public async Task<TwoFactorStatus> Status(ApplicationUser user)
    {
        var state = Read(user);
        return new(user.TwoFactorEnabled, state.PendingSecret is not null && state.SetupExpiresAt > Now,
            state.RecoveryHashes.Length, ShouldPrompt(user, await users.GetRolesAsync(user)));
    }

    public async Task DismissPrompt(ApplicationUser user)
    {
        user.TwoFactorPromptHandled = true;
        await SaveUser(user);
    }

    private void EnsureAllowed(ApplicationUser user, State state)
    {
        if (!user.IsActive || user.LockoutEnd > Now) throw new TwoFactorException(401, "two_factor_invalid_credentials");
        if (state.BlockedUntil > Now) throw new TwoFactorException(429, "two_factor_locked");
        if (state.BlockedUntil is not null) { state.BlockedUntil = null; state.FailedAttempts = 0; }
    }

    private async Task Fail(ApplicationUser user, State state, string code = "two_factor_invalid_code")
    {
        state.FailedAttempts++;
        var locked = state.FailedAttempts >= _options.MaxFailedAttempts;
        if (locked)
        {
            state.BlockedUntil = Now.AddSeconds(_options.LockoutSeconds);
            state.ChallengeHash = null;
        }
        await Save(user, state);
        throw new TwoFactorException(locked ? 429 : 401, locked ? "two_factor_locked" : code);
    }

    public async Task<TwoFactorSetup> StartSetup(ApplicationUser user)
    {
        var state = Read(user);
        EnsureAllowed(user, state);
        if (user.TwoFactorEnabled) throw new TwoFactorException(409, "two_factor_already_enabled");
        state.PendingSecret = TotpCode.NewSecret();
        state.SetupExpiresAt = Now.AddSeconds(_options.SetupLifetimeSeconds);
        await Save(user, state);
        var label = Uri.EscapeDataString($"{_options.Issuer}:{user.Email}");
        var uri = $"otpauth://totp/{label}?secret={state.PendingSecret}&issuer={Uri.EscapeDataString(_options.Issuer)}&algorithm=SHA1&digits=6&period=30";
        return new(state.PendingSecret, uri, state.SetupExpiresAt.Value);
    }

    public async Task<string[]> ConfirmSetup(ApplicationUser user, string? code)
    {
        var state = Read(user);
        EnsureAllowed(user, state);
        if (user.TwoFactorEnabled) throw new TwoFactorException(409, "two_factor_already_enabled");
        if (state.PendingSecret is null || state.SetupExpiresAt <= Now)
            throw new TwoFactorException(400, "two_factor_setup_expired");
        var step = TotpCode.Match(state.PendingSecret, code, Now, -1);
        if (step is null) await Fail(user, state);
        state.Secret = state.PendingSecret;
        state.PendingSecret = null;
        state.SetupExpiresAt = null;
        state.LastUsedStep = step!.Value;
        state.FailedAttempts = 0;
        user.TwoFactorEnabled = true;
        user.TwoFactorPromptHandled = true;
        InvalidateSessions(user, state);
        var codes = GenerateRecoveryCodes(state);
        await Save(user, state);
        return codes;
    }

    public async Task<TwoFactorChallenge> BeginChallenge(ApplicationUser user)
    {
        var state = Read(user);
        EnsureAllowed(user, state);
        if (!user.TwoFactorEnabled || state.Secret is null)
            throw new TwoFactorException(401, "two_factor_invalid_credentials");
        var token = $"{user.Id:N}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        state.ChallengeHash = Hash(token);
        state.ChallengeExpiresAt = Now.AddSeconds(_options.ChallengeLifetimeSeconds);
        state.ChallengeSecurityStamp = user.SecurityStamp;
        await Save(user, state);
        return new(true, token, state.ChallengeExpiresAt.Value);
    }

    public async Task<AuthTokens> CompleteLogin(string? challengeToken, string? code, string? recoveryCode)
    {
        if (challengeToken is null || challengeToken.Length != 97 || challengeToken[32] != '.'
            || !Guid.TryParseExact(challengeToken[..32], "N", out var id))
            throw new TwoFactorException(401, "two_factor_invalid_challenge");
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) throw new TwoFactorException(401, "two_factor_invalid_challenge");
        var state = Read(user);
        EnsureAllowed(user, state);
        if (!user.TwoFactorEnabled || state.ChallengeHash is null || !Equal(state.ChallengeHash, Hash(challengeToken))
            || state.ChallengeExpiresAt <= Now || state.ChallengeSecurityStamp != user.SecurityStamp)
            throw new TwoFactorException(401, "two_factor_invalid_challenge");
        var roles = await users.GetRolesAsync(user);
        if (PatientAccountRules.RequiresEmailConfirmation(user, roles))
            throw new TwoFactorException(403, "email_confirmation_required");
        if (!VerifyProof(state, code, recoveryCode)) await Fail(user, state);
        state.ChallengeHash = null;
        state.ChallengeExpiresAt = null;
        state.FailedAttempts = 0;
        user.AccessFailedCount = 0;
        var tokens = CreateTokens(user, roles);
        await Save(user, state);
        return tokens;
    }

    private bool VerifyProof(State state, string? code, string? recoveryCode)
    {
        // Exactly one factor, never silently choose one of two supplied inputs.
        if ((string.IsNullOrWhiteSpace(code)) == (string.IsNullOrWhiteSpace(recoveryCode))) return false;
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (state.Secret is null) return false;
            var step = TotpCode.Match(state.Secret, code, Now, state.LastUsedStep);
            if (step is null) return false;
            state.LastUsedStep = step.Value;
            return true;
        }
        var hash = Hash(recoveryCode!.Replace("-", "").Trim().ToUpperInvariant());
        var index = Array.FindIndex(state.RecoveryHashes, h => Equal(h, hash));
        if (index < 0) return false;
        state.RecoveryHashes = state.RecoveryHashes.Where((_, i) => i != index).ToArray();
        return true;
    }

    public async Task<string[]> RegenerateRecoveryCodes(ApplicationUser user, string? password, string? code, string? recoveryCode)
    {
        var state = await Reauthenticate(user, password, code, recoveryCode);
        InvalidateSessions(user, state);
        var codes = GenerateRecoveryCodes(state);
        await Save(user, state);
        return codes;
    }

    public async Task Disable(ApplicationUser user, string? password, string? code, string? recoveryCode)
    {
        var state = await Reauthenticate(user, password, code, recoveryCode);
        InvalidateSessions(user, state);
        user.TwoFactorEnabled = false;
        // Preserve the handled prompt and any lockout state; erase all authenticator material.
        state.Secret = null;
        state.PendingSecret = null;
        state.SetupExpiresAt = null;
        state.RecoveryHashes = [];
        state.LastUsedStep = -1;
        await Save(user, state);
    }

    private async Task<State> Reauthenticate(ApplicationUser user, string? password, string? code, string? recoveryCode)
    {
        var state = Read(user);
        EnsureAllowed(user, state);
        if (!user.TwoFactorEnabled) throw new TwoFactorException(409, "two_factor_not_enabled");
        if (string.IsNullOrEmpty(password) || !await users.CheckPasswordAsync(user, password))
            await Fail(user, state, "two_factor_invalid_credentials");
        if (!VerifyProof(state, code, recoveryCode)) await Fail(user, state);
        state.FailedAttempts = 0;
        return state;
    }

    private string[] GenerateRecoveryCodes(State state)
    {
        var codes = Enumerable.Range(0, _options.RecoveryCodeCount)
            .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        state.RecoveryHashes = codes.Select(Hash).ToArray();
        return codes;
    }

    private static void InvalidateSessions(ApplicationUser user, State state)
    {
        user.AuthVersion++;
        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        state.ChallengeHash = null;
        state.ChallengeExpiresAt = null;
    }

    public async Task<AuthTokens> IssueSession(ApplicationUser user, IList<string> roles)
    {
        var tokens = CreateTokens(user, roles);
        await SaveUser(user);
        return tokens;
    }

    private AuthTokens CreateTokens(ApplicationUser user, IList<string> roles)
    {
        var access = jwt.GenerateAccessToken(user, roles);
        var refresh = jwt.GenerateRefreshToken();
        user.RefreshToken = refresh;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        var minutes = configuration.GetValue<int?>("Jwt:AccessTokenExpiryMinutes") ?? 60;
        return new(access, refresh, DateTime.UtcNow.AddMinutes(minutes), ShouldPrompt(user, roles));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Equal(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
