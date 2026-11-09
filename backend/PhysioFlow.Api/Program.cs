using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PhysioFlow.Api.Hubs;
using PhysioFlow.Api.Middleware;
using PhysioFlow.Api.Options;
using PhysioFlow.Api.Services;
using PhysioFlow.Application;
using PhysioFlow.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Logging
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

// --- Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Controllers & Swagger
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Pozwala wysyłać enumy jako stringi (np. "Front", "AcutePain")
        // zamiast liczb (0, 1) - frontend wtedy używa zrozumiałych nazw.
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        opts.JsonSerializerOptions.Converters.Add(new PhysioFlow.Api.Json.TimeOnlyJsonConverter());
        opts.JsonSerializerOptions.Converters.Add(new PhysioFlow.Api.Json.NullableTimeOnlyJsonConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PhysioFlow API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Wklej JWT: Bearer {token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// --- JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        opts.Events = new JwtBearerEvents
        {
            // SignalR wymaga tokena z query stringu (bo WebSocket nie ma nagłówków)
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
            // Odrzucamy tokeny dla userów, którzy już nie istnieją w bazie
            // (np. po wipe DB / migracji - frontend dostanie 401 i zrobi cleanup tokena)
            OnTokenValidated = async ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                try
                {
                    var userIdClaim = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    logger.LogDebug("OnTokenValidated: nameid claim = {Claim}", userIdClaim);

                    if (!Guid.TryParse(userIdClaim, out var userId))
                    {
                        logger.LogWarning("OnTokenValidated: invalid/missing nameid claim, rejecting");
                        ctx.Fail("Invalid user id claim");
                        return;
                    }

                    var userManager = ctx.HttpContext.RequestServices
                        .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PhysioFlow.Infrastructure.Identity.ApplicationUser>>();
                    var user = await userManager.FindByIdAsync(userId.ToString());
                    if (user is null)
                    {
                        logger.LogWarning("OnTokenValidated: user {UserId} not found, rejecting", userId);
                        ctx.Fail("User no longer exists");
                        return;
                    }
                    if (!user.IsActive)
                    {
                        logger.LogWarning("OnTokenValidated: user {UserId} inactive, rejecting", userId);
                        ctx.Fail("User inactive");
                        return;
                    }
                    // Tokeny wydane przed wprowadzeniem blokady nie mogą jej omijać.
                    var roles = ctx.Principal!.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value);
                    if (PhysioFlow.Infrastructure.Identity.PatientAccountRules.RequiresEmailConfirmation(user, roles))
                    {
                        logger.LogWarning("OnTokenValidated: patient {UserId} has unconfirmed email, rejecting", userId);
                        ctx.Fail("Email not confirmed");
                        return;
                    }
                    logger.LogDebug("OnTokenValidated: user {UserId} OK", userId);
                }
                catch (Exception ex)
                {
                    // Nie chcemy aby jakikolwiek błąd w lookupie wywalał całe auth.
                    logger.LogError(ex, "OnTokenValidated: unexpected error, allowing request through");
                }
            }
        };
    });

builder.Services.AddAuthorization();

// --- API-level validators & options
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<ConfirmationEmailOptions>(
    builder.Configuration.GetSection(ConfirmationEmailOptions.SectionName));
builder.Services.Configure<PhysioFlow.Application.Features.Cooperation.CooperationRequestOptions>(
    builder.Configuration.GetSection(PhysioFlow.Application.Features.Cooperation.CooperationRequestOptions.SectionName));
foreach (var (policy, section) in RateLimitPolicies.ConfigSections)
    builder.Services.Configure<FixedWindowRateLimitOptions>(policy, builder.Configuration.GetSection(section));

builder.Services.AddMemoryCache();
builder.Services.AddScoped<PatientEmailConfirmationService>();

// --- Rate limiting
// Partycja po IP klienta. Za reverse proxy trzeba dodać UseForwardedHeaders, inaczej wszyscy dzielą IP proxy.
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too many requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Zbyt wiele prób. Spróbuj ponownie później."
        }, ct);
    };

    foreach (var policy in RateLimitPolicies.ConfigSections.Keys)
    {
        opts.AddPolicy(policy, http =>
        {
            // Opcje czytane przy pierwszym żądaniu z danego IP, więc testy mogą je nadpisać konfiguracją hosta.
            var limits = http.RequestServices
                .GetRequiredService<IOptionsMonitor<FixedWindowRateLimitOptions>>().Get(policy);
            return RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = TimeSpan.FromMinutes(limits.WindowMinutes),
                    QueueLimit = 0,
                });
        });
    }
});

// --- SignalR
builder.Services.AddSignalR();

// --- CORS (Next.js frontend)
builder.Services.AddCors(opts =>
{
    opts.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]?.Split(',')
            ?? new[] { "http://localhost:3000" };
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();  // wymagane dla SignalR
    });
});

var app = builder.Build();

// --- Seed ról przy starcie aplikacji
// (Userzy tworzą się przez rejestrację; tu robimy tylko role + backfill brakujących TherapistProfile.)
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider
        .GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<PhysioFlow.Infrastructure.Identity.ApplicationRole>>();
    foreach (var role in new[] { "Therapist", "Patient", "Admin" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new PhysioFlow.Infrastructure.Identity.ApplicationRole { Name = role });
    }

    // Backfill: każdy user w roli "Therapist" musi mieć TherapistProfile.
    // Bez tego GetPatientListQuery (i podobne) rzucają NotFoundException("TherapistProfile").
    var userManager = scope.ServiceProvider
        .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PhysioFlow.Infrastructure.Identity.ApplicationUser>>();
    var db = scope.ServiceProvider
        .GetRequiredService<PhysioFlow.Application.Common.Interfaces.IApplicationDbContext>();

    var therapistUsers = await userManager.GetUsersInRoleAsync("Therapist");
    var existingProfileUserIds = new HashSet<Guid>(
        db.Therapists.Select(t => t.UserId).ToList());

    foreach (var user in therapistUsers.Where(u => !existingProfileUserIds.Contains(u.Id)))
    {
        var localPart = (user.Email ?? user.UserName ?? "Therapist").Split('@')[0];
        db.Therapists.Add(new PhysioFlow.Domain.Entities.TherapistProfile
        {
            UserId = user.Id,
            FirstName = localPart,
            LastName = PhysioFlow.Domain.Entities.TherapistProfile.PlaceholderLastName,
        });
    }
    await db.SaveChangesAsync(default);
}

if (string.IsNullOrEmpty(app.Configuration["Resend:ApiKey"]))
    app.Logger.LogWarning("Resend:ApiKey is not configured - emails (confirmation, notifications) will not be sent.");
else if (app.Configuration["Resend:FromAddress"]?.Contains("@resend.dev", StringComparison.OrdinalIgnoreCase) == true)
    app.Logger.LogWarning(
        "Resend:FromAddress uses the resend.dev test domain - Resend delivers only to the account owner's address. " +
        "Verify your own domain in Resend before deployment.");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
