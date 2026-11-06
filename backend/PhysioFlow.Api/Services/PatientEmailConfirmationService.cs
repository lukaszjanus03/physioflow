using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PhysioFlow.Api.Options;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure.Identity;

namespace PhysioFlow.Api.Services;

/// <summary>Buduje link do /confirm-email i wysyła go, pilnując odstępu między wiadomościami do jednego konta.</summary>
public class PatientEmailConfirmationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;
    private readonly FrontendOptions _frontend;
    private readonly ConfirmationEmailOptions _options;

    public PatientEmailConfirmationService(
        UserManager<ApplicationUser> userManager,
        IEmailService email,
        IMemoryCache cache,
        IOptions<FrontendOptions> frontend,
        IOptions<ConfirmationEmailOptions> options)
    {
        _userManager = userManager;
        _email = email;
        _cache = cache;
        _frontend = frontend.Value;
        _options = options.Value;
    }

    // Cooldown trzymany w pamięci procesu - przy kilku instancjach API każda liczy osobno.
    public bool IsOnCooldown(ApplicationUser user) => _cache.TryGetValue(CooldownKey(user), out _);

    /// <exception cref="PhysioFlow.Application.Common.Exceptions.EmailDeliveryException">Dostawca nie przyjął wiadomości.</exception>
    public async Task SendAsync(ApplicationUser user, CancellationToken ct)
    {
        var link = await BuildLinkAsync(user);
        await _email.SendEmailConfirmationAsync(user.Email!, user.FirstName ?? string.Empty, link, ct);

        if (_options.CooldownSeconds > 0)
            _cache.Set(CooldownKey(user), true, TimeSpan.FromSeconds(_options.CooldownSeconds));
    }

    /// <summary>Odwrotność kodowania z linku. Zwraca null, gdy token nie jest poprawnym Base64Url.</summary>
    public static string? DecodeToken(string encodedToken)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<string> BuildLinkAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(_frontend.BaseUrl))
            throw new InvalidOperationException($"{FrontendOptions.SectionName}:BaseUrl is not configured.");

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        return QueryHelpers.AddQueryString(
            $"{_frontend.BaseUrl.TrimEnd('/')}/confirm-email",
            new Dictionary<string, string?>
            {
                ["userId"] = user.Id.ToString(),
                ["token"] = encodedToken,
            });
    }

    private static string CooldownKey(ApplicationUser user) => $"confirmation-email:{user.Id}";
}
