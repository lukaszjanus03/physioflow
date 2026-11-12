using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using SkiaSharp;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PhotoTestFactory : TestWebApplicationFactory
{
    public FakePhotoStorage Storage { get; } = new();
    public FailPhotoSaveInterceptor Saves { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IStorageService>();
        services.AddSingleton<IStorageService>(Storage);
        services.AddDbContext<PhysioFlow.Infrastructure.Persistence.ApplicationDbContext>(o => o.AddInterceptors(Saves));
    }
}

public class FakePhotoStorage : IStorageService
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public bool FailUpload { get; set; }
    public bool FailDelete { get; set; }
    public bool FailDownload { get; set; }
    public string? LastUploadContentType { get; private set; }
    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        if (FailUpload) throw new InvalidOperationException("SECRET-storage-credentials");
        using var output = new MemoryStream();
        await content.CopyToAsync(output, ct);
        var key = Guid.NewGuid() + "/" + fileName;
        Files[key] = output.ToArray();
        LastUploadContentType = contentType;
        return key;
    }
    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        if (FailDownload) throw new InvalidOperationException("SECRET-storage-credentials");
        return Task.FromResult<Stream>(new MemoryStream(Files[storageKey], writable: false));
    }
    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        if (FailDelete) throw new InvalidOperationException("SECRET-storage-credentials");
        Files.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }
    public Task<string> GetPresignedUrlAsync(string storageKey, TimeSpan expiry, CancellationToken ct = default)
        => throw new InvalidOperationException("API must not expose presigned storage URLs.");
}

public class FailPhotoSaveInterceptor : SaveChangesInterceptor
{
    public bool FailNextSave { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new DbUpdateException("Simulated failed save");
        }
        return ValueTask.FromResult(result);
    }
}

