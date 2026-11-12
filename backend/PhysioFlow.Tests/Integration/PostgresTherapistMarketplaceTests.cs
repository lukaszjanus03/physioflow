using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PostgresProfileFactory : PostgresCalendarFactory
{
    public FakePhotoStorage Storage { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IStorageService>();
        services.AddSingleton<IStorageService>(Storage);
    }
}

public class PostgresTherapistMarketplaceTests
{
    private const string Url = "/api/therapists/me/profile";

    [PostgresFact]
    public async Task Migration_should_preserve_existing_published_and_draft_profiles_and_legacy_data()
    {
        using var factory = new PostgresProfileFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var draft = await factory.CreateTherapistClientAsync("Piotr", "Kowalski");
        var request = TherapistProfileTests.Draft("Kraków", "Istniejący gabinet", true, bio: "Dotychczasowy opis.");
        (await owner.PutAsJsonAsync(Url, request)).EnsureSuccessStatusCode();
        var before = (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!;
        var draftBefore = (await draft.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!;

        await factory.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261111122338_AddAppointmentNotifications");
            // These writes run against the old schema, where the marketplace columns do not exist.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE therapists SET specialization = {"Dowolny stary opis"}, phone_number = {"123456789"}, license_number = {"PWZF-123"} WHERE id = {before.Id}");
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
            return 0;
        });

        (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url)).Should().BeEquivalentTo(before);
        (await draft.GetFromJsonAsync<TherapistOwnProfileDto>(Url)).Should().BeEquivalentTo(draftBefore);
        var stored = await factory.WithDbAsync(db => db.Therapists.AsNoTracking().SingleAsync(t => t.Id == before.Id));
        stored.Specialization.Should().Be("Dowolny stary opis");
        stored.PhoneNumber.Should().Be("123456789");
        stored.LicenseNumber.Should().Be("PWZF-123");
        stored.Specializations.Should().BeEmpty();
        stored.PublicPhoneNumber.Should().BeNull();
        stored.PublicEmail.Should().BeNull();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        (await patient.GetAsync($"/api/therapists/{before.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await patient.GetAsync($"/api/therapists/{draftBefore.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PostgresFact]
    public async Task Marketplace_and_photo_should_round_trip_through_postgres_and_catalog_projection()
    {
        using var factory = new PostgresProfileFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        var marketplace = new TherapistMarketplaceDto(["sports", "neurological"], "15 lat", "AWF", "Długa", "12A", "30-001",
            "+48 123 456 789", "public@example.pl", "Winda", "Dokumentacja");
        var request = TherapistProfileTests.Draft("Kraków", Guid.NewGuid().ToString("N"), true) with { Marketplace = marketplace };
        var response = await owner.PutAsJsonAsync(Url, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var photo = await ProfilePhotoTests.UploadAsync(owner);
        photo.Marketplace.Should().BeEquivalentTo(marketplace);
        var details = await patient.GetFromJsonAsync<TherapistCatalogEntryDto>($"/api/therapists/{photo.Id}");
        details!.Marketplace.Should().BeEquivalentTo(marketplace);
        details.PhotoUrl.Should().Be(photo.PhotoUrl);
        var page = await patient.GetFromJsonAsync<PhysioFlow.Application.Common.Models.PaginatedList<TherapistCatalogEntryDto>>(
            $"/api/therapists?search={request.PracticeName}");
        page!.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(details);
        (await owner.DeleteAsync(Url + "/photo")).EnsureSuccessStatusCode();
        (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!.PhotoUrl.Should().BeNull();
    }

    [PostgresFact]
    public async Task Concurrent_photo_changes_should_reject_stale_pointer_updates()
    {
        using var factory = new PostgresProfileFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var profile = (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(Url))!;
        using var scope1 = factory.Services.CreateScope();
        using var scope2 = factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var p1 = await db1.Therapists.SingleAsync(p => p.Id == profile.Id);
        var p2 = await db2.Therapists.SingleAsync(p => p.Id == profile.Id);
        p1.PhotoStorageKey = "winning-photo/profile.webp";
        p2.PhotoStorageKey = "stale-photo/profile.webp";
        await db1.SaveChangesAsync();
        var stale = () => db2.SaveChangesAsync();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await factory.WithDbAsync(db => db.Therapists.AsNoTracking().SingleAsync(p => p.Id == profile.Id)))
            .PhotoStorageKey.Should().Be(p1.PhotoStorageKey);
    }
}
