using System.Text;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using PhysioFlow.Api.Options;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Persistence.Configurations;

namespace PhysioFlow.Api.Controllers;

// Pola nullable, żeby brakujące pole przeszło do walidatora zamiast do automatycznej odpowiedzi 400 z [ApiController],
// która ma inny format błędów. Roli celowo nie ma - nieznane pola JSON są ignorowane.
public record RegisterPatientAccountRequest(string? FirstName, string? LastName, string? Email, string? Password);
public record RegisterPatientAccountResponse(Guid UserId, string Email, bool EmailConfirmationRequired);

public class RegisterPatientAccountRequestValidator : AbstractValidator<RegisterPatientAccountRequest>
{
    public RegisterPatientAccountRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.FirstName)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Imię jest wymagane.")
            .Must(v => v is null || v.Trim().Length <= ApplicationUserConfiguration.NameMaxLength)
                .WithMessage($"Imię może mieć maksymalnie {ApplicationUserConfiguration.NameMaxLength} znaków.")
            .OverridePropertyName("firstName");

        RuleFor(x => x.LastName)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Nazwisko jest wymagane.")
            .Must(v => v is null || v.Trim().Length <= ApplicationUserConfiguration.NameMaxLength)
                .WithMessage($"Nazwisko może mieć maksymalnie {ApplicationUserConfiguration.NameMaxLength} znaków.")
            .OverridePropertyName("lastName");

        RuleFor(x => x.Email)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Adres e-mail jest wymagany.")
            .Must(v => v is null || v.Trim().Length <= 256).WithMessage("Adres e-mail jest za długi.")
            .EmailAddress().WithMessage("Nieprawidłowy adres e-mail.")
            .OverridePropertyName("email");

        // Złożoność hasła sprawdza polityka Identity (DependencyInjection.AddIdentityCore).
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Hasło jest wymagane.")
            .MaximumLength(128).WithMessage("Hasło może mieć maksymalnie 128 znaków.")
            .OverridePropertyName("password");
    }
}

/// <summary>Samodzielna rejestracja pacjenta - bez zaproszenia i bez karty medycznej.</summary>
[Route("api/patient-accounts")]
public class PatientAccountsController : ApiControllerBase
{
    private const string PatientRole = "Patient";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly IValidator<RegisterPatientAccountRequest> _validator;
    private readonly FrontendOptions _frontend;

    public PatientAccountsController(
        UserManager<ApplicationUser> userManager,
        IEmailService email,
        IValidator<RegisterPatientAccountRequest> validator,
        IOptions<FrontendOptions> frontend)
    {
        _userManager = userManager;
        _email = email;
        _validator = validator;
        _frontend = frontend.Value;
    }

    /// <summary>
    /// Zakłada konto z rolą Patient i niepotwierdzonym e-mailem, po czym wysyła link do /confirm-email.
    /// Nie zwraca tokenów sesji. Gdy wysyłka się nie uda, konto jest usuwane i zwracane jest 503.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(PatientRegistrationRateLimitOptions.PolicyName)]
    [ProducesResponseType(typeof(RegisterPatientAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Register([FromBody] RegisterPatientAccountRequest req, CancellationToken ct)
    {
        await ValidateAsync(req, ct);

        var email = req.Email!.Trim();
        var firstName = req.FirstName!.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
            throw new ConflictException("Konto z tym adresem e-mail już istnieje.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            FirstName = firstName,
            LastName = req.LastName!.Trim(),
        };

        var createResult = await _userManager.CreateAsync(user, req.Password!);
        if (!createResult.Succeeded)
            throw ToException(createResult.Errors);

        var roleResult = await _userManager.AddToRoleAsync(user, PatientRole);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException(
                $"Nie udało się nadać roli {PatientRole}: {string.Join(", ", roleResult.Errors.Select(e => e.Code))}");
        }

        try
        {
            await _email.SendEmailConfirmationAsync(email, firstName, await BuildConfirmationLinkAsync(user), ct);
        }
        catch
        {
            // Bez potwierdzenia konto byłoby martwe, a ponowna rejestracja kończyłaby się 409.
            await _userManager.DeleteAsync(user);
            throw;
        }

        return StatusCode(StatusCodes.Status201Created,
            new RegisterPatientAccountResponse(user.Id, email, EmailConfirmationRequired: true));
    }

    /// <summary>Łączy błędy pól z polityką hasła Identity, żeby klient dostał wszystkie błędy w jednej odpowiedzi.</summary>
    private async Task ValidateAsync(RegisterPatientAccountRequest req, CancellationToken ct)
    {
        var failures = (await _validator.ValidateAsync(req, ct)).Errors;

        if (!string.IsNullOrEmpty(req.Password) && failures.All(f => f.PropertyName != "password"))
        {
            foreach (var passwordValidator in _userManager.PasswordValidators)
            {
                var result = await passwordValidator.ValidateAsync(_userManager, new ApplicationUser(), req.Password);
                failures.AddRange(result.Errors.Select(e => new ValidationFailure("password", TranslatePasswordError(e))));
            }
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);
    }

    private async Task<string> BuildConfirmationLinkAsync(ApplicationUser user)
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

    private Exception ToException(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        if (list.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                                 or nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new ConflictException("Konto z tym adresem e-mail już istnieje.");

        return new ValidationException(list.Select(e => e.Code switch
        {
            _ when e.Code.StartsWith("Password", StringComparison.Ordinal) =>
                new ValidationFailure("password", TranslatePasswordError(e)),
            nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) =>
                new ValidationFailure("email", "Nieprawidłowy adres e-mail."),
            _ => new ValidationFailure(string.Empty, e.Description),
        }));
    }

    private string TranslatePasswordError(IdentityError error) => error.Code switch
    {
        nameof(IdentityErrorDescriber.PasswordTooShort) =>
            $"Hasło musi mieć co najmniej {_userManager.Options.Password.RequiredLength} znaków.",
        nameof(IdentityErrorDescriber.PasswordRequiresDigit) => "Hasło musi zawierać cyfrę.",
        nameof(IdentityErrorDescriber.PasswordRequiresLower) => "Hasło musi zawierać małą literę.",
        nameof(IdentityErrorDescriber.PasswordRequiresUpper) => "Hasło musi zawierać wielką literę.",
        nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => "Hasło musi zawierać znak specjalny.",
        _ => error.Description,
    };
}
