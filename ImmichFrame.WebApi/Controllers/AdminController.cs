using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>
/// Configuration admin API. Authentication is via the <see cref="AuthConstants.AdminCookieScheme"/>
/// cookie; the global ImmichFrame bearer middleware bypasses everything under /api/admin.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AdminAuthService _auth;
    private readonly AppDbContext _db;
    private readonly DatabaseServerSettings _settingsProvider;
    private readonly IServerSettings _serverSettings;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AdminAuthService auth,
        AppDbContext db,
        DatabaseServerSettings settingsProvider,
        IServerSettings serverSettings,
        ILogger<AdminController> logger)
    {
        _auth = auth;
        _db = db;
        _settingsProvider = settingsProvider;
        _serverSettings = serverSettings;
        _logger = logger;
    }

    public record CredentialsDto(string Username, string Password);

    /// <summary>Whether no admin exists yet (so the UI can show first-run setup instead of login).</summary>
    [HttpGet("setup-required")]
    public object SetupRequired() => new { setupRequired = !_auth.AnyAdminExists() };

    /// <summary>Creates the first admin account. Only works while no admin exists.</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] CredentialsDto request)
    {
        if (_auth.AnyAdminExists())
            return Conflict(new { message = "An administrator account already exists." });
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Username and password are required." });

        var user = _auth.CreateAdmin(request.Username.Trim(), request.Password);
        _logger.LogInformation("Administrator account '{username}' created via first-run setup.", user.Username);
        await SignInAsync(user);
        return Ok(new { username = user.Username });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] CredentialsDto request)
    {
        var user = _auth.ValidateCredentials(request.Username?.Trim() ?? string.Empty, request.Password ?? string.Empty);
        if (user is null || user.Role != UserRoles.Admin)
            return Unauthorized(new { message = "Invalid username or password." });

        await SignInAsync(user);
        return Ok(new { username = user.Username });
    }

    [Authorize(Policy = AuthConstants.AdminPolicy)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthConstants.AdminCookieScheme);
        return Ok();
    }

    [Authorize(Policy = AuthConstants.AdminPolicy)]
    [HttpGet("me")]
    public object Me() => new { username = User.Identity?.Name };

    /// <summary>Returns the currently active general (display) settings.</summary>
    [Authorize(Policy = AuthConstants.AdminPolicy)]
    [HttpGet("general")]
    public GeneralSettingsEntity GetGeneral() => GeneralSettingsEntity.From(_serverSettings.GeneralSettings);

    /// <summary>Persists general settings and applies them live (no restart).</summary>
    [Authorize(Policy = AuthConstants.AdminPolicy)]
    [HttpPut("general")]
    public IActionResult PutGeneral([FromBody] GeneralSettingsEntity posted)
    {
        if (posted is null)
            return BadRequest(new { message = "Request body is required." });

        var existing = _db.GeneralSettings.OrderBy(g => g.Id).FirstOrDefault();
        if (existing is null)
        {
            existing = new GeneralSettingsEntity { Id = 1 };
            _db.GeneralSettings.Add(existing);
        }

        existing.Apply(posted);
        _db.SaveChanges();
        _settingsProvider.Load(_db);

        _logger.LogInformation("General settings updated by '{username}'.", User.Identity?.Name);
        return Ok(GeneralSettingsEntity.From(_serverSettings.GeneralSettings));
    }

    private Task SignInAsync(UserEntity user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
        };
        var identity = new ClaimsIdentity(claims, AuthConstants.AdminCookieScheme);
        var principal = new ClaimsPrincipal(identity);
        return HttpContext.SignInAsync(AuthConstants.AdminCookieScheme, principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
