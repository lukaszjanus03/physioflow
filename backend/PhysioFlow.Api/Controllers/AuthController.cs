using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Services;

namespace PhysioFlow.Api.Controllers;

public record RegisterRequest(string Email, string Password, string FirstName, string LastName);
public record LoginRequest(string Email, string Password);
public record RefreshTokenRequest(string RefreshToken);
public record TokenResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);

[AllowAnonymous]
public class AuthController : ApiControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenService _jwt;
    private readonly IApplicationDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenService jwt,
        IApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwt = jwt;
        _db = db;
    }

    /// <summary>Rejestracja nowego terapeuty. Pacjent dostaje konto przez zaproszenie.</summary>
    [HttpPost("register")]
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
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user is null)
            return Unauthorized(new { error = "Nieprawidłowy email lub hasło" });

        var result = await _signInManager.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { error = "Nieprawidłowy email lub hasło" });

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwt.GenerateAccessToken(user, roles);
        var refreshToken = _jwt.GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(user);

        return Ok(new TokenResponse(accessToken, refreshToken, DateTime.UtcNow.AddHours(1)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req)
    {
        var user = _userManager.Users.FirstOrDefault(u => u.RefreshToken == req.RefreshToken);
        if (user is null || user.RefreshTokenExpiresAt < DateTime.UtcNow)
            return Unauthorized(new { error = "Nieprawidłowy refresh token" });

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwt.GenerateAccessToken(user, roles);
        var newRefresh = _jwt.GenerateRefreshToken();

        user.RefreshToken = newRefresh;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);
        await _userManager.UpdateAsync(user);

        return Ok(new TokenResponse(accessToken, newRefresh, DateTime.UtcNow.AddHours(1)));
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
}