public class ProfilePhotoTests
{
    private const string ProfileUrl = "/api/therapists/me/profile";
    private const string UploadUrl = ProfileUrl + "/photo";

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Supported_content_should_upload_regardless_of_untrusted_file_name_and_mime(SKEncodedImageFormat format)
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        using var form = Form(PhotoFixtures.Image(format), "misleading.txt", "application/octet-stream");
        var response = await owner.PutAsync(UploadUrl, form);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var profile = (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!;
        profile.PhotoUrl.Should().Be($"/api/therapists/{profile.Id}/photo");
        profile.IsPublished.Should().BeFalse();
        factory.Storage.Files.Should().ContainSingle();
        factory.Storage.LastUploadContentType.Should().Be("image/webp");
        using var decoded = SKCodec.Create(new MemoryStream(factory.Storage.Files.Single().Value));
        decoded.EncodedFormat.Should().Be(SKEncodedImageFormat.Webp);
        (await owner.GetAsync(profile.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("photoStorageKey").And.NotContain("SECRET-");
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><rect width='10' height='10'/></svg>")]
    [InlineData("not an image")]
    [InlineData("")]
    public async Task Invalid_files_should_be_rejected_even_when_claiming_jpeg(string content)
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        using var form = Form(Encoding.UTF8.GetBytes(content));
        var response = await owner.PutAsync(UploadUrl, form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Storage.Files.Should().BeEmpty();
        (await owner.GetFromJsonAsync<TherapistOwnProfileDto>(ProfileUrl))!.PhotoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Wrong_formats_truncated_images_missing_file_and_oversized_uploads_should_be_rejected()
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        foreach (var bytes in new[]
        {
            Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"),
            PhotoFixtures.Image(SKEncodedImageFormat.Jpeg)[..30],
            new byte[IProfilePhotoProcessor.MaxUploadBytes + 1]
        })
        {
            using var form = Form(bytes);
            (await owner.PutAsync(UploadUrl, form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        using var empty = new MultipartFormDataContent();
        (await owner.PutAsync(UploadUrl, empty)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Replacement_and_idempotent_deletion_should_remove_old_objects()
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var first = await UploadAsync(owner);
        var oldKey = factory.Storage.Files.Keys.Single();
        await UploadAsync(owner, SKEncodedImageFormat.Png);
        factory.Storage.Files.Should().ContainSingle();
        factory.Storage.Files.Should().NotContainKey(oldKey);
        var deleted = await owner.DeleteAsync(UploadUrl);
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deleted.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!.PhotoUrl.Should().BeNull();
        factory.Storage.Files.Should().BeEmpty();
        (await owner.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync(UploadUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_upload_storage_failure_and_database_failure_should_preserve_working_previous_photo()
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var first = await UploadAsync(owner);
        var oldKey = factory.Storage.Files.Keys.Single();
        var original = await owner.GetByteArrayAsync(first.PhotoUrl);
        using (var bad = Form(Encoding.UTF8.GetBytes("<svg/>")))
            (await owner.PutAsync(UploadUrl, bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.Storage.FailUpload = true;
        using (var valid = Form(PhotoFixtures.Image(SKEncodedImageFormat.Png)))
        {
            var failure = await owner.PutAsync(UploadUrl, valid);
            failure.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await failure.Content.ReadAsStringAsync()).Should().NotContain("SECRET-");
        }
        factory.Storage.FailUpload = false;
        factory.Saves.FailNextSave = true;
        using (var valid = Form(PhotoFixtures.Image(SKEncodedImageFormat.Png)))
            (await owner.PutAsync(UploadUrl, valid)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        factory.Storage.Files.Should().ContainKey(oldKey);
        (await owner.GetByteArrayAsync(first.PhotoUrl)).Should().Equal(original);
        var storedKey = await factory.WithDbAsync(db => db.Therapists.Where(t => t.Id == first.Id)
            .Select(t => t.PhotoStorageKey).SingleAsync());
        storedKey.Should().Be(oldKey);
    }

    [Fact]
    public async Task Failed_old_object_cleanup_should_keep_successful_replacement_and_deleted_photo_inaccessible()
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var first = await UploadAsync(owner);
        factory.Storage.FailDelete = true;
        await UploadAsync(owner);
        (await owner.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.DeleteAsync(UploadUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hidden_photos_should_be_owner_only_and_publication_hiding_and_deactivation_should_control_access()
    {
        using var factory = new PhotoTestFactory();
        var owner = await factory.CreateTherapistClientAsync();
        var other = await factory.CreateTherapistClientAsync();
        var anonymous = factory.CreateClient();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        var first = await UploadAsync(owner);
        foreach (var client in new[] { other, anonymous, patient })
            (await client.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var ownOther = await UploadAsync(other);
        (await other.DeleteAsync(UploadUrl)).EnsureSuccessStatusCode();
        (await owner.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PutAsync($"/api/therapists/{ownOther.Id}/photo", Form(PhotoFixtures.Image(SKEncodedImageFormat.Jpeg))))
            .StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var publish = TherapistProfileTests.Draft("Kraków", "Gabinet", true);
        (await owner.PutAsJsonAsync(ProfileUrl, publish)).EnsureSuccessStatusCode();
        var publicPhoto = await anonymous.GetAsync(first.PhotoUrl);
        publicPhoto.StatusCode.Should().Be(HttpStatusCode.OK);
        publicPhoto.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        publicPhoto.Headers.CacheControl!.NoStore.Should().BeTrue();
        publicPhoto.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        (await owner.PutAsJsonAsync(ProfileUrl, publish with { IsPublished = false })).EnsureSuccessStatusCode();
        (await anonymous.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync(ProfileUrl, publish)).EnsureSuccessStatusCode();
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => db.Therapists.Any(t => t.Id == first.Id && t.UserId == u.Id));
            user.IsActive = false;
            return await db.SaveChangesAsync();
        });
        (await anonymous.GetAsync(first.PhotoUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upload_and_delete_should_require_therapist_and_storage_errors_should_be_sanitized()
    {
        using var factory = new PhotoTestFactory();
        var anonymous = factory.CreateClient();
        var (patient, _) = await factory.CreateConfirmedPatientClientAsync();
        (await anonymous.PutAsync(UploadUrl, Form(PhotoFixtures.Image(SKEncodedImageFormat.Jpeg)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync(UploadUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await patient.PutAsync(UploadUrl, Form(PhotoFixtures.Image(SKEncodedImageFormat.Jpeg)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await patient.DeleteAsync(UploadUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var owner = await factory.CreateTherapistClientAsync();
        var profile = await UploadAsync(owner);
        factory.Storage.FailDownload = true;
        var response = await owner.GetAsync(profile.PhotoUrl);
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SECRET-");
    }

    internal static MultipartFormDataContent Form(byte[] bytes, string fileName = "photo.jpg", string type = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", fileName);
        return form;
    }

    internal static async Task<TherapistOwnProfileDto> UploadAsync(HttpClient owner, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var form = Form(PhotoFixtures.Image(format));
        var response = await owner.PutAsync(UploadUrl, form);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TherapistOwnProfileDto>())!;
    }
}

public static class PhotoFixtures
{
    public static byte[] Image(SKEncodedImageFormat format, int width = 30, int height = 20)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static byte[] JpegWithPrivateMetadata(ushort orientation = 6)
    {
        var jpeg = Image(SKEncodedImageFormat.Jpeg);
        using var exif = new MemoryStream();
        using (var w = new BinaryWriter(exif, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("Exif\0\0II"));
            w.Write((ushort)42); w.Write(8u); w.Write((ushort)2);
            w.Write((ushort)0x0112); w.Write((ushort)3); w.Write(1u); w.Write(orientation); w.Write((ushort)0);
            w.Write((ushort)0x8825); w.Write((ushort)4); w.Write(1u); w.Write(38u); w.Write(0u);
            var gps = Encoding.ASCII.GetBytes("ASCII\0\0\0PRIVATE-GPS-LOCATION-50.0614-19.9366");
            w.Write((ushort)1); w.Write((ushort)0x001b); w.Write((ushort)7); w.Write((uint)gps.Length); w.Write(56u); w.Write(0u); w.Write(gps);
        }
        using var output = new MemoryStream();
        output.Write(jpeg.AsSpan(0, 2));
        foreach (var payload in new[] { exif.ToArray(), Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>PRIVATE-XMP-LOCATION</x:xmpmeta>") })
        {
            output.WriteByte(0xff); output.WriteByte(0xe1);
            var length = payload.Length + 2;
            output.WriteByte((byte)(length >> 8)); output.WriteByte((byte)length);
            output.Write(payload);
        }
        output.Write(jpeg.AsSpan(2));
        return output.ToArray();
    }
}
