using FluentValidation;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Commands.UpdateMyTherapistProfile;

public class TherapistMarketplaceValidator : AbstractValidator<TherapistMarketplaceDto>
{
    public TherapistMarketplaceValidator()
    {
        RuleFor(x => x.Specializations)
            .Must(v => v == null || (v.Length <= TherapistSpecializations.All.Count
                && v.All(code => code != null && TherapistSpecializations.All.ContainsKey(code))
                && v.Distinct(StringComparer.Ordinal).Count() == v.Length))
            .WithMessage("Wybierz niepowtarzające się kody z listy specjalizacji.")
            .OverridePropertyName("specializations");

        Text(x => x.Experience, TherapistProfile.DescriptionMaxLength, "experience");
        Text(x => x.Education, TherapistProfile.DescriptionMaxLength, "education");
        Text(x => x.Street, TherapistProfile.StreetMaxLength, "street");
        Text(x => x.BuildingNumber, TherapistProfile.BuildingNumberMaxLength, "buildingNumber");
        Text(x => x.OrganizationalInfo, TherapistProfile.DescriptionMaxLength, "organizationalInfo");
        Text(x => x.FirstVisitPreparation, TherapistProfile.DescriptionMaxLength, "firstVisitPreparation");

        RuleFor(x => x.PostalCode)
            .Must(v => string.IsNullOrWhiteSpace(v) || System.Text.RegularExpressions.Regex.IsMatch(v.Trim(), @"\A[0-9]{2}-[0-9]{3}\z"))
            .WithMessage("Kod pocztowy musi mieć format 00-000.")
            .OverridePropertyName("postalCode");
        RuleFor(x => x.PublicEmail).Cascade(CascadeMode.Stop)
            .Must(v => v == null || v.Trim().Length <= TherapistProfile.PublicEmailMaxLength)
            .WithMessage("Publiczny e-mail może mieć maksymalnie 254 znaki.")
            .Must(v => string.IsNullOrWhiteSpace(v) ||
                System.Net.Mail.MailAddress.TryCreate(v.Trim(), out var address)
                && address.Address == v.Trim() && address.Host.Contains('.') && !v.Any(char.IsControl))
            .WithMessage("Podaj poprawny adres e-mail gabinetu.")
            .OverridePropertyName("publicEmail");
        RuleFor(x => x.PublicPhoneNumber).Cascade(CascadeMode.Stop)
            .Must(v => v == null || v.Trim().Length <= TherapistProfile.PublicPhoneMaxLength)
            .WithMessage("Publiczny telefon może mieć maksymalnie 30 znaków.")
            .Must(v => string.IsNullOrWhiteSpace(v) ||
                System.Text.RegularExpressions.Regex.IsMatch(v.Trim(), @"\A\+?[0-9 ()-]+\z")
                && v.Count(c => c is >= '0' and <= '9') is >= 7 and <= 15)
            .WithMessage("Telefon musi zawierać 7–15 cyfr; dozwolone są +, spacje, nawiasy i myślniki.")
            .OverridePropertyName("publicPhoneNumber");
    }

    private void Text(System.Linq.Expressions.Expression<Func<TherapistMarketplaceDto, string?>> field, int max, string name) =>
        RuleFor(field).Must(v => v == null || v.Trim().Length <= max)
            .WithMessage($"Pole może mieć maksymalnie {max} znaków.").OverridePropertyName(name);
}
