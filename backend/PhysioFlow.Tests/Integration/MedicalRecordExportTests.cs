using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class MedicalRecordExportTestSteps
{
    public static string ExportUrl(this DocumentationScenario s) => s.ListUrl + "/export";

    public static async Task<PaginatedList<MedicalRecordExportItemDto>> ExportAsync(this DocumentationScenario s, string query = "") =>
        (await s.Therapist.GetFromJsonAsync<PaginatedList<MedicalRecordExportItemDto>>(s.ExportUrl() + query, CooperationTestSteps.Json))!;

    // Also seed legacy records without appointments and divergent editable content to prove that
    // exports select immutable snapshots. Approval through HTTP is covered separately below.
    public static Task<List<Guid>> SeedAsync(TestWebApplicationFactory factory, DocumentationScenario s,
        IReadOnlyList<DateTime?> starts, int versions = 1) => factory.WithDbAsync(async db =>
    {
        var author = await db.Therapists.Where(t => t.Id == s.TherapistId).Select(t => t.UserId).SingleAsync();
        var ids = new List<Guid>();
        foreach (var start in starts)
        {
            Appointment? visit = start.HasValue ? new Appointment
            {
                PatientId = s.PatientId, TherapistId = s.TherapistId, StartsAt = start.Value,
                EndsAt = start.Value.AddMinutes(30), Duration = AppointmentDuration.Thirty, Status = AppointmentStatus.Completed,
                Notes = "LEGACY_VISIT_NOTE", PatientMessage = "BOOKING_MESSAGE"
            } : null;
            if (visit != null) db.Appointments.Add(visit);
            var record = new MedicalRecord
            {
                PatientId = s.PatientId, TherapistId = s.TherapistId, AppointmentId = visit?.Id,
                Status = MedicalRecordStatus.Approved, CurrentVersion = versions,
                Interview = "UNAPPROVED_PROJECTION", PatientSummary = "UNAPPROVED_SUMMARY",
                InterviewForm = "{\"secret\":\"LEGACY_FORM\"}", PostVisitNoteHtml = "<p>LEGACY_HTML</p>"
            };
            db.MedicalRecords.Add(record);
            for (var number = 1; number <= versions; number++)
                db.MedicalRecordVersions.Add(new MedicalRecordVersion
                {
                    MedicalRecordId = record.Id, VersionNumber = number, AuthorUserId = author,
                    RecordedAt = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc).AddMinutes(number),
                    CorrectionReason = number == 1 ? null : "PRIVATE_CORRECTION_REASON",
                    Interview = $"APPROVED_INTERVIEW_{number}", FunctionalAssessment = "APPROVED_ASSESSMENT",
                    PerformedActions = "APPROVED_ACTIONS", Recommendations = "APPROVED_RECOMMENDATIONS", PatientSummary = null
                });
            ids.Add(record.Id);
        }
        await db.SaveChangesAsync();
        return ids;
    });

    public static async Task AssertCompletePaginationAsync(TestWebApplicationFactory factory)
    {
        var s = await factory.DocumentationScenarioAsync();
        // More than the maximum page size; equal visit times exercise the ID tie breaker.
        var starts = Enumerable.Range(0, 105).Select(i => (DateTime?)new DateTime(2026, 10, 20 + i % 3, 10, 0, 0, DateTimeKind.Utc)).ToArray();
        var ids = await SeedAsync(factory, s, starts, versions: 2);
        await s.CreateRecordAsync(); // A complete but unapproved draft must not affect totals or pages.
        var collected = new List<MedicalRecordExportItemDto>();
        for (var number = 1; ; number++)
        {
            var page = await s.ExportAsync($"?from=2026-10-20&to=2026-10-22&pageSize=100&pageNumber={number}");
            page.TotalCount.Should().Be(105);
            page.TotalPages.Should().Be(2);
            page.PageNumber.Should().Be(number);
            page.PageSize.Should().Be(100);
            page.HasPrevious.Should().Be(number > 1);
            collected.AddRange(page.Items);
            if (!page.HasNext) break;
            number.Should().BeLessThan(3);
        }
        var expected = ids.Select((id, i) => new { Id = id, Start = starts[i] }).OrderBy(x => x.Start).ThenBy(x => x.Id).Select(x => x.Id);
        collected.Select(x => x.Id).Should().Equal(expected).And.OnlyHaveUniqueItems();
        collected.Should().OnlyContain(x => x.VersionNumber == 2 && x.Content.Interview == "APPROVED_INTERVIEW_2");
        (await s.ExportAsync("?pageSize=100&pageNumber=3")).Items.Should().BeEmpty();
    }

    public static async Task AssertWarsawDateBoundariesAsync(TestWebApplicationFactory factory)
    {
        var s = await factory.DocumentationScenarioAsync();
        // DST ends on 25 October: this local day lasts 25 hours, starts the previous UTC date.
        DateTime start = new(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc);
        DateTime end = new(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc);
        var ids = await SeedAsync(factory, s, [start.AddTicks(-10), start, end.AddTicks(-10), end, null]);
        var unfiltered = await s.ExportAsync();
        unfiltered.Items.Select(x => x.Id).Should().Equal(ids);
        unfiltered.Items[^1].AppointmentStartsAt.Should().BeNull();
        var page = await s.ExportAsync("?from=2026-10-25&to=2026-10-25");
        page.Items.Select(x => x.Id).Should().Equal(ids[1], ids[2]);
        page.TotalCount.Should().Be(2);
        (await s.ExportAsync("?from=2026-10-25")).Items.Select(x => x.Id).Should().Equal(ids[1], ids[2], ids[3]);
        (await s.ExportAsync("?to=2026-10-25")).Items.Select(x => x.Id).Should().Equal(ids[0], ids[1], ids[2]);
        (await s.ExportAsync("?from=2026-10-26&to=2026-10-26")).Items.Should().ContainSingle(x => x.Id == ids[3]);
        (await s.ExportAsync("?from=2026-10-27")).Items.Should().BeEmpty();
    }
}

