using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Services;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PatientEmailConfirmationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Haslo1234!";
    private const string ConfirmUrl = "/api/patient-accounts/confirm-email";
    private const string ResendUrl = "/api/patient-accounts/resend-confirmation";

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PatientEmailConfirmationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Email.SimulateProviderFailure = false;
        _client = factory.CreateClient();
    }

    // --- confirm-email

    [Fact]
    public async Task Confirm_with_valid_link_should_confirm_email_without_creating_session()
    {
        var (email, link) = await RegisterPatientAsync();

        var response = await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(link.UserId, link.Token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("{\"emailConfirmed\":true}");

        var user = await FindUserAsync(email);
        user!.EmailConfirmed.Should().BeTrue();
        user.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_with_already_used_link_should_return_409()
    {
        var (_, link) = await RegisterPatientAsync();
        (await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(link.UserId, link.Token)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(link.UserId, link.Token));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(second)).Code.Should().Be(PatientAccountErrorCodes.EmailAlreadyConfirmed);
    }

    public static TheoryData<string?, string?> InvalidLinks => new()
    {
        { null, null },
        { "not-a-guid", "abc" },
        { Guid.NewGuid().ToString(), "abc" },
        { Guid.NewGuid().ToString(), "%%%not-base64%%%" },
    };

    [Theory]
    [MemberData(nameof(InvalidLinks))]
    public async Task Confirm_with_malformed_or_unknown_link_should_return_400(string? userId, string? token)
    {
        var response = await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(userId, token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Code.Should().Be(PatientAccountErrorCodes.InvalidOrExpiredLink);
    }

    [Fact]
    public async Task Confirm_with_tampered_token_should_return_400_and_keep_email_unconfirmed()
    {
        var (email, link) = await RegisterPatientAsync();
        var tampered = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("forged-token"));

        var response = await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(link.UserId, tampered));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FindUserAsync(email))!.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_with_token_of_another_account_should_return_400()
    {
        var (_, first) = await RegisterPatientAsync();
        var (secondEmail, second) = await RegisterPatientAsync();

        var response = await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(second.UserId, first.Token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FindUserAsync(secondEmail))!.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_for_therapist_account_should_return_400_even_with_valid_token()
    {
        var email = NewEmail("therapist");
        (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, "Tomasz", "Terapeuta")))
            .EnsureSuccessStatusCode();

        string userId, token;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            userId = user!.Id.ToString();
            token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                await userManager.GenerateEmailConfirmationTokenAsync(user)));
        }

        var response = await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(userId, token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FindUserAsync(email))!.EmailConfirmed.Should().BeFalse();
    }

    // --- login / refresh

    [Fact]
    public async Task Login_before_confirmation_should_return_403_without_tokens()
    {
        var (email, _) = await RegisterPatientAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("accessToken").And.NotContain("refreshToken");
        (await ReadProblemAsync(response)).Code.Should().Be(PatientAccountErrorCodes.EmailConfirmationRequired);
        (await FindUserAsync(email))!.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Login_before_confirmation_with_wrong_password_should_return_401_not_403()
    {
        var (email, _) = await RegisterPatientAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "ZleHaslo123"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_after_confirmation_should_return_tokens_with_patient_role()
    {
        var (email, link) = await RegisterPatientAsync();
        (await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(link.UserId, link.Token)))
            .EnsureSuccessStatusCode();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens!.AccessToken);
        jwt.Claims.Where(c => c.Type is ClaimTypes.Role or "role").Select(c => c.Value)
            .Should().BeEquivalentTo(PatientAccountRules.PatientRole);
    }

    [Fact]
    public async Task Refresh_for_unconfirmed_patient_should_return_403_and_revoke_refresh_token()
    {
        var (email, _) = await RegisterPatientAsync();
        const string refreshToken = "session-issued-before-email-confirmation-was-required";
        await UpdateUserAsync(email, u =>
        {
            u.RefreshToken = refreshToken;
            u.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        });

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(refreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).Code.Should().Be(PatientAccountErrorCodes.EmailConfirmationRequired);
        (await FindUserAsync(email))!.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task Access_token_of_unconfirmed_patient_should_be_rejected()
    {
        var (email, _) = await RegisterPatientAsync();

        string accessToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
            accessToken = scope.ServiceProvider.GetRequiredService<IJwtTokenService>()
                .GenerateAccessToken(user!, new[] { PatientAccountRules.PatientRole });
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/patients");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Therapist_with_unconfirmed_email_should_still_log_in()
    {
        var email = NewEmail("therapist");
        (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, "Anna", "Terapeutka")))
            .EnsureSuccessStatusCode();
        (await FindUserAsync(email))!.EmailConfirmed.Should().BeFalse();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- resend-confirmation

    [Fact]
    public async Task Resend_for_unconfirmed_patient_should_send_new_working_link()
    {
        var (email, _) = await RegisterPatientAsync();

        var response = await _client.PostAsJsonAsync(ResendUrl, new ResendConfirmationRequest(email));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var links = _factory.Email.SentConfirmations.Where(m => m.To == email).ToList();
        links.Should().HaveCount(2);

        var newLink = ParseLink(links.Last().ConfirmationLink);
        (await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(newLink.UserId, newLink.Token)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resend_should_return_identical_response_without_sending_for_unknown_confirmed_or_therapist_accounts()
    {
        var (confirmedEmail, link) = await RegisterPatientAsync();
        (await _client.PostAsJsonAsync(ConfirmUrl, new ConfirmPatientEmailRequest(link.UserId, link.Token)))
            .EnsureSuccessStatusCode();

        var therapistEmail = NewEmail("therapist");
        (await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(therapistEmail, Password, "T", "T")))
            .EnsureSuccessStatusCode();

        var (unconfirmedEmail, _) = await RegisterPatientAsync();
        var reference = await _client.PostAsJsonAsync(ResendUrl, new ResendConfirmationRequest(unconfirmedEmail));
        var referenceBody = await reference.Content.ReadAsStringAsync();

        var sentBefore = _factory.Email.SentConfirmations.Count;
        foreach (var email in new[] { NewEmail("unknown"), confirmedEmail, therapistEmail })
        {
            var response = await _client.PostAsJsonAsync(ResendUrl, new ResendConfirmationRequest(email));
            response.StatusCode.Should().Be(reference.StatusCode);
            (await response.Content.ReadAsStringAsync()).Should().Be(referenceBody);
        }

        _factory.Email.SentConfirmations.Count.Should().Be(sentBefore);
    }

    [Fact]
    public async Task Resend_when_email_provider_fails_should_return_same_response()
    {
        var (email, _) = await RegisterPatientAsync();
        var expected = await (await _client.PostAsJsonAsync(ResendUrl, new ResendConfirmationRequest(NewEmail("unknown"))))
            .Content.ReadAsStringAsync();

        _factory.Email.SimulateProviderFailure = true;
        var response = await _client.PostAsJsonAsync(ResendUrl, new ResendConfirmationRequest(email));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task Resend_with_invalid_email_should_return_400()
    {
        var response = await _client.PostAsJsonAsync(ResendUrl, new ResendConfirmationRequest("to-nie-email"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- helpers

    private async Task<(string Email, ConfirmationLink Link)> RegisterPatientAsync()
    {
        var email = NewEmail("patient");
        (await _client.PostAsJsonAsync("/api/patient-accounts/register",
            new RegisterPatientAccountRequest("Anna", "Nowak", email, Password)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var sent = _factory.Email.SentConfirmations.Last(m => m.To == email);
        return (email, ParseLink(sent.ConfirmationLink));
    }

    internal static ConfirmationLink ParseLink(string link)
    {
        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        return new ConfirmationLink(query["userId"].ToString(), query["token"].ToString());
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task UpdateUserAsync(string email, Action<ApplicationUser> update)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        update(user!);
        (await userManager.UpdateAsync(user!)).Succeeded.Should().BeTrue();
    }

    internal static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!;

    internal static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@physioflow.pl";

    internal record ConfirmationLink(string UserId, string Token);

    internal record ProblemBody(string? Title, int Status, string? Code, string? Detail);
}

public class ExpiredConfirmationLinkTests : IClassFixture<ExpiredConfirmationLinkTests.ShortTokenLifespanFactory>
{
    public class ShortTokenLifespanFactory : TestWebApplicationFactory
    {
        // Ujemna ważność sprawia, że token jest przeterminowany od chwili wygenerowania.
        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(-1));
    }

    private readonly ShortTokenLifespanFactory _factory;

    public ExpiredConfirmationLinkTests(ShortTokenLifespanFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_with_expired_link_should_return_400_and_keep_email_unconfirmed()
    {
        var client = _factory.CreateClient();
        var email = PatientEmailConfirmationTests.NewEmail("expired");
        (await client.PostAsJsonAsync("/api/patient-accounts/register",
            new RegisterPatientAccountRequest("Anna", "Nowak", email, "Haslo1234!")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var link = PatientEmailConfirmationTests.ParseLink(
            _factory.Email.SentConfirmations.Single(m => m.To == email).ConfirmationLink);

        var response = await client.PostAsJsonAsync("/api/patient-accounts/confirm-email",
            new ConfirmPatientEmailRequest(link.UserId, link.Token));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PatientEmailConfirmationTests.ReadProblemAsync(response)).Code
            .Should().Be(PatientAccountErrorCodes.InvalidOrExpiredLink);
    }
}

public class ConfirmationResendLimitTests : IClassFixture<ConfirmationResendLimitTests.LowLimitFactory>
{
    public class LowLimitFactory : TestWebApplicationFactory
    {
        protected override int ConfirmationResendPermitLimit => 3;
        protected override int ConfirmationEmailCooldownSeconds => 3600;
    }

    private readonly LowLimitFactory _factory;

    public ConfirmationResendLimitTests(LowLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Resend_should_respect_per_account_cooldown_and_per_ip_limit()
    {
        var client = _factory.CreateClient();
        var email = PatientEmailConfirmationTests.NewEmail("cooldown");
        (await client.PostAsJsonAsync("/api/patient-accounts/register",
            new RegisterPatientAccountRequest("Anna", "Nowak", email, "Haslo1234!")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Rejestracja uruchamia odstęp, więc kolejne prośby w jego trakcie nie wysyłają nic.
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/patient-accounts/resend-confirmation", new ResendConfirmationRequest(email)))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        _factory.Email.SentConfirmations.Count(m => m.To == email).Should().Be(1);

        var blocked = await client.PostAsJsonAsync("/api/patient-accounts/resend-confirmation",
            new ResendConfirmationRequest(email));
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
