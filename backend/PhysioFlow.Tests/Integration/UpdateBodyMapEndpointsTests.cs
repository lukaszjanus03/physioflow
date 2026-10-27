using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class UpdateBodyMapEndpointsTests
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public UpdateBodyMapEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Update_should_replace_points_and_save_notes()
    {
        using var client = await CreateTherapistClient();
        var mapId = await CreateMap(client);

        var before = await GetMap(client, mapId);
        var oldPointIds = before.PainPoints.Select(p => p.Id).ToArray();

        var response = await client.PutAsJsonAsync(
            $"/api/bodymapping/{mapId}",
            new
            {
                notes = "Updated map",
                painPoints = new[]
                {
                    new
                    {
                        side = "Back",
                        x = 0.42m,
                        y = 0.31m,
                        severity = "ChronicPain",
                        painScale = 7,
                        note = "Updated point",
                    },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();

        var after = await GetMap(client, mapId);

        after.Id.Should().Be(before.Id);
        after.PatientId.Should().Be(before.PatientId);
        after.RecordedAt.Should().Be(before.RecordedAt);
        after.Notes.Should().Be("Updated map");

        var point = after.PainPoints.Should().ContainSingle().Subject;
        point.Side.Should().Be("Back");
        point.X.Should().Be(0.42m);
        point.Y.Should().Be(0.31m);
        point.Severity.Should().Be("ChronicPain");
        point.PainScale.Should().Be(7);
        point.Note.Should().Be("Updated point");
        oldPointIds.Should().NotContain(point.Id);

        // Sprawdzamy też bazę, nie tylko odpowiedź GET.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

        var storedPoints = await db.PainPoints
            .Where(p => p.BodyMapId == mapId)
            .ToListAsync();

        storedPoints.Should().ContainSingle();
        storedPoints.Should().NotContain(p => oldPointIds.Contains(p.Id));
    }

    [Fact]
    public async Task Update_should_allow_removing_all_points()
    {
        using var client = await CreateTherapistClient();
        var mapId = await CreateMap(client);

        var response = await client.PutAsJsonAsync(
            $"/api/bodymapping/{mapId}",
            new
            {
                notes = (string?)null,
                painPoints = Array.Empty<object>(),
            });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var map = await GetMap(client, mapId);

        map.Notes.Should().BeNull();
        map.PainPoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_should_reject_another_therapist()
    {
        using var owner = await CreateTherapistClient();
        var mapId = await CreateMap(owner);
        var before = await GetMap(owner, mapId);

        using var otherTherapist = await CreateTherapistClient();

        var response = await otherTherapist.PutAsJsonAsync(
            $"/api/bodymapping/{mapId}",
            CreateUpdatePayload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var after = await GetMap(owner, mapId);
        after.Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Update_should_require_authentication()
    {
        using var owner = await CreateTherapistClient();
        var mapId = await CreateMap(owner);

        using var anonymous = _factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync(
            $"/api/bodymapping/{mapId}",
            CreateUpdatePayload());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_should_return_404_for_missing_map()
    {
        using var client = await CreateTherapistClient();

        var response = await client.PutAsJsonAsync(
            $"/api/bodymapping/{Guid.NewGuid()}",
            CreateUpdatePayload());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Front", -0.1, 0.5, "AcutePain", 7)]
    [InlineData("Front", 1.1, 0.5, "AcutePain", 7)]
    [InlineData("Front", 0.5, -0.1, "AcutePain", 7)]
    [InlineData("Front", 0.5, 1.1, "AcutePain", 7)]
    [InlineData("Front", 0.5, 0.5, "AcutePain", 0)]
    [InlineData("Front", 0.5, 0.5, "AcutePain", 11)]
    [InlineData("Unknown", 0.5, 0.5, "AcutePain", 7)]
    [InlineData("Front", 0.5, 0.5, "Unknown", 7)]
    public async Task Update_should_reject_invalid_data_without_changing_map(
        string side,
        double x,
        double y,
        string severity,
        int painScale)
    {
        using var client = await CreateTherapistClient();
        var mapId = await CreateMap(client);
        var before = await GetMap(client, mapId);

        var response = await client.PutAsJsonAsync(
            $"/api/bodymapping/{mapId}",
            new
            {
                notes = "This change must not be saved",
                painPoints = new[]
                {
                    new
                    {
                        side,
                        x,
                        y,
                        severity,
                        painScale,
                        note = "Invalid point",
                    },
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await GetMap(client, mapId);
        after.Should().BeEquivalentTo(before);
    }

    private async Task<HttpClient> CreateTherapistClient()
    {
        var client = _factory.CreateClient();
        var email = $"therapist-{Guid.NewGuid():N}@example.com";
        const string password = "Haslo1234!";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, password, "Anna", "Test"));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokens = await loginResponse.Content
            .ReadFromJsonAsync<TokenResponse>();

        tokens.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }

    private static async Task<Guid> CreateMap(HttpClient client)
    {
        var patientResponse = await client.PostAsJsonAsync(
            "/api/patients",
            new
            {
                firstName = "Jan",
                lastName = "Test",
            });

        patientResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var patient = await patientResponse.Content
            .ReadFromJsonAsync<IdResponse>();

        patient.Should().NotBeNull();

        var mapResponse = await client.PostAsJsonAsync(
            "/api/bodymapping",
            new
            {
                patientId = patient!.Id,
                notes = "Original map",
                painPoints = new[]
                {
                    new
                    {
                        side = "Front",
                        x = 0.3m,
                        y = 0.4m,
                        severity = "AcutePain",
                        painScale = 3,
                        note = "Original point",
                    },
                    new
                    {
                        side = "Back",
                        x = 0.6m,
                        y = 0.5m,
                        severity = "Tension",
                        painScale = 4,
                        note = "Second original point",
                    },
                },
            });

        mapResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var map = await mapResponse.Content
            .ReadFromJsonAsync<IdResponse>();

        map.Should().NotBeNull();

        return map!.Id;
    }

    private static async Task<BodyMapDto> GetMap(
        HttpClient client,
        Guid mapId)
    {
        var response = await client.GetAsync(
            $"/api/bodymapping/{mapId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var map = await response.Content
            .ReadFromJsonAsync<BodyMapDto>();

        map.Should().NotBeNull();

        return map!;
    }

    private static object CreateUpdatePayload()
    {
        return new
        {
            notes = "Updated map",
            painPoints = new[]
            {
                new
                {
                    side = "Front",
                    x = 0.5m,
                    y = 0.5m,
                    severity = "AcutePain",
                    painScale = 7,
                    note = "Updated point",
                },
            },
        };
    }

    private record IdResponse(Guid Id);
}