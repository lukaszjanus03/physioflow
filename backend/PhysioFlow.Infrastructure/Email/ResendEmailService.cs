using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using Resend;

namespace PhysioFlow.Infrastructure.Email;

public class ResendEmailService : IEmailService
{
    private const string DeliveryFailedMessage = "Dostawca poczty nie przyjął wiadomości do wysyłki.";

    private readonly IResend _resend;
    private readonly string _fromAddress;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IResend resend, IConfiguration config, ILogger<ResendEmailService> logger)
    {
        _resend = resend;
        _fromAddress = config["Resend:FromAddress"] ?? "noreply@physioflow.pl";
        _logger = logger;
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        SendCoreAsync(to, subject, htmlBody, null, ct);

    public Task SendIdempotentAsync(string to, string subject, string htmlBody, string idempotencyKey, CancellationToken ct = default) =>
        SendCoreAsync(to, subject, htmlBody, idempotencyKey, ct);

    private async Task SendCoreAsync(string to, string subject, string htmlBody, string? idempotencyKey, CancellationToken ct)
    {
        var message = new EmailMessage
        {
            From = _fromAddress,
            To = to,
            Subject = subject,
            HtmlBody = htmlBody
        };

        ResendResponse<Guid> response;
        try
        {
            response = idempotencyKey is null
                ? await _resend.EmailSendAsync(message, ct)
                : await _resend.EmailSendAsync(idempotencyKey, message, ct);
        }
        catch (ResendException ex)
        {
            // Bez ex.Message i treści wiadomości - mogą zawierać dane odbiorcy lub link z tokenem.
            _logger.LogWarning(
                "Resend rejected email | subject: {Subject} | status: {Status} | error: {ErrorType}",
                subject, ex.StatusCode, ex.ErrorType);
            throw new EmailDeliveryException(DeliveryFailedMessage, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(
                "Resend unreachable | subject: {Subject} | error: {ErrorType}",
                subject, ex.GetType().Name);
            throw new EmailDeliveryException(DeliveryFailedMessage, ex);
        }

        if (!response.Success)
        {
            _logger.LogWarning(
                "Resend rejected email | subject: {Subject} | status: {Status} | error: {ErrorType}",
                subject, response.Exception?.StatusCode, response.Exception?.ErrorType);
            throw new EmailDeliveryException(DeliveryFailedMessage);
        }

        _logger.LogInformation("Email sent | id: {EmailId} | subject: {Subject}", response.Content, subject);
    }

    public Task SendEmailConfirmationAsync(
        string to, string firstName, string confirmationLink, CancellationToken ct = default)
    {
        var html = $"""
            <h2>Potwierdź adres e-mail</h2>
            <p>Cześć {HtmlEncoder.Default.Encode(firstName)}!</p>
            <p>Dziękujemy za założenie konta pacjenta w PhysioFlow. Aby dokończyć rejestrację, potwierdź swój adres e-mail.</p>
            <p><a href="{HtmlEncoder.Default.Encode(confirmationLink)}">Potwierdź adres e-mail</a></p>
            <p>Jeśli to nie Ty zakładałeś konto, zignoruj tę wiadomość.</p>
            """;
        return SendAsync(to, "Potwierdź adres e-mail - PhysioFlow", html, ct);
    }

    public Task SendAppointmentReminderAsync(
        string to, string patientName, DateTime appointmentTime, CancellationToken ct = default)
    {
        var html = $"""
            <h2>Przypomnienie o wizycie</h2>
            <p>Cześć {patientName}!</p>
            <p>Przypominamy o wizycie {appointmentTime:dd.MM.yyyy HH:mm}.</p>
            <p>W razie konieczności odwołania, zaloguj się do portalu pacjenta.</p>
            """;
        return SendAsync(to, "Przypomnienie o wizycie - PhysioFlow", html, ct);
    }

    public Task SendPatientInviteAsync(
        string to, string inviteLink, string therapistName, CancellationToken ct = default)
    {
        var html = $"""
            <h2>Zaproszenie do portalu pacjenta</h2>
            <p>{therapistName} zaprasza Cię do portalu PhysioFlow.</p>
            <p><a href="{inviteLink}">Aktywuj konto</a></p>
            <p>Link jest ważny przez 7 dni.</p>
            """;
        return SendAsync(to, "Zaproszenie do PhysioFlow", html, ct);
    }
}
