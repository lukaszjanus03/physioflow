using System.Collections.Concurrent;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Tests.Integration;

public record SentConfirmationEmail(string To, string FirstName, string ConfirmationLink);

public record SentEmail(string To, string Subject, string HtmlBody);

/// <summary>Zastępuje Resend w testach. Przechwytuje wiadomości i pozwala zasymulować awarię dostawcy.</summary>
public class FakeEmailService : IEmailService
{
    public ConcurrentQueue<SentConfirmationEmail> SentConfirmations { get; } = new();

    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public bool SimulateProviderFailure { get; set; }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        ThrowIfFailing();
        Sent.Enqueue(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public IReadOnlyList<SentEmail> SentTo(string to) =>
        Sent.Where(e => string.Equals(e.To, to, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendEmailConfirmationAsync(
        string to, string firstName, string confirmationLink, CancellationToken ct = default)
    {
        ThrowIfFailing();
        SentConfirmations.Enqueue(new SentConfirmationEmail(to, firstName, confirmationLink));
        return Task.CompletedTask;
    }

    public Task SendAppointmentReminderAsync(
        string to, string patientName, DateTime appointmentTime, CancellationToken ct = default) =>
        SendAsync(to, "reminder", string.Empty, ct);

    public Task SendPatientInviteAsync(
        string to, string inviteLink, string therapistName, CancellationToken ct = default) =>
        SendAsync(to, "invite", string.Empty, ct);

    private void ThrowIfFailing()
    {
        if (SimulateProviderFailure)
            throw new EmailDeliveryException("Dostawca poczty nie przyjął wiadomości do wysyłki.");
    }
}
