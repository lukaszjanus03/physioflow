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

public record DocumentationScenario(HttpClient Therapist, Guid TherapistId, Guid PatientId, Guid AppointmentId)
{
    public string ListUrl => $"/api/patients/{PatientId}/medical-records";
}

public static class DocumentationTestSteps
{
    public static string RecordUrl(Guid id) => $"/api/medical-records/{id}";
    public static CreateMedicalRecordRequest CompleteContent(Guid appointmentId = default) => new()
    {
        AppointmentId = appointmentId, Interview = "PRIVATE_INTERVIEW", FunctionalAssessment = "PRIVATE_ASSESSMENT",
        PerformedActions = "PRIVATE_ACTIONS", Recommendations = "PRIVATE_RECOMMENDATIONS", PatientSummary = "PATIENT_SUMMARY"
    };

    public static CorrectMedicalRecordRequest Correction(MedicalRecordDto record, string? reason = "Correction of an inaccurate assessment.") => new()
    {
        Version = record.Version, CorrectionReason = reason, Interview = "CORRECTED_INTERVIEW",
        FunctionalAssessment = record.Content.FunctionalAssessment, PerformedActions = record.Content.PerformedActions,
        Recommendations = record.Content.Recommendations, PatientSummary = record.Content.PatientSummary
    };

    public static async Task<DocumentationScenario> DocumentationScenarioAsync(this TestWebApplicationFactory factory,
        AppointmentStatus status = AppointmentStatus.Completed)
    {
        var therapist = await factory.CreateTherapistClientAsync();
        var profile = (await therapist.GetFromJsonAsync<TherapistOwnProfileDto>("/api/therapists/me/profile"))!;
        var patientId = await therapist.CreateCardAsync();
        var appointmentId = await factory.WithDbAsync(async db =>
        {
            var appointment = new Appointment
            {
                PatientId = patientId, TherapistId = profile.Id, Status = status,
                StartsAt = DateTime.UtcNow.AddDays(-1), EndsAt = DateTime.UtcNow.AddDays(-1).AddHours(1),
                Duration = AppointmentDuration.Sixty, Notes = "LEGACY_INTERNAL_NOTE", PatientMessage = "BOOKING_MESSAGE"
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        });
        return new(therapist, profile.Id, patientId, appointmentId);
    }

    public static async Task<MedicalRecordDto> CreateRecordAsync(this DocumentationScenario s, bool complete = true)
    {
        var response = await s.Therapist.PostAsJsonAsync(s.ListUrl, complete
            ? CompleteContent(s.AppointmentId) : new CreateMedicalRecordRequest { AppointmentId = s.AppointmentId });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location.Should().NotBeNull();
        return (await response.Content.ReadFromJsonAsync<MedicalRecordDto>(CooperationTestSteps.Json))!;
    }

    public static async Task<MedicalRecordDto> ApproveRecordAsync(this DocumentationScenario s, MedicalRecordDto record)
    {
        var response = await s.Therapist.PostAsJsonAsync(RecordUrl(record.Id) + "/approve", new { record.Version });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MedicalRecordDto>(CooperationTestSteps.Json))!;
    }

    public static async Task<MedicalRecordDto> ReadRecordAsync(this HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<MedicalRecordDto>(RecordUrl(id), CooperationTestSteps.Json))!;

    public static async Task<List<MedicalRecordVersionDto>> HistoryAsync(this HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<MedicalRecordVersionDto>>(RecordUrl(id) + "/versions"))!;
}

