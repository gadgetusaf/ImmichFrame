namespace ImmichFrame.WebApi.Persistence.Entities;

/// <summary>
/// An application user. Phase 1 uses the <see cref="UserRoles.Admin"/> role for the configuration
/// UI; the <see cref="UserRoles.Viewer"/> role is reserved for "auth required" slideshow links.
/// </summary>
public class UserEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;

    /// <summary>Hashed with ASP.NET Core's PasswordHasher; never stored in plain text.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = UserRoles.Admin;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Viewer = "Viewer";
}
