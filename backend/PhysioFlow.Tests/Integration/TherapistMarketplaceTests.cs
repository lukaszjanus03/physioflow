using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class TherapistMarketplaceTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Url = "/api/therapists/me/profile";
    private readonly TestWebApplicationFactory _factory;
    public TherapistMarketplaceTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Marketplace_should_round_trip_without_touching_another_therapist_or_login_contact()
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var other = await _factory.CreateTherapistClientAsync();
        var before = await other.GetFromJsonAsync<TherapistOwnProfileDto>(Url);
        var data = new TherapistMarketplaceDto(["sports", "manual-therapy"], "  12 lat praktyki  ",
            "AWF Kraków", "Długa", "12/3", "30-001", "+48 123 456 789", "gabinet@example.pl",
            "Parking od podwórza. Wejście bez schodów.", "Zabierz wyniki badań.");
        var request = TherapistProfileTests.Draft("Kraków", "Gabinet", false) with { Marketplace = data };
        var response = await owner.PutAsJsonAsync(Url, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!;
        profile.Marketplace.Should().BeEquivalentTo(data with { Experience = "12 lat praktyki" });
        (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url)).Should().BeEquivalentTo(profile);
        (await other.GetFromJsonAsync<TherapistOwnProfileDto>(Url)).Should().BeEquivalentTo(before);
        var loginEmail = await _factory.WithDbAsync(async db =>
            await db.Users.Where(u => db.Therapists.Any(t => t.Id == profile.Id && t.UserId == u.Id))
                .Select(u => u.Email).SingleAsync());
        loginEmail.Should().NotBe("gabinet@example.pl");
    }

    [Fact]
    public async Task Old_client_saves_should_preserve_new_fields_and_legacy_data()
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var profile = (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!;
        await _factory.WithDbAsync(async db =>
        {
            var p = await db.Therapists.SingleAsync(t => t.Id == profile.Id);
            p.Specialization = "Stary opis specjalizacji";
            p.PhoneNumber = "111222333";
            p.LicenseNumber = "PWZF-123";
            return await db.SaveChangesAsync();
        });
        var draft = TherapistProfileTests.Draft(null, null, false);
        var marketplace = new TherapistMarketplaceDto(["orthopedic"], Education: "AWF", PublicEmail: "public@example.pl");
        (await owner.PutAsJsonAsync(Url, draft with { Marketplace = marketplace })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync(Url, draft)).EnsureSuccessStatusCode();
        var after = (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!;
        after.Marketplace.Should().BeEquivalentTo(marketplace);
        var stored = await _factory.WithDbAsync(db => db.Therapists.AsNoTracking().SingleAsync(t => t.Id == profile.Id));
        stored.Specialization.Should().Be("Stary opis specjalizacji");
        stored.PhoneNumber.Should().Be("111222333");
        stored.LicenseNumber.Should().Be("PWZF-123");
    }

    [Fact]
    public async Task Optional_fields_should_allow_drafts_clearing_and_existing_publication_rules()
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var draft = TherapistProfileTests.Draft(null, null, false) with
        { Marketplace = new TherapistMarketplaceDto(["sports"], PublicEmail: "public@example.pl") };
        (await owner.PutAsJsonAsync(Url, draft)).EnsureSuccessStatusCode();
        var blank = new TherapistMarketplaceDto([], "  ", "", " ", "", " ", " ", "", " ", "");
        var cleared = await owner.PutAsJsonAsync(Url, draft with { Marketplace = blank });
        cleared.EnsureSuccessStatusCode();
        var profile = (await cleared.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!;
        profile.Marketplace.Should().BeEquivalentTo(new TherapistMarketplaceDto([]));
        profile.MissingFieldsForPublication.Should().BeEquivalentTo("city", "practiceName");

        var published = await owner.PutAsJsonAsync(Url, TherapistProfileTests.Draft("Kraków", "Gabinet", true));
        published.EnsureSuccessStatusCode();
        var body = await published.Content.ReadAsStringAsync();
        body.Should().NotContain("verified", because: "entering education is not qualification verification");
        (await published.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!.MissingFieldsForPublication.Should().BeEmpty();
    }

    [Theory]
    [InlineData("specializations", "unknown")]
    [InlineData("specializations", "SPORTS")]
    [InlineData("publicEmail", "nie-email")]
    [InlineData("publicEmail", "Osoba <public@example.pl>")]
    [InlineData("publicPhoneNumber", "abc123456789")]
    [InlineData("publicPhoneNumber", "123")]
    [InlineData("postalCode", "30001")]
    public async Task Invalid_marketplace_values_should_report_field_and_preserve_profile(string field, string value)
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var before = await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url);
        var response = await owner.PutAsJsonAsync(Url, new
        {
            firstName = "Anna", lastName = "Nowak", isPublished = false,
            marketplace = new Dictionary<string, object?> { [field] = field == "specializations" ? new[] { value } : value }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TherapistProfileTests.ErrorFieldsAsync(response)).Should().Contain("marketplace." + field);
        (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Dictionary_should_be_stable_and_reject_duplicates_and_null_codes()
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var codes = (await owner.GetFromJsonAsync<TherapistSpecializationDto[]>("/api/therapists/specializations"))!;
        codes.Select(c => c.Code).Should().BeEquivalentTo(TherapistSpecializations.All.Keys);
        codes.Select(c => c.Label).Should().OnlyContain(label => !string.IsNullOrWhiteSpace(label));
        foreach (var input in new string?[][] { ["sports", "sports"], [null], Enumerable.Repeat("sports", 9).ToArray() })
        {
            var response = await owner.PutAsJsonAsync(Url, TherapistProfileTests.Draft(null, null, false) with
            { Marketplace = new TherapistMarketplaceDto(input!) });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Theory]
    [InlineData("experience", 2000)]
    [InlineData("education", 2000)]
    [InlineData("street", 150)]
    [InlineData("buildingNumber", 20)]
    [InlineData("publicEmail", 254)]
    [InlineData("publicPhoneNumber", 30)]
    [InlineData("organizationalInfo", 2000)]
    [InlineData("firstVisitPreparation", 2000)]
    public async Task Text_limits_should_be_enforced(string field, int max)
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var response = await owner.PutAsJsonAsync(Url, new
        {
            firstName = "Anna", lastName = "Nowak", isPublished = false,
            marketplace = new Dictionary<string, object> { [field] = new string('x', max + 1) }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TherapistProfileTests.ErrorFieldsAsync(response)).Should().Contain("marketplace." + field);
    }

    [Fact]
    public async Task Catalog_should_expose_only_explicit_public_contact_and_hide_unpublished_extensions()
    {
        var owner = await _factory.CreateTherapistClientAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var profile = (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!;
        profile.Marketplace!.PublicEmail.Should().BeNull();
        profile.Marketplace.PublicPhoneNumber.Should().BeNull();
        var tag = Guid.NewGuid().ToString("N");
        var m = new TherapistMarketplaceDto(["pediatric", "respiratory"], PublicEmail: "public@example.pl", Street: "Długa");
        var command = TherapistProfileTests.Draft("Kraków", tag, true) with { Marketplace = m };
        (await owner.PutAsJsonAsync(Url, command)).EnsureSuccessStatusCode();
        var details = (await patient.GetFromJsonAsync<TherapistCatalogEntryDto>($"/api/therapists/{profile.Id}"))!;
        details.Marketplace.Should().BeEquivalentTo(m);
        var page = await patient.GetFromJsonAsync<PhysioFlow.Application.Common.Models.PaginatedList<TherapistCatalogEntryDto>>(
            $"/api/therapists?search={tag}");
        page!.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(details);
        var json = JsonDocument.Parse(await patient.GetStringAsync($"/api/therapists/{profile.Id}")).RootElement;
        json.TryGetProperty("userId", out _).Should().BeFalse();
        json.TryGetProperty("licenseNumber", out _).Should().BeFalse();
        (await owner.PutAsJsonAsync(Url, command with { IsPublished = false })).EnsureSuccessStatusCode();
        (await patient.GetAsync($"/api/therapists/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
