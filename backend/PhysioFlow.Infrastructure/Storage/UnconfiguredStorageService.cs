using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Storage;

/// <summary>Profile editing remains available when optional R2 configuration is absent.</summary>
public class UnconfiguredStorageService : IStorageService
{
    public Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
        => throw new StorageUnavailableException();
    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
        => throw new StorageUnavailableException();
    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
        => throw new StorageUnavailableException();
    public Task<string> GetPresignedUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct = default)
        => throw new StorageUnavailableException();
}
