using FluentValidation;
using FluentValidation.Results;
using PhysioFlow.Application.Common.Interfaces;
using SkiaSharp;

namespace PhysioFlow.Infrastructure.Storage;

public class ProfilePhotoProcessor : IProfilePhotoProcessor
{
    public async Task<byte[]> ProcessAsync(Stream content, CancellationToken ct)
    {
        // Bound actual bytes even if the caller supplies a false length or a non-seekable stream.
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) != 0)
        {
            if (input.Length + read > IProfilePhotoProcessor.MaxUploadBytes)
                throw Invalid("Zdjęcie może mieć maksymalnie 5 MB (5242880 bajtów).");
            input.Write(buffer, 0, read);
        }
        ct.ThrowIfCancellationRequested();
        input.Position = 0;
        using var codec = SKCodec.Create(input);
        if (codec == null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            throw Invalid("Dozwolone są wyłącznie poprawne obrazy JPEG, PNG i WebP.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > IProfilePhotoProcessor.MaxImagePixels)
            throw Invalid("Zdjęcie może mieć maksymalnie 20 milionów pikseli.");

        using var pixels = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(pixels.Info, pixels.GetPixels()) != SKCodecResult.Success)
            throw Invalid("Nie można odczytać obrazu. Prześlij nieuszkodzony plik JPEG, PNG lub WebP.");
        ct.ThrowIfCancellationRequested();

        // Apply EXIF orientation to pixels, then encode a new image with no source metadata.
        var swapped = (int)codec.EncodedOrigin >= 5;
        var orientedWidth = swapped ? info.Height : info.Width;
        var orientedHeight = swapped ? info.Width : info.Height;
        var scale = Math.Min(1d, (double)IProfilePhotoProcessor.MaxOutputDimension / Math.Max(orientedWidth, orientedHeight));
        var width = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        var height = Math.Max(1, (int)Math.Round(orientedHeight * scale));
        using var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(output))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)width / orientedWidth, (float)height / orientedHeight);
            canvas.Concat(Orientation(codec.EncodedOrigin, info.Width, info.Height));
            canvas.DrawBitmap(pixels, 0, 0);
        }
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85)
            ?? throw Invalid("Nie można przetworzyć obrazu.");
        return encoded.ToArray();
    }

    private static SKMatrix Orientation(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    private static ValidationException Invalid(string message) =>
        new(new[] { new ValidationFailure("file", message) });
}
