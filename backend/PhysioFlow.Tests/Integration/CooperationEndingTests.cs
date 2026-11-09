using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Enums;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class CooperationEndingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CooperationEndingTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Therapist_should_end_cooperation_and_unlink_card_keeping_documentation()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var cardId = await therapist.CreateCardAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(sent.Id, cardId)).EnsureSuccessStatusCode();
        await _factory.WithDbAsync(async db =>
        {
            var card = await db.Patients.SingleAsync(p => p.Id == cardId);
            card.DocumentationSharedWithAccount = true;
            return await db.SaveChangesAsync();
        });

        var response = await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}/end", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ended = await response.Content.ReadFromJsonAsync<TherapistCooperationRequestDto>(CooperationTestSteps.Json);
        ended!.Status.Should().Be(CooperationRequestStatus.Ended);
        ended.EndedBy.Should().Be(CooperationParty.Therapist);
        ended.EndedAt.Should().NotBeNull();
        ended.LinkedPatientId.Should().Be(cardId);

        var stored = await _factory.WithDbAsync(db => db.Patients.SingleAsync(p => p.Id == cardId));
        stored.UserId.Should().BeNull();
        stored.DocumentationSharedWithAccount.Should().BeFalse();
        stored.GeneralNotes.Should().Be("Tajna notatka terapeuty");

        (await patient.GetFromJsonAsync<List<PatientCooperationDto>>(CooperationTestSteps.PatientCooperationsUrl))
            .Should().BeEmpty();
        _factory.Email.SentTo(email).Should().Contain(e => e.Subject.StartsWith("Współpraca zakończona"));
    }

    [Fact]
    public async Task Patient_should_end_cooperation_and_then_send_a_new_request()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var cardId = await therapist.CreateCardAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(sent.Id, cardId)).EnsureSuccessStatusCode();

        var response = await patient.PostAsync($"{CooperationTestSteps.PatientCooperationsUrl}/{sent.Id}/end", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ended = await response.Content.ReadFromJsonAsync<PatientCooperationRequestDto>(CooperationTestSteps.Json);
        ended!.Status.Should().Be(CooperationRequestStatus.Ended);
        ended.EndedBy.Should().Be(CooperationParty.Patient);
        (await _factory.WithDbAsync(db => db.Patients.SingleAsync(p => p.Id == cardId))).UserId.Should().BeNull();
        _factory.Email.SentTo(await TherapistEmailAsync(therapistId))
            .Should().Contain(e => e.Subject.StartsWith("Pacjent zakończył współpracę"));

        var again = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(again.Id, cardId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ending_should_be_allowed_only_for_own_active_cooperation()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (otherTherapist, _) = await _factory.CreatePublishedTherapistAsync("Inna");
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var (otherPatient, _) = await _factory.CreateConfirmedPatientClientAsync("Ewa");
        var sent = await patient.SendRequestOkAsync(therapistId);
        var therapistEnd = $"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}/end";
        var patientEnd = $"{CooperationTestSteps.PatientCooperationsUrl}/{sent.Id}/end";

        var pending = await therapist.PostAsync(therapistEnd, null);
        pending.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await pending.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationNotActive);

        (await therapist.AcceptAsNewCardAsync(sent.Id)).EnsureSuccessStatusCode();
        (await otherTherapist.PostAsync(therapistEnd, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherPatient.PostAsync(patientEnd, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await patient.PostAsync(therapistEnd, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await therapist.PostAsync(patientEnd, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var results = await Task.WhenAll(therapist.PostAsync(therapistEnd, null), patient.PostAsync(patientEnd, null));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [Fact]
    public async Task Patient_list_and_details_should_show_whether_card_is_linked_to_account()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var linkedId = await therapist.CreateCardAsync();
        var unlinkedId = await therapist.CreateCardAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(sent.Id, linkedId)).EnsureSuccessStatusCode();

        var list = JsonDocument.Parse(await therapist.GetStringAsync("/api/patients?pageSize=100")).RootElement
            .GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("id").GetGuid(), i => i.GetProperty("isLinkedToAccount").GetBoolean());
        list[linkedId].Should().BeTrue();
        list[unlinkedId].Should().BeFalse();

        var details = JsonDocument.Parse(await therapist.GetStringAsync($"/api/patients/{linkedId}")).RootElement;
        details.GetProperty("isLinkedToAccount").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Request_decisions_should_notify_the_other_side_by_email()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync("Mailowa");
        var therapistEmail = await TherapistEmailAsync(therapistId);
        var (accepted, acceptedEmail) = await _factory.CreateConfirmedPatientClientAsync("Adam", "Nowak");
        var (rejected, rejectedEmail) = await _factory.CreateConfirmedPatientClientAsync();

        var first = await accepted.SendRequestOkAsync(therapistId);
        var second = await rejected.SendRequestOkAsync(therapistId);
        var received = _factory.Email.SentTo(therapistEmail);
        received.Should().HaveCount(2).And.OnlyContain(e => e.Subject.StartsWith("Nowa prośba o współpracę"));
        received.Should().Contain(e => e.HtmlBody.Contains("Adam Nowak"));

        (await therapist.AcceptAsNewCardAsync(first.Id)).EnsureSuccessStatusCode();
        (await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{second.Id}/reject", null)).EnsureSuccessStatusCode();

        _factory.Email.SentTo(acceptedEmail).Should().ContainSingle(e => e.Subject.StartsWith("Prośba o współpracę zaakceptowana"))
            .Which.HtmlBody.Should().Contain("Anna Mailowa");
        _factory.Email.SentTo(rejectedEmail).Should().ContainSingle(e => e.Subject.StartsWith("Prośba o współpracę odrzucona"));
    }

    [Fact]
    public async Task Email_provider_failure_should_not_fail_cooperation_actions()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        _factory.Email.SimulateProviderFailure = true;
        try
        {
            var sent = await patient.SendRequestOkAsync(therapistId);
            (await therapist.AcceptAsNewCardAsync(sent.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}/end", null))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            _factory.Email.SimulateProviderFailure = false;
        }
    }

    [Fact]
    public async Task Duplicate_pesel_conflict_should_not_echo_the_pesel()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        await therapist.CreateCardAsync(pesel: "85030312345");

        var response = await therapist.PostAsJsonAsync("/api/patients", new
        {
            firstName = "Jan",
            lastName = "Drugi",
            pesel = "85030312345",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("pesel_conflict");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("85030312345");
    }

    [Fact]
    public async Task Update_patient_pesel_conflict_should_not_echo_the_pesel()
    {
        var therapist = await _factory.CreateTherapistClientAsync();
        await therapist.CreateCardAsync(pesel: "85030312345");
        var secondId = await therapist.CreateCardAsync();

        var response = await therapist.PutAsJsonAsync($"/api/patients/{secondId}", new
        {
            id = secondId,
            firstName = "Jan",
            lastName = "Drugi",
            pesel = "85030312345",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("pesel_conflict");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("85030312345");
    }

    private Task<string> TherapistEmailAsync(Guid profileId) =>
        _factory.WithDbAsync(db => db.Therapists
            .Where(t => t.Id == profileId)
            .Join(db.Users, t => t.UserId, u => u.Id, (_, u) => u.Email!)
            .SingleAsync());
}
