namespace PhysioFlow.Application.DTOs;

/// <summary>Unavailable bookmarks expose only the saved identifier and timestamp.</summary>
public record FavoriteTherapistDto(
    Guid TherapistId,
    DateTime CreatedAt,
    bool IsAvailable,
    TherapistCatalogEntryDto? Profile);
