using System.Text;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>CRUD for named slideshow links. Each change is applied live via <see cref="ConfigReloadService"/>.</summary>
[ApiController]
[Route("api/admin/links")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public class AdminLinksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PinHasher _pin;
    private readonly ConfigReloadService _reload;
    private readonly ILogger<AdminLinksController> _logger;

    public AdminLinksController(AppDbContext db, PinHasher pin, ConfigReloadService reload, ILogger<AdminLinksController> logger)
    {
        _db = db;
        _pin = pin;
        _reload = reload;
        _logger = logger;
    }

    [HttpGet]
    public IEnumerable<LinkDto> List() =>
        _db.SlideshowLinks.AsNoTracking().OrderBy(l => l.Slug).ToList().Select(LinkDto.FromEntity);

    [HttpPost]
    public IActionResult Create([FromBody] LinkDto dto)
    {
        var slug = NormalizeSlug(dto.Slug);
        if (string.IsNullOrEmpty(slug)) return BadRequest(new { message = "A valid link path (slug) is required." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "A name is required." });
        if (!_db.Accounts.Any(a => a.Id == dto.AccountId)) return BadRequest(new { message = "Choose a valid account." });
        if (_db.SlideshowLinks.Any(l => l.Slug == slug)) return Conflict(new { message = $"The link '{slug}' is already in use." });
        if (dto.AccessPolicy == SlideshowAccess.Pin && string.IsNullOrWhiteSpace(dto.Pin))
            return BadRequest(new { message = "A PIN is required for PIN-protected links." });

        var entity = new SlideshowLinkEntity { Id = Guid.NewGuid(), Slug = slug };
        dto.ApplyTo(entity);
        entity.PinHash = entity.AccessPolicy == SlideshowAccess.Pin ? _pin.Hash(dto.Pin!.Trim()) : null;

        _db.SlideshowLinks.Add(entity);
        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Lost the race against the unique slug index between the pre-check and the insert.
            return Conflict(new { message = $"The link '{slug}' is already in use." });
        }
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Slideshow link '{slug}' created by '{user}'.", slug, User.Identity?.Name);
        return Ok(LinkDto.FromEntity(entity));
    }

    [HttpPut("{id:guid}")]
    public IActionResult Update(Guid id, [FromBody] LinkDto dto)
    {
        var entity = _db.SlideshowLinks.FirstOrDefault(l => l.Id == id);
        if (entity is null) return NotFound();

        var slug = NormalizeSlug(dto.Slug);
        if (string.IsNullOrEmpty(slug)) return BadRequest(new { message = "A valid link path (slug) is required." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "A name is required." });
        if (!_db.Accounts.Any(a => a.Id == dto.AccountId)) return BadRequest(new { message = "Choose a valid account." });
        if (_db.SlideshowLinks.Any(l => l.Slug == slug && l.Id != id)) return Conflict(new { message = $"The link '{slug}' is already in use." });

        var hadPin = !string.IsNullOrEmpty(entity.PinHash);

        // Snapshot the security-relevant state so we can rotate the stamp (revoking all issued cookies)
        // only when one of these actually changes.
        var previousPolicy = entity.AccessPolicy;
        var previousPinHash = entity.PinHash;
        var previousEnabled = entity.Enabled;

        dto.ApplyTo(entity);
        entity.Slug = slug;

        if (entity.AccessPolicy == SlideshowAccess.Pin)
        {
            if (!string.IsNullOrWhiteSpace(dto.Pin))
                entity.PinHash = _pin.Hash(dto.Pin.Trim());
            else if (!hadPin)
                return BadRequest(new { message = "A PIN is required for PIN-protected links." });
            // else: keep the existing PinHash
        }
        else
        {
            entity.PinHash = null;
        }

        // Rotate the security stamp on any security-relevant change: access policy, PIN, or the link
        // being disabled. This invalidates every previously-issued access cookie for this link. (A
        // brand-new PIN hash differs from the old one even for the same PIN, which is the safe default.)
        var policyChanged = !string.Equals(previousPolicy, entity.AccessPolicy, StringComparison.Ordinal);
        var pinChanged = !string.Equals(previousPinHash, entity.PinHash, StringComparison.Ordinal);
        var disabled = previousEnabled && !entity.Enabled;
        if (policyChanged || pinChanged || disabled)
            entity.SecurityStamp = Guid.NewGuid().ToString("N");

        try
        {
            _db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            // Lost the race against the unique slug index between the pre-check and the update.
            return Conflict(new { message = $"The link '{slug}' is already in use." });
        }
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Slideshow link {id} updated by '{user}'.", id, User.Identity?.Name);
        return Ok(LinkDto.FromEntity(entity));
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        var entity = _db.SlideshowLinks.FirstOrDefault(l => l.Id == id);
        if (entity is null) return NotFound();

        _db.SlideshowLinks.Remove(entity);
        _db.SaveChanges();
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Slideshow link {id} deleted by '{user}'.", id, User.Identity?.Name);
        return NoContent();
    }

    /// <summary>Lowercases and strips a slug down to URL-safe [a-z0-9-].</summary>
    private static string NormalizeSlug(string? slug)
    {
        var sb = new StringBuilder();
        foreach (var ch in (slug ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }

        var result = sb.ToString().Trim('-');
        while (result.Contains("--")) result = result.Replace("--", "-");
        return result;
    }
}
