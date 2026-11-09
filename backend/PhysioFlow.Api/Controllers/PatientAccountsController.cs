using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhysioFlow.Api.Options;
using PhysioFlow.Api.Services;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Persistence.Configurations;

namespace PhysioFlow.Api.Controllers;

// Pola nullable, żeby brakujące pole przeszło do walidatora zamiast do automatycznej odpowiedzi 400 z [ApiController],
// która ma inny format błędów. Roli celowo nie ma - nieznane pola JSON są ignorowane.
public record RegisterPatientAccountRequest(string? FirstName, string? LastName, string? Email, string? Password);
public record RegisterPatientAccountResponse(Guid UserId, string Email, bool EmailConfirmationRequired);

public record ConfirmPatientEmailRequest(string? UserId, string? Token);
public record ConfirmPatientEmailResponse(bool EmailConfirmed);

public record ResendConfirmationRequest(string? Email);
public record ResendConfirmationResponse(string Message);

/// <summary>Dane konta zalogowanego pacjenta. Id to identyfikator konta, nie karty pacjenta.</summary>
public record PatientAccountDto(Guid Id, string? FirstName, string? LastName, string Email, bool EmailConfirmed);

public static class PatientAccountErrorCodes
{
    public const string InvalidOrExpiredLink = "invalid_or_expired_link";
    public const string EmailAlreadyConfirmed = "email_already_confirmed";
    public const string EmailConfirmationRequired = "email_confirmation_required";
}

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

public class ResendConfirmationRequestValidator : AbstractValidator<ResendConfirmationRequest>
{
    public ResendConfirmationRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Email)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Adres e-mail jest wymagany.")
            .Must(v => v is null || v.Trim().Length <= 256).WithMessage("Adres e-mail jest za długi.")
            .EmailAddress().WithMessage("Nieprawidłowy adres e-mail.")
            .OverridePropertyName("email");
    }
}

/// <summary>Samodzielna rejestracja pacjenta i potwierdzanie jego adresu e-mail.</summary>
[Route("api/patient-accounts")]
public class PatientAccountsController : ApiControllerBase
{
    private const string ResendMessage =
        "Jeśli konto wymaga potwierdzenia adresu, wysłaliśmy nową wiadomość. Sprawdź skrzynkę, także folder spam.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PatientEmailConfirmationService _confirmation;
    private readonly IValidator<RegisterPatientAccountRequest> _registerValidator;
    private readonly IValidator<ResendConfirmationRequest> _resendValidator;
    private readonly ILogger<PatientAccountsController> _logger;

    public PatientAccountsController(
        UserManager<ApplicationUser> userManager,
        PatientEmailConfirmationService confirmation,
        IValidator<RegisterPatientAccountRequest> registerValidator,
        IValidator<ResendConfirmationRequest> resendValidator,
        ILogger<PatientAccountsController> logger)
    {
        _userManager = userManager;
        _confirmation = confirmation;
        _registerValidator = registerValidator;
        _resendValidator = resendValidator;
        _logger = logger;
    }

