using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PatientAccountsEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private const string RegisterUrl = "/api/patient-accounts/register";
    private const string Password = "Haslo1234!";

    private readonly TestWebApplicationFactory _factory;

    public PatientAccountsEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Email.SimulateProviderFailure = false;
    }

    [Fact]
    public async Task Register_should_create_unconfirmed_patient_without_session_or_medical_card()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("  Anna ", " Nowak  ", email, Password));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = body.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        properties.Should().BeEquivalentTo("userId", "email", "emailConfirmationRequired");
        body.RootElement.GetProperty("emailConfirmationRequired").GetBoolean().Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        user!.Id.Should().Be(body.RootElement.GetProperty("userId").GetGuid());
        user.EmailConfirmed.Should().BeFalse();
        user.FirstName.Should().Be("Anna");
        user.LastName.Should().Be("Nowak");
        user.RefreshToken.Should().BeNull();
        (await userManager.GetRolesAsync(user)).Should().BeEquivalentTo("Patient");

        (await db.Patients.IgnoreQueryFilters().AnyAsync(p => p.UserId == user.Id || p.Email == email))
            .Should().BeFalse("rejestracja nie tworzy ani nie łączy karty medycznej");
        (await db.Therapists.IgnoreQueryFilters().AnyAsync(t => t.UserId == user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Register_should_send_confirmation_link_to_configured_frontend()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("Jan", "Kowalski", email, Password));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var sent = _factory.Email.SentConfirmations.Single(m => m.To == email);
        sent.FirstName.Should().Be("Jan");

        var link = new Uri(sent.ConfirmationLink);
        link.GetLeftPart(UriPartial.Path).Should().Be($"{TestWebApplicationFactory.FrontendBaseUrl}/confirm-email");

        var query = QueryHelpers.ParseQuery(link.Query);
        query.Keys.Should().BeEquivalentTo("userId", "token");

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        query["userId"].ToString().Should().Be(user!.Id.ToString());

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["token"].ToString()));
        (await userManager.ConfirmEmailAsync(user, token)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Register_should_ignore_role_sent_by_client()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync(RegisterUrl, new
        {
            firstName = "Ewa",
            lastName = "Zielińska",
            email,
            password = Password,
            role = "Therapist",
            roles = new[] { "Admin" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        (await userManager.GetRolesAsync(user!)).Should().BeEquivalentTo("Patient");
    }

    [Fact]
    public async Task Register_with_existing_patient_email_should_return_409_and_keep_account()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        (await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("Piotr", "Wiśniewski", email, Password)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("Inny", "Ktoś", email.ToUpperInvariant(), "NoweHaslo99"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<ProblemBody>();
        problem!.Status.Should().Be(409);
        problem.Detail.Should().NotBeNullOrWhiteSpace();

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user!.FirstName.Should().Be("Piotr");
        (await userManager.CheckPasswordAsync(user, Password)).Should().BeTrue();
        (await userManager.CheckPasswordAsync(user, "NoweHaslo99")).Should().BeFalse();
        _factory.Email.SentConfirmations.Count(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public async Task Register_with_therapist_email_should_return_409_and_keep_therapist_role()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, "Tomasz", "Terapeuta")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("Tomasz", "Pacjent", email, "NoweHaslo99"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        (await userManager.GetRolesAsync(user!)).Should().BeEquivalentTo("Therapist");
        (await userManager.CheckPasswordAsync(user!, Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Register_with_missing_fields_should_return_400_with_field_errors()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(RegisterUrl, new { firstName = " ", email = "to-nie-email" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        problem!.Errors.Should().ContainKeys("firstName", "lastName", "email", "password");
    }

    [Fact]
    public async Task Register_with_invalid_fields_and_weak_password_should_report_all_errors_at_once()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("", "Nowak", "zly-email", "abc"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        problem!.Errors.Should().ContainKeys("firstName", "email", "password");
    }

    [Fact]
    public async Task Register_with_weak_password_should_return_400_and_not_create_account()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync(RegisterUrl,
            new RegisterPatientAccountRequest("Ola", "Lis", email, "krotkie"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        problem!.Errors.Should().ContainKey("password");
        problem.Errors!["password"].Should().Contain(m => m.Contains("8"));

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.FindByEmailAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task Register_when_email_provider_fails_should_return_503_and_allow_retry()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        var request = new RegisterPatientAccountRequest("Kasia", "Mazur", email, Password);

        _factory.Email.SimulateProviderFailure = true;
        var failed = await client.PostAsJsonAsync(RegisterUrl, request);

        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var problem = await failed.Content.ReadFromJsonAsync<ProblemBody>();
        problem!.Status.Should().Be(503);
        problem.Detail.Should().NotBeNullOrWhiteSpace();

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await userManager.FindByEmailAsync(email)).Should().BeNull();
        }

        _factory.Email.SimulateProviderFailure = false;
        var retry = await client.PostAsJsonAsync(RegisterUrl, request);

        retry.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static string NewEmail() => $"patient-{Guid.NewGuid():N}@physioflow.pl";

    private record ProblemBody(string? Title, int Status, string? Detail, Dictionary<string, string[]>? Errors);
}

public class PatientAccountsRateLimitTests : IClassFixture<PatientAccountsRateLimitTests.LowLimitFactory>
{
    public class LowLimitFactory : TestWebApplicationFactory
    {
        protected override int PatientRegistrationPermitLimit => 2;
    }

    private readonly LowLimitFactory _factory;

    public PatientAccountsRateLimitTests(LowLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_should_return_429_after_exceeding_limit()
    {
        var client = _factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/patient-accounts/register",
                new RegisterPatientAccountRequest("Adam", "Limit", $"limit-{Guid.NewGuid():N}@physioflow.pl", "Haslo1234!"));
            allowed.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var blocked = await client.PostAsJsonAsync("/api/patient-accounts/register",
            new RegisterPatientAccountRequest("Adam", "Limit", $"limit-{Guid.NewGuid():N}@physioflow.pl", "Haslo1234!"));

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        _factory.Email.SentConfirmations.Should().HaveCount(2);
    }
}
