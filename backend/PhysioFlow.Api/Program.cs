using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PhysioFlow.Api.Hubs;
using PhysioFlow.Api.Middleware;
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
            LastName = "(uzupełnij)",
        });
    }
    await db.SaveChangesAsync(default);
}

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

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
