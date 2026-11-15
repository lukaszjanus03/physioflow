using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace PhysioFlow.Application.Features.Therapy;

public static partial class YoutubeVideoUrl
{
    [GeneratedRegex("\\A[A-Za-z0-9_-]{11}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();

    public static string? ParseId(string? value)
    {
        if (value is null || value.Length > 2048
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http")
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            return null;

        var host = uri.IdnHost.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        if (host == "youtu.be" && segments.Length == 1)
            id = segments[0];
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com")
        {
            if (uri.AbsolutePath == "/watch")
            {
                var values = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(pair => Uri.UnescapeDataString(pair[0]) == "v").ToArray();
                if (values.Length == 1 && values[0].Length == 2)
                    id = Uri.UnescapeDataString(values[0][1]);
            }
            else if (segments.Length == 2 && segments[0] is "shorts" or "embed" or "live")
                id = segments[1];
        }
        else if (host is "youtube-nocookie.com" or "www.youtube-nocookie.com"
            && segments.Length == 2 && segments[0] == "embed")
            id = segments[1];

        return id != null && VideoIdPattern().IsMatch(id) ? id : null;
    }

    public static string CanonicalUrl(string id) => $"https://www.youtube.com/watch?v={id}";
}

public static class ExerciseVideoFile
{
    public const long MaxBytes = 100L * 1024 * 1024;
    public const long MaxRequestBytes = MaxBytes + 1024 * 1024;

    /// <summary>Bounded disk buffering also handles non-seekable streams and verifies the actual byte count.</summary>
    public static async Task<(FileStream Content, string Extension, string ContentType)> ValidateAsync(
        Stream source, long declaredLength, string fileName, string contentType, CancellationToken ct)
    {
        TherapyValidation.Require(declaredLength is > 0 and <= MaxBytes, "file", "Video must contain 1 to 104857600 bytes.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var expectedType = extension switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            _ => null
        };
        TherapyValidation.Require(expectedType != null && string.Equals(expectedType, contentType, StringComparison.OrdinalIgnoreCase),
            "file", "Allowed video formats are MP4 (video/mp4), WebM (video/webm) and MOV (video/quicktime).");

        var content = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                total += read;
                TherapyValidation.Require(total <= MaxBytes && total <= declaredLength, "file", "Video size exceeds its declared or allowed length.");
                await content.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            TherapyValidation.Require(total == declaredLength, "file", "Video length does not match the uploaded file.");
            content.Position = 0;
            var header = new byte[(int)Math.Min(4096, total)];
            await content.ReadExactlyAsync(header, ct);
            TherapyValidation.Require(HasContainerHeader(header, total, extension), "file", "Video container does not match the declared format.");
            content.Position = 0;
            return (content, extension, expectedType!);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    private static bool HasContainerHeader(byte[] header, long length, string extension)
    {
        if (extension == ".webm")
            return header.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })
                && header.AsSpan().IndexOf(new byte[] { 0x42, 0x82, 0x84, (byte)'w', (byte)'e', (byte)'b', (byte)'m' }) >= 0;

        if (header.Length < 16 || !header.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return false;
        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (boxSize < 16 || boxSize > length || boxSize % 4 != 0) return false;
        var brand = Encoding.ASCII.GetString(header, 8, 4);
        if (extension == ".mov") return brand == "qt  ";
        return brand is "isom" or "iso2" or "iso3" or "iso4" or "iso5" or "iso6" or "iso7" or "iso8"
            or "iso9" or "mp41" or "mp42" or "avc1" or "M4V " or "MSNV" or "dash";
    }
}
