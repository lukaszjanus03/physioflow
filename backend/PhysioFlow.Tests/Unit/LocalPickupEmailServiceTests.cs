using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PhysioFlow.Infrastructure.Email;
using Xunit;

namespace PhysioFlow.Tests.Unit;

public class LocalPickupEmailServiceTests
{
    [Fact]
    public async Task Restarted_local_provider_should_not_duplicate_same_reminder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "physioflow-mail-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Email:PickupDirectory"] = directory }).Build();
        try
        {
            await new LocalPickupEmailService(config).SendIdempotentAsync("patient@example.test", "Przypomnienie",
                "<p>Europe/Warsaw</p>", "appointment-reminder/visit/schedule");
            await new LocalPickupEmailService(config).SendIdempotentAsync("patient@example.test", "Przypomnienie",
                "<p>Europe/Warsaw</p>", "appointment-reminder/visit/schedule");
            var files = Directory.GetFiles(directory);
            files.Should().ContainSingle();
            (await File.ReadAllTextAsync(files[0])).Should().Contain("patient@example.test").And.Contain("Europe/Warsaw");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
