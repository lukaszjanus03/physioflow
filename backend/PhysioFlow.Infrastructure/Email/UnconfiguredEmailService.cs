using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Email;

/// <summary>
/// Rejestrowany, gdy brakuje Resend:ApiKey. Wysyłka kończy się błędem zamiast udawać sukces.
/// </summary>
public class UnconfiguredEmailService : IEmailService
{
    private const string NotConfiguredMessage = "Wysyłka wiadomości e-mail nie jest skonfigurowana.";

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        throw new EmailDeliveryException(NotConfiguredMessage);

    public Task SendEmailConfirmationAsync(
        string to, string firstName, string confirmationLink, CancellationToken ct = default) =>
        throw new EmailDeliveryException(NotConfiguredMessage);

    public Task SendAppointmentReminderAsync(
        string to, string patientName, DateTime appointmentTime, CancellationToken ct = default) =>
        throw new EmailDeliveryException(NotConfiguredMessage);

    public Task SendPatientInviteAsync(
        string to, string inviteLink, string therapistName, CancellationToken ct = default) =>
        throw new EmailDeliveryException(NotConfiguredMessage);
}
