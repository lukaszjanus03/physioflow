using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure.Email;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Persistence;
using PhysioFlow.Infrastructure.Services;
using PhysioFlow.Infrastructure.Storage;
using Resend;

namespace PhysioFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // --- DbContext (PostgreSQL)
        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured");

        services.AddDbContext<ApplicationDbContext>(opts =>
        {
            opts.UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npg.EnableRetryOnFailure(3);
            });
            // snake_case wszędzie - standard Postgresowy.
            // Bez tego Npgsql robi cudzysłowy wokół identyfikatorów w PascalCase ("Pesel"),
            // co uniemożliwia m.in. surowy SQL w filter'ach indeksów bez escapowania.
            opts.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // --- Identity
        // AddIdentityCore zamiast AddIdentity - ta druga rejestruje cookie auth jako domyślne,
        // co powoduje że requesty bez cookie są redirectowane na /Account/Login zamiast
        // przejść przez JWT middleware. Dla API tokenowego użyjemy IdentityCore + ręcznie dodamy role
        // i SignInManager, który jest potrzebny w AuthController do CheckPasswordSignInAsync.
        services.AddIdentityCore<ApplicationUser>(opts =>
        {
            opts.Password.RequiredLength = 8;
            opts.Password.RequireNonAlphanumeric = false;
            opts.User.RequireUniqueEmail = true;
            opts.SignIn.RequireConfirmedEmail = false; // TODO: zmienić na true w prod
        })
        .AddRoles<ApplicationRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

        // --- Services
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IUserAccountService, UserAccountService>();

        // --- Cloudflare R2 (S3-compatible)
        var r2AccountId = config["R2:AccountId"];
        var r2AccessKey = config["R2:AccessKey"];
        var r2SecretKey = config["R2:SecretKey"];

        if (!string.IsNullOrEmpty(r2AccountId))
        {
            services.AddSingleton<IAmazonS3>(_ =>
            {
                var s3Config = new AmazonS3Config
                {
                    ServiceURL = $"https://{r2AccountId}.r2.cloudflarestorage.com",
                    ForcePathStyle = true,
                    RegionEndpoint = RegionEndpoint.USEast1  // R2 ignoruje region, ale SDK wymaga
                };
                return new AmazonS3Client(new BasicAWSCredentials(r2AccessKey, r2SecretKey), s3Config);
            });
            services.AddScoped<IStorageService, R2StorageService>();
        }

        // --- Resend (email)
        services.AddOptions<AppointmentReminderOptions>()
            .Bind(config.GetSection(AppointmentReminderOptions.SectionName))
            .Validate(o => o.PollIntervalSeconds is >= 1 and <= 3600 && o.BatchSize is >= 1 and <= 1000
                && o.MaxAttempts is >= 1 and <= 10 && o.RetryDelayMinutes is >= 1 and <= 60,
                "Nieprawidłowa konfiguracja AppointmentReminders.")
            .ValidateOnStart();
        services.AddScoped<AppointmentReminderProcessor>();
        var resendKey = config["Resend:ApiKey"];
        if (!string.IsNullOrWhiteSpace(config["Email:PickupDirectory"]))
        {
            services.AddScoped<IEmailService, LocalPickupEmailService>();
        }
        else if (!string.IsNullOrEmpty(resendKey))
        {
            services.AddHttpClient<ResendClient>();
            services.Configure<ResendClientOptions>(o => o.ApiToken = resendKey);
            services.AddTransient<IResend, ResendClient>();
            services.AddScoped<IEmailService, ResendEmailService>();
        }
        else
        {
            services.AddScoped<IEmailService, UnconfiguredEmailService>();
        }
        services.AddScoped<ICooperationNotifier, CooperationNotifier>();

        return services;
    }
}
