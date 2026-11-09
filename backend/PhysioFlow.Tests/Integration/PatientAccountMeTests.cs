using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PatientAccountMeTests : IClassFixture<TestWebApplicationFactory>
{
    private const string MeUrl = "/api/patient-accounts/me";

    private readonly TestWebApplicationFactory _factory;

    public PatientAccountMeTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Me_without_session_should_return_401()
    {
        var response = await _factory.CreateClient().GetAsync(MeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_for_therapist_should_return_403()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var response = await therapist.GetAsync(MeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Me_for_patient_without_card_should_return_own_account_only()
    {
        var (client, email) = await _factory.CreateConfirmedPatientClientAsync("Jan", "Kowalski");

        var response = await client.GetAsync(MeUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("id", "firstName", "lastName", "email", "emailConfirmed");

        var me = await response.Content.ReadFromJsonAsync<PatientAccountDto>();
        me!.Email.Should().Be(email);
        me.FirstName.Should().Be("Jan");
        me.LastName.Should().Be("Kowalski");
        me.EmailConfirmed.Should().BeTrue();
        me.Id.Should().Be(await AccountIdAsync(email));
    }

    [Fact]
    public async Task Me_should_return_data_of_session_owner_regardless_of_client_supplied_id()
    {
        var (first, firstEmail) = await _factory.CreateConfirmedPatientClientAsync("Ewa", "Pierwsza");
        var (second, secondEmail) = await _factory.CreateConfirmedPatientClientAsync("Olga", "Druga");
        var secondId = await AccountIdAsync(secondEmail);

        var firstMe = await first.GetFromJsonAsync<PatientAccountDto>($"{MeUrl}?id={secondId}&userId={secondId}");
        var secondMe = await second.GetFromJsonAsync<PatientAccountDto>(MeUrl);

        firstMe!.Email.Should().Be(firstEmail);
        firstMe.Id.Should().NotBe(secondId);
        secondMe!.Email.Should().Be(secondEmail);
        secondMe.Id.Should().Be(secondId);
    }

    [Fact]
    public async Task Me_should_not_expose_linked_patient_card_data()
    {
        var (client, email) = await _factory.CreateConfirmedPatientClientAsync();
        var accountId = await AccountIdAsync(email);
        await LinkPatientCardAsync(accountId);

        var body = await (await client.GetAsync(MeUrl)).Content.ReadAsStringAsync();

        body.Should().NotContain("90010112345")
            .And.NotContain("Tajna notatka")
            .And.NotContain("Historia choroby");
        JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid().Should().Be(accountId);
    }

    private async Task<Guid> AccountIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private async Task LinkPatientCardAsync(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var therapist = new TherapistProfile { UserId = Guid.NewGuid(), FirstName = "Karta", LastName = "Terapeuta" };
        db.Therapists.Add(therapist);
        db.Patients.Add(new Patient
        {
            UserId = accountId,
            TherapistId = therapist.Id,
            FirstName = "Jan",
            LastName = "Pacjent",
            Pesel = "90010112345",
            GeneralNotes = "Tajna notatka",
            MedicalHistory = "Historia choroby",
        });
        await db.SaveChangesAsync();
    }
}
