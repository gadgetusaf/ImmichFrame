using ImmichFrame.Core.Api;
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
    private readonly ImmichBrowseService _browse;
    private readonly ILogger<AdminAccountsController> _logger;

    public AdminAccountsController(AppDbContext db, ApiKeyProtector protector, ConfigReloadService reload, ImmichBrowseService browse, ILogger<AdminAccountsController> logger)
    {
        _db = db;
        _protector = protector;
        _reload = reload;
        _browse = browse;
        _logger = logger;
    }

    [HttpGet]
    public IEnumerable<AccountDto> List() =>
        _db.Accounts.AsNoTracking().OrderBy(a => a.ImmichServerUrl).ToList().Select(AccountDto.FromEntity);

    public record BrowseRequest(string ImmichServerUrl, string? ApiKey, Guid? AccountId);
    public record AccountSaveResult(AccountDto Account, List<string> Warnings);

    /// <summary>Lists albums and people from an Immich server to power the account editor's pickers.</summary>
    [HttpPost("browse")]
    public async Task<IActionResult> Browse([FromBody] BrowseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImmichServerUrl))
            return BadRequest(new { message = "Immich server URL is required." });

        // Use the supplied key (new account / changed key) or fall back to the saved account's key.
        var apiKey = request.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) && request.AccountId is Guid accountId)
        {
            var account = _db.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is not null)
                apiKey = _protector.Unprotect(account.ApiKey);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(new { message = "An API key is required to browse this server." });

        try
        {
            var result = await _browse.BrowseAsync(request.ImmichServerUrl.Trim(), apiKey, ct);
            return Ok(result);
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Browse failed for {url} (HTTP {status}).", request.ImmichServerUrl, apiEx.StatusCode);
            var detail = apiEx.StatusCode switch
            {
                401 or 403 => "the API key was rejected (unauthorized).",
                404 => "the server URL looks wrong — Immich's API was not found there.",
                _ => $"Immich returned HTTP {apiEx.StatusCode}."
            };
            return StatusCode(StatusCodes.Status502BadGateway, new { message = $"Could not load from Immich — {detail}" });
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Browse failed for {url}.", request.ImmichServerUrl);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                message = $"Could not reach the Immich server. ImmichFrame connects to Immich from the server, so the URL must be reachable from the ImmichFrame host (not just your browser). Details: {e.Message}"
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AccountDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ImmichServerUrl))
            return BadRequest(new { message = "Immich server URL is required." });
        if (string.IsNullOrWhiteSpace(dto.ApiKey))
            return BadRequest(new { message = "API key is required." });

        var entity = new AccountEntity { Id = Guid.NewGuid() };
        dto.ApplyTo(entity);
        var plainKey = dto.ApiKey.Trim();
        entity.ApiKey = _protector.Protect(plainKey);

        _db.Accounts.Add(entity);
        _db.SaveChanges();
        _reload.ReloadFromDatabase();

        _logger.LogInformation("Account for {url} created by '{user}'.", entity.ImmichServerUrl, User.Identity?.Name);
        var warnings = await ValidateQuietly(entity.ImmichServerUrl, plainKey, ct);
        return Ok(new AccountSaveResult(AccountDto.FromEntity(entity), warnings));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AccountDto dto, CancellationToken ct)
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
        var warnings = await ValidateQuietly(entity.ImmichServerUrl, _protector.Unprotect(entity.ApiKey), ct);
        return Ok(new AccountSaveResult(AccountDto.FromEntity(entity), warnings));
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

    /// <summary>Best-effort permission probe; never fails the save, bounded so it can't hang it.</summary>
    private async Task<List<string>> ValidateQuietly(string url, string apiKey, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            return await _browse.ValidateAsync(url, apiKey, timeout.Token);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Account validation probe failed for {url}.", url);
            return new List<string>();
        }
    }
}