public class MedicalRecordExportTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Export_should_return_the_latest_approved_correction_and_its_approval_time()
    {
        var s = await factory.DocumentationScenarioAsync();
        var approved = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        var response = await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(approved.Id) + "/corrections",
            DocumentationTestSteps.Correction(approved));
        response.EnsureSuccessStatusCode();
        var corrected = await s.Therapist.ReadRecordAsync(approved.Id);
        (await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(approved.Id) + "/corrections",
            DocumentationTestSteps.Correction(corrected) with { Interview = "UNAPPROVED_TEXT", Recommendations = null }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var export = await s.ExportAsync();
        var item = export.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(approved.Id);
        item.AppointmentId.Should().Be(s.AppointmentId);
        item.AppointmentStartsAt.Should().Be(await factory.WithDbAsync(db => db.Appointments.Where(a => a.Id == s.AppointmentId).Select(a => a.StartsAt).SingleAsync()));
        item.VersionNumber.Should().Be(2);
        item.ApprovedAt.Should().Be((await s.Therapist.HistoryAsync(approved.Id))[1].RecordedAt);
        item.Content.Should().BeEquivalentTo(corrected.Content);
        var http = await s.Therapist.GetAsync(s.ExportUrl());
        http.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await http.Content.ReadAsStringAsync()).Should().NotContain("UNAPPROVED_TEXT").And.NotContain("LEGACY_INTERNAL_NOTE")
            .And.NotContain("BOOKING_MESSAGE").And.NotContain("correctionReason").And.NotContain("authorUserId");
    }

    [Fact]
    public Task Export_should_traverse_all_pages_in_ascending_visit_and_id_order() =>
        MedicalRecordExportTestSteps.AssertCompletePaginationAsync(factory);

    [Fact]
    public Task Dates_should_filter_visit_starts_inclusive_in_Warsaw_independently_of_edit_and_approval_dates() =>
        MedicalRecordExportTestSteps.AssertWarsawDateBoundariesAsync(factory);

    [Fact]
    public async Task Spring_DST_day_should_use_the_correct_end_boundary()
    {
        var s = await factory.DocumentationScenarioAsync();
        DateTime start = new(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);
        DateTime end = new(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc);
        var ids = await MedicalRecordExportTestSteps.SeedAsync(factory, s, [start.AddTicks(-10), start, end.AddTicks(-10), end]);
        (await s.ExportAsync("?from=2026-03-29&to=2026-03-29")).Items.Select(x => x.Id).Should().Equal(ids[1], ids[2]);
    }

    [Fact]
    public async Task Legacy_entries_without_visits_should_be_last_and_only_in_unfiltered_exports()
    {
        var s = await factory.DocumentationScenarioAsync();
        var ids = await MedicalRecordExportTestSteps.SeedAsync(factory, s,
            [null, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc)], versions: 2);
        var page = await s.ExportAsync();
        page.Items.Select(x => x.Id).Should().Equal(ids[1], ids[0]);
        page.Items[1].AppointmentId.Should().BeNull();
        page.Items[1].AppointmentStartsAt.Should().BeNull();
        page.Items[1].ApprovedAt.Should().Be(new DateTime(2026, 11, 18, 10, 2, 0, DateTimeKind.Utc));
        var json = await (await s.Therapist.GetAsync(s.ExportUrl())).Content.ReadAsStringAsync();
        json.Should().NotContain("UNAPPROVED_").And.NotContain("LEGACY_").And.NotContain("PRIVATE_CORRECTION_REASON");
        (await s.ExportAsync("?to=9999-12-31")).Items.Should().ContainSingle(x => x.Id == ids[1]);
        (await s.ExportAsync("?from=9999-12-31")).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_cards_drafts_and_nonmatching_ranges_should_return_a_valid_empty_page()
    {
        var s = await factory.DocumentationScenarioAsync();
        foreach (var query in new[] { "", "?from=2020-01-01&to=2020-01-31" })
        {
            var page = await s.ExportAsync(query);
            page.Items.Should().BeEmpty();
            page.TotalCount.Should().Be(0);
            page.TotalPages.Should().Be(0);
            page.HasNext.Should().BeFalse();
        }
        await s.CreateRecordAsync();
        (await s.ExportAsync()).Items.Should().BeEmpty();
        await MedicalRecordExportTestSteps.SeedAsync(factory, s, [new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc)]);
        (await s.ExportAsync("?from=2020-01-01&to=2020-01-31")).Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("from=2026-11-19&to=2026-11-18", "to")]
    [InlineData("from=not-a-date", "from")]
    [InlineData("to=2026-02-30", "to")]
    [InlineData("from=0001-01-01", "from")]
    [InlineData("to=0001-01-01", "to")]
    [InlineData("pageNumber=0", "pageNumber")]
    [InlineData("pageNumber=-1", "pageNumber")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=100&pageNumber=2147483647", "pageNumber")]
    public async Task Invalid_ranges_and_pagination_should_return_validation_errors(string query, string field)
    {
        var s = await factory.DocumentationScenarioAsync();
        var response = await s.Therapist.GetAsync(s.ExportUrl() + "?" + query);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
        body.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Export_should_isolate_cards_and_preserve_owner_access_to_archived_cards()
    {
        var s = await factory.DocumentationScenarioAsync();
        var ownOtherCard = await s.Therapist.CreateCardAsync();
        var otherOwnRecordIds = await MedicalRecordExportTestSteps.SeedAsync(factory, s with { PatientId = ownOtherCard },
            [new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc)]);
        var other = await factory.DocumentationScenarioAsync();
        var record = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        await other.ApproveRecordAsync(await other.CreateRecordAsync());
        (await s.ExportAsync()).Items.Should().ContainSingle(x => x.Id == record.Id);
        var otherCardResponse = await s.Therapist.GetAsync($"/api/patients/{ownOtherCard}/medical-records/export");
        (await otherCardResponse.Content.ReadFromJsonAsync<PaginatedList<MedicalRecordExportItemDto>>(CooperationTestSteps.Json))!
            .Items.Should().ContainSingle(x => x.Id == otherOwnRecordIds[0]);
        (await s.Therapist.DeleteAsync($"/api/patients/{s.PatientId}")).EnsureSuccessStatusCode();
        (await s.ExportAsync()).Items.Should().ContainSingle(x => x.Id == record.Id);
        (await other.Therapist.GetAsync(s.ExportUrl())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.GetAsync(other.ExportUrl())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.GetAsync($"/api/patients/{Guid.NewGuid()}/medical-records/export")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_patient_and_admin_should_not_export_even_if_documentation_is_shared()
    {
        var s = await factory.DocumentationScenarioAsync();
        var (patient, email) = await factory.CreateConfirmedPatientClientAsync();
        await factory.WithDbAsync(async db =>
        {
            var card = await db.Patients.SingleAsync(p => p.Id == s.PatientId);
            card.UserId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            card.DocumentationSharedWithAccount = true;
            return await db.SaveChangesAsync();
        });
        await s.ApproveRecordAsync(await s.CreateRecordAsync());
        (await factory.CreateClient().GetAsync(s.ExportUrl())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await patient.GetAsync(s.ExportUrl())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await factory.CreateModeratorAsync()).GetAsync(s.ExportUrl())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleted_records_and_cards_should_not_be_exported()
    {
        var s = await factory.DocumentationScenarioAsync();
        var record = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        await factory.WithDbAsync(async db =>
        {
            (await db.MedicalRecords.SingleAsync(r => r.Id == record.Id)).IsDeleted = true;
            return await db.SaveChangesAsync();
        });
        (await s.ExportAsync()).Items.Should().BeEmpty();
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == s.PatientId)).IsDeleted = true;
            return await db.SaveChangesAsync();
        });
        (await s.Therapist.GetAsync(s.ExportUrl())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

public class PostgresMedicalRecordExportTests
{
    [PostgresFact]
    public async Task Export_query_should_translate_and_traverse_every_page_without_duplicates()
    {
        using var factory = new PostgresDocumentationFactory();
        await MedicalRecordExportTestSteps.AssertCompletePaginationAsync(factory);
    }

    [PostgresFact]
    public async Task Date_filter_should_translate_with_inclusive_Warsaw_boundaries_and_nullable_appointments()
    {
        using var factory = new PostgresDocumentationFactory();
        await MedicalRecordExportTestSteps.AssertWarsawDateBoundariesAsync(factory);
    }
}
