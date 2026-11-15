using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Storage;

public class R2ExerciseVideoStorage : R2StorageService, IExerciseVideoStorage
{
    public R2ExerciseVideoStorage(IConfiguration config, IAmazonS3 s3, ILogger<R2StorageService> logger)
        : base(config["R2:VideoBucketName"] ?? throw new InvalidOperationException("R2:VideoBucketName not configured"), s3, logger)
    {
    }
}
