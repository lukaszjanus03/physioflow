using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Commands.UpdateMyTherapistProfile;

// Pola nullable, żeby brakujące pole trafiło do walidatora zamiast do automatycznej odpowiedzi 400 z [ApiController].
// Właściciel profilu pochodzi z sesji, więc komenda nie ma identyfikatora.
public record UpdateMyTherapistProfileCommand(
    string? FirstName,
    string? LastName,
    string? City,
    string? PracticeName,
    string? Bio,
    bool? IsPublished,
    TherapistMarketplaceDto? Marketplace = null
) : IRequest<TherapistOwnProfileDto>;

public class UpdateMyTherapistProfileCommandValidator : AbstractValidator<UpdateMyTherapistProfileCommand>
{
    public UpdateMyTherapistProfileCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.FirstName)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Imię jest wymagane.")
            .Must(v => v!.Trim().Length <= TherapistProfile.NameMaxLength)
                .WithMessage($"Imię może mieć maksymalnie {TherapistProfile.NameMaxLength} znaków.")
            .OverridePropertyName("firstName");

        RuleFor(x => x.LastName)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("Nazwisko jest wymagane.")
            .Must(v => v!.Trim().Length <= TherapistProfile.NameMaxLength)
                .WithMessage($"Nazwisko może mieć maksymalnie {TherapistProfile.NameMaxLength} znaków.")
            .Must(v => v!.Trim() != TherapistProfile.PlaceholderLastName)
                .When(x => x.IsPublished == true, ApplyConditionTo.CurrentValidator)
                .WithMessage("Uzupełnij nazwisko przed publikacją profilu.")
            .OverridePropertyName("lastName");

        RuleFor(x => x.City)
            .Must(v => !string.IsNullOrWhiteSpace(v)).When(x => x.IsPublished == true, ApplyConditionTo.CurrentValidator)
                .WithMessage("Miasto jest wymagane do publikacji profilu.")
            .Must(v => v is null || v.Trim().Length <= TherapistProfile.CityMaxLength)
                .WithMessage($"Miasto może mieć maksymalnie {TherapistProfile.CityMaxLength} znaków.")
            .OverridePropertyName("city");

        RuleFor(x => x.PracticeName)
            .Must(v => !string.IsNullOrWhiteSpace(v)).When(x => x.IsPublished == true, ApplyConditionTo.CurrentValidator)
                .WithMessage("Nazwa gabinetu jest wymagana do publikacji profilu.")
            .Must(v => v is null || v.Trim().Length <= TherapistProfile.PracticeNameMaxLength)
                .WithMessage($"Nazwa gabinetu może mieć maksymalnie {TherapistProfile.PracticeNameMaxLength} znaków.")
            .OverridePropertyName("practiceName");

        RuleFor(x => x.Bio)
            .Must(v => v is null || v.Trim().Length <= TherapistProfile.BioMaxLength)
                .WithMessage($"Opis może mieć maksymalnie {TherapistProfile.BioMaxLength} znaków.")
            .OverridePropertyName("bio");

        RuleFor(x => x.IsPublished)
            .NotNull().WithMessage("Określ, czy profil ma być widoczny w katalogu.")
            .OverridePropertyName("isPublished");

        RuleFor(x => x.Marketplace).SetValidator(new TherapistMarketplaceValidator()!)
            .OverridePropertyName("marketplace");
    }
}

public class UpdateMyTherapistProfileCommandHandler
    : IRequestHandler<UpdateMyTherapistProfileCommand, TherapistOwnProfileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateMyTherapistProfileCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<TherapistOwnProfileDto> Handle(UpdateMyTherapistProfileCommand request, CancellationToken ct)
    {
        var therapistUserId = _currentUser.UserId
            ?? throw new ForbiddenAccessException();

        var profile = await _db.Therapists
            .FirstOrDefaultAsync(t => t.UserId == therapistUserId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), therapistUserId);

        profile.FirstName = request.FirstName!.Trim();
        profile.LastName = request.LastName!.Trim();
        profile.City = NullIfBlank(request.City);
        profile.PracticeName = NullIfBlank(request.PracticeName);
        profile.Bio = NullIfBlank(request.Bio);
        profile.IsPublished = request.IsPublished!.Value;
        // Older clients do not send this object; their saves must preserve marketplace data.
        if (request.Marketplace is { } m)
        {
            profile.Specializations = m.Specializations?.ToArray() ?? [];
            profile.Experience = NullIfBlank(m.Experience);
            profile.Education = NullIfBlank(m.Education);
            profile.Street = NullIfBlank(m.Street);
            profile.BuildingNumber = NullIfBlank(m.BuildingNumber);
            profile.PostalCode = NullIfBlank(m.PostalCode);
            profile.PublicPhoneNumber = NullIfBlank(m.PublicPhoneNumber);
            profile.PublicEmail = NullIfBlank(m.PublicEmail);
            profile.OrganizationalInfo = NullIfBlank(m.OrganizationalInfo);
            profile.FirstVisitPreparation = NullIfBlank(m.FirstVisitPreparation);
        }
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return profile.ToOwnProfileDto();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
