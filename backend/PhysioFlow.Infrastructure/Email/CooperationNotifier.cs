using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Email;

public class CooperationNotifier : ICooperationNotifier
{
    private readonly IEmailService _email;
    private readonly IUserAccountService _accounts;
    private readonly ILogger<CooperationNotifier> _logger;
    private readonly string _frontendBaseUrl;

    public CooperationNotifier(
        IEmailService email,
        IUserAccountService accounts,
        IConfiguration config,
        ILogger<CooperationNotifier> logger)
    {
        _email = email;
        _accounts = accounts;
        _logger = logger;
        _frontendBaseUrl = (config["Frontend:BaseUrl"] ?? string.Empty).TrimEnd('/');
    }

    public Task RequestReceivedAsync(
        Guid requestId, Guid therapistUserId, string? patientFirstName, string? patientLastName, CancellationToken ct = default) =>
        SendAsync(requestId, therapistUserId, "Nowa prośba o współpracę - PhysioFlow", $"""
            <h2>Nowa prośba o współpracę</h2>
            <p>{Encode(FullName(patientFirstName, patientLastName))} prosi o współpracę z Tobą w PhysioFlow.</p>
            <p>Przed akceptacją zweryfikuj tożsamość pacjenta i wybierz kartę, z którą połączysz jego konto.</p>
            {Link("/cooperation-requests", "Przejdź do próśb o współpracę")}
            """, ct);

    public Task RequestAcceptedAsync(
        Guid requestId, Guid patientUserId, string therapistName, CancellationToken ct = default) =>
        SendAsync(requestId, patientUserId, "Prośba o współpracę zaakceptowana - PhysioFlow", $"""
            <h2>Prośba zaakceptowana</h2>
            <p>{Encode(therapistName)} zaakceptował(a) Twoją prośbę o współpracę.</p>
            {Link("/portal", "Przejdź do portalu pacjenta")}
            """, ct);

    public Task RequestRejectedAsync(
        Guid requestId, Guid patientUserId, string therapistName, CancellationToken ct = default) =>
        SendAsync(requestId, patientUserId, "Prośba o współpracę odrzucona - PhysioFlow", $"""
            <h2>Prośba odrzucona</h2>
            <p>{Encode(therapistName)} nie przyjął(-ęła) Twojej prośby o współpracę.</p>
            <p>W katalogu PhysioFlow możesz wybrać innego terapeutę.</p>
            {Link("/portal", "Przejdź do portalu pacjenta")}
            """, ct);

    public Task CooperationEndedByTherapistAsync(
        Guid requestId, Guid patientUserId, string therapistName, CancellationToken ct = default) =>
        SendAsync(requestId, patientUserId, "Współpraca zakończona - PhysioFlow", $"""
            <h2>Współpraca zakończona</h2>
            <p>{Encode(therapistName)} zakończył(a) współpracę z Tobą w PhysioFlow. Twoje konto nie jest już powiązane z kartą pacjenta tego terapeuty.</p>
            {Link("/portal", "Przejdź do portalu pacjenta")}
            """, ct);

    public Task CooperationEndedByPatientAsync(
        Guid requestId, Guid therapistUserId, string? patientFirstName, string? patientLastName, CancellationToken ct = default) =>
        SendAsync(requestId, therapistUserId, "Pacjent zakończył współpracę - PhysioFlow", $"""
            <h2>Współpraca zakończona</h2>
            <p>{Encode(FullName(patientFirstName, patientLastName))} zakończył(a) współpracę w PhysioFlow. Karta pacjenta pozostaje u Ciebie, ale nie jest już powiązana z kontem pacjenta.</p>
            {Link("/cooperation-requests", "Przejdź do próśb o współpracę")}
            """, ct);

    private async Task SendAsync(Guid requestId, Guid recipientUserId, string subject, string html, CancellationToken ct)
    {
        try
        {
            var recipient = await _accounts.FindAsync(recipientUserId, ct);
            if (recipient is null)
            {
                _logger.LogWarning(
                    "Cooperation notification skipped | request {RequestId} | recipient account {AccountId} not found",
                    requestId, recipientUserId);
                return;
            }

            await _email.SendAsync(recipient.Email, subject, html, ct);
        }
        catch (Exception ex)
        {
            // Zmiana jest już zapisana - nieudane powiadomienie nie może jej cofnąć ani zwrócić błędu.
            _logger.LogWarning(
                "Cooperation notification failed | request {RequestId} | recipient account {AccountId} | error: {ErrorType}",
                requestId, recipientUserId, ex.GetType().Name);
        }
    }

    private string Link(string path, string text) =>
        string.IsNullOrEmpty(_frontendBaseUrl)
            ? string.Empty
            : $"""<p><a href="{Encode(_frontendBaseUrl + path)}">{text}</a></p>""";

    private static string FullName(string? firstName, string? lastName)
    {
        var name = $"{firstName} {lastName}".Trim();
        return name.Length > 0 ? name : "Pacjent";
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
