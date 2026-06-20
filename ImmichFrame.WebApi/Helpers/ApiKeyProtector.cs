using Microsoft.AspNetCore.DataProtection;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Encrypts Immich API keys at rest using ASP.NET Core Data Protection. The protection keys are
/// persisted to disk (see Program.cs) so ciphertext stays decryptable across restarts.
/// </summary>
public class ApiKeyProtector
{
    private readonly IDataProtector _protector;

    public ApiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ImmichFrame.AccountApiKey.v1");
    }

    public string Protect(string plaintext) =>
        string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);

    /// <summary>
    /// Decrypts a stored key. Values that aren't valid protected payloads (e.g. legacy plaintext
    /// keys imported before encryption existed) are returned unchanged so they keep working.
    /// </summary>
    public string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            return _protector.Unprotect(value);
        }
        catch
        {
            return value;
        }
    }
}
