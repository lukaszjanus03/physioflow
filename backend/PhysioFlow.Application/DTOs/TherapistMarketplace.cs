namespace PhysioFlow.Application.DTOs;

/// <summary>Optional marketplace data. City remains on the existing profile contract.</summary>
public record TherapistMarketplaceDto(
    string[]? Specializations = null,
    string? Experience = null,
    string? Education = null,
    string? Street = null,
    string? BuildingNumber = null,
    string? PostalCode = null,
    string? PublicPhoneNumber = null,
    string? PublicEmail = null,
    string? OrganizationalInfo = null,
    string? FirstVisitPreparation = null);

public record TherapistSpecializationDto(string Code, string Label);
