using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure.Persistence;
using PhysioFlow.Infrastructure.Persistence.Seeds;

namespace PhysioFlow.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string FrontendBaseUrl = "https://app.physioflow.test";

    private readonly string _databaseName = Guid.NewGuid().ToString();

    public FakeEmailService Email { get; } = new();

    // TestServer nie ustawia IP klienta, więc wszystkie testy trafiają do jednej partycji limitera.
    protected virtual int PatientRegistrationPermitLimit => 10_000;
    protected virtual int ConfirmationResendPermitLimit => 10_000;
    protected virtual int ConfirmationEmailCooldownSeconds => 0;

    protected virtual void ConfigureTestServices(IServiceCollection services) { }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Te ustawienia muszą być dostępne już podczas wykonywania Program.cs.
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=fake;Database=fake;Username=fake;Password=fake",

                    ["Jwt:Key"] =
                        "test_jwt_key_super_secret_minimum_32_characters_long_for_HS256",

                    ["Jwt:Issuer"] = "PhysioFlow.Api.Tests",
                    ["Jwt:Audience"] = "PhysioFlow.Client.Tests",
                    ["Jwt:AccessTokenExpiryMinutes"] = "60",

                    ["Frontend:BaseUrl"] = FrontendBaseUrl + "/",

                    ["RateLimiting:PatientRegistration:PermitLimit"] =
                        PatientRegistrationPermitLimit.ToString(),
                    ["RateLimiting:EmailConfirmation:PermitLimit"] = "10000",
                    ["RateLimiting:ConfirmationResend:PermitLimit"] =
                        ConfirmationResendPermitLimit.ToString(),

                    ["ConfirmationEmail:CooldownSeconds"] =
                        ConfirmationEmailCooldownSeconds.ToString(),
                });
        });

        var host = base.CreateHost(builder);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

            // Przygotowanie danych referencyjnych wyłącznie dla testów.
            if (!db.AnatomicalRegionInfos.Any())
            {
                db.AnatomicalRegionInfos.AddRange(
                AnatomicalRegionInfoSeed.Create());

                db.SaveChangesAsync().GetAwaiter().GetResult();
            }
        }

        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Usuwamy również konfigurację dostawcy PostgreSQL,
            // aby nie została połączona z dostawcą InMemory.
            services.RemoveAll<
                IDbContextOptionsConfiguration<ApplicationDbContext>>();

            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);

            ConfigureTestServices(services);
        });
    }
}