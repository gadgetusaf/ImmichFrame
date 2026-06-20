using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>
/// CRUD for Immich accounts. Every change is persisted (API key encrypted at rest) and then applied
/// live via <see cref="ConfigReloadService"/> — no restart required.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[Authorize(Policy = AuthConstants.AdminPolicy)]
public class AdminAccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ApiKeyProtector _protector;
    private readonly ConfigReloadService _reload;
    private readonly ILogger<AdminAccountsController> _logger;

    public AdminAccountsController(AppDbContext db, ApiKeyProtector protector, ConfigReloadService reload, ILogger<AdminAccountsController> logger)
    {
        _db = db;
        _protector = protector;
        _reload = reload;
        _logger = logger;
    }

    [HttpGet]
    public IEnumerable<AccountDto> List() =>
        _db.Accounts.AsNoTracking().OrderBy(a => a.ImmichServerUrl).ToList().Select(AccountDto.FromEntity);

    [HttpPost]
    public IActionResult Create([FromBody] AccountDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ImmichServerUrl))
            return BadRequest(new { message = "Immich server URL is required." });
        if (string.IsNullOrWhiteSpace(dto.ApiKey))
            return BadRequest(new { message = "API key is required." });

        var entity = new AccountEntity { Id = Guid.NewGuid() };
        dto.ApplyTo(entity);
        entity.ApiKey = _protector.Protect(dto.ApiKey.Trim());

        _db.Accounts.Add(entity);
        _db.SaveChanges();
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Account for {url} created by '{user}'.", entity.ImmichServerUrl, User.Identity?.Name);
        return Ok(AccountDto.FromEntity(entity));
    }

    [HttpPut("{id:guid}")]
    public IActionResult Update(Guid id, [FromBody] AccountDto dto)
    {
        var entity = _db.Accounts.FirstOrDefault(a => a.Id == id);
        if (entity is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.ImmichServerUrl))
            return BadRequest(new { message = "Immich server URL is required." });

        dto.ApplyTo(entity);
        // Empty API key means "keep the existing one".
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            entity.ApiKey = _protector.Protect(dto.ApiKey.Trim());

        _db.SaveChanges();
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Account {id} updated by '{user}'.", id, User.Identity?.Name);
        return Ok(AccountDto.FromEntity(entity));
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        var entity = _db.Accounts.FirstOrDefault(a => a.Id == id);
        if (entity is null) return NotFound();

        _db.Accounts.Remove(entity);
        _db.SaveChanges();
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Account {id} deleted by '{user}'.", id, User.Identity?.Name);
        return NoContent();
    }
}
