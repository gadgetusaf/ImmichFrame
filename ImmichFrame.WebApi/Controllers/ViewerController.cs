using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>Viewer login for "auth required" slideshow links. Uses the ViewerCookie scheme.</summary>
[ApiController]
[Route("api/viewer")]
public class ViewerController : ControllerBase
{
    private readonly AdminAuthService _auth;
    private readonly ILogger<ViewerController> _logger;

    public ViewerController(AdminAuthService auth, ILogger<ViewerController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    public record CredentialsDto(string Username, string Password);

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] CredentialsDto request)
    {
        var user = _auth.ValidateViewer(request.Username?.Trim() ?? string.Empty, request.Password ?? string.Empty);
        if (user is null)
            return Unauthorized(new { message = "Invalid username or password." });

        await SignInAsync(user);
        return Ok(new { username = user.Username });
    }

    [Authorize(Policy = AuthConstants.ViewerPolicy)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthConstants.ViewerCookieScheme);
        return Ok();
    }

    [Authorize(Policy = AuthConstants.ViewerPolicy)]
    [HttpGet("me")]
    public object Me() => new { username = User.Identity?.Name };

    private Task SignInAsync(UserEntity user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
        };
        var identity = new ClaimsIdentity(claims, AuthConstants.ViewerCookieScheme);
        return HttpContext.SignInAsync(AuthConstants.ViewerCookieScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
