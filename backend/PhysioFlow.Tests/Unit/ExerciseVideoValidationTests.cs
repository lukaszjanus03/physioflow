using FluentAssertions;
using FluentValidation;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Tests.Integration;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ExerciseVideoValidationTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("http://youtube.com/watch?feature=share&v=dQw4w9WgXcQ&t=10")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=tracker#fragment")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=abc")]
    [InlineData("https://youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    public void Supported_urls_normalize_to_a_canonical_video(string url)
    {
        var id = YoutubeVideoUrl.ParseId(url);
        id.Should().Be("dQw4w9WgXcQ");
        YoutubeVideoUrl.CanonicalUrl(id!).Should().Be("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dQw4w9WgXcQ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://127.0.0.1/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.evil.test/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com@evil.test/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://evil.test@youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com:8443/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/redirect?q=https://example.com")]
    [InlineData("https://youtube.com/watch?v=too-short")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&v=abcdefghijk")]
    [InlineData("https://youtube.com/playlist?list=dQw4w9WgXcQ")]
    [InlineData("https://youtube-nocookie.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ/extra")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQx")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXc%3C")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ%0A")]
    public void Arbitrary_hosts_non_video_urls_and_ambiguous_ids_are_rejected(string? url) =>
        YoutubeVideoUrl.ParseId(url).Should().BeNull();

    [Fact]
    public void Overlong_youtube_urls_are_rejected() =>
        YoutubeVideoUrl.ParseId("https://youtu.be/dQw4w9WgXcQ?tracking=" + new string('x', 2048)).Should().BeNull();

    [Theory]
    [InlineData("mp4", "video/mp4")]
    [InlineData("MP4", "VIDEO/MP4")]
    [InlineData("mov", "video/quicktime")]
    [InlineData("webm", "video/webm")]
    public async Task Supported_containers_are_validated_and_rewound(string extension, string type)
    {
        var bytes = ExerciseVideoTestSteps.Mp4;
        if (extension == "mov") "qt  "u8.CopyTo(bytes.AsSpan(8));
        if (extension == "webm") bytes = [0x1a, 0x45, 0xdf, 0xa3, 0x87, 0x42, 0x82, 0x84, 119, 101, 98, 109];
        await using var input = new MemoryStream(bytes);
        var result = await ExerciseVideoFile.ValidateAsync(input, bytes.Length, "video." + extension, type, default);
        await using var file = result.Content;
        file.Position.Should().Be(0);
        file.Length.Should().Be(bytes.Length);
        result.Extension.Should().Be("." + extension.ToLowerInvariant());
        result.ContentType.Should().Be(type.ToLowerInvariant());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(104857601)]
    public async Task Invalid_declared_lengths_are_rejected_before_reading(long length)
    {
        await using var input = new MemoryStream(ExerciseVideoTestSteps.Mp4);
        var act = () => ExerciseVideoFile.ValidateAsync(input, length, "file.mp4", "video/mp4", default);
        await act.Should().ThrowAsync<ValidationException>();
        input.Position.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Actual_length_must_equal_the_declared_length(long declared)
    {
        await using var input = new MemoryStream(ExerciseVideoTestSteps.Mp4);
        var act = () => ExerciseVideoFile.ValidateAsync(input, declared, "file.mp4", "video/mp4", default);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(104857600, true)]
    [InlineData(104857601, false)]
    public async Task Actual_stream_length_enforces_the_100_MiB_boundary(long actual, bool valid)
    {
        await using var input = new GeneratedVideoStream(actual);
        var act = () => ExerciseVideoFile.ValidateAsync(input, ExerciseVideoFile.MaxBytes, "file.mp4", "video/mp4", default);
        if (valid)
        {
            var result = await act();
            await using var file = result.Content;
            file.Length.Should().Be(actual);
        }
        else await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class GeneratedVideoStream(long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var size = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, size);
            if (_position == 0) ExerciseVideoTestSteps.Mp4.CopyTo(buffer, offset);
            _position += size;
            return size;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
