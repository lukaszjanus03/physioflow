using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Email;

/// <summary>Lokalny dostawca zapisujący wiadomości HTML; nie łączy się z pocztą.</summary>
public class LocalPickupEmailService(IConfiguration configuration) : IEmailService
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        SendIdempotentAsync(to, subject, htmlBody, Guid.NewGuid().ToString(), ct);

    public async Task SendIdempotentAsync(string to, string subject, string htmlBody, string idempotencyKey, CancellationToken ct = default)
    {
        var directory = Path.GetFullPath(configuration["Email:PickupDirectory"]!);
        Directory.CreateDirectory(directory);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        var target = Path.Combine(directory, name + ".html");
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary,
                $"<meta charset=\"utf-8\"><p>Do: {HtmlEncoder.Default.Encode(to)}</p>" +
                $"<p>Temat: {HtmlEncoder.Default.Encode(subject)}</p>{htmlBody}", ct);
            try { File.Move(temporary, target, overwrite: false); }
            catch (IOException) when (File.Exists(target)) { /* Ten klucz jest już obsłużony. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task SendEmailConfirmationAsync(string to, string firstName, string confirmationLink, CancellationToken ct = default) =>
        SendAsync(to, "Potwierdź adres e-mail - PhysioFlow",
            $"<a href=\"{HtmlEncoder.Default.Encode(confirmationLink)}\">Potwierdź adres e-mail</a>", ct);

    public Task SendAppointmentReminderAsync(string to, string patientName, DateTime appointmentTime, CancellationToken ct = default) =>
        SendAsync(to, "Przypomnienie o wizycie - PhysioFlow", $"<p>{appointmentTime:O}</p>", ct);

    public Task SendPatientInviteAsync(string to, string inviteLink, string therapistName, CancellationToken ct = default) =>
        SendAsync(to, "Zaproszenie do PhysioFlow", $"<a href=\"{HtmlEncoder.Default.Encode(inviteLink)}\">Aktywuj konto</a>", ct);
}
