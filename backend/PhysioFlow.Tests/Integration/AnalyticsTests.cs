using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public static class AnalyticsTestSteps
{
    public const string Dashboard = "/api/analytics/dashboard";
    public static string Pain(Guid patientId) => $"/api/analytics/patients/{patientId}/pain-trend";
    public static string Range(DateOnly from, DateOnly to) => $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
    public static async Task<DashboardStatsDto> Stats(this HttpClient therapist, string query = "") =>
        await (await therapist.GetAsync(Dashboard + query)).Body<DashboardStatsDto>();
    public static async Task<PatientPainTrendDto> Pain(this SharingScenario s, string query = "") =>
        await (await s.Therapist.GetAsync(Pain(s.CardId) + query)).Body<PatientPainTrendDto>();

    public static Appointment Visit(Guid therapistId, Guid cardId, DateTime start,
        AppointmentStatus status = AppointmentStatus.Scheduled, bool deleted = false) => new()
    {
        TherapistId = therapistId, PatientId = cardId, StartsAt = start, EndsAt = start.AddMinutes(30),
        Duration = AppointmentDuration.Thirty, Status = status, IsDeleted = deleted,
        Reason = "PRIVATE_REASON", Notes = "PRIVATE_ADMIN_NOTES", PatientMessage = "PRIVATE_MESSAGE"
    };

    public static async Task<List<ExerciseJournalEntryDto>> Reports(this SharingScenario s,
        TestWebApplicationFactory factory, params (DateOnly Date, int? Pain)[] values)
    {
        var plan = await s.Publish(await s.ActivePlan());
        var entries = new List<ExerciseJournalEntryDto>();
        foreach (var _ in values) entries.Add(await s.Submit(plan));
        await factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = await db.ExerciseJournalEntries.SingleAsync(e => e.Id == entries[i].Id);
                entry.PerformedOn = values[i].Date;
                entry.PainLevel = values[i].Pain;
                entry.Comment = "PRIVATE_JOURNAL_COMMENT";
                // Group by exercise performance date, never submission timestamp.
                entry.CreatedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            }
            return await db.SaveChangesAsync();
        });
        return entries;
    }
}

