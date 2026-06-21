using Microsoft.AspNetCore.DataProtection;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Issues and validates the short-lived access token a viewer holds (in a path-scoped cookie) after
/// resolving/unlocking a slideshow link. The token is a signed, time-limited payload bound to one
/// slug AND the link's current security stamp, so it only grants access to that link's content and
/// stops validating as soon as the stamp is rotated (PIN/policy change, or the link being disabled).
/// </summary>
public class SlideshowTokenService
{
    private readonly ITimeLimitedDataProtector _protector;

    public SlideshowTokenService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ImmichFrame.SlideshowLink.v1").ToTimeLimitedDataProtector();
    }

    /// <summary>Issues a token bound to both the slug and the link's current security stamp.</summary>
    public string Issue(string slug, string securityStamp) =>
        _protector.Protect($"{slug}|{securityStamp}", TimeSpan.FromDays(30));

    /// <summary>
    /// Succeeds only if the protected payload's slug matches <paramref name="slug"/> AND its embedded
    /// stamp equals <paramref name="currentStamp"/>. Fails closed on any parse/decrypt error.
    /// </summary>
    public bool Validate(string? token, string slug, string currentStamp)
    {
        if (string.IsNullOrEmpty(token)) return false;
        try
        {
            var payload = _protector.Unprotect(token);
            // Split on the first separator only: slugs are [a-z0-9-] (no '|'), so a stamp can never be
            // mistaken for part of the slug, but splitting on the first '|' keeps us robust regardless.
            var sep = payload.IndexOf('|');
            if (sep < 0) return false;

            var tokenSlug = payload[..sep];
            var tokenStamp = payload[(sep + 1)..];

            return string.Equals(tokenSlug, slug, StringComparison.Ordinal)
                && string.Equals(tokenStamp, currentStamp, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
