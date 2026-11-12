using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure;
using PhysioFlow.Infrastructure.Storage;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class ProfileStorageConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("account-without-credentials")]
    public async Task Absent_or_incomplete_storage_config_should_use_sanitized_fallback(string? account)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=fake;Database=fake;Username=fake;Password=fake",
            ["R2:AccountId"] = account,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageService>();
        storage.Should().BeOfType<UnconfiguredStorageService>();
        var act = () => storage.UploadAsync(new MemoryStream([1]), "photo.webp", "image/webp");
        await act.Should().ThrowAsync<StorageUnavailableException>();
    }
}
