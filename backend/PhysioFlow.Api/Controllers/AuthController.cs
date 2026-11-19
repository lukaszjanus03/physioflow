using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhysioFlow.Api.Options;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Api.Controllers;

public record RegisterRequest(string Email, string Password, string FirstName, string LastName);
public record LoginRequest(string Email, string Password)
{
    public override string ToString() => "LoginRequest [REDACTED]";
}
public record RefreshTokenRequest(string RefreshToken)
{
    public override string ToString() => "RefreshTokenRequest [REDACTED]";
}
public record TokenResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, bool ShouldPromptTwoFactor = false)
{
    public override string ToString() => "TokenResponse [REDACTED]";
}

[Authorize]
public class AuthController : ApiControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly TwoFactorService _twoFactor;
    private readonly IApplicationDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        TwoFactorService twoFactor,
        IApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _twoFactor = twoFactor;
        _db = db;
    }

    /// <summary>Rejestracja nowego terapeuty. Pacjent zakłada konto przez /api/patient-accounts/register.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var user = new ApplicationUser { UserName = req.Email, Email = req.Email };
        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        var roleResult = await _userManager.AddToRoleAsync(user, "Therapist");
        if (!roleResult.Succeeded)
        {
            // Cleanup - nie chcemy usera bez roli
            await _userManager.DeleteAsync(user);
            return BadRequest(new { errors = roleResult.Errors.Select(e => e.Description) });
        }

        // TherapistProfile - domenowy profil powiązany 1:1 z ApplicationUser
        _db.Therapists.Add(new TherapistProfile
        {
            UserId = user.Id,
            FirstName = req.FirstName,
            LastName = req.LastName,
        });
        await _db.SaveChangesAsync();

        return Ok(new { userId = user.Id });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive)
            return Unauthorized(new { error = "Nieprawidłowy email lub hasło" });

        var result = await _signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { error = "Nieprawidłowy email lub hasło" });

        var roles = await _userManager.GetRolesAsync(user);
        if (PatientAccountRules.RequiresEmailConfirmation(user, roles))
            return EmailConfirmationRequired();

        if (user.TwoFactorEnabled)
            return Ok(await _twoFactor.BeginChallenge(user));

        return Ok(await _twoFactor.IssueSession(user, roles));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken)) return Unauthorized();
        var user = _userManager.Users.FirstOrDefault(u => u.RefreshToken == req.RefreshToken);
        if (user is null || !user.IsActive || user.RefreshTokenExpiresAt is null || user.RefreshTokenExpiresAt <= DateTime.UtcNow)
            return Unauthorized(new { error = "Nieprawidłowy refresh token" });

        var roles = await _userManager.GetRolesAsync(user);
        if (PatientAccountRules.RequiresEmailConfirmation(user, roles))
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiresAt = null;
            await _userManager.UpdateAsync(user);
            return EmailConfirmationRequired();
        }

        return Ok(await _twoFactor.IssueSession(user, roles));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userId, out var guid))
        {
            var user = await _userManager.FindByIdAsync(guid.ToString());
            if (user is not null)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiresAt = null;
                await _userManager.UpdateAsync(user);
            }
        }
        return NoContent();
    }

    private ObjectResult EmailConfirmationRequired() => ProblemWithCode(
        StatusCodes.Status403Forbidden,
        "Email confirmation required",
        PatientAccountErrorCodes.EmailConfirmationRequired,
        "Potwierdź adres e-mail, aby się zalogować. Link znajdziesz w wiadomości wysłanej po rejestracji.");
}