public class AnalyticsTests(PatientCalendarFactory factory) : IClassFixture<PatientCalendarFactory>
{
    [Fact]
    public async Task Dashboard_isolates_therapists_counts_current_cards_and_all_visit_statuses_by_start()
    {
        var s = await factory.DocumentationScenarioAsync();
        var other = await factory.DocumentationScenarioAsync();
        var active = await s.Therapist.CreateCardAsync(); // Counts without a portal account or cooperation.
        var archived = await s.Therapist.CreateCardAsync();
        var deleted = await s.Therapist.CreateCardAsync();
        var date = new DateOnly(2026, 11, 19);
        var (start, end) = WarsawTime.DayRangeUtc(date);
        await factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == archived)).IsArchived = true;
            (await db.Patients.SingleAsync(p => p.Id == deleted)).IsDeleted = true;
            var original = await db.Appointments.SingleAsync(a => a.Id == s.AppointmentId);
            original.StartsAt = start.AddHours(1);
            original.EndsAt = start.AddHours(2);
            // An archived card's historical visits still belong in the appointment statistics.
            db.Appointments.AddRange(
                AnalyticsTestSteps.Visit(s.TherapistId, active, start),
                AnalyticsTestSteps.Visit(s.TherapistId, archived, start.AddHours(3), AppointmentStatus.Cancelled),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, start.AddHours(4), AppointmentStatus.NoShow),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, start.AddHours(5), deleted: true),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, start.AddMinutes(-30), AppointmentStatus.Completed),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, end),
                AnalyticsTestSteps.Visit(other.TherapistId, other.PatientId, start));
            return await db.SaveChangesAsync();
        });

        var query = AnalyticsTestSteps.Range(date, date);
        var response = await s.Therapist.GetAsync(AnalyticsTestSteps.Dashboard + query);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = await response.Body<DashboardStatsDto>();
        result.Should().BeEquivalentTo(new DashboardStatsDto(date, date, "Europe/Warsaw", 2, new(1, 1, 1, 1)));
        result.Appointments.Total.Should().Be(4);
        (await other.Therapist.Stats(query)).Appointments.Should().BeEquivalentTo(new AppointmentStatsDto(1, 0, 0, 0));
        var nextDay = await s.Therapist.Stats(AnalyticsTestSteps.Range(date.AddDays(1), date.AddDays(1)));
        nextDay.ActivePatientCount.Should().Be(2);
        nextDay.Appointments.Should().BeEquivalentTo(new AppointmentStatsDto(1, 0, 0, 0));
        var empty = await s.Therapist.Stats(AnalyticsTestSteps.Range(date.AddDays(2), date.AddDays(2)));
        empty.ActivePatientCount.Should().Be(2); // Current, not historical and not restricted to the range.
        empty.Appointments.Should().BeEquivalentTo(new AppointmentStatsDto(0, 0, 0, 0));
        (await s.Therapist.DeleteAsync($"/api/patients/{active}")).EnsureSuccessStatusCode();
        (await s.Therapist.Stats(query)).ActivePatientCount.Should().Be(1);
        var newcomer = await factory.CreateTherapistClientAsync();
        var noData = await newcomer.Stats(query);
        noData.ActivePatientCount.Should().Be(0);
        noData.Appointments.Total.Should().Be(0);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo("from", "to", "timeZone", "activePatientCount", "appointments");
        json.RootElement.GetProperty("appointments").EnumerateObject().Select(p => p.Name).Should()
            .BeEquivalentTo("scheduled", "completed", "cancelled", "noShow", "total");
    }

    [Theory]
    [InlineData("2026-11-22T23:30:00Z", "2026-11-23")]
    [InlineData("2026-07-05T22:30:00Z", "2026-07-06")]
    [InlineData("2026-11-22T22:30:00Z", "2026-11-16")]
    public async Task Default_range_is_current_Warsaw_week_including_Monday_and_Sunday(string now, string monday)
    {
        var s = await factory.DocumentationScenarioAsync();
        var from = DateOnly.Parse(monday);
        var to = from.AddDays(6);
        var start = WarsawTime.DayRangeUtc(from).StartUtc;
        var end = WarsawTime.DayRangeUtc(to).EndUtc;
        await factory.WithDbAsync(async db =>
        {
            var original = await db.Appointments.SingleAsync(a => a.Id == s.AppointmentId);
            original.StartsAt = start;
            original.EndsAt = start.AddMinutes(30);
            db.Appointments.AddRange(
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, end.AddMinutes(-30), AppointmentStatus.Completed),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, start.AddMinutes(-30), AppointmentStatus.Completed),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, end));
            return await db.SaveChangesAsync();
        });
        var previous = factory.Clock.UtcNow;
        try
        {
            factory.Clock.UtcNow = DateTime.Parse(now).ToUniversalTime();
            var stats = await s.Therapist.Stats();
            stats.From.Should().Be(from);
            stats.To.Should().Be(to);
            stats.Appointments.Should().BeEquivalentTo(new AppointmentStatsDto(0, 2, 0, 0));
        }
        finally { factory.Clock.UtcNow = previous; }
    }

    [Theory]
    [InlineData("2026-03-29", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z")]
    [InlineData("2026-10-25", "2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z")]
    public async Task Visit_day_respects_both_DST_changes_and_exclusive_next_midnight(string day, string lower, string upper)
    {
        var s = await factory.DocumentationScenarioAsync();
        var date = DateOnly.Parse(day);
        var start = DateTime.Parse(lower).ToUniversalTime();
        var end = DateTime.Parse(upper).ToUniversalTime();
        await factory.WithDbAsync(async db =>
        {
            var original = await db.Appointments.SingleAsync(a => a.Id == s.AppointmentId);
            original.StartsAt = start.AddTicks(-10); // PostgreSQL microsecond, overlapping by end only.
            original.EndsAt = start.AddMinutes(30);
            db.Appointments.AddRange(
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, start),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, end.AddTicks(-10), AppointmentStatus.Completed),
                AnalyticsTestSteps.Visit(s.TherapistId, s.PatientId, end));
            return await db.SaveChangesAsync();
        });
        (await s.Therapist.Stats(AnalyticsTestSteps.Range(date, date))).Appointments.Should()
            .BeEquivalentTo(new AppointmentStatsDto(1, 1, 0, 0));
    }

    [Fact]
    public async Task Pain_groups_all_daily_reports_preserves_zero_null_and_gaps_and_returns_only_aggregates()
    {
        var s = await factory.SharingScenario();
        var other = await factory.SharingScenario();
        var day = new DateOnly(2026, 10, 25);
        var entries = await s.Reports(factory, (day, 0), (day, 5), (day, null), (day.AddDays(1), null),
            (day.AddDays(3), 0), (day.AddDays(-1), 10), (day.AddDays(4), 10), (day, 10));
        await other.Reports(factory, (day, 10));
        await factory.WithDbAsync(async db =>
        {
            var deletedId = entries[^1].Id;
            (await db.ExerciseJournalEntries.SingleAsync(e => e.Id == deletedId)).IsDeleted = true;
            // Skipped and partial reports can also contain patient-reported pain; include every rating.
            var partial = await db.ExerciseJournalEntries.SingleAsync(e => e.Id == entries[1].Id);
            partial.Status = ExerciseCompletionStatus.Partial;
            var skipped = await db.ExerciseJournalEntries.SingleAsync(e => e.Id == entries[4].Id);
            skipped.Status = ExerciseCompletionStatus.Skipped;
            skipped.ActualSets = null;
            skipped.ActualRepetitions = null;
            return await db.SaveChangesAsync();
        });
        var query = AnalyticsTestSteps.Range(day, day.AddDays(3));
        var response = await s.Therapist.GetAsync(AnalyticsTestSteps.Pain(s.CardId) + query);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = await response.Body<PatientPainTrendDto>();
        result.Should().BeEquivalentTo(new PatientPainTrendDto(day, day.AddDays(3), "Europe/Warsaw",
            [new(day, 2.5, 2), new(day.AddDays(1), null, 0), new(day.AddDays(3), 0, 1)]), o => o.WithStrictOrdering());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("from", "to", "timeZone", "points");
        foreach (var point in json.RootElement.GetProperty("points").EnumerateArray())
            point.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("date", "averagePainLevel", "ratingCount");
        json.RootElement.GetProperty("points")[1].GetProperty("averagePainLevel").ValueKind.Should().Be(JsonValueKind.Null);
        (await s.Pain(AnalyticsTestSteps.Range(day.AddDays(2), day.AddDays(2)))).Points.Should().BeEmpty();
        (await s.Pain(AnalyticsTestSteps.Range(day.AddDays(5), day.AddDays(5)))).Points.Should().BeEmpty();

        await factory.WithDbAsync(async db =>
        {
            var edited = await db.ExerciseJournalEntries.SingleAsync(e => e.Id == entries[1].Id);
            edited.PainLevel = null;
            return await db.SaveChangesAsync();
        });
        (await s.Pain(query)).Points[0].Should().BeEquivalentTo(new DailyPainPointDto(day, 0, 1));
    }

    [Fact]
    public async Task Analytics_requires_authentication_and_therapist_role_and_hides_foreign_patients()
    {
        var s = await factory.SharingScenario();
        var other = await factory.SharingScenario();
        var anonymous = factory.CreateClient();
        foreach (var url in new[] { AnalyticsTestSteps.Dashboard, AnalyticsTestSteps.Pain(s.CardId) })
        {
            (await anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await s.Patient.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        (await other.Therapist.GetAsync(AnalyticsTestSteps.Pain(s.CardId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.GetAsync(AnalyticsTestSteps.Pain(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.GetAsync(AnalyticsTestSteps.Pain(other.CardId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pain_uses_journal_access_for_archive_confirmation_deletion_and_cooperation()
    {
        foreach (var reason in new[] { "archive", "profile", "card", "confirmation", "cooperation", "unlink", "wrongAccount", "wrongTherapist" })
        {
            var s = await factory.SharingScenario();
            var day = ExerciseJournalTestSteps.WarsawToday;
            await s.Reports(factory, (day, 0));
            var query = AnalyticsTestSteps.Range(day, day);
            (await s.Pain(query)).Points.Should().ContainSingle();
            await factory.WithDbAsync(async db =>
            {
                var card = await db.Patients.SingleAsync(p => p.Id == s.CardId);
                var request = await db.CooperationRequests.SingleAsync(r => r.Id == s.CooperationId);
                switch (reason)
                {
                    case "archive": card.IsArchived = true; break;
                    case "profile": (await db.Therapists.SingleAsync(t => t.Id == s.TherapistId)).IsDeleted = true; break;
                    case "card": card.IsDeleted = true; break;
                    case "confirmation": (await db.Users.SingleAsync(u => u.Id == card.UserId)).EmailConfirmed = false; break;
                    case "cooperation": request.Status = CooperationRequestStatus.Ended; break;
                    case "unlink": card.UserId = null; break;
                    case "wrongAccount": request.PatientUserId = (await db.Users.FirstAsync(u => u.Id != card.UserId)).Id; break;
                    case "wrongTherapist": request.TherapistId = (await db.Therapists.FirstAsync(t => t.Id != s.TherapistId)).Id; break;
                }
                return await db.SaveChangesAsync();
            });
            (await s.Therapist.GetAsync(AnalyticsTestSteps.Pain(s.CardId) + query)).StatusCode.Should().Be(HttpStatusCode.NotFound, reason);
            if (reason == "archive")
            {
                (await s.Therapist.PostAsync($"/api/patients/{s.CardId}/unarchive", null)).EnsureSuccessStatusCode();
                (await s.Pain(query)).Points.Should().ContainSingle();
            }
        }
    }

    [Fact]
    public async Task Renewed_cooperation_or_new_account_cannot_inherit_previous_link_pain_history()
    {
        foreach (var sameAccount in new[] { true, false })
        {
            var s = await factory.SharingScenario();
            var day = ExerciseJournalTestSteps.WarsawToday;
            await s.Reports(factory, (day, 10));
            var query = AnalyticsTestSteps.Range(day, day);
            (await s.Therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{s.CooperationId}/end", null))
                .EnsureSuccessStatusCode();
            (await s.Therapist.GetAsync(AnalyticsTestSteps.Pain(s.CardId) + query)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            var patient = sameAccount ? s.Patient : (await factory.CreateConfirmedPatientClientAsync()).Client;
            var request = await patient.SendRequestOkAsync(s.TherapistId);
            (await s.Therapist.AcceptWithExistingCardAsync(request.Id, s.CardId)).EnsureSuccessStatusCode();
            s = s with { Patient = patient, CooperationId = request.Id };
            (await s.Pain(query)).Points.Should().BeEmpty();
            await s.Reports(factory, (day, 0));
            (await s.Pain(query)).Points.Should().ContainSingle().Which.Should().BeEquivalentTo(new DailyPainPointDto(day, 0, 1));
        }
    }

    [Fact]
    public async Task Range_validation_rejects_partial_reversed_malformed_and_overlong_ranges_and_accepts_366_days()
    {
        var s = await factory.SharingScenario();
        foreach (var url in new[] { AnalyticsTestSteps.Dashboard, AnalyticsTestSteps.Pain(s.CardId) })
        {
            foreach (var query in new[] { "?from=garbage&to=2026-01-01", "?from=2026-01-01&to=garbage",
                "?from=2026-01-01", "?to=2026-01-01", "?from=2026-01-02&to=2026-01-01",
                "?from=0001-01-01&to=0001-01-01", "?from=2026-01-01&to=2027-01-02",
                "?from=9999-12-30&to=9999-12-31" })
            {
                var response = await s.Therapist.GetAsync(url + query);
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest, url + query);
                (await response.ErrorCodeAsync()).Should().Be("validation_failed");
            }
            foreach (var query in new[] { "?from=2024-01-01&to=2024-12-31", "?from=2026-01-01&to=2027-01-01",
                "?from=1900-01-01&to=1900-01-01", "?from=9999-12-30&to=9999-12-30" })
                (await s.Therapist.GetAsync(url + query)).StatusCode.Should().Be(HttpStatusCode.OK, url + query);
        }
        var trend = await s.Pain();
        var dashboard = await s.Therapist.Stats();
        trend.From.Should().Be(dashboard.From);
        trend.To.Should().Be(dashboard.To);
        trend.Points.Should().BeEmpty();
    }
}
