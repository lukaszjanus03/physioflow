using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class FavoriteTherapistsTests : IClassFixture<PatientCalendarFactory>
{
    internal const string Url = "/api/patient-accounts/me/favorite-therapists";
    private readonly PatientCalendarFactory _factory;
    public FavoriteTherapistsTests(PatientCalendarFactory factory) => _factory = factory;

    [Fact]
    public async Task Repeated_add_and_delete_should_be_idempotent_and_isolated_between_accounts()
    {
        var (_, id) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var (other, otherEmail) = await _factory.CreateConfirmedPatientClientAsync();
        var otherId = await _factory.WithDbAsync(db => db.Users.Where(u => u.Email == otherEmail).Select(u => u.Id).SingleAsync());
        // Body/query cannot choose the owner: only the token does.
        var added = await patient.PutAsJsonAsync($"{Url}/{id}?patientUserId={otherId}", new { patientUserId = otherId });
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await patient.PutAsync($"{Url}/{id}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var page = (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!;
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Profile!.Id.Should().Be(id);
        page.Items.Single().IsAvailable.Should().BeTrue();
        (await other.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.Items.Should().BeEmpty();
        (await other.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.TotalCount.Should().Be(1);
        (await other.PutAsync($"{Url}/{id}", null)).EnsureSuccessStatusCode();
        (await patient.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await patient.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await other.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.TotalCount.Should().Be(1);
        (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.Items.Should().BeEmpty();
        var ownerId = await _factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        (await _factory.WithDbAsync(db => db.FavoriteTherapists.CountAsync(f => f.PatientUserId == ownerId))).Should().Be(0);
        (await other.GetAsync($"/api/patient-accounts/{ownerId}/favorite-therapists")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    public async Task Unavailable_bookmark_should_remain_removable_without_disclosing_profile_data(string reason)
    {
        var (_, id) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        (await patient.PutAsync($"{Url}/{id}", null)).EnsureSuccessStatusCode();
        await _factory.WithDbAsync(async db =>
        {
            var profile = await db.Therapists.SingleAsync(t => t.Id == id);
            if (reason == "hidden") profile.IsPublished = false;
            if (reason == "inactive") (await db.Users.SingleAsync(u => u.Id == profile.UserId)).IsActive = false;
            if (reason == "deleted") db.Therapists.Remove(profile);
            return await db.SaveChangesAsync();
        });
        var response = await patient.GetAsync(Url);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items")[0];
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("therapistId", "createdAt", "isAvailable", "profile");
        body.GetProperty("profile").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("isAvailable").GetBoolean().Should().BeFalse();
        body.GetProperty("therapistId").GetGuid().Should().Be(id);
        (await patient.PutAsync($"{Url}/{id}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await patient.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url))!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_profile_and_identity_user_id_should_not_be_addable()
    {
        var (_, id) = await _factory.CreatePublishedTherapistAsync();
        var userId = await _factory.WithDbAsync(db => db.Therapists.Where(t => t.Id == id).Select(t => t.UserId).SingleAsync());
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        foreach (var invalid in new[] { Guid.NewGuid(), userId, Guid.Empty })
            (await patient.PutAsync($"{Url}/{invalid}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Favorite_routes_should_require_patient_role()
    {
        var anonymous = _factory.CreateClient();
        var therapist = await _factory.CreateTherapistClientAsync();
        var id = Guid.NewGuid();
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (therapist, HttpStatusCode.Forbidden) })
        {
            (await client.GetAsync(Url)).StatusCode.Should().Be(status);
            (await client.PutAsync($"{Url}/{id}", null)).StatusCode.Should().Be(status);
            (await client.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(status);
        }
    }

    [Fact]
    public async Task Favorites_should_paginate_with_id_tiebreaker_and_validate_page_boundaries()
    {
        var (_, a) = await _factory.CreatePublishedTherapistAsync();
        var (_, b) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        (await patient.PutAsync($"{Url}/{a}", null)).EnsureSuccessStatusCode();
        (await patient.PutAsync($"{Url}/{b}", null)).EnsureSuccessStatusCode();
        await _factory.WithDbAsync(async db =>
        {
            var id = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            foreach (var f in await db.FavoriteTherapists.Where(f => f.PatientUserId == id).ToListAsync())
                f.CreatedAt = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
            return await db.SaveChangesAsync();
        });
        var first = (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url + "?pageSize=1"))!;
        var second = (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url + "?pageSize=1&pageNumber=2"))!;
        first.TotalCount.Should().Be(2);
        first.HasNext.Should().BeTrue();
        second.HasNext.Should().BeFalse();
        first.Items.Concat(second.Items).Select(f => f.TherapistId).Should().Equal(new[] { a, b }.OrderBy(id => id));
        (await patient.GetFromJsonAsync<PaginatedList<FavoriteTherapistDto>>(Url + "?pageNumber=3"))!.Items.Should().BeEmpty();
        foreach (var query in new[] { "pageSize=0", "pageSize=51", "pageNumber=0", "pageNumber=2147483647" })
            (await patient.GetAsync(Url + "?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bookmark_should_not_grant_booking_cooperation_or_documentation_access()
    {
        var (_, id) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        (await patient.PutAsync($"{Url}/{id}", null)).EnsureSuccessStatusCode();
        var response = await patient.GetAsync($"{PatientCalendarSteps.Url}/therapists/{id}/settings");
        (await response.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationNotActive);
        var userId = await _factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        (await _factory.WithDbAsync(db => db.Patients.AnyAsync(p => p.UserId == userId))).Should().BeFalse();
        (await _factory.WithDbAsync(db => db.CooperationRequests.AnyAsync(r => r.PatientUserId == userId))).Should().BeFalse();
    }

    [Fact]
    public async Task Removing_bookmark_should_preserve_active_cooperation_and_appointments()
    {
        var scenario = await _factory.CalendarScenarioAsync();
        var appointment = await scenario.BookOkAsync();
        (await scenario.Patient.PutAsync($"{Url}/{scenario.TherapistId}", null)).EnsureSuccessStatusCode();
        (await scenario.Patient.DeleteAsync($"{Url}/{scenario.TherapistId}")).EnsureSuccessStatusCode();
        var stored = await _factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == appointment.Id));
        stored.Status.Should().Be(appointment.Status);
        (await scenario.Patient.GetAsync($"{PatientCalendarSteps.Url}/therapists/{scenario.TherapistId}/settings"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
