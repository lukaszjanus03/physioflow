namespace PhysioFlow.Application.Common.Interfaces;

/// <summary>
/// Abstrakcja nad Cloudflare R2 (S3-compatible).
/// Implementacja w Infrastructure używa AWSSDK.S3 skierowanego na R2 endpoint.
/// </summary>
public interface IStorageService
{
    /// <summary>Zwraca klucz pliku w bucket (np. "attachments/{guid}.pdf").</summary>
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Pre-signed URL dla frontu - bez tunelowania przez backend.</summary>
    Task<string> GetPresignedUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct = default);
}

public interface IEmailService
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    Task SendAppointmentReminderAsync(string to, string patientName, DateTime appointmentTime, CancellationToken ct = default);
    Task SendPatientInviteAsync(string to, string inviteLink, string therapistName, CancellationToken ct = default);
}

public interface IPdfGenerationService
{
    Task<byte[]> GeneratePatientCardAsync(Guid patientId, CancellationToken ct = default);
}
