using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Infrastructure.Identity;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public sealed class TwoFactorClock : TimeProvider
{
    private long _seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Interlocked.Read(ref _seconds));
    public void Advance(int seconds = 30) => Interlocked.Add(ref _seconds, seconds);
}

public class TwoFactorFactory : TestWebApplicationFactory
{
    public TwoFactorClock Clock { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(Clock);
    }
}

public class TwoFactorTests(TwoFactorFactory factory) : IClassFixture<TwoFactorFactory>
{
    public const string Url = "/api/auth/2fa";

    public static async Task<(HttpClient Client, string Email)> Account(TestWebApplicationFactory factory, string role,
        bool confirmed = true)
    {
        var client = factory.CreateClient();
        var email = TestAccounts.NewEmail("two-factor-" + role);
        if (role == "Patient")
        {
            (await client.PostAsJsonAsync("/api/patient-accounts/register",
                new RegisterPatientAccountRequest("Jan", "Test", email, TestAccounts.Password)))
                .StatusCode.Should().Be(HttpStatusCode.Created);
            if (confirmed)
            {
                var query = QueryHelpers.ParseQuery(new Uri(factory.Email.SentConfirmations.Last(x => x.To == email).ConfirmationLink).Query);
                (await client.PostAsJsonAsync("/api/patient-accounts/confirm-email",
                    new ConfirmPatientEmailRequest(query["userId"].ToString(), query["token"].ToString())))
                    .EnsureSuccessStatusCode();
            }
        }
        else
        {
            (await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest(email, TestAccounts.Password, "Anna", "Test"))).EnsureSuccessStatusCode();
            if (confirmed)
                await ChangeUser(factory, email, user => user.EmailConfirmed = true);
        }
        if (confirmed || role != "Patient") await Login(client, email);
        return (client, email);
    }

    internal static async Task ChangeUser(TestWebApplicationFactory factory, string email, Action<ApplicationUser> change)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        change(user);
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    public static async Task<TokenResponse> Login(HttpClient client, string email)
    {
        var tokens = await Read<TokenResponse>(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return tokens;
    }

    public static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<string> Error(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.Unauthorized)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("code").GetString()!;
    }

    public static string Code(TwoFactorClock clock, string key) => TotpCode.Generate(key, clock.GetUtcNow().ToUnixTimeSeconds() / 30);

    public static async Task<TwoFactorChallenge> Challenge(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password));
        var json = await Read<JsonElement>(response);
        json.TryGetProperty("accessToken", out _).Should().BeFalse();
        json.TryGetProperty("refreshToken", out _).Should().BeFalse();
        return json.Deserialize<TwoFactorChallenge>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public static async Task<TokenResponse> Verify(HttpClient client, TwoFactorChallenge challenge,
        string? code = null, string? recovery = null)
    {
        var tokens = await Read<TokenResponse>(await client.PostAsJsonAsync(Url + "/verify",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, code, recovery)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return tokens;
    }

    public static async Task<(string Key, string[] Codes, TokenResponse Tokens)> Enable(
        HttpClient client, string email, TwoFactorClock clock)
    {
        var setup = await Read<TwoFactorSetup>(await client.PostAsync(Url + "/setup", null));
        var codes = await Read<RecoveryCodesResponse>(await client.PostAsJsonAsync(Url + "/confirm", new { code = Code(clock, setup.SharedKey) }));
        clock.Advance();
        var tokens = await Verify(client, await Challenge(client, email), Code(clock, setup.SharedKey));
        return (setup.SharedKey, codes.RecoveryCodes, tokens);
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Therapist")]
    public async Task Setup_is_optional_requires_a_valid_code_and_login_requires_second_factor(string role)
    {
        var (client, email) = await Account(factory, role);
        var original = await Login(client, email);
        original.ShouldPromptTwoFactor.Should().BeTrue();
        var status = await Read<TwoFactorStatus>(await client.GetAsync(Url + "/status"));
        status.Enabled.Should().BeFalse();
        var setup = await Read<TwoFactorSetup>(await client.PostAsync(Url + "/setup", null));
        setup.AuthenticatorUri.Should().StartWith("otpauth://totp/").And.Contain("secret=" + setup.SharedKey);
        (await Login(client, email)).AccessToken.Should().NotBeNullOrWhiteSpace();
        (await Error(await client.PostAsJsonAsync(Url + "/confirm", new { code = "bad" }))).Should().Be("two_factor_invalid_code");
        (await Read<TwoFactorStatus>(await client.GetAsync(Url + "/status"))).Enabled.Should().BeFalse();
        var recovery = await Read<RecoveryCodesResponse>(await client.PostAsJsonAsync(Url + "/confirm", new { code = Code(factory.Clock, setup.SharedKey) }));
        recovery.RecoveryCodes.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        (await client.GetAsync(Url + "/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(original.RefreshToken)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = await Challenge(client, email);
        challenge.RequiresTwoFactor.Should().BeTrue();
        using var outsider = factory.CreateClient();
        outsider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", challenge.ChallengeToken);
        (await outsider.GetAsync(Url + "/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken })))
            .Should().Be("two_factor_invalid_code");
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, code = "bad" })))
            .Should().Be("two_factor_invalid_code");
        // The setup code has already been consumed.
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, code = Code(factory.Clock, setup.SharedKey) })))
            .Should().Be("two_factor_invalid_code");
        factory.Clock.Advance();
        var tokens = await Verify(client, challenge, Code(factory.Clock, setup.SharedKey));
        tokens.ShouldPromptTwoFactor.Should().BeFalse();
        (await Read<TwoFactorStatus>(await client.GetAsync(Url + "/status"))).Enabled.Should().BeTrue();
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, recoveryCode = recovery.RecoveryCodes[0] })))
            .Should().Be("two_factor_invalid_challenge");
        (await Read<TokenResponse>(await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(tokens.RefreshToken))))
            .ShouldPromptTwoFactor.Should().BeFalse();
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Therapist")]
    public async Task Dismissal_is_persisted_across_devices_and_does_not_prevent_setup(string role)
    {
        var (client, email) = await Account(factory, role);
        (await Login(client, email)).ShouldPromptTwoFactor.Should().BeTrue();
        (await client.PostAsync(Url + "/prompt/dismiss", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var otherDevice = factory.CreateClient();
        (await Login(otherDevice, email)).ShouldPromptTwoFactor.Should().BeFalse();
        (await Read<TwoFactorStatus>(await otherDevice.GetAsync(Url + "/status"))).ShouldPromptTwoFactor.Should().BeFalse();
        await Enable(otherDevice, email, factory.Clock);
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Therapist")]
    public async Task Recovery_is_single_use_regeneration_revokes_old_codes_and_disable_needs_reauthentication(string role)
    {
        var (client, email) = await Account(factory, role);
        var enabled = await Enable(client, email, factory.Clock);
        var challenge = await Challenge(client, email);
        await Verify(client, challenge, recovery: enabled.Codes[0]);
        (await Read<TwoFactorStatus>(await client.GetAsync(Url + "/status"))).RecoveryCodesRemaining.Should().Be(9);
        challenge = await Challenge(client, email);
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, recoveryCode = enabled.Codes[0] })))
            .Should().Be("two_factor_invalid_code");
        await Verify(client, challenge, recovery: enabled.Codes[9]);
        foreach (var action in new[] { "/disable", "/recovery-codes/regenerate" })
        {
            (await Error(await client.PostAsJsonAsync(Url + action, new { password = "wrong", recoveryCode = enabled.Codes[1] })))
                .Should().Be("two_factor_invalid_credentials");
            (await Error(await client.PostAsJsonAsync(Url + action, new { password = TestAccounts.Password })))
                .Should().Be("two_factor_invalid_code");
        }
        // A valid reauthentication proof replaces every recovery code atomically.
        var regenerated = await Read<RecoveryCodesResponse>(await client.PostAsJsonAsync(Url + "/recovery-codes/regenerate",
            new { password = TestAccounts.Password, recoveryCode = enabled.Codes[1] }));
        regenerated.RecoveryCodes.Should().NotIntersectWith(enabled.Codes);
        (await client.GetAsync(Url + "/status")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        challenge = await Challenge(client, email);
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, recoveryCode = enabled.Codes[2] })))
            .Should().Be("two_factor_invalid_code");
        await Verify(client, challenge, recovery: regenerated.RecoveryCodes[0]);
        (await client.PostAsJsonAsync(Url + "/disable", new { password = TestAccounts.Password, recoveryCode = regenerated.RecoveryCodes[1] }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var tokens = await Login(client, email);
        tokens.ShouldPromptTwoFactor.Should().BeFalse();
        var status = await Read<TwoFactorStatus>(await client.GetAsync(Url + "/status"));
        status.Enabled.Should().BeFalse();
        status.RecoveryCodesRemaining.Should().Be(0);
        // Disabling does not prevent explicitly enabling later.
        await Enable(client, email, factory.Clock);
    }

    [Fact]
    public async Task Expired_replaced_and_security_stamp_changed_challenges_cannot_issue_a_session()
    {
        var (client, email) = await Account(factory, "Patient");
        var enabled = await Enable(client, email, factory.Clock);
        var old = await Challenge(client, email);
        var current = await Challenge(client, email);
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = old.ChallengeToken, recoveryCode = enabled.Codes[0] })))
            .Should().Be("two_factor_invalid_challenge");
        factory.Clock.Advance(301);
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = current.ChallengeToken, recoveryCode = enabled.Codes[0] })))
            .Should().Be("two_factor_invalid_challenge");
        current = await Challenge(client, email);
        await ChangeUser(factory, email, u => u.SecurityStamp = Guid.NewGuid().ToString());
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = current.ChallengeToken, recoveryCode = enabled.Codes[0] })))
            .Should().Be("two_factor_invalid_challenge");
        await Verify(client, await Challenge(client, email), recovery: enabled.Codes[0]);
    }

    [Fact]
    public async Task Failed_proofs_lock_the_account_even_across_new_challenges_and_unlock_after_timeout()
    {
        var (client, email) = await Account(factory, "Patient");
        var enabled = await Enable(client, email, factory.Clock);
        for (var i = 0; i < 5; i++)
        {
            var challenge = await Challenge(client, email);
            await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, code = "bad" }),
                i == 4 ? HttpStatusCode.TooManyRequests : HttpStatusCode.Unauthorized);
        }
        (await Error(await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password)), HttpStatusCode.TooManyRequests))
            .Should().Be("two_factor_locked");
        factory.Clock.Advance(301);
        await Verify(client, await Challenge(client, email), recovery: enabled.Codes[0]);
    }

    [Fact]
    public async Task Unconfirmed_patient_cannot_login_and_unconfirmed_therapist_is_not_prompted()
    {
        var (patient, email) = await Account(factory, "Patient", confirmed: false);
        (await Error(await patient.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password)), HttpStatusCode.Forbidden))
            .Should().Be("email_confirmation_required");
        var (therapist, therapistEmail) = await Account(factory, "Therapist", confirmed: false);
        (await Login(therapist, therapistEmail)).ShouldPromptTwoFactor.Should().BeFalse();
    }

    [Fact]
    public async Task Restarting_or_expiring_setup_does_not_enable_two_factor_or_change_password_login()
    {
        var (client, email) = await Account(factory, "Patient");
        var old = await Read<TwoFactorSetup>(await client.PostAsync(Url + "/setup", null));
        var current = await Read<TwoFactorSetup>(await client.PostAsync(Url + "/setup", null));
        current.SharedKey.Should().NotBe(old.SharedKey);
        (await Error(await client.PostAsJsonAsync(Url + "/confirm", new { code = Code(factory.Clock, old.SharedKey) })))
            .Should().Be("two_factor_invalid_code");
        factory.Clock.Advance(601);
        (await Error(await client.PostAsJsonAsync(Url + "/confirm", new { code = Code(factory.Clock, current.SharedKey) }), HttpStatusCode.BadRequest))
            .Should().Be("two_factor_setup_expired");
        (await Login(client, email)).ShouldPromptTwoFactor.Should().BeTrue();
    }

    [Fact]
    public async Task Management_accepts_fresh_totp_and_revokes_sessions_and_outstanding_challenges()
    {
        var (client, email) = await Account(factory, "Therapist");
        var enabled = await Enable(client, email, factory.Clock);
        var oldChallenge = await Challenge(client, email);
        factory.Clock.Advance();
        var regenerated = await Read<RecoveryCodesResponse>(await client.PostAsJsonAsync(Url + "/recovery-codes/regenerate",
            new { password = TestAccounts.Password, code = Code(factory.Clock, enabled.Key) }));
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = oldChallenge.ChallengeToken, recoveryCode = enabled.Codes[0] })))
            .Should().Be("two_factor_invalid_challenge");
        (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(enabled.Tokens.RefreshToken)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Verify(client, await Challenge(client, email), recovery: regenerated.RecoveryCodes[0]);
        factory.Clock.Advance();
        (await client.PostAsJsonAsync(Url + "/disable", new { password = TestAccounts.Password, code = Code(factory.Clock, enabled.Key) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Login(client, email)).ShouldPromptTwoFactor.Should().BeFalse();
    }

    [Fact]
    public async Task Both_proofs_at_once_and_inactive_accounts_are_rejected()
    {
        var (client, email) = await Account(factory, "Patient");
        var enabled = await Enable(client, email, factory.Clock);
        var challenge = await Challenge(client, email);
        factory.Clock.Advance();
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new
        {
            challengeToken = challenge.ChallengeToken, code = Code(factory.Clock, enabled.Key), recoveryCode = enabled.Codes[0]
        }))).Should().Be("two_factor_invalid_code");
        // Rejected requests do not consume a recovery code.
        await Verify(client, challenge, recovery: enabled.Codes[0]);
        challenge = await Challenge(client, email);
        await ChangeUser(factory, email, u => u.IsActive = false);
        (await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, recoveryCode = enabled.Codes[1] }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Secret_material_is_encrypted_and_challenges_and_recovery_codes_are_not_stored_as_plaintext()
    {
        var (client, email) = await Account(factory, "Patient");
        var enabled = await Enable(client, email, factory.Clock);
        var challenge = await Challenge(client, email);
        using var scope = factory.Services.CreateScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
        user.ProtectedTwoFactorState.Should().NotContain(enabled.Key).And.NotContain(challenge.ChallengeToken);
        foreach (var recovery in enabled.Codes) user.ProtectedTwoFactorState.Should().NotContain(recovery);
        using var outsider = factory.CreateClient();
        (await outsider.PostAsync(Url + "/setup", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // Status never includes secret material, and responses containing credentials cannot be cached.
        var response = await client.GetAsync(Url + "/status");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().NotContain(enabled.Key);
    }

    public record RecoveryCodesResponse(string[] RecoveryCodes);
}

public sealed class LimitedTwoFactorFactory : TwoFactorFactory
{
    protected override int TwoFactorPermitLimit => 1;
}

public class TwoFactorRateLimitTests
{
    [Fact]
    public async Task Anonymous_verification_is_limited_by_ip()
    {
        using var factory = new LimitedTwoFactorFactory();
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync(TwoFactorTests.Url + "/verify", new { challengeToken = "bad" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TwoFactorTests.Error(await client.PostAsJsonAsync(TwoFactorTests.Url + "/verify", new { challengeToken = "bad" }), HttpStatusCode.TooManyRequests))
            .Should().Be("rate_limit_exceeded");
    }
}
