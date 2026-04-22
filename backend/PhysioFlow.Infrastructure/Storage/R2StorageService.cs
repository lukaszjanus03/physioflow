using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 jest S3-compatible - używamy AWSSDK.S3 z custom endpointem.
/// Klucze dostępowe R2 bierzemy z Cloudflare dashboard (R2 > Manage API Tokens).
/// </summary>
public class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly ILogger<R2StorageService> _logger;

    public R2StorageService(
        IConfiguration configuration,
        IAmazonS3 s3,
        ILogger<R2StorageService> logger)
    {
        _s3 = s3;
        _bucketName = configuration["R2:BucketName"]
            ?? throw new InvalidOperationException("R2:BucketName not configured");
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid()}/{fileName}";
        var req = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = true  // wymagane dla R2
        };

        await _s3.PutObjectAsync(req, ct);
        _logger.LogInformation("Uploaded {Key} to R2", key);
        return key;
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var req = new GetObjectRequest { BucketName = _bucketName, Key = storageKey };
        var response = await _s3.GetObjectAsync(req, ct);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        await _s3.DeleteObjectAsync(_bucketName, storageKey, ct);
    }

    public Task<string> GetPresignedUrlAsync(
        string storageKey, TimeSpan expiry, CancellationToken ct = default)
    {
        var req = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET
        };
        var url = _s3.GetPreSignedURL(req);
        return Task.FromResult(url);
    }
}
