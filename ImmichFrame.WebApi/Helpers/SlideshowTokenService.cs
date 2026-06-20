using Microsoft.AspNetCore.DataProtection;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Issues and validates the short-lived access token a viewer holds (in a path-scoped cookie) after
/// resolving/unlocking a slideshow link. The token is a signed, time-limited payload bound to one
/// slug, so it only grants access to that link's content.
/// </summary>
public class SlideshowTokenService
{
    private readonly ITimeLimitedDataProtector _protector;

    public SlideshowTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ImmichFrame.SlideshowLink.v1").ToTimeLimitedDataProtector();
    }

    public string Issue(string slug) => _protector.Protect(slug, TimeSpan.FromDays(30));

    public bool Validate(string? token, string slug)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            return string.Equals(_protector.Unprotect(token), slug, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
