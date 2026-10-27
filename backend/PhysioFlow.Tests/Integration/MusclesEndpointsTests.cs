using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using PhysioFlow.Api.Controllers;
using PhysioFlow.Application.Features.Muscles.Queries.GetMuscleByRegion;
using PhysioFlow.Infrastructure.Persistence.Seeds;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class MusclesEndpointsTests
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MusclesEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_should_return_seeded_information_for_every_region()
    {
        using var client = await CreateTherapistClient();

        foreach (var expected in AnatomicalRegionInfoSeed.Create())
        {
            var response = await client.GetAsync(
                $"/api/muscles/by-region/{expected.RegionId}");

            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                $"region {expected.RegionId} should be available");

            var actual = await response.Content
                .ReadFromJsonAsync<AnatomicalRegionInfoDto>();

            actual.Should().NotBeNull();

            actual!.RegionId.Should().Be(expected.RegionId);
            actual.Name.Should().Be(expected.Name);
            actual.Description.Should().Be(expected.Description);
            actual.Function.Should().Be(expected.Function);

            actual.CommonIssues.Should()
                .Equal(expected.CommonIssues);

            actual.ExaminationTips.Should()
                .Equal(expected.ExaminationTips);
        }
    }

    [Fact]
    public async Task Get_should_return_404_for_unknown_region()
    {
        using var client = await CreateTherapistClient();

        var response = await client.GetAsync(
            "/api/muscles/by-region/front-unknown-region");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_should_require_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            "/api/muscles/by-region/front-right-biceps");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_should_reject_invalid_region_id()
    {
        using var client = await CreateTherapistClient();

        var response = await client.GetAsync(
            "/api/muscles/by-region/INVALID_REGION");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Seed_should_cover_current_hotspot_identifiers()
    {
        string[] expectedRegionIds =
        {
            "front-neck",
            "front-right-shoulder",
            "front-left-shoulder",
            "front-right-pec",
            "front-left-pec",
            "front-sternum",
            "front-upper-abdomen",
            "front-lower-abdomen",
            "front-right-biceps",
            "front-left-biceps",
            "front-right-forearm",
            "front-left-forearm",
            "front-right-hand",
            "front-left-hand",
            "front-right-thigh",
            "front-left-thigh",
            "front-right-knee",
            "front-left-knee",
            "front-right-shin",
            "front-left-shin",
            "front-right-foot",
            "front-left-foot",
            "back-neck",
            "back-left-trapezius",
            "back-right-trapezius",
            "back-left-rear-shoulder",
            "back-right-rear-shoulder",
            "back-left-scapula",
            "back-right-scapula",
            "back-thoracic-spine",
            "back-lumbar-spine",
            "back-left-triceps",
            "back-right-triceps",
            "back-left-forearm",
            "back-right-forearm",
            "back-left-hand",
            "back-right-hand",
            "back-left-glute",
            "back-right-glute",
            "back-left-hamstring",
            "back-right-hamstring",
            "back-left-knee",
            "back-right-knee",
            "back-left-calf",
            "back-right-calf",
            "back-left-foot",
            "back-right-foot",
        };

        var regions = AnatomicalRegionInfoSeed.Create();

        regions.Select(x => x.RegionId)
            .Should().BeEquivalentTo(expectedRegionIds);

        regions.Select(x => x.RegionId)
            .Should().OnlyHaveUniqueItems();

        regions.Select(x => x.Id)
            .Should().OnlyHaveUniqueItems();

        regions.Should().OnlyContain(x =>
            x.Id != Guid.Empty &&
            !string.IsNullOrWhiteSpace(x.Name) &&
            !string.IsNullOrWhiteSpace(x.Description) &&
            !string.IsNullOrWhiteSpace(x.Function));
    }

    [Fact]
    public void Seed_should_generate_stable_data()
    {
        var first = AnatomicalRegionInfoSeed.Create();
        var second = AnatomicalRegionInfoSeed.Create();

        second.Should().BeEquivalentTo(first);
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
}