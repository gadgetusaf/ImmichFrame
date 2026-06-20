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
    public bool AnyAdminExists() => db.Users.Any(u => u.Role == UserRoles.Admin);

    public UserEntity CreateAdmin(string username, string password) => CreateUser(username, password, UserRoles.Admin);
    public UserEntity CreateViewer(string username, string password) => CreateUser(username, password, UserRoles.Viewer);

    public bool UsernameExists(string username) => db.Users.Any(u => u.Username == username);

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
        var user = new UserEntity { Username = username, Role = role };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private UserEntity? Validate(string username, string password, string? role)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;

        var user = db.Users.FirstOrDefault(u => u.Username == username);
        if (user is null) return null;
        if (role is not null && user.Role != role) return null;

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            db.SaveChanges();
        }

        return user;
    }
}
