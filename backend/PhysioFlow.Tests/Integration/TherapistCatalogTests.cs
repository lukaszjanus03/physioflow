using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Commands.UpdateMyTherapistProfile;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class TherapistProfileTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ProfileUrl = "/api/therapists/me/profile";

    private readonly TestWebApplicationFactory _factory;

    public TherapistProfileTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task New_therapist_profile_should_be_unpublished_and_report_missing_fields()
    {
        var therapist = await _factory.CreateTherapistClientAsync("Anna", "Nowak");

        var profile = await therapist.GetFromJsonAsync<TherapistOwnProfileDto>(ProfileUrl);

        profile!.FirstName.Should().Be("Anna");
        profile.LastName.Should().Be("Nowak");
        profile.IsPublished.Should().BeFalse();
        profile.MissingFieldsForPublication.Should().BeEquivalentTo("city", "practiceName");
    }

    [Fact]
    public async Task Saving_data_should_not_publish_profile()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var response = await therapist.PutAsJsonAsync(ProfileUrl, Draft("Kraków", "Gabinet Ruch", isPublished: false));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>();
        profile!.IsPublished.Should().BeFalse();
        profile.City.Should().Be("Kraków");
        profile.MissingFieldsForPublication.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_without_explicit_publication_state_should_return_400()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var response = await therapist.PutAsJsonAsync(ProfileUrl, new { firstName = "Anna", lastName = "Nowak" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().Contain("isPublished");
    }

    [Fact]
    public async Task Publishing_incomplete_profile_should_return_400_and_keep_it_hidden()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var response = await therapist.PutAsJsonAsync(ProfileUrl, Draft(city: null, practiceName: " ", isPublished: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().BeEquivalentTo("city", "practiceName");
        (await therapist.GetFromJsonAsync<TherapistOwnProfileDto>(ProfileUrl))!.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task Publishing_with_placeholder_last_name_should_return_400()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var response = await therapist.PutAsJsonAsync(ProfileUrl,
            Draft("Gdańsk", "Gabinet", isPublished: true) with { LastName = TherapistProfile.PlaceholderLastName });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response)).Should().BeEquivalentTo("lastName");
    }

    [Fact]
    public async Task Too_long_or_missing_fields_should_return_400()
    {
        var therapist = await _factory.CreateTherapistClientAsync();

        var response = await therapist.PutAsJsonAsync(ProfileUrl, new UpdateMyTherapistProfileCommand(
            FirstName: "",
            LastName: new string('N', TherapistProfile.NameMaxLength + 1),
            City: new string('C', TherapistProfile.CityMaxLength + 1),
            PracticeName: new string('P', TherapistProfile.PracticeNameMaxLength + 1),
            Bio: new string('B', TherapistProfile.BioMaxLength + 1),
            IsPublished: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorFieldsAsync(response))
            .Should().BeEquivalentTo("firstName", "lastName", "city", "practiceName", "bio");
    }

    [Fact]
    public async Task Therapist_should_only_change_own_profile()
    {
        var owner = await _factory.CreateTherapistClientAsync("Adam", "Właściciel");
        var other = await _factory.CreateTherapistClientAsync("Beata", "Inna");
        var otherProfile = await other.GetFromJsonAsync<TherapistOwnProfileDto>(ProfileUrl);

        // Identyfikator w body jest ignorowany, a edycja po ID w adresie nie istnieje.
        var response = await owner.PutAsJsonAsync(ProfileUrl, new
        {
            id = otherProfile!.Id,
            userId = otherProfile.Id,
            firstName = "Adam",
            lastName = "Zmieniony",
            city = "Poznań",
            practiceName = "Gabinet Adama",
            isPublished = false,
        });
        var byId = await owner.PutAsJsonAsync($"/api/therapists/{otherProfile.Id}", Draft("X", "Y", isPublished: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!.Id.Should().NotBe(otherProfile.Id);
        ((int)byId.StatusCode).Should().BeOneOf(404, 405);

        var otherAfter = await other.GetFromJsonAsync<TherapistOwnProfileDto>(ProfileUrl);
        otherAfter.Should().BeEquivalentTo(otherProfile);
    }

    [Fact]
    public async Task Profile_endpoints_should_require_therapist_role()
    {
        var anonymous = _factory.CreateClient();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        (await anonymous.GetAsync(ProfileUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await patient.GetAsync(ProfileUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.PutAsJsonAsync(ProfileUrl, Draft("A", "B", isPublished: true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    internal static UpdateMyTherapistProfileCommand Draft(
        string? city, string? practiceName, bool isPublished,
        string firstName = "Anna", string lastName = "Nowak", string? bio = null) =>
        new(firstName, lastName, city, practiceName, bio, isPublished);

    internal static async Task<IReadOnlyCollection<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
    }
}

public class TherapistCatalogTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ProfileUrl = "/api/therapists/me/profile";
    private const string CatalogUrl = "/api/therapists";

    private readonly TestWebApplicationFactory _factory;

    public TherapistCatalogTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Published_profile_should_be_listed_and_available_in_details()
    {
        var tag = UniqueTag();
        var therapist = await _factory.CreateTherapistClientAsync();
        var profile = await PublishAsync(therapist, "Marta", "Zielińska", city: $"Toruń{tag}", practiceName: "Fizjo Plus",
            bio: "Terapia manualna.");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var page = await SearchAsync(patient, tag);
        var details = await patient.GetAsync($"{CatalogUrl}/{profile.Id}");

        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(new TherapistCatalogEntryDto(
            profile.Id, "Marta", "Zielińska", $"Toruń{tag}", "Fizjo Plus", "Terapia manualna.",
            new TherapistMarketplaceDto(Specializations: [])));
        details.StatusCode.Should().Be(HttpStatusCode.OK);
        (await details.Content.ReadFromJsonAsync<TherapistCatalogEntryDto>())
            .Should().BeEquivalentTo(page.Items.Single());
    }

    [Fact]
    public async Task Catalog_entries_should_contain_only_public_professional_data()
    {
        var tag = UniqueTag();
        var therapist = await _factory.CreateTherapistClientAsync();
        var profile = await PublishAsync(therapist, "Iga", "Publiczna", city: $"Opole{tag}", practiceName: "Gabinet Iga");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var listBody = await patient.GetStringAsync($"{CatalogUrl}?search={tag}");
        var detailBody = await patient.GetStringAsync($"{CatalogUrl}/{profile.Id}");

        var expectedFields = new[] { "id", "firstName", "lastName", "city", "practiceName", "bio", "marketplace", "photoUrl", "averageRating", "reviewCount" };
        JsonDocument.Parse(listBody).RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(expectedFields);
        JsonDocument.Parse(detailBody).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(expectedFields);
        listBody.Should().NotContain("@");
        detailBody.Should().NotContain("@");
    }

    [Fact]
    public async Task Inactive_therapist_should_not_be_listed_nor_available_in_details()
    {
        var tag = UniqueTag();
        var therapist = await _factory.CreateTherapistClientAsync();
        var profile = await PublishAsync(therapist, "Ewa", "Nieaktywna", city: $"Radom{tag}", practiceName: "Gabinet Ewa");
        await _factory.WithDbAsync(async db =>
        {
            var userId = await db.Therapists.Where(t => t.Id == profile.Id).Select(t => t.UserId).SingleAsync();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.IsActive = false;
            return await db.SaveChangesAsync();
        });
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        (await SearchAsync(patient, tag)).Items.Should().BeEmpty();
        (await patient.GetAsync($"{CatalogUrl}/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AllPublishedIdsAsync(patient)).Should().NotContain(profile.Id);
    }

    [Fact]
    public async Task Unpublished_profile_should_not_be_listed_nor_available_in_details()
    {
        var tag = UniqueTag();
        var therapist = await _factory.CreateTherapistClientAsync();
        (await therapist.PutAsJsonAsync(ProfileUrl,
            TherapistProfileTests.Draft($"Elbląg{tag}", "Ukryty Gabinet", isPublished: false))).EnsureSuccessStatusCode();
        var draft = await therapist.GetFromJsonAsync<TherapistOwnProfileDto>(ProfileUrl);
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        (await SearchAsync(patient, tag)).Items.Should().BeEmpty();
        (await patient.GetAsync($"{CatalogUrl}/{draft!.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AllPublishedIdsAsync(patient)).Should().NotContain(draft.Id);
    }

    [Fact]
    public async Task Hiding_profile_should_remove_it_from_results_and_details()
    {
        var tag = UniqueTag();
        var therapist = await _factory.CreateTherapistClientAsync();
        var profile = await PublishAsync(therapist, "Ola", "Znikająca", city: $"Kielce{tag}", practiceName: "Gabinet Ola");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        (await SearchAsync(patient, tag)).Items.Should().ContainSingle();

        var hide = await therapist.PutAsJsonAsync(ProfileUrl, TherapistProfileTests.Draft(
            $"Kielce{tag}", "Gabinet Ola", isPublished: false, firstName: "Ola", lastName: "Znikająca"));

        hide.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SearchAsync(patient, tag)).Items.Should().BeEmpty();
        (await patient.GetAsync($"{CatalogUrl}/{profile.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_should_match_first_name_last_name_city_and_practice_name_case_insensitively()
    {
        var tag = UniqueTag();
        var therapist = await _factory.CreateTherapistClientAsync();
        var profile = await PublishAsync(therapist, $"Zofia{tag}", $"Łukaszewska{tag}",
            city: $"Wrocław{tag}", practiceName: $"Centrum{tag} Rehabilitacji");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        foreach (var search in new[] { $"zofia{tag}", $"ŁUKASZEWSKA{tag}", $"wrocław{tag}", $"centrum{tag} rehab" })
        {
            (await SearchAsync(patient, search)).Items.Select(i => i.Id)
                .Should().Equal(new[] { profile.Id }, $"fraza '{search}' powinna znaleźć profil");
        }
    }

    [Fact]
    public async Task Search_with_multiple_terms_should_narrow_results()
    {
        var tag = UniqueTag();
        var inKrakow = await PublishAsync(await _factory.CreateTherapistClientAsync(), "Piotr", "Wolny",
            city: $"Kraków{tag}", practiceName: "Ruch");
        await PublishAsync(await _factory.CreateTherapistClientAsync(), "Piotr", "Szybki",
            city: $"Gdynia{tag}", practiceName: "Ruch");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var page = await SearchAsync(patient, $"piotr kraków{tag}");

        page.Items.Select(i => i.Id).Should().Equal(inKrakow.Id);
    }

    [Fact]
    public async Task Search_without_matches_should_return_empty_page()
    {
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var page = await SearchAsync(patient, UniqueTag());

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Empty_search_should_return_first_page_of_published_profiles()
    {
        await PublishAsync(await _factory.CreateTherapistClientAsync(), "Ela", "Widoczna",
            city: "Lublin", practiceName: "Gabinet Ela");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var page = await patient.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(CatalogUrl);

        page!.PageNumber.Should().Be(1);
        page.PageSize.Should().Be(20);
        page.TotalCount.Should().Be(await PublishedCountInDbAsync());
        page.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pagination_should_split_results_with_stable_sorting_by_last_and_first_name()
    {
        var tag = UniqueTag();
        var city = $"Płock{tag}";
        var c = await PublishAsync(await _factory.CreateTherapistClientAsync(), "Adam", "Cichy", city, "G");
        var a2 = await PublishAsync(await _factory.CreateTherapistClientAsync(), "Zenon", "Adamski", city, "G");
        var a1 = await PublishAsync(await _factory.CreateTherapistClientAsync(), "Bartek", "Adamski", city, "G");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var first = await SearchAsync(patient, city, pageNumber: 1, pageSize: 2);
        var second = await SearchAsync(patient, city, pageNumber: 2, pageSize: 2);

        first.TotalCount.Should().Be(3);
        first.TotalPages.Should().Be(2);
        first.HasNext.Should().BeTrue();
        first.Items.Select(i => i.Id).Should().Equal(a1.Id, a2.Id);
        second.Items.Select(i => i.Id).Should().Equal(c.Id);
        second.HasNext.Should().BeFalse();
    }

    [Theory]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=51", "pageSize")]
    [InlineData("pageNumber=0", "pageNumber")]
    public async Task Invalid_paging_should_return_400(string query, string field)
    {
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var response = await patient.GetAsync($"{CatalogUrl}?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TherapistProfileTests.ErrorFieldsAsync(response)).Should().Contain(field);
    }

    [Fact]
    public async Task Details_of_unknown_profile_should_return_404()
    {
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var response = await patient.GetAsync($"{CatalogUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Catalog_should_allow_anonymous_and_therapist_readers()
    {
        var anonymous = _factory.CreateClient();
        var therapist = await _factory.CreateTherapistClientAsync();

        (await anonymous.GetAsync(CatalogUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync($"{CatalogUrl}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await therapist.GetAsync(CatalogUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string UniqueTag() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<TherapistOwnProfileDto> PublishAsync(HttpClient therapist,
        string firstName, string lastName, string city, string practiceName, string? bio = null)
    {
        var response = await therapist.PutAsJsonAsync(ProfileUrl, TherapistProfileTests.Draft(
            city, practiceName, isPublished: true, firstName: firstName, lastName: lastName, bio: bio));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!;
    }

    private static async Task<PaginatedList<TherapistCatalogEntryDto>> SearchAsync(
        HttpClient patient, string search, int pageNumber = 1, int pageSize = 20)
    {
        var url = $"{CatalogUrl}?search={Uri.EscapeDataString(search)}&pageNumber={pageNumber}&pageSize={pageSize}";
        return (await patient.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(url))!;
    }

    private static async Task<List<Guid>> AllPublishedIdsAsync(HttpClient patient)
    {
        var ids = new List<Guid>();
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = (await patient.GetFromJsonAsync<PaginatedList<TherapistCatalogEntryDto>>(
                $"{CatalogUrl}?pageNumber={pageNumber}&pageSize=50"))!;
            ids.AddRange(page.Items.Select(i => i.Id));
            if (!page.HasNext) return ids;
        }
    }

    private async Task<int> PublishedCountInDbAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Therapists.CountAsync(t => t.IsPublished
            && db.Users.Any(u => u.Id == t.UserId && u.IsActive));
    }
}
