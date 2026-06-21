using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>
/// Public, per-link slideshow API. Resolving/unlocking a link issues a path-scoped cookie holding a
/// slug-bound token; the content endpoints mirror the global API under /slideshow/{slug}/api/... and
/// only serve that link's photos to a holder of a valid token.
/// </summary>
[ApiController]
public class SlideshowController : ControllerBase
{
    private const string CookieName = "immichframe_slideshow";

    private readonly SlideshowLinkManager _links;
    private readonly SlideshowTokenService _tokens;
    private readonly PinHasher _pin;
    private readonly AdminAuthService _auth;
    private readonly IGeneralSettings _settings;
    private readonly IWeatherService _weather;
    private readonly ICalendarService _calendar;

    public SlideshowController(SlideshowLinkManager links, SlideshowTokenService tokens, PinHasher pin,
        AdminAuthService auth, IGeneralSettings settings, IWeatherService weather, ICalendarService calendar)
    {
        _links = links;
        _tokens = tokens;
        _pin = pin;
        _auth = auth;
        _settings = settings;
        _weather = weather;
        _calendar = calendar;
    }

    public record UnlockRequest(string Pin);

    // --- resolve / unlock ---

    /// <summary>Returns whether a link needs a PIN; for public links it also issues the access cookie.</summary>
    [HttpGet("api/slideshow/{slug}")]
    public async Task<IActionResult> Resolve(string slug)
    {
        var entry = _links.Get(slug);
        if (entry is null) return NotFound(new { message = "Slideshow link not found." });

        if (entry.Link.AccessPolicy == SlideshowAccess.Pin)
            return Ok(new { name = entry.Link.Name, requiresPin = true, requiresAuth = false });

        if (entry.Link.AccessPolicy == SlideshowAccess.ViewerAuth)
        {
            var viewer = await HttpContext.AuthenticateAsync(AuthConstants.ViewerCookieScheme);
            if (!viewer.Succeeded)
                return Ok(new { name = entry.Link.Name, requiresPin = false, requiresAuth = true });
        }

        IssueCookie(entry);
        return Ok(new { name = entry.Link.Name, requiresPin = false, requiresAuth = false });
    }

    [EnableRateLimiting("auth")]
    [HttpPost("api/slideshow/{slug}/unlock")]
    public IActionResult Unlock(string slug, [FromBody] UnlockRequest request)
    {
        var entry = _links.Get(slug);
        if (entry is null) return NotFound(new { message = "Slideshow link not found." });

        // unlock serves PIN links ONLY. ViewerAuth/None links must never get a cookie here —
        // they are issued one only via Resolve, which verifies the viewer session first.
        if (entry.Link.AccessPolicy != SlideshowAccess.Pin)
            return BadRequest(new { message = "This link does not use a PIN." });

        if (!_pin.Verify(entry.Link.PinHash, request.Pin ?? string.Empty))
            return Unauthorized(new { message = "Incorrect PIN." });

        IssueCookie(entry);
        return Ok(new { name = entry.Link.Name });
    }

    // --- scoped content (mirrors the global API under /slideshow/{slug}/api/...) ---

    [HttpGet("slideshow/{slug}/api/Config")]
    public async Task<IActionResult> Config(string slug)
    {
        NoStore();
        if (await Authorize(slug) is null) return Unauthorized();
        return Ok(ClientSettingsDto.FromGeneralSettings(_settings));
    }

    [HttpGet("slideshow/{slug}/api/Weather")]
    public async Task<IActionResult> Weather(string slug)
    {
        NoStore();
        if (await Authorize(slug) is null) return Unauthorized();
        return Ok(await _weather.GetWeather());
    }

    [HttpGet("slideshow/{slug}/api/Calendar")]
    public async Task<IActionResult> Calendar(string slug)
    {
        NoStore();
        if (await Authorize(slug) is null) return Unauthorized();
        return Ok(await _calendar.GetAppointments());
    }

