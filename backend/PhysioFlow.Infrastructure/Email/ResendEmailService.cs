using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Interfaces;
using Resend;

namespace PhysioFlow.Infrastructure.Email;

public class ResendEmailService : IEmailService
{
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

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            From = _fromAddress,
            To = to,
            Subject = subject,
            HtmlBody = htmlBody
        };
        await _resend.EmailSendAsync(message, ct);
        _logger.LogInformation("Email sent to {To} | subject: {Subject}", to, subject);
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
