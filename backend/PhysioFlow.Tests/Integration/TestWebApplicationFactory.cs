using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Persistence;

namespace PhysioFlow.Tests.Integration;

/// <summary>
/// Custom factory zamieniająca PostgreSQL na InMemory DB dla testów integracyjnych.
/// Każdy test dostaje świeżą bazę przez unikalną nazwę.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Wstrzyknij minimalną konfigurację - JWT key + fake connection (i tak podmienimy DbContext)
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=fake;Database=fake;Username=fake;Password=fake",
                ["Jwt:Key"] = "test_jwt_key_super_secret_minimum_32_characters_long_for_HS256",
                ["Jwt:Issuer"] = "PhysioFlow.Api.Tests",
                ["Jwt:Audience"] = "PhysioFlow.Client.Tests",
                ["Jwt:AccessTokenExpiryMinutes"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Usuń zarejestrowany DbContext (Npgsql)
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            // Podmień na InMemory
            services.AddDbContext<ApplicationDbContext>(opts =>
                opts.UseInMemoryDatabase(_databaseName));

            // Upewnij się, że baza istnieje + seed ról
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();

            // Seed ról przy pierwszym uruchomieniu
            var roleManager = scope.ServiceProvider
                .GetService<Microsoft.AspNetCore.Identity.RoleManager<ApplicationRole>>();
            if (roleManager is not null)
            {
                foreach (var role in new[] { "Therapist", "Patient", "Admin" })
                {
                    if (!roleManager.RoleExistsAsync(role).GetAwaiter().GetResult())
                        roleManager.CreateAsync(new ApplicationRole { Name = role }).GetAwaiter().GetResult();
                }
            }
        });
    }
}
