using ImmichFrame.WebApi.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>Admin management of viewer accounts (used by "auth required" links).</summary>
[ApiController]
[Route("api/admin/viewers")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public class AdminViewersController : ControllerBase
{
    private readonly AdminAuthService _auth;
    private readonly ILogger<AdminViewersController> _logger;

    public AdminViewersController(AdminAuthService auth, ILogger<AdminViewersController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    public record CreateViewerRequest(string Username, string Password);
    public record ViewerDto(Guid Id, string Username, DateTime CreatedAt);

    [HttpGet]
    public IEnumerable<ViewerDto> List() =>
        _auth.ListViewers().Select(u => new ViewerDto(u.Id, u.Username, u.CreatedAt));

    [HttpPost]
    public IActionResult Create([FromBody] CreateViewerRequest request)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Username and password are required." });
        if (_auth.UsernameExists(username))
            return Conflict(new { message = $"The username '{username}' is already taken." });

        var user = _auth.CreateViewer(username, request.Password);
        _logger.LogInformation("Viewer '{username}' created by '{admin}'.", username, User.Identity?.Name);
        return Ok(new ViewerDto(user.Id, user.Username, user.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        if (!_auth.DeleteViewer(id)) return NotFound();
        _logger.LogInformation("Viewer {id} deleted by '{admin}'.", id, User.Identity?.Name);
        return NoContent();
    }
}
