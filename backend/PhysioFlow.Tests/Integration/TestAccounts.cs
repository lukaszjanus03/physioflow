using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using PhysioFlow.Api.Controllers;

namespace PhysioFlow.Tests.Integration;

/// <summary>Zakładanie kont przez API i klienci HTTP z gotową sesją.</summary>
public static class TestAccounts
{
    public const string Password = "Haslo1234!";

    public static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@physioflow.pl";

    public static async Task<HttpClient> CreateTherapistClientAsync(
        this TestWebApplicationFactory factory, string firstName = "Anna", string lastName = "Terapeutka")
    {
        var client = factory.CreateClient();
        var email = NewEmail("therapist");

        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, Password, firstName, lastName)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await LoginAsync(client, email);
        return client;
    }

    /// <summary>Pacjent po rejestracji i potwierdzeniu adresu, bez karty i bez terapeuty.</summary>
    public static async Task<(HttpClient Client, string Email)> CreateConfirmedPatientClientAsync(
        this TestWebApplicationFactory factory, string firstName = "Jan", string lastName = "Pacjent")
    {
        var client = factory.CreateClient();
        var email = NewEmail("patient");

        (await client.PostAsJsonAsync("/api/patient-accounts/register",
            new RegisterPatientAccountRequest(firstName, lastName, email, Password)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var link = factory.Email.SentConfirmations.Last(m => m.To == email).ConfirmationLink;
        var query = QueryHelpers.ParseQuery(new Uri(link).Query);
        (await client.PostAsJsonAsync("/api/patient-accounts/confirm-email",
            new ConfirmPatientEmailRequest(query["userId"].ToString(), query["token"].ToString())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await LoginAsync(client, email);
        return (client, email);
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
