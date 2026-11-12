using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PublicTherapistCatalogTests : IClassFixture<PhotoTestFactory>
{
    private readonly PhotoTestFactory _factory;
    public PublicTherapistCatalogTests(PhotoTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_responses_should_expose_only_public_dto_and_explicit_contacts_without_personalization()
    {
        var tag = Guid.NewGuid().ToString("N");
        var owner = await _factory.CreateTherapistClientAsync();
        var marketplace = new TherapistMarketplaceDto(["sports"], "10 lat", "AWF", "Długa", "12", "30-001",
            "+48 123 456 789", "gabinet@example.pl", "Parking", "Strój sportowy");
        var request = TherapistProfileTests.Draft("Kraków", tag, true) with { Marketplace = marketplace };
        var save = await owner.PutAsJsonAsync("/api/therapists/me/profile", request);
        save.EnsureSuccessStatusCode();
        var profile = (await save.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!;
        var privateEmail = await _factory.WithDbAsync(async db =>
        {
            var row = await db.Therapists.SingleAsync(t => t.Id == profile.Id);
            row.PhoneNumber = "PRIVATE-PHONE";
            row.LicenseNumber = "PRIVATE-LICENSE";
            row.Specialization = "PRIVATE-SPECIALIZATION";
            await db.SaveChangesAsync();
            return await db.Users.Where(u => u.Id == row.UserId).Select(u => u.Email).SingleAsync();
        });
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        (await patient.PutAsync($"{FavoriteTherapistsTests.Url}/{profile.Id}", null)).EnsureSuccessStatusCode();
        await patient.SendRequestOkAsync(profile.Id);
        var anonymous = _factory.CreateClient();

        foreach (var url in new[] { $"/api/therapists/{profile.Id}", $"/api/therapists?search={tag}" })
        {
            var response = await anonymous.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body).RootElement;
            if (json.TryGetProperty("items", out var items)) json = items[0];
            json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                "id", "firstName", "lastName", "city", "practiceName", "bio", "marketplace", "photoUrl");
            json.GetProperty("marketplace").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                "specializations", "experience", "education", "street", "buildingNumber", "postalCode",
                "publicPhoneNumber", "publicEmail", "organizationalInfo", "firstVisitPreparation");
            body.Should().NotContain(privateEmail!).And.NotContain("PRIVATE-");
            json.Deserialize<TherapistCatalogEntryDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!
                .Marketplace.Should().BeEquivalentTo(marketplace);
            (await patient.GetStringAsync(url)).Should().Be(body);
        }

        var dictionary = await anonymous.GetFromJsonAsync<TherapistSpecializationDto[]>("/api/therapists/specializations");
        dictionary!.Should().HaveCount(8).And.Contain(s => s.Code == "sports");
        // An old profile has no automatic contact fallback and does not require a photo.
        var (_, oldId) = await _factory.CreatePublishedTherapistAsync();
        var old = await anonymous.GetFromJsonAsync<TherapistCatalogEntryDto>($"/api/therapists/{oldId}");
        old!.Marketplace!.PublicEmail.Should().BeNull();
        old.Marketplace.PublicPhoneNumber.Should().BeNull();
        old.PhotoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Search_city_and_specialization_should_combine_with_stable_pagination_and_empty_pages()
    {
        var tag = Guid.NewGuid().ToString("N");
        async Task<Guid> Publish(string city, string specialization, string name = "Anna")
        {
            var owner = await _factory.CreateTherapistClientAsync();
            var response = await owner.PutAsJsonAsync("/api/therapists/me/profile",
                TherapistProfileTests.Draft(city, tag, true, firstName: name, lastName: "Nowak") with
                { Marketplace = new TherapistMarketplaceDto([specialization]) });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!.Id;
        }
        var a = await Publish("Kraków", "sports");
        var b = await Publish("Kraków", "sports");
        await Publish("Kraków", "orthopedic");
        await Publish("Warszawa", "sports");
        await Publish("Kraków", "sports", "Beata");
        var anonymous = _factory.CreateClient();
        var url = $"/api/therapists?search={Uri.EscapeDataString($"  ANNA   {tag}  ")}&city={Uri.EscapeDataString("  KRAKÓW ")}&specialization=%20sports%20&pageSize=1";
        var first = (await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(url))!;
        var second = (await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(url + "&pageNumber=2"))!;
        first.TotalCount.Should().Be(2);
        first.TotalPages.Should().Be(2);
        first.HasNext.Should().BeTrue();
        first.Items.Concat(second.Items).Select(t => t.Id).Should().Equal(new[] { a, b }.OrderBy(id => id));
        var empty = (await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(url + "&pageNumber=3"))!;
        empty.Items.Should().BeEmpty();
        empty.TotalCount.Should().Be(2);
        var noMatch = (await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(
            $"/api/therapists?search={tag}&city=Gdynia&specialization=sports"))!;
        noMatch.Items.Should().BeEmpty();
        noMatch.TotalCount.Should().Be(0);
        noMatch.TotalPages.Should().Be(0);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("deleted")]
    [InlineData("inactive")]
    public async Task Unavailable_profiles_should_disappear_from_anonymous_catalog_details_and_photos(string reason)
    {
        var (owner, id) = await _factory.CreatePublishedTherapistAsync();
        await ProfilePhotoTests.UploadAsync(owner);
        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync($"/api/therapists/{id}")).EnsureSuccessStatusCode();
        (await anonymous.GetAsync($"/api/therapists/{id}/photo")).EnsureSuccessStatusCode();
        await _factory.WithDbAsync(async db =>
        {
            var profile = await db.Therapists.SingleAsync(t => t.Id == id);
            if (reason == "hidden") profile.IsPublished = false;
            if (reason == "deleted") db.Therapists.Remove(profile);
            if (reason == "inactive") (await db.Users.SingleAsync(u => u.Id == profile.UserId)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        foreach (var suffix in new[] { "", "/photo" })
        {
            var response = await anonymous.GetAsync($"/api/therapists/{id}{suffix}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
        var page = await anonymous.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>("/api/therapists?pageSize=50");
        page!.Items.Should().NotContain(t => t.Id == id);
    }

    [Theory]
    [InlineData("pageSize=51", "pageSize")]
    [InlineData("pageSize=-1", "pageSize")]
    [InlineData("pageNumber=0", "pageNumber")]
    [InlineData("pageNumber=2147483647", "pageNumber")]
    [InlineData("specialization=SPORTS", "specialization")]
    [InlineData("specialization=unknown", "specialization")]
    public async Task Anonymous_invalid_parameters_should_return_validation_errors(string query, string field)
    {
        var response = await _factory.CreateClient().GetAsync("/api/therapists?" + query);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TherapistProfileTests.ErrorFieldsAsync(response)).Should().Contain(field);
    }

    [Theory]
    [InlineData("search", 101)]
    [InlineData("city", 101)]
    [InlineData("specialization", 51)]
    public async Task Query_lengths_should_be_bounded(string field, int length)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/therapists?{field}={new string('x', length)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TherapistProfileTests.ErrorFieldsAsync(response)).Should().Contain(field);
    }
}

public class PublicCatalogLimitFactory : TestWebApplicationFactory
{
    protected override int PublicTherapistCatalogPermitLimit => 1;
}

public class PublicCatalogRateLimitTests
{
    [Theory]
    [InlineData("/api/therapists")]
    [InlineData("/api/therapists/specializations")]
    [InlineData("/api/therapists/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/therapists/00000000-0000-0000-0000-000000000001/photo")]
    public async Task Public_routes_should_share_one_rate_limit(string url)
    {
        using var factory = new PublicCatalogLimitFactory();
        var client = factory.CreateClient();
        (await client.GetAsync(url)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        var response = await client.GetAsync("/api/therapists/specializations");
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.RetryAfter!.Delta.Should().BePositive();
    }
}