    [HttpGet("slideshow/{slug}/api/Asset")]
    public async Task<IActionResult> Assets(string slug)
    {
        NoStore();
        var entry = await Authorize(slug);
        if (entry is null) return Unauthorized();
        return Ok((await entry.Logic.GetAssets()).ToList());
    }

    [HttpGet("slideshow/{slug}/api/Asset/{id}/AssetInfo")]
    public async Task<IActionResult> AssetInfo(string slug, Guid id)
    {
        NoStore();
        var entry = await Authorize(slug);
        if (entry is null) return Unauthorized();
        // Enforce link scope: a viewer must not read metadata for assets outside this link's pool.
        if (!await entry.Logic.IsInScope(id)) return NotFound();
        return Ok(await entry.Logic.GetAssetInfoById(id));
    }

    [HttpGet("slideshow/{slug}/api/Asset/{id}/AlbumInfo")]
    public async Task<IActionResult> AlbumInfo(string slug, Guid id)
    {
        NoStore();
        var entry = await Authorize(slug);
        if (entry is null) return Unauthorized();
        // Enforce link scope: reject out-of-scope assets outright, and for in-scope assets return
        // only the albums this link was granted (GetScopedAlbumInfoById) so we never leak the names
        // of other albums the asset also happens to belong to.
        if (!await entry.Logic.IsInScope(id)) return NotFound();
        return Ok((await entry.Logic.GetScopedAlbumInfoById(id)).ToList());
    }

    [HttpGet("slideshow/{slug}/api/Asset/{id}/Asset")]
    public async Task<IActionResult> Asset(string slug, Guid id, AssetTypeEnum? assetType = null)
    {
        NoStore();
        var entry = await Authorize(slug);
        if (entry is null) return Unauthorized();
        // Enforce link scope: a viewer must not fetch bytes for assets outside this link's pool.
        if (!await entry.Logic.IsInScope(id)) return NotFound();

        var rangeHeader = Request.Headers["Range"].FirstOrDefault();
        AssetResponse asset;
        try
        {
            asset = await entry.Logic.GetAsset(id, assetType, rangeHeader);
        }
        catch (ApiException ex) when (ex.StatusCode == 416)
        {
            return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        return await AssetResults.StreamAsync(this, asset);
    }

    // --- helpers ---

    /// <summary>Marks the response uncacheable so per-link data is never retained by any cache.</summary>
    private void NoStore() => Response.Headers["Cache-Control"] = "no-store";

    private void IssueCookie(SlideshowLinkManager.LinkEntry entry)
    {
        var slug = entry.Link.Slug;
        Response.Cookies.Append(CookieName, _tokens.Issue(slug, entry.Link.SecurityStamp), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Path = $"/slideshow/{slug}",
            MaxAge = TimeSpan.FromDays(30)
        });
    }

    /// <summary>
    /// Returns the link entry if the request is authorized for this slug's content. The token cookie
    /// must validate against the link's current security stamp (so a rotated stamp denies stale
    /// cookies), and for <see cref="SlideshowAccess.ViewerAuth"/> links the caller must additionally
    /// carry a currently-valid viewer session for an account that still exists.
    /// </summary>
    private async Task<SlideshowLinkManager.LinkEntry?> Authorize(string slug)
    {
        var entry = _links.Get(slug);
        if (entry is null) return null;

        if (!_tokens.Validate(Request.Cookies[CookieName], slug, entry.Link.SecurityStamp))
            return null;

        // ViewerAuth links must re-verify the viewer on every request: a slug cookie alone is not
        // enough, so logging out (session gone) or deleting the viewer (account gone) stops access.
        if (entry.Link.AccessPolicy == SlideshowAccess.ViewerAuth)
        {
            var viewer = await HttpContext.AuthenticateAsync(AuthConstants.ViewerCookieScheme);
            if (!viewer.Succeeded) return null;

            var username = viewer.Principal?.Identity?.Name;
            if (string.IsNullOrEmpty(username) || !_auth.ViewerIsActive(username)) return null;
        }

        return entry;
    }
}
