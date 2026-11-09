using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

/// <summary>Wspólne kroki scenariuszy próśb o współpracę.</summary>
public static class CooperationTestSteps
{
    public const string PatientRequestsUrl = "/api/patient-accounts/me/cooperation-requests";
    public const string PatientCooperationsUrl = "/api/patient-accounts/me/cooperations";
    public const string TherapistRequestsUrl = "/api/therapists/me/cooperation-requests";

    // API serializuje enumy jako tekst.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static async Task<(HttpClient Client, Guid ProfileId)> CreatePublishedTherapistAsync(
        this TestWebApplicationFactory factory, string lastName = "Terapeutka")
    {
        var client = await factory.CreateTherapistClientAsync("Anna", lastName);
        var response = await client.PutAsJsonAsync("/api/therapists/me/profile", new
        {
            firstName = "Anna",
            lastName,
            city = "Kraków",
            practiceName = "Gabinet Ruch",
            isPublished = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>();
        return (client, profile!.Id);
    }

    public static Task<HttpResponseMessage> SendRequestAsync(this HttpClient patient, Guid therapistId, string? message = null) =>
        patient.PostAsJsonAsync(PatientRequestsUrl, new { therapistId, message });

    public static async Task<PatientCooperationRequestDto> SendRequestOkAsync(this HttpClient patient, Guid therapistId)
    {
        var response = await patient.SendRequestAsync(therapistId);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PatientCooperationRequestDto>(CooperationTestSteps.Json))!;
    }

    public static Task<HttpResponseMessage> AcceptAsNewCardAsync(
        this HttpClient therapist, Guid requestId, string? pesel = null) =>
        therapist.PostAsJsonAsync($"{TherapistRequestsUrl}/{requestId}/accept", new
        {
            identityVerified = true,
            cardAction = "CreateNew",
            newPatient = new { firstName = "Jan", lastName = "Pacjent", pesel },
        });

    public static Task<HttpResponseMessage> AcceptWithExistingCardAsync(
        this HttpClient therapist, Guid requestId, Guid patientId) =>
        therapist.PostAsJsonAsync($"{TherapistRequestsUrl}/{requestId}/accept", new
        {
            identityVerified = true,
            cardAction = "LinkExisting",
            patientId,
        });

    public static async Task<Guid> CreateCardAsync(this HttpClient therapist, string? pesel = null)
    {
        var response = await therapist.PostAsJsonAsync("/api/patients", new
        {
            firstName = "Jan",
            lastName = "Kartowy",
            pesel,
            medicalHistory = "Historia choroby z karty",
            generalNotes = "Tajna notatka terapeuty",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<T> WithDbAsync<T>(this TestWebApplicationFactory factory, Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}

public class CooperationRequestsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CooperationRequestsTests(TestWebApplicationFactory factory) => _factory = factory;

    // --- wysyłanie

    [Fact]
    public async Task Patient_should_send_request_to_published_therapist_as_session_account()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, patientEmail) = await _factory.CreateConfirmedPatientClientAsync("Ewa", "Nadawczyni");
        var (_, otherEmail) = await _factory.CreateConfirmedPatientClientAsync();
        var otherAccountId = await AccountIdAsync(otherEmail);

        // Pola wskazujące inne konto są ignorowane.
        var response = await patient.PostAsJsonAsync(CooperationTestSteps.PatientRequestsUrl, new
        {
            therapistId,
            message = "Dzień dobry, proszę o konsultację.",
            patientUserId = otherAccountId,
            userId = otherAccountId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var sent = await response.Content.ReadFromJsonAsync<PatientCooperationRequestDto>(CooperationTestSteps.Json);
        sent!.Status.Should().Be(CooperationRequestStatus.Pending);
        sent.Therapist.Id.Should().Be(therapistId);

        var stored = await _factory.WithDbAsync(db => db.CooperationRequests.SingleAsync(r => r.Id == sent.Id));
        stored.PatientUserId.Should().Be(await AccountIdAsync(patientEmail));

        var inbox = await therapist.GetFromJsonAsync<TherapistCooperationRequestDto>(
            $"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}", CooperationTestSteps.Json);
        inbox!.Patient.Should().BeEquivalentTo(new RequestingPatientDto("Ewa", "Nadawczyni", patientEmail));
        inbox.Message.Should().Be("Dzień dobry, proszę o konsultację.");
    }

    [Fact]
    public async Task Request_to_unpublished_or_unknown_therapist_should_return_404()
    {
        var hidden = await _factory.CreateTherapistClientAsync();
        var hiddenId = (await hidden.GetFromJsonAsync<TherapistOwnProfileDto>("/api/therapists/me/profile"))!.Id;
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        (await patient.SendRequestAsync(hiddenId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await patient.SendRequestAsync(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Request_to_inactive_therapist_should_return_404()
    {
        var (_, therapistId) = await _factory.CreatePublishedTherapistAsync();
        await _factory.WithDbAsync(async db =>
        {
            var userId = await db.Therapists.Where(t => t.Id == therapistId).Select(t => t.UserId).SingleAsync();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.IsActive = false;
            return await db.SaveChangesAsync();
        });
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        (await patient.SendRequestAsync(therapistId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_pending_request_should_return_409()
    {
        var (_, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        await patient.SendRequestOkAsync(therapistId);

        var duplicate = await patient.SendRequestAsync(therapistId);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.RequestAlreadyPending);
    }

    [Fact]
    public async Task Request_with_active_cooperation_should_return_409()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptAsNewCardAsync(sent.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await patient.SendRequestAsync(therapistId);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CooperationAlreadyActive);
    }

    [Fact]
    public async Task Request_with_too_long_message_should_return_400()
    {
        var (_, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var response = await patient.SendRequestAsync(therapistId, new string('x', CooperationRequest.MessageMaxLength + 1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- wycofywanie i ponowne prośby

    [Fact]
    public async Task Patient_should_withdraw_only_own_pending_request()
    {
        var (_, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (owner, _) = await _factory.CreateConfirmedPatientClientAsync();
        var (stranger, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await owner.SendRequestOkAsync(therapistId);
        var withdrawUrl = $"{CooperationTestSteps.PatientRequestsUrl}/{sent.Id}/withdraw";

        (await stranger.PostAsync(withdrawUrl, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var withdrawn = await owner.PostAsync(withdrawUrl, null);
        withdrawn.StatusCode.Should().Be(HttpStatusCode.OK);
        (await withdrawn.Content.ReadFromJsonAsync<PatientCooperationRequestDto>(CooperationTestSteps.Json))!.Status
            .Should().Be(CooperationRequestStatus.Withdrawn);

        var again = await owner.PostAsync(withdrawUrl, null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.RequestNotPending);
    }

    [Fact]
    public async Task Accepted_request_cannot_be_withdrawn()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptAsNewCardAsync(sent.Id)).EnsureSuccessStatusCode();

        var response = await patient.PostAsync($"{CooperationTestSteps.PatientRequestsUrl}/{sent.Id}/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Resending_right_after_withdrawal_or_rejection_should_return_429()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (withdrawer, _) = await _factory.CreateConfirmedPatientClientAsync();
        var (rejected, _) = await _factory.CreateConfirmedPatientClientAsync();

        var first = await withdrawer.SendRequestOkAsync(therapistId);
        (await withdrawer.PostAsync($"{CooperationTestSteps.PatientRequestsUrl}/{first.Id}/withdraw", null))
            .EnsureSuccessStatusCode();
        var second = await rejected.SendRequestOkAsync(therapistId);
        (await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{second.Id}/reject", null))
            .EnsureSuccessStatusCode();

        foreach (var patient in new[] { withdrawer, rejected })
        {
            var response = await patient.SendRequestAsync(therapistId);
            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await response.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.ResubmissionCooldown);
        }
    }

    // --- terapeuta: widoczność i rozpatrywanie

    [Fact]
    public async Task Therapist_should_see_and_decide_only_requests_addressed_to_them()
    {
        var (owner, ownerId) = await _factory.CreatePublishedTherapistAsync();
        var (other, _) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(ownerId);
        var url = $"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}";

        (await other.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"{url}/reject", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.AcceptAsNewCardAsync(sent.Id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetFromJsonAsync<PaginatedList<TherapistCooperationRequestDto>>(CooperationTestSteps.TherapistRequestsUrl, CooperationTestSteps.Json))!
            .Items.Should().NotContain(r => r.Id == sent.Id);

        var ownerList = await owner.GetFromJsonAsync<PaginatedList<TherapistCooperationRequestDto>>(
            $"{CooperationTestSteps.TherapistRequestsUrl}?status=Pending", CooperationTestSteps.Json);
        ownerList!.Items.Should().ContainSingle(r => r.Id == sent.Id);
        (await _factory.WithDbAsync(db => db.CooperationRequests.SingleAsync(r => r.Id == sent.Id)))
            .Status.Should().Be(CooperationRequestStatus.Pending);
    }

    [Fact]
    public async Task Rejecting_should_close_request_and_block_further_decisions()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);

        var rejected = await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}/reject", null);
        var acceptAfter = await therapist.AcceptAsNewCardAsync(sent.Id);

        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rejected.Content.ReadFromJsonAsync<TherapistCooperationRequestDto>(CooperationTestSteps.Json))!.Status
            .Should().Be(CooperationRequestStatus.Rejected);
        acceptAfter.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await acceptAfter.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.RequestNotPending);
        (await patient.GetFromJsonAsync<List<PatientCooperationRequestDto>>(CooperationTestSteps.PatientRequestsUrl, CooperationTestSteps.Json))!
            .Single(r => r.Id == sent.Id).Status.Should().Be(CooperationRequestStatus.Rejected);
    }

    // --- akceptacja i karta

    [Theory]
    [InlineData("{\"cardAction\":\"CreateNew\",\"newPatient\":{\"firstName\":\"Jan\",\"lastName\":\"P\"}}", "identityVerified")]
    [InlineData("{\"identityVerified\":false,\"cardAction\":\"CreateNew\",\"newPatient\":{\"firstName\":\"Jan\",\"lastName\":\"P\"}}", "identityVerified")]
    [InlineData("{\"identityVerified\":true}", "cardAction")]
    [InlineData("{\"identityVerified\":true,\"cardAction\":\"LinkExisting\"}", "patientId")]
    [InlineData("{\"identityVerified\":true,\"cardAction\":\"CreateNew\"}", "newPatient")]
    [InlineData("{\"identityVerified\":true,\"cardAction\":\"CreateNew\",\"newPatient\":{\"firstName\":\"\",\"lastName\":\"P\",\"pesel\":\"123\"}}", "newPatient.firstName")]
    public async Task Accept_with_invalid_body_should_return_400_and_keep_request_pending(string body, string field)
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);

        var response = await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{sent.Id}/accept",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TherapistProfileTests.ErrorFieldsAsync(response)).Should().Contain(field);
        (await _factory.WithDbAsync(db => db.CooperationRequests.SingleAsync(r => r.Id == sent.Id)))
            .Status.Should().Be(CooperationRequestStatus.Pending);
    }

    [Fact]
    public async Task Accept_with_new_card_should_create_one_linked_card_without_sharing_documentation()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var accountId = await AccountIdAsync(email);
        var sent = await patient.SendRequestOkAsync(therapistId);

        var response = await therapist.AcceptAsNewCardAsync(sent.Id, pesel: "90010112345");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await response.Content.ReadFromJsonAsync<TherapistCooperationRequestDto>(CooperationTestSteps.Json);
        accepted!.Status.Should().Be(CooperationRequestStatus.Accepted);
        accepted.CardLinkMode.Should().Be(CardLinkMode.CreateNew);

        var cards = await _factory.WithDbAsync(db => db.Patients.Where(p => p.UserId == accountId).ToListAsync());
        var card = cards.Should().ContainSingle().Subject;
        card.Id.Should().Be(accepted.LinkedPatientId!.Value);
        card.TherapistId.Should().Be(therapistId);
        card.Email.Should().Be(email);
        card.Pesel.Should().Be("90010112345");
        card.MedicalHistory.Should().BeNull();
        card.GeneralNotes.Should().BeNull();
        card.DocumentationSharedWithAccount.Should().BeFalse();

        var stored = await _factory.WithDbAsync(db => db.CooperationRequests.SingleAsync(r => r.Id == sent.Id));
        stored.IdentityVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Accept_with_existing_own_card_should_link_it_and_keep_its_data()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var cardId = await therapist.CreateCardAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);

        var response = await therapist.AcceptWithExistingCardAsync(sent.Id, cardId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TherapistCooperationRequestDto>(CooperationTestSteps.Json))!.LinkedPatientId.Should().Be(cardId);
        var card = await _factory.WithDbAsync(db => db.Patients.SingleAsync(p => p.Id == cardId));
        card.UserId.Should().Be(await AccountIdAsync(email));
        card.GeneralNotes.Should().Be("Tajna notatka terapeuty");
        card.DocumentationSharedWithAccount.Should().BeFalse();
    }

    [Fact]
    public async Task Accept_with_card_of_another_therapist_should_return_404_and_leave_card_unlinked()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (otherTherapist, _) = await _factory.CreatePublishedTherapistAsync();
        var foreignCardId = await otherTherapist.CreateCardAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);

        var response = await therapist.AcceptWithExistingCardAsync(sent.Id, foreignCardId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.WithDbAsync(db => db.Patients.SingleAsync(p => p.Id == foreignCardId))).UserId.Should().BeNull();
        (await _factory.WithDbAsync(db => db.CooperationRequests.SingleAsync(r => r.Id == sent.Id)))
            .Status.Should().Be(CooperationRequestStatus.Pending);
    }

    [Fact]
    public async Task Accept_with_archived_card_should_return_409()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var cardId = await therapist.CreateCardAsync();
        (await therapist.DeleteAsync($"/api/patients/{cardId}")).EnsureSuccessStatusCode();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);

        var response = await therapist.AcceptWithExistingCardAsync(sent.Id, cardId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CardArchived);
    }

    [Fact]
    public async Task Accept_with_card_linked_to_another_account_should_return_409()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var cardId = await therapist.CreateCardAsync();
        var (firstPatient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var (secondPatient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var first = await firstPatient.SendRequestOkAsync(therapistId);
        var second = await secondPatient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(first.Id, cardId)).EnsureSuccessStatusCode();

        var response = await therapist.AcceptWithExistingCardAsync(second.Id, cardId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.CardAlreadyLinked);
        (await _factory.WithDbAsync(db => db.CooperationRequests.SingleAsync(r => r.Id == second.Id)))
            .Status.Should().Be(CooperationRequestStatus.Pending);
    }

    [Fact]
    public async Task Accept_when_account_already_has_card_at_this_therapist_should_return_409()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        var linkedCardId = await therapist.CreateCardAsync();
        var otherCardId = await therapist.CreateCardAsync();
        var accountId = await AccountIdAsync(email);
        await _factory.WithDbAsync(async db =>
        {
            (await db.Patients.SingleAsync(p => p.Id == linkedCardId)).UserId = accountId;
            return await db.SaveChangesAsync();
        });

        var linkSecond = await therapist.AcceptWithExistingCardAsync(sent.Id, otherCardId);
        var createSecond = await therapist.AcceptAsNewCardAsync(sent.Id);

        foreach (var response in new[] { linkSecond, createSecond })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.AccountAlreadyLinked);
        }
        (await _factory.WithDbAsync(db => db.Patients.CountAsync(p => p.UserId == accountId))).Should().Be(1);
    }

    [Fact]
    public async Task Accept_new_card_with_pesel_of_existing_card_should_return_409_without_creating_card()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        await therapist.CreateCardAsync(pesel: "85050512345");
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);

        var response = await therapist.AcceptAsNewCardAsync(sent.Id, pesel: "85050512345");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.PeselConflict);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("85050512345");
        var accountId = await AccountIdAsync(email);
        (await _factory.WithDbAsync(db => db.Patients.AnyAsync(p => p.UserId == accountId))).Should().BeFalse();
    }

    [Fact]
    public async Task Repeated_and_concurrent_accepts_should_create_exactly_one_card_and_cooperation()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, email) = await _factory.CreateConfirmedPatientClientAsync();
        var accountId = await AccountIdAsync(email);
        var sent = await patient.SendRequestOkAsync(therapistId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => therapist.AcceptAsNewCardAsync(sent.Id)));
        var repeated = await therapist.AcceptAsNewCardAsync(sent.Id);

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        repeated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _factory.WithDbAsync(db => db.Patients.CountAsync(p => p.UserId == accountId))).Should().Be(1);
        (await patient.GetFromJsonAsync<List<PatientCooperationDto>>(CooperationTestSteps.PatientCooperationsUrl))!
            .Should().ContainSingle();
    }

    // --- współprace pacjenta i izolacja

    [Fact]
    public async Task Cooperation_list_should_expose_only_public_therapist_data()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var cardId = await therapist.CreateCardAsync();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(sent.Id, cardId)).EnsureSuccessStatusCode();

        var body = await patient.GetStringAsync(CooperationTestSteps.PatientCooperationsUrl);
        var requestsBody = await patient.GetStringAsync(CooperationTestSteps.PatientRequestsUrl);

        var item = JsonDocument.Parse(body).RootElement[0];
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("requestId", "since", "therapist");
        item.GetProperty("therapist").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("id", "firstName", "lastName", "city", "practiceName");
        foreach (var text in new[] { body, requestsBody })
        {
            text.Should().NotContain(cardId.ToString())
                .And.NotContain("Tajna notatka")
                .And.NotContain("Historia choroby");
        }
    }

    [Fact]
    public async Task Two_patients_and_two_therapists_should_see_only_their_own_data()
    {
        var (t1, t1Id) = await _factory.CreatePublishedTherapistAsync("Pierwsza");
        var (t2, t2Id) = await _factory.CreatePublishedTherapistAsync("Druga");
        var (p1, p1Email) = await _factory.CreateConfirmedPatientClientAsync();
        var (p2, _) = await _factory.CreateConfirmedPatientClientAsync();

        var p1ToT1 = await p1.SendRequestOkAsync(t1Id);
        var p1ToT2 = await p1.SendRequestOkAsync(t2Id);
        var p2ToT1 = await p2.SendRequestOkAsync(t1Id);

        var t1Accept = await (await t1.AcceptAsNewCardAsync(p1ToT1.Id)).Content.ReadFromJsonAsync<TherapistCooperationRequestDto>(CooperationTestSteps.Json);
        var t2Accept = await (await t2.AcceptAsNewCardAsync(p1ToT2.Id)).Content.ReadFromJsonAsync<TherapistCooperationRequestDto>(CooperationTestSteps.Json);

        var t1Inbox = await t1.GetFromJsonAsync<PaginatedList<TherapistCooperationRequestDto>>(CooperationTestSteps.TherapistRequestsUrl, CooperationTestSteps.Json);
        var t2Inbox = await t2.GetFromJsonAsync<PaginatedList<TherapistCooperationRequestDto>>(CooperationTestSteps.TherapistRequestsUrl, CooperationTestSteps.Json);
        t1Inbox!.Items.Select(r => r.Id).Should().BeEquivalentTo(new[] { p1ToT1.Id, p2ToT1.Id });
        t2Inbox!.Items.Select(r => r.Id).Should().BeEquivalentTo(new[] { p1ToT2.Id });

        (await p1.GetFromJsonAsync<List<PatientCooperationRequestDto>>(CooperationTestSteps.PatientRequestsUrl, CooperationTestSteps.Json))!
            .Select(r => r.Id).Should().BeEquivalentTo(new[] { p1ToT1.Id, p1ToT2.Id });
        (await p2.GetFromJsonAsync<List<PatientCooperationRequestDto>>(CooperationTestSteps.PatientRequestsUrl, CooperationTestSteps.Json))!
            .Select(r => r.Id).Should().BeEquivalentTo(new[] { p2ToT1.Id });

        (await p1.GetFromJsonAsync<List<PatientCooperationDto>>(CooperationTestSteps.PatientCooperationsUrl))!
            .Select(c => c.Therapist.Id).Should().BeEquivalentTo(new[] { t1Id, t2Id });
        (await p2.GetFromJsonAsync<List<PatientCooperationDto>>(CooperationTestSteps.PatientCooperationsUrl))!
            .Should().BeEmpty();

        // Jedno konto, dwie osobne karty; każdy terapeuta widzi tylko swoją.
        var p1Account = await AccountIdAsync(p1Email);
        (await _factory.WithDbAsync(db => db.Patients.CountAsync(p => p.UserId == p1Account))).Should().Be(2);
        (await t1.GetAsync($"/api/patients/{t1Accept!.LinkedPatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await t1.GetAsync($"/api/patients/{t2Accept!.LinkedPatientId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await t2.GetAsync($"/api/patients/{t1Accept.LinkedPatientId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await t2.GetFromJsonAsync<PaginatedList<PatientListItemDto>>("/api/patients"))!
            .Items.Select(p => p.Id).Should().Equal(t2Accept.LinkedPatientId!.Value);
    }

    [Fact]
    public async Task Linking_card_should_not_give_patient_access_to_documentation()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();
        var cardId = await therapist.CreateCardAsync();
        var bodyMap = await therapist.PostAsJsonAsync("/api/bodymapping", new
        {
            patientId = cardId,
            notes = "Notatka z wizyty",
            painPoints = Array.Empty<object>(),
        });
        bodyMap.StatusCode.Should().Be(HttpStatusCode.Created);
        var bodyMapId = JsonDocument.Parse(await bodyMap.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var sent = await patient.SendRequestOkAsync(therapistId);
        (await therapist.AcceptWithExistingCardAsync(sent.Id, cardId)).EnsureSuccessStatusCode();

        (await patient.GetAsync($"/api/patients/{cardId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.GetAsync("/api/patients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.GetAsync($"/api/bodymapping/patient/{cardId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.GetAsync($"/api/bodymapping/{bodyMapId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.DeleteAsync($"/api/bodymapping/{bodyMapId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.PostAsJsonAsync("/api/bodymapping", new { painPoints = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await therapist.GetAsync($"/api/bodymapping/{bodyMapId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoints_should_require_matching_role()
    {
        var anonymous = _factory.CreateClient();
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        (await anonymous.GetAsync(CooperationTestSteps.PatientRequestsUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(CooperationTestSteps.TherapistRequestsUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await therapist.SendRequestAsync(therapistId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await therapist.GetAsync(CooperationTestSteps.PatientCooperationsUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.GetAsync(CooperationTestSteps.TherapistRequestsUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.AcceptAsNewCardAsync(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<Guid> AccountIdAsync(string email) =>
        _factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
}

public class CooperationResubmissionPolicyTests : IClassFixture<CooperationResubmissionPolicyTests.NoCooldownFactory>
{
    public class NoCooldownFactory : TestWebApplicationFactory
    {
        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.Configure<CooperationRequestOptions>(o =>
            {
                o.RejectionCooldownDays = 0;
                o.WithdrawalCooldownMinutes = 0;
                o.MaxRequestsPerDay = 3;
            });
    }

    private readonly NoCooldownFactory _factory;

    public CooperationResubmissionPolicyTests(NoCooldownFactory factory) => _factory = factory;

    [Fact]
    public async Task After_cooldown_patient_can_resend_until_daily_limit()
    {
        var (therapist, therapistId) = await _factory.CreatePublishedTherapistAsync();
        var (_, otherTherapistId) = await _factory.CreatePublishedTherapistAsync();
        var (patient, _) = await _factory.CreateConfirmedPatientClientAsync();

        var first = await patient.SendRequestOkAsync(therapistId);
        (await therapist.PostAsync($"{CooperationTestSteps.TherapistRequestsUrl}/{first.Id}/reject", null))
            .EnsureSuccessStatusCode();
        var second = await patient.SendRequestOkAsync(therapistId);
        (await patient.PostAsync($"{CooperationTestSteps.PatientRequestsUrl}/{second.Id}/withdraw", null))
            .EnsureSuccessStatusCode();
        await patient.SendRequestOkAsync(therapistId);

        var overLimit = await patient.SendRequestAsync(otherTherapistId);

        overLimit.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await overLimit.ErrorCodeAsync()).Should().Be(CooperationErrorCodes.DailyRequestLimit);
    }
}
