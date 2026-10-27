using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PhysioFlow.Infrastructure.Persistence;

namespace PhysioFlow.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

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
                });
        });

        return base.CreateHost(builder);
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
        });
    }
}