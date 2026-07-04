using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Encrypts Immich API keys at rest using ASP.NET Core Data Protection. The protection keys are
/// persisted to disk (see Program.cs) so ciphertext stays decryptable across restarts.
/// </summary>
public class ApiKeyProtector
{
    // Data Protection payloads are base64url and always begin with this magic prefix.
    private const string ProtectedPayloadPrefix = "CfDJ8";

    private readonly IDataProtector _protector;
    private readonly ILogger<ApiKeyProtector> _logger;

    public ApiKeyProtector(IDataProtectionProvider provider, ILogger<ApiKeyProtector> logger)
    {
        _protector = provider.CreateProtector("ImmichFrame.AccountApiKey.v1");
        _logger = logger;
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
        catch when (!value.StartsWith(ProtectedPayloadPrefix, StringComparison.Ordinal))
        {
            // Not a Data Protection payload (e.g. a legacy plaintext key imported before encryption
            // existed): return it unchanged so it keeps working.
            return value;
        }
        catch (Exception ex)
        {
            // The value looks like a protected payload but can't be decrypted. The overwhelmingly
            // likely cause is a lost/regenerated data-protection key ring (config volume recreated,
            // keys directory not mounted, or an application-name mismatch). Surface it loudly instead
            // of silently handing the raw ciphertext to Immich as an API key.
            _logger.LogError(ex,
                "Failed to decrypt a stored API key. The data-protection key ring is likely missing or was regenerated; the affected account(s) will fail to authenticate until the key ring is restored or the key is re-entered.");
            return value;
        }
    }
}
