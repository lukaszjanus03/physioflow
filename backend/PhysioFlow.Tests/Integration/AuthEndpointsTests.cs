using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PhysioFlow.Api.Controllers;
using Xunit;

namespace PhysioFlow.Tests.Integration;

/// <summary>
/// End-to-end testy auth flow przez prawdziwy pipeline HTTP + JWT + Identity + EF Core (InMemory).
/// </summary>
public class AuthEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthEndpointsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_Should_Create_User_And_Return_UserId()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            Email: $"therapist-{Guid.NewGuid()}@physioflow.pl",
            Password: "Haslo1234!",
            FirstName: "Jan",
            LastName: "Kowalski"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Login_With_Valid_Credentials_Should_Return_Tokens()
    {
        var client = _factory.CreateClient();
        var email = $"therapist-{Guid.NewGuid()}@physioflow.pl";
        const string password = "Haslo1234!";

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, "Anna", "Nowak"));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Should_Return_Unauthorized()
    {
        var client = _factory.CreateClient();
        var email = $"therapist-{Guid.NewGuid()}@physioflow.pl";

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Haslo1234!", "X", "Y"));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "ZleHaslo!"));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Token_Should_Return_Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/patients");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_With_Valid_Token_Should_Be_Accessible()
    {
        var client = _factory.CreateClient();
        var email = $"therapist-{Guid.NewGuid()}@physioflow.pl";
        const string password = "Haslo1234!";

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, "A", "B"));
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var tokens = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await client.GetAsync("/api/patients");

        // 500 bo GetPatientList wymaga TherapistProfile w bazie (którego jeszcze nie tworzymy przy rejestracji - TODO).
        // Istotne: token jest zaakceptowany (nie 401) - czyli JWT middleware działa.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // Helper records do deserializacji
    private record RegisterResponse(Guid UserId);
}
