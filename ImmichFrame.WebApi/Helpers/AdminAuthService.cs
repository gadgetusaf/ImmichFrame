using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Identity;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Creates and validates application users using ASP.NET Core's <see cref="IPasswordHasher{TUser}"/>.
/// Scoped — it depends on the request-scoped <see cref="AppDbContext"/>.
/// </summary>
public class AdminAuthService(AppDbContext db, IPasswordHasher<UserEntity> hasher)
{
    public bool AnyAdminExists() => db.Users.Any(u => u.Role == UserRoles.Admin);

    public UserEntity CreateAdmin(string username, string password)
    {
        var user = new UserEntity { Username = username, Role = UserRoles.Admin };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>Returns the user when the credentials are valid, otherwise null.</summary>
    public UserEntity? ValidateCredentials(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = db.Users.FirstOrDefault(u => u.Username == username);
        if (user is null)
        {
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            db.SaveChanges();
        }

        return user;
    }
}
