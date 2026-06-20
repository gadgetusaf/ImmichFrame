using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

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
    private readonly IGeneralSettings _settings;
    private readonly IWeatherService _weather;
    private readonly ICalendarService _calendar;

    public SlideshowController(SlideshowLinkManager links, SlideshowTokenService tokens, PinHasher pin,
        IGeneralSettings settings, IWeatherService weather, ICalendarService calendar)
    {
        _links = links;
        _tokens = tokens;
        _pin = pin;
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

        IssueCookie(slug);
        return Ok(new { name = entry.Link.Name, requiresPin = false, requiresAuth = false });
    }

    [HttpPost("api/slideshow/{slug}/unlock")]
    public IActionResult Unlock(string slug, [FromBody] UnlockRequest request)
    {
        var entry = _links.Get(slug);
        if (entry is null) return NotFound(new { message = "Slideshow link not found." });

        if (entry.Link.AccessPolicy == SlideshowAccess.Pin && !_pin.Verify(entry.Link.PinHash, request.Pin ?? string.Empty))
            return Unauthorized(new { message = "Incorrect PIN." });

        IssueCookie(slug);
        return Ok(new { name = entry.Link.Name });
    }

    // --- scoped content (mirrors the global API under /slideshow/{slug}/api/...) ---

    [HttpGet("slideshow/{slug}/api/Config")]
    public IActionResult Config(string slug)
    {
        if (Authorize(slug) is null) return Unauthorized();
        return Ok(ClientSettingsDto.FromGeneralSettings(_settings));
    }

    [HttpGet("slideshow/{slug}/api/Weather")]
    public async Task<IActionResult> Weather(string slug)
    {
        if (Authorize(slug) is null) return Unauthorized();
        return Ok(await _weather.GetWeather());
    }

    [HttpGet("slideshow/{slug}/api/Calendar")]
    public async Task<IActionResult> Calendar(string slug)
    {
        if (Authorize(slug) is null) return Unauthorized();
        return Ok(await _calendar.GetAppointments());
    }

    [HttpGet("slideshow/{slug}/api/Asset")]
    public async Task<IActionResult> Assets(string slug)
    {
        var entry = Authorize(slug);
        if (entry is null) return Unauthorized();
        return Ok((await entry.Logic.GetAssets()).ToList());
    }

    [HttpGet("slideshow/{slug}/api/Asset/{id}/AssetInfo")]
    public async Task<IActionResult> AssetInfo(string slug, Guid id)
    {
        var entry = Authorize(slug);
        if (entry is null) return Unauthorized();
        return Ok(await entry.Logic.GetAssetInfoById(id));
    }

    [HttpGet("slideshow/{slug}/api/Asset/{id}/AlbumInfo")]
    public async Task<IActionResult> AlbumInfo(string slug, Guid id)
    {
        var entry = Authorize(slug);
        if (entry is null) return Unauthorized();
        return Ok((await entry.Logic.GetAlbumInfoById(id)).ToList());
    }

    [HttpGet("slideshow/{slug}/api/Asset/{id}/Asset")]
    public async Task<IActionResult> Asset(string slug, Guid id, AssetTypeEnum? assetType = null)
    {
        var entry = Authorize(slug);
        if (entry is null) return Unauthorized();

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

    private void IssueCookie(string slug)
    {
        Response.Cookies.Append(CookieName, _tokens.Issue(slug), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Path = $"/slideshow/{slug}",
            MaxAge = TimeSpan.FromDays(30)
        });
    }

    /// <summary>Returns the link entry if the request carries a valid token cookie for this slug.</summary>
    private SlideshowLinkManager.LinkEntry? Authorize(string slug)
    {
        var entry = _links.Get(slug);
        if (entry is null) return null;
        return _tokens.Validate(Request.Cookies[CookieName], slug) ? entry : null;
    }
}
