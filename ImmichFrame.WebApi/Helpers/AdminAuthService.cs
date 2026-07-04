using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Identity;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Creates and validates application users (admins and viewers) using ASP.NET Core's
/// <see cref="IPasswordHasher{TUser}"/>. Scoped — depends on the request-scoped <see cref="AppDbContext"/>.
/// </summary>
public class AdminAuthService(AppDbContext db, IPasswordHasher<UserEntity> hasher)
{
    // Fixed decoy used to equalize login timing when no matching user exists (username-enumeration defence).
    private static readonly UserEntity DecoyUser = new() { Username = string.Empty, Role = string.Empty };
    private static readonly string DecoyPasswordHash =
        new PasswordHasher<UserEntity>().HashPassword(DecoyUser, "immichframe-login-decoy");

    // Short-lived snapshot of active viewer usernames. ViewerAuth links re-check the viewer on every
    // content request (~25+ per page load), so serving them from an in-memory set for a few seconds
    // avoids a SQLite round-trip per request while keeping revocation near-immediate.
    private static readonly TimeSpan ActiveViewersTtl = TimeSpan.FromSeconds(5);
    private static volatile HashSet<string>? _activeViewers;
    private static DateTime _activeViewersExpiry;
    private static readonly object _activeViewersLock = new();

    public bool AnyAdminExists() => db.Users.Any(u => u.Role == UserRoles.Admin);

    public UserEntity CreateAdmin(string username, string password) => CreateUser(username, password, UserRoles.Admin);
    public UserEntity CreateViewer(string username, string password) => CreateUser(username, password, UserRoles.Viewer);

    public bool UsernameExists(string username) => db.Users.Any(u => u.Username == Normalize(username));

    /// <summary>
    /// True iff a viewer account with this (normalized) username still exists. Used to re-verify a
    /// ViewerAuth link's holder on every content request, so a logged-out or deleted viewer is denied
    /// immediately rather than coasting on a previously-issued cookie.
    /// </summary>
    public bool ViewerIsActive(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var normalized = Normalize(username);
        return GetActiveViewers().Contains(normalized);
    }

    private HashSet<string> GetActiveViewers()
    {
        var cached = _activeViewers;
        if (cached is not null && DateTime.UtcNow < _activeViewersExpiry)
            return cached;

        lock (_activeViewersLock)
        {
            if (_activeViewers is not null && DateTime.UtcNow < _activeViewersExpiry)
                return _activeViewers;

            var viewers = db.Users
                .Where(u => u.Role == UserRoles.Viewer)
                .Select(u => u.Username)
                .ToHashSet();
            _activeViewers = viewers;
            _activeViewersExpiry = DateTime.UtcNow + ActiveViewersTtl;
            return viewers;
        }
    }

    public IEnumerable<UserEntity> ListViewers() =>
        db.Users.Where(u => u.Role == UserRoles.Viewer).OrderBy(u => u.Username).ToList();

    public bool DeleteViewer(Guid id)
    {
        var user = db.Users.FirstOrDefault(u => u.Id == id && u.Role == UserRoles.Viewer);
        if (user is null) return false;
        db.Users.Remove(user);
        db.SaveChanges();
        return true;
    }

    /// <summary>Validates any user's credentials (callers check the role for their context).</summary>
    public UserEntity? ValidateCredentials(string username, string password) => Validate(username, password, role: null);

    /// <summary>Validates credentials for a viewer account specifically.</summary>
    public UserEntity? ValidateViewer(string username, string password) => Validate(username, password, UserRoles.Viewer);

    private UserEntity CreateUser(string username, string password, string role)
    {
        // Store usernames lowercased so the unique index and login are effectively case-insensitive.
        var user = new UserEntity { Username = Normalize(username), Role = role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private UserEntity? Validate(string username, string password, string? role)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        var normalized = Normalize(username);
        var user = db.Users.FirstOrDefault(u => u.Username == normalized);
        if (user is null || (role is not null && user.Role != role))
        {
            // Run a dummy verification so the response time doesn't reveal whether the account exists.
            hasher.VerifyHashedPassword(DecoyUser, DecoyPasswordHash, password);
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            db.SaveChanges();
        }

        return user;
    }

    /// <summary>Lowercases a username so uniqueness and login are case-insensitive (SQLite is BINARY by default).</summary>
    private static string Normalize(string username) => (username ?? string.Empty).Trim().ToLowerInvariant();
}
