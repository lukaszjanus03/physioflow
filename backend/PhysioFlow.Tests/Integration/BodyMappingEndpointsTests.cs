using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.BodyMapping.Commands.CreateBodyMap;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

/// <summary>
/// End-to-end test body mapping flow:
/// register therapist → login → add patient → create body map → list → get by id.
/// </summary>
public class BodyMappingEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BodyMappingEndpointsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Full_Flow_Create_List_Get_BodyMap()
    {
        var client = _factory.CreateClient();

        // 1. Rejestracja terapeuty
        var email = $"therapist-{Guid.NewGuid()}@physioflow.pl";
        const string password = "Haslo1234!";
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, password, "Anna", "Test"));
        reg.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Login
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var tokens = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        // 3. Utworzenie pacjenta
        var createPatientResp = await client.PostAsJsonAsync("/api/patients", new
        {
            firstName = "Jan",
            lastName = "Kowalski",
        });
        createPatientResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var patientIdResp = await createPatientResp.Content.ReadFromJsonAsync<IdResponse>();
        var patientId = patientIdResp!.Id;

        // 4. Utworzenie body map z 3 pain pointami
        var createMapCmd = new CreateBodyMapCommand(
            patientId,
            null,
            "Pierwsza ocena",
            new List<PainPointInput>
            {
                new(BodySide.Front, 0.5m, 0.2m, PainPointSeverity.AcutePain, "Szyja"),
                new(BodySide.Front, 0.3m, 0.4m, PainPointSeverity.Tension, "Prawe ramię"),
                new(BodySide.Back, 0.5m, 0.5m, PainPointSeverity.ChronicPain, "Lędźwie"),
            });

        var createMapResp = await client.PostAsJsonAsync("/api/bodymapping", createMapCmd);
        createMapResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var mapIdResp = await createMapResp.Content.ReadFromJsonAsync<IdResponse>();
        var mapId = mapIdResp!.Id;

        // 5. Lista snapshotów pacjenta
        var listResp = await client.GetAsync($"/api/bodymapping/patient/{patientId}");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await listResp.Content.ReadFromJsonAsync<List<BodyMapSummaryDto>>();
        summaries.Should().HaveCount(1);
        summaries![0].PainPointsCount.Should().Be(3);
        summaries[0].CreatedByRole.Should().Be("Therapist");

        // 6. Pobranie szczegółów
        var getResp = await client.GetAsync($"/api/bodymapping/{mapId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await getResp.Content.ReadFromJsonAsync<BodyMapDto>();
        detail!.PainPoints.Should().HaveCount(3);
        detail.PainPoints.Should().Contain(p => p.Severity == "AcutePain" && p.Note == "Szyja");
        detail.Notes.Should().Be("Pierwsza ocena");
    }

    [Fact]
    public async Task Create_BodyMap_Should_Fail_Without_Auth()
    {
        var client = _factory.CreateClient();

        var cmd = new CreateBodyMapCommand(Guid.NewGuid(), null, null,
            new List<PainPointInput> { new(BodySide.Front, 0.5m, 0.5m, PainPointSeverity.AcutePain, null) });

        var response = await client.PostAsJsonAsync("/api/bodymapping", cmd);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_BodyMap_With_Invalid_Coordinates_Should_Return_400()
    {
        var client = _factory.CreateClient();

        // Setup: rejestracja + login + utworzenie pacjenta
        var email = $"therapist-{Guid.NewGuid()}@physioflow.pl";
        await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, "Haslo1234!", "T", "T"));
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Haslo1234!"));
        var tokens = await loginResp.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var patientResp = await client.PostAsJsonAsync("/api/patients", new { firstName = "A", lastName = "B" });
        var patientId = (await patientResp.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        // X > 1.0 (niepoprawne znormalizowane koordynaty)
        var cmd = new CreateBodyMapCommand(patientId, null, null,
            new List<PainPointInput> { new(BodySide.Front, 1.5m, 0.5m, PainPointSeverity.AcutePain, null) });

        var response = await client.PostAsJsonAsync("/api/bodymapping", cmd);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Helper record
    private record IdResponse(Guid Id);
}
