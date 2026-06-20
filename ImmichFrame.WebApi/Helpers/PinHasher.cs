using Microsoft.AspNetCore.Identity;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>Hashes and verifies slideshow-link PINs using ASP.NET Core's password hasher.</summary>
public class PinHasher
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string pin) => _hasher.HashPassword(Subject, pin);

    public bool Verify(string? hash, string pin) =>
        !string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(pin) &&
        _hasher.VerifyHashedPassword(Subject, hash, pin) != PasswordVerificationResult.Failed;
}
