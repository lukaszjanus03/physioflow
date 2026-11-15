using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure;
using PhysioFlow.Infrastructure.Storage;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ExerciseVideoStorageTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("photos", false)]
    [InlineData("recordings-private", true)]
    public void Recordings_require_a_dedicated_bucket(string? bucket, bool configured)
    {
        var config = Configuration(bucket);
        var services = new ServiceCollection().AddLogging().AddInfrastructure(config);
        services.AddSingleton(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IExerciseVideoStorage>();
        if (configured) storage.Should().BeOfType<R2ExerciseVideoStorage>();
        else storage.Should().BeOfType<UnconfiguredStorageService>();
        scope.ServiceProvider.GetRequiredService<IStorageService>().Should().BeOfType<R2StorageService>();
    }

    [Fact]
    public async Task R2_uploads_to_the_private_bucket_with_compatible_signing_and_short_lived_GET_links()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());
        s3.Setup(s => s.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>())).Returns("https://signed.test");
        var storage = new R2ExerciseVideoStorage(Configuration("recordings-private"), s3.Object, NullLogger<R2StorageService>.Instance);
        await using var content = new MemoryStream([1, 2, 3]);
        var key = await storage.UploadAsync(content, "exercise-video.mp4", "video/mp4");
        s3.Verify(s => s.PutObjectAsync(It.Is<PutObjectRequest>(r => r.BucketName == "recordings-private"
            && r.Key == key && r.ContentType == "video/mp4" && r.CannedACL == null
            && r.DisablePayloadSigning == true && r.DisableDefaultChecksumValidation == true && !r.AutoCloseStream),
            It.IsAny<CancellationToken>()), Times.Once);
        (await storage.GetPresignedUrlAsync(key, TimeSpan.FromMinutes(5))).Should().Be("https://signed.test");
        s3.Verify(s => s.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(r => r.BucketName == "recordings-private"
            && r.Key == key && r.Verb == HttpVerb.GET && r.Expires > DateTime.UtcNow.AddMinutes(4)
            && r.Expires <= DateTime.UtcNow.AddMinutes(5))), Times.Once);
    }

    private static IConfiguration Configuration(string? bucket) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=fake;Database=fake;Username=fake;Password=fake",
            ["R2:AccountId"] = "test-account", ["R2:AccessKey"] = "test-key", ["R2:SecretKey"] = "test-secret",
            ["R2:BucketName"] = "photos", ["R2:VideoBucketName"] = bucket
        }).Build();
}
