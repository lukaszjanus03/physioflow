using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhysioFlow.Api.Options;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Api.Controllers;

public record ConfirmTwoFactorRequest([StringLength(6)] string? Code)
{
    public override string ToString() => "ConfirmTwoFactorRequest [REDACTED]";
}
public record VerifyTwoFactorRequest(
    [StringLength(128)] string? ChallengeToken,
    [StringLength(6)] string? Code = null,
    [StringLength(64)] string? RecoveryCode = null)
{
    public override string ToString() => "VerifyTwoFactorRequest [REDACTED]";
}
public record ReauthenticateTwoFactorRequest(
    [StringLength(1024)] string? Password,
    [StringLength(6)] string? Code = null,
    [StringLength(64)] string? RecoveryCode = null)
{
    public override string ToString() => "ReauthenticateTwoFactorRequest [REDACTED]";
}

public record RecoveryCodesResponse(string[] RecoveryCodes)
{
    public override string ToString() => "RecoveryCodesResponse [REDACTED]";
}

[ApiController]
[Route("api/auth/2fa")]
[Authorize(Roles = "Patient,Therapist")]
[EnableRateLimiting(RateLimitPolicies.TwoFactor)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TwoFactorController(UserManager<ApplicationUser> users, TwoFactorService twoFactor) : ControllerBase
{
    private async Task<ApplicationUser> CurrentUser() =>
        await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
        ?? throw new UnauthorizedAccessException();

    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await twoFactor.Status(await CurrentUser()));

    [HttpPost("setup")]
    public async Task<IActionResult> Setup() => Ok(await twoFactor.StartSetup(await CurrentUser()));

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmTwoFactorRequest request) =>
        Ok(new RecoveryCodesResponse(await twoFactor.ConfirmSetup(await CurrentUser(), request.Code)));

    [HttpPost("prompt/dismiss")]
    public async Task<IActionResult> Dismiss()
    {
        await twoFactor.DismissPrompt(await CurrentUser());
        return NoContent();
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify(VerifyTwoFactorRequest request) =>
        Ok(await twoFactor.CompleteLogin(request.ChallengeToken, request.Code, request.RecoveryCode));

    [HttpPost("recovery-codes/regenerate")]
    public async Task<IActionResult> Regenerate(ReauthenticateTwoFactorRequest request) =>
        Ok(new RecoveryCodesResponse(await twoFactor.RegenerateRecoveryCodes(await CurrentUser(),
            request.Password, request.Code, request.RecoveryCode)));

    [HttpPost("disable")]
    public async Task<IActionResult> Disable(ReauthenticateTwoFactorRequest request)
    {
        await twoFactor.Disable(await CurrentUser(), request.Password, request.Code, request.RecoveryCode);
        return NoContent();
    }
}
