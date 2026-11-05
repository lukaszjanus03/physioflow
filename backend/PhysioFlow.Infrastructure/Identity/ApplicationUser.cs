using Microsoft.AspNetCore.Identity;

namespace PhysioFlow.Infrastructure.Identity;

/// <summary>
/// Rozszerzenie IdentityUser o Guid jako klucz - spójny z resztą encji domenowych.
/// Role: "Therapist", "Patient", "Admin".
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    // Nullable, bo konta utworzone przed samodzielną rejestracją pacjenta nie mają tych danych.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
}

public class ApplicationRole : IdentityRole<Guid> { }