    /// <summary>
    /// Zakłada konto z rolą Patient i niepotwierdzonym e-mailem, po czym wysyła link do /confirm-email.
    /// Nie zwraca tokenów sesji. Gdy wysyłka się nie uda, konto jest usuwane i zwracane jest 503.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PatientRegistration)]
    [ProducesResponseType(typeof(RegisterPatientAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Register([FromBody] RegisterPatientAccountRequest req, CancellationToken ct)
    {
        await ValidateRegistrationAsync(req, ct);

        var email = req.Email!.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
            throw new ConflictException("Konto z tym adresem e-mail już istnieje.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            FirstName = req.FirstName!.Trim(),
            LastName = req.LastName!.Trim(),
        };

        var createResult = await _userManager.CreateAsync(user, req.Password!);
        if (!createResult.Succeeded)
            throw ToException(createResult.Errors);

        var roleResult = await _userManager.AddToRoleAsync(user, PatientAccountRules.PatientRole);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException(
                $"Nie udało się nadać roli {PatientAccountRules.PatientRole}: {string.Join(", ", roleResult.Errors.Select(e => e.Code))}");
        }

        try
        {
            await _confirmation.SendAsync(user, ct);
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

    /// <summary>
    /// Potwierdza adres na podstawie userId i tokenu z linku. Nie tworzy sesji - po sukcesie pacjent loguje się normalnie.
    /// Nieprawidłowy i wygasły link dają ten sam błąd; status "już potwierdzony" jest ujawniany tylko przy ważnym tokenie.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.EmailConfirmation)]
    [ProducesResponseType(typeof(ConfirmPatientEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmPatientEmailRequest req)
    {
        if (!Guid.TryParse(req.UserId, out var userId) || string.IsNullOrWhiteSpace(req.Token))
            return InvalidLink();

        var token = PatientEmailConfirmationService.DecodeToken(req.Token);
        if (token is null)
            return InvalidLink();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !await _userManager.IsInRoleAsync(user, PatientAccountRules.PatientRole))
            return InvalidLink();

        var tokenIsValid = await _userManager.VerifyUserTokenAsync(
            user,
            _userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<ApplicationUser>.ConfirmEmailTokenPurpose,
            token);
        if (!tokenIsValid)
            return InvalidLink();

        if (user.EmailConfirmed)
        {
            return ProblemWithCode(StatusCodes.Status409Conflict, "Email already confirmed",
                PatientAccountErrorCodes.EmailAlreadyConfirmed,
                "Adres e-mail został już potwierdzony. Możesz się zalogować.");
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
            return InvalidLink();

        return Ok(new ConfirmPatientEmailResponse(EmailConfirmed: true));
    }

    /// <summary>
    /// Wysyła nowy link potwierdzający. Odpowiedź jest zawsze taka sama, niezależnie od tego,
    /// czy konto istnieje, jest już potwierdzone, jest objęte odstępem między wysyłkami albo wysyłka się nie udała.
    /// </summary>
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.ConfirmationResend)]
    [ProducesResponseType(typeof(ResendConfirmationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest req, CancellationToken ct)
    {
        await _resendValidator.ValidateAndThrowAsync(req, ct);

        var user = await _userManager.FindByEmailAsync(req.Email!.Trim());
        if (user is not null
            && !user.EmailConfirmed
            && await _userManager.IsInRoleAsync(user, PatientAccountRules.PatientRole)
            && !_confirmation.IsOnCooldown(user))
        {
            try
            {
                await _confirmation.SendAsync(user, ct);
            }
            catch (EmailDeliveryException)
            {
                // Zwrócenie błędu ujawniłoby, że konto istnieje. Odpowiedź nie twierdzi, że wiadomość wysłano.
                _logger.LogWarning("Resending confirmation email failed for user {UserId}", user.Id);
            }
        }

        return Accepted(new ResendConfirmationResponse(ResendMessage));
    }

    /// <summary>Dane konta zalogowanego pacjenta. Konto ustalane z sesji; działa także bez powiązanej karty i terapeuty.</summary>
    [HttpGet("me")]
    [Authorize(Roles = PatientAccountRules.PatientRole)]
    [ProducesResponseType(typeof(PatientAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMe()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
            return Unauthorized();

        // Walidacja JWT już odrzuca takie sesje; to zabezpieczenie na wypadek błędu w tamtym sprawdzeniu.
        if (!user.EmailConfirmed)
        {
            return ProblemWithCode(StatusCodes.Status403Forbidden, "Email confirmation required",
                PatientAccountErrorCodes.EmailConfirmationRequired,
                "Potwierdź adres e-mail, aby korzystać z portalu pacjenta.");
        }

        return Ok(new PatientAccountDto(user.Id, user.FirstName, user.LastName, user.Email!, user.EmailConfirmed));
    }

    private ObjectResult InvalidLink() => ProblemWithCode(StatusCodes.Status400BadRequest, "Invalid confirmation link",
        PatientAccountErrorCodes.InvalidOrExpiredLink,
        "Link potwierdzający jest nieprawidłowy lub wygasł. Poproś o nową wiadomość.");

    /// <summary>Łączy błędy pól z polityką hasła Identity, żeby klient dostał wszystkie błędy w jednej odpowiedzi.</summary>
    private async Task ValidateRegistrationAsync(RegisterPatientAccountRequest req, CancellationToken ct)
    {
        var failures = (await _registerValidator.ValidateAsync(req, ct)).Errors;

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
