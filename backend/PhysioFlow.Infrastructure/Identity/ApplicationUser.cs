using Microsoft.AspNetCore.Identity;

namespace PhysioFlow.Infrastructure.Identity;

/// <summary>
/// Rozszerzenie IdentityUser o Guid jako klucz - spójny z resztą encji domenowych.
/// Role: "Therapist", "Patient", "Admin".
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
}

public class ApplicationRole : IdentityRole<Guid> { }
