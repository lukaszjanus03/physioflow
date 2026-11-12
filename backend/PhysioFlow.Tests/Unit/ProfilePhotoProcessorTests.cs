using System.Text;
using FluentAssertions;
using FluentValidation;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure.Storage;
using PhysioFlow.Tests.Integration;
using SkiaSharp;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ProfilePhotoProcessorTests
{
    [Theory]
    [InlineData((ushort)1, 30, 20)]
    [InlineData((ushort)6, 20, 30)]
    [InlineData((ushort)8, 20, 30)]
    public async Task Reencoding_should_strip_gps_exif_xmp_and_keep_orientation(ushort orientation, int width, int height)
    {
        var input = PhotoFixtures.JpegWithPrivateMetadata(orientation);
        Encoding.ASCII.GetString(input).Should().Contain("PRIVATE-GPS").And.Contain("PRIVATE-XMP");
        var bytes = await new ProfilePhotoProcessor().ProcessAsync(new MemoryStream(input), default);
        var text = Encoding.ASCII.GetString(bytes);
        text.Should().NotContain("PRIVATE-").And.NotContain("EXIF").And.NotContain("XMP ").And.NotContain("ICCP");
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        codec.Info.Width.Should().Be(width);
        codec.Info.Height.Should().Be(height);
        codec.EncodedOrigin.Should().Be(SKEncodedOrigin.TopLeft);
    }

    [Fact]
    public async Task Large_photos_should_be_resized_and_images_over_pixel_limit_rejected()
    {
        var processor = new ProfilePhotoProcessor();
        var bytes = await processor.ProcessAsync(new MemoryStream(PhotoFixtures.Image(SKEncodedImageFormat.Png, 1800, 1200)), default);
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        codec.Info.Width.Should().Be(1024);
        codec.Info.Height.Should().Be(683);
        var large = PhotoFixtures.Image(SKEncodedImageFormat.Png, 5000, 4001);
        var act = () => processor.ProcessAsync(new MemoryStream(large), default);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Actual_byte_limit_should_be_enforced_and_exactly_5_MB_accepted()
    {
        var image = PhotoFixtures.Image(SKEncodedImageFormat.Jpeg);
        var exact = new byte[IProfilePhotoProcessor.MaxUploadBytes];
        image.CopyTo(exact, 0);
        var processor = new ProfilePhotoProcessor();
        (await processor.ProcessAsync(new MemoryStream(exact), default)).Should().NotBeEmpty();
        var act = () => processor.ProcessAsync(new MemoryStream(new byte[IProfilePhotoProcessor.MaxUploadBytes + 1]), default);
        await act.Should().ThrowAsync<ValidationException>();
    }
}
