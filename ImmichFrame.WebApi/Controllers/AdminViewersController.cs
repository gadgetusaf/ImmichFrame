using ImmichFrame.WebApi.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        ViewerDto created;
        try
        {
            // The pre-check above isn't atomic with the insert; the unique username index is the real guard.
            var user = _auth.CreateViewer(username, request.Password);
            created = new ViewerDto(user.Id, user.Username, user.CreatedAt);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = $"The username '{username}' is already taken." });
        }

        _logger.LogInformation("Viewer '{username}' created by '{admin}'.", username, User.Identity?.Name);
        return Ok(created);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        if (!_auth.DeleteViewer(id)) return NotFound();
        _logger.LogInformation("Viewer {id} deleted by '{admin}'.", id, User.Identity?.Name);
        return NoContent();
    }
}
