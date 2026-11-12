namespace PhysioFlow.Application.Common.Interfaces;

public interface IProfilePhotoProcessor
{
    const int MaxUploadBytes = 5 * 1024 * 1024;
    const int MaxImagePixels = 20_000_000;
    const int MaxOutputDimension = 1024;

    /// <summary>Validates content and returns a static WebP containing pixels only.</summary>
    Task<byte[]> ProcessAsync(Stream content, CancellationToken ct);
}