public class MedicalRecordsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public MedicalRecordsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Empty_draft_should_roundtrip_and_full_replacement_should_rotate_edit_token_without_changing_visit()
    {
        var s = await _factory.DocumentationScenarioAsync(AppointmentStatus.Scheduled);
        var record = await s.CreateRecordAsync(complete: false);
        record.Status.Should().Be(MedicalRecordStatus.Draft);
        record.CurrentVersion.Should().Be(0);
        record.Content.Should().BeEquivalentTo(new MedicalRecordContent());
        (await s.Therapist.ReadRecordAsync(record.Id)).Should().BeEquivalentTo(record);
        (await s.Therapist.HistoryAsync(record.Id)).Should().BeEmpty();
        var response = await s.Therapist.PutAsJsonAsync(DocumentationTestSteps.RecordUrl(record.Id), new
        {
            record.Version, interview = "  Interview text  ", patientSummary = "  Summary  ",
            appointmentId = Guid.NewGuid(), patientId = Guid.NewGuid(), therapistId = Guid.NewGuid(), status = "Approved"
        });
        response.EnsureSuccessStatusCode();
        var saved = await s.Therapist.ReadRecordAsync(record.Id);
        saved.Content.Interview.Should().Be("Interview text");
        saved.Content.PatientSummary.Should().Be("Summary");
        saved.Content.FunctionalAssessment.Should().BeNull();
        saved.Version.Should().NotBe(record.Version);
        saved.PatientId.Should().Be(s.PatientId);
        saved.AppointmentId.Should().Be(s.AppointmentId);
        saved.TherapistId.Should().Be(s.TherapistId);
        saved.Status.Should().Be(MedicalRecordStatus.Draft);
        var visit = await _factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == s.AppointmentId));
        visit.Status.Should().Be(AppointmentStatus.Scheduled);
        visit.Notes.Should().Be("LEGACY_INTERNAL_NOTE");
        visit.PatientMessage.Should().Be("BOOKING_MESSAGE");
        (await s.Therapist.GetAsync(DocumentationTestSteps.RecordUrl(record.Id))).Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Approval_and_corrections_should_preserve_each_version_author_time_and_reason()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var draft = await s.CreateRecordAsync();
        var approved = await s.ApproveRecordAsync(draft);
        approved.Status.Should().Be(MedicalRecordStatus.Approved);
        approved.CurrentVersion.Should().Be(1);
        var cannotOverwrite = await s.Therapist.PutAsJsonAsync(DocumentationTestSteps.RecordUrl(draft.Id),
            new SaveMedicalRecordRequest { Version = approved.Version, Interview = "overwrite" });
        (await cannotOverwrite.ErrorCodeAsync()).Should().Be("medical_record_correction_required");
        var correction = await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(draft.Id) + "/corrections",
            DocumentationTestSteps.Correction(approved));
        correction.EnsureSuccessStatusCode();
        var latest = await s.Therapist.ReadRecordAsync(draft.Id);
        latest.CurrentVersion.Should().Be(2);
        latest.Version.Should().NotBe(approved.Version);
        latest.Content.Interview.Should().Be("CORRECTED_INTERVIEW");
        var history = await s.Therapist.HistoryAsync(draft.Id);
        history.Select(v => v.VersionNumber).Should().Equal(1, 2);
        history[0].Content.Should().BeEquivalentTo(draft.Content);
        history[0].CorrectionReason.Should().BeNull();
        history[1].Content.Should().BeEquivalentTo(latest.Content);
        history[1].CorrectionReason.Should().Be("Correction of an inaccurate assessment.");
        var authorId = await _factory.WithDbAsync(db => db.Therapists.Where(t => t.Id == s.TherapistId).Select(t => t.UserId).SingleAsync());
        history.Should().OnlyContain(v => v.AuthorUserId == authorId && v.RecordedAt.Kind == DateTimeKind.Utc);
        history[1].RecordedAt.Should().BeOnOrAfter(history[0].RecordedAt);
        (await s.Therapist.DeleteAsync(DocumentationTestSteps.RecordUrl(draft.Id))).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await s.Therapist.HistoryAsync(draft.Id)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Approval_should_require_a_completed_visit(AppointmentStatus status)
    {
        var s = await _factory.DocumentationScenarioAsync(status);
        var draft = await s.CreateRecordAsync();
        var response = await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(draft.Id) + "/approve", new { draft.Version });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("medical_record_completed_appointment_required");
        (await s.Therapist.ReadRecordAsync(draft.Id)).Should().BeEquivalentTo(draft);
        (await s.Therapist.HistoryAsync(draft.Id)).Should().BeEmpty();
        (await _factory.WithDbAsync(db => db.Appointments.SingleAsync(a => a.Id == s.AppointmentId))).Status.Should().Be(status);
    }

    [Fact]
    public async Task Approval_should_validate_all_required_clinical_fields_but_summary_is_optional()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var empty = await s.CreateRecordAsync(complete: false);
        var response = await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(empty.Id) + "/approve", new { empty.Version });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("interview", "functionalAssessment", "performedActions", "recommendations");
        var full = DocumentationTestSteps.CompleteContent();
        (await s.Therapist.PutAsJsonAsync(DocumentationTestSteps.RecordUrl(empty.Id), new SaveMedicalRecordRequest
        {
            Version = empty.Version, Interview = full.Interview, FunctionalAssessment = full.FunctionalAssessment,
            PerformedActions = full.PerformedActions, Recommendations = full.Recommendations
        })).EnsureSuccessStatusCode();
        var approved = await s.ApproveRecordAsync(await s.Therapist.ReadRecordAsync(empty.Id));
        approved.Content.PatientSummary.Should().BeNull();
    }

    [Fact]
    public async Task Stale_tokens_should_conflict_for_draft_save_approval_and_correction_without_adding_history()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var draft = await s.CreateRecordAsync();
        var url = DocumentationTestSteps.RecordUrl(draft.Id);
        (await s.Therapist.PutAsJsonAsync(url, new SaveMedicalRecordRequest
        {
            Version = draft.Version, Interview = draft.Content.Interview, FunctionalAssessment = draft.Content.FunctionalAssessment,
            PerformedActions = draft.Content.PerformedActions, Recommendations = draft.Content.Recommendations
        })).EnsureSuccessStatusCode();
        foreach (var response in new[]
        {
            await s.Therapist.PutAsJsonAsync(url, new SaveMedicalRecordRequest { Version = draft.Version }),
            await s.Therapist.PostAsJsonAsync(url + "/approve", new { draft.Version })
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.ErrorCodeAsync()).Should().Be("concurrent_modification");
        }
        var approved = await s.ApproveRecordAsync(await s.Therapist.ReadRecordAsync(draft.Id));
        var conflict = await s.Therapist.PostAsJsonAsync(url + "/corrections", DocumentationTestSteps.Correction(approved with { Version = draft.Version }));
        (await conflict.ErrorCodeAsync()).Should().Be("concurrent_modification");
        (await s.Therapist.HistoryAsync(draft.Id)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Corrections_should_require_a_reason(string? reason)
    {
        var s = await _factory.DocumentationScenarioAsync();
        var approved = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        var response = await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(approved.Id) + "/corrections",
            DocumentationTestSteps.Correction(approved, reason));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("correctionReason");
        (await s.Therapist.HistoryAsync(approved.Id)).Should().ContainSingle();
        (await s.Therapist.ReadRecordAsync(approved.Id)).Should().BeEquivalentTo(approved);
    }

    [Fact]
    public async Task Invalid_transitions_should_not_write_versions()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var draft = await s.CreateRecordAsync();
        var url = DocumentationTestSteps.RecordUrl(draft.Id);
        var response = await s.Therapist.PostAsJsonAsync(url + "/corrections", DocumentationTestSteps.Correction(draft));
        (await response.ErrorCodeAsync()).Should().Be("medical_record_not_approved");
        var approved = await s.ApproveRecordAsync(draft);
        response = await s.Therapist.PostAsJsonAsync(url + "/approve", new { approved.Version });
        (await response.ErrorCodeAsync()).Should().Be("medical_record_already_approved");
        (await s.Therapist.HistoryAsync(draft.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Own_patient_and_visit_links_and_one_record_per_visit_should_be_enforced()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var other = await _factory.DocumentationScenarioAsync();
        var otherOwnCard = await s.Therapist.CreateCardAsync();
        foreach (var (patientId, appointmentId) in new[]
        {
            (s.PatientId, other.AppointmentId), (other.PatientId, s.AppointmentId),
            (otherOwnCard, s.AppointmentId), (s.PatientId, Guid.NewGuid())
        })
        {
            var response = await s.Therapist.PostAsJsonAsync($"/api/patients/{patientId}/medical-records",
                DocumentationTestSteps.CompleteContent(appointmentId));
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        await s.CreateRecordAsync();
        var duplicate = await s.Therapist.PostAsJsonAsync(s.ListUrl, DocumentationTestSteps.CompleteContent(s.AppointmentId));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).Should().Be("medical_record_already_exists");
    }

    [Fact]
    public async Task Other_therapist_should_not_list_read_save_approve_correct_or_read_versions()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var other = await _factory.CreateTherapistClientAsync();
        var record = await s.CreateRecordAsync();
        var url = DocumentationTestSteps.RecordUrl(record.Id);
        foreach (var response in new[]
        {
            await other.GetAsync(s.ListUrl), await other.GetAsync(url), await other.GetAsync(url + "/versions"),
            await other.PutAsJsonAsync(url, new SaveMedicalRecordRequest { Version = record.Version }),
            await other.PostAsJsonAsync(url + "/approve", new { record.Version }),
            await other.PostAsJsonAsync(url + "/corrections", DocumentationTestSteps.Correction(record)),
            await other.PostAsJsonAsync(s.ListUrl, DocumentationTestSteps.CompleteContent(s.AppointmentId))
        }) response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Therapist.ReadRecordAsync(record.Id)).Should().BeEquivalentTo(record);
    }

    [Fact]
    public async Task Anonymous_patient_and_moderator_should_not_access_any_documentation_routes_even_with_sharing_flag()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        await _factory.WithDbAsync(async db =>
        {
            var card = await db.Patients.SingleAsync(p => p.Id == s.PatientId);
            card.UserId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            card.DocumentationSharedWithAccount = true;
            (await db.Appointments.SingleAsync(a => a.Id == s.AppointmentId)).PatientUserId = card.UserId;
            return await db.SaveChangesAsync();
        });
        var record = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        var moderator = await _factory.CreateModeratorAsync();
        var url = DocumentationTestSteps.RecordUrl(record.Id);
        foreach (var (client, expected) in new[] { (_factory.CreateClient(), HttpStatusCode.Unauthorized),
                     (patient, HttpStatusCode.Forbidden), (moderator, HttpStatusCode.Forbidden) })
        {
            foreach (var response in new[]
            {
                await client.GetAsync(s.ListUrl), await client.GetAsync(url), await client.GetAsync(url + "/versions"),
                await client.PostAsJsonAsync(s.ListUrl, DocumentationTestSteps.CompleteContent(s.AppointmentId)),
                await client.PutAsJsonAsync(url, new SaveMedicalRecordRequest { Version = record.Version }),
                await client.PostAsJsonAsync(url + "/approve", new { record.Version }),
                await client.PostAsJsonAsync(url + "/corrections", DocumentationTestSteps.Correction(record))
            })
            {
                response.StatusCode.Should().Be(expected);
                (await response.Content.ReadAsStringAsync()).Should().NotContain("PRIVATE_").And.NotContain("PATIENT_SUMMARY");
            }
        }
        var portal = await patient.GetStringAsync($"{PatientCalendarSteps.Url}/{s.AppointmentId}");
        portal.Should().NotContain("PRIVATE_").And.NotContain("LEGACY_INTERNAL_NOTE").And.NotContain("PATIENT_SUMMARY");
    }

    [Fact]
    public async Task Archive_should_preserve_patient_history_and_allow_owner_to_read_and_correct()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var approved = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        (await s.Therapist.DeleteAsync($"/api/patients/{s.PatientId}")).EnsureSuccessStatusCode();
        (await s.Therapist.ReadRecordAsync(approved.Id)).Should().BeEquivalentTo(approved);
        var list = (await s.Therapist.GetFromJsonAsync<PaginatedList<MedicalRecordListItemDto>>(s.ListUrl, CooperationTestSteps.Json))!;
        list.Items.Should().ContainSingle(x => x.Id == approved.Id);
        (await s.Therapist.PostAsJsonAsync(DocumentationTestSteps.RecordUrl(approved.Id) + "/corrections",
            DocumentationTestSteps.Correction(approved))).EnsureSuccessStatusCode();
        (await s.Therapist.HistoryAsync(approved.Id)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageNumber=2147483647")]
    public async Task Invalid_pagination_should_be_rejected(string query)
    {
        var owner = await _factory.CreateTherapistClientAsync();
        (await owner.GetAsync($"/api/patients/{Guid.NewGuid()}/medical-records?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("interview", 10000)]
    [InlineData("functionalAssessment", 10000)]
    [InlineData("performedActions", 10000)]
    [InlineData("recommendations", 10000)]
    [InlineData("patientSummary", 2000)]
    public async Task Field_length_boundaries_should_be_enforced_for_create_save_and_correction(string field, int maxLength)
    {
        var s = await _factory.DocumentationScenarioAsync();
        var body = new Dictionary<string, object?>
        {
            ["appointmentId"] = s.AppointmentId, ["interview"] = "Interview", ["functionalAssessment"] = "Assessment",
            ["performedActions"] = "Actions", ["recommendations"] = "Recommendations", [field] = new string('x', maxLength + 1)
        };
        var invalid = await s.Therapist.PostAsJsonAsync(s.ListUrl, body);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var error = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        error.RootElement.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
        body[field] = new string('x', maxLength);
        var response = await s.Therapist.PostAsJsonAsync(s.ListUrl, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = (await response.Content.ReadFromJsonAsync<MedicalRecordDto>(CooperationTestSteps.Json))!;
        body["version"] = draft.Version;
        body[field] = new string('x', maxLength + 1);
        var url = DocumentationTestSteps.RecordUrl(draft.Id);
        (await s.Therapist.PutAsJsonAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var approved = await s.ApproveRecordAsync(draft);
        body["version"] = approved.Version;
        body["correctionReason"] = "A valid reason";
        (await s.Therapist.PostAsJsonAsync(url + "/corrections", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Therapist.HistoryAsync(draft.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Corrections_should_validate_reason_length_and_all_clinical_fields_without_altering_approved_content()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var approved = await s.ApproveRecordAsync(await s.CreateRecordAsync());
        var url = DocumentationTestSteps.RecordUrl(approved.Id) + "/corrections";
        var correction = DocumentationTestSteps.Correction(approved);
        (await s.Therapist.PostAsJsonAsync(url, correction with { CorrectionReason = new string('r', 1001) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var invalid in new[] { correction with { Interview = " " }, correction with { FunctionalAssessment = null },
                     correction with { PerformedActions = "" }, correction with { Recommendations = null } })
            (await s.Therapist.PostAsJsonAsync(url, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Therapist.ReadRecordAsync(approved.Id)).Should().BeEquivalentTo(approved);
        (await s.Therapist.HistoryAsync(approved.Id)).Should().ContainSingle();
        (await s.Therapist.PostAsJsonAsync(url, correction with { CorrectionReason = new string('r', 1000) })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Missing_edit_tokens_and_appointment_ids_should_return_validation_errors()
    {
        var s = await _factory.DocumentationScenarioAsync();
        (await s.Therapist.PostAsJsonAsync(s.ListUrl, new { interview = "Draft" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var draft = await s.CreateRecordAsync();
        var url = DocumentationTestSteps.RecordUrl(draft.Id);
        (await s.Therapist.PutAsJsonAsync(url, new { interview = "Draft" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Therapist.PostAsJsonAsync(url + "/approve", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Therapist.PostAsJsonAsync(url + "/corrections", DocumentationTestSteps.Correction(draft) with { Version = Guid.Empty }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_should_paginate_in_visit_order_and_omit_clinical_content()
    {
        var s = await _factory.DocumentationScenarioAsync();
        var first = await s.CreateRecordAsync();
        var secondVisit = await _factory.WithDbAsync(async db =>
        {
            var visit = new Appointment { PatientId = s.PatientId, TherapistId = s.TherapistId,
                StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddHours(1), Duration = AppointmentDuration.Sixty };
            db.Appointments.Add(visit);
            await db.SaveChangesAsync();
            return visit.Id;
        });
        var second = await (s with { AppointmentId = secondVisit }).CreateRecordAsync();
        var response = await s.Therapist.GetAsync(s.ListUrl + "?pageSize=1");
        var page = (await response.Content.ReadFromJsonAsync<PaginatedList<MedicalRecordListItemDto>>(CooperationTestSteps.Json))!;
        page.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle(i => i.Id == second.Id);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("PRIVATE_").And.NotContain("interview");
        page = (await s.Therapist.GetFromJsonAsync<PaginatedList<MedicalRecordListItemDto>>(s.ListUrl + "?pageSize=1&pageNumber=2", CooperationTestSteps.Json))!;
        page.Items.Should().ContainSingle(i => i.Id == first.Id);
    }
}
