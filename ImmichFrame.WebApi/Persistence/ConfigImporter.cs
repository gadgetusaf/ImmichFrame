using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Helpers.Config;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// One-time migration of the legacy file/environment configuration into the database.
/// Runs on startup: if the database has not been initialized yet, it imports any existing
/// Settings.json / Settings.yml / environment configuration. If none is found the database is
/// left empty and the app boots with in-memory defaults (to be configured via the admin UI).
///
/// Setting the environment variable IMMICHFRAME_REIMPORT=true forces a re-import on the next
/// boot, overwriting the stored configuration from the file/env source. This is the file-based
/// escape hatch until the browser admin UI lands.
/// </summary>
public class ConfigImporter(ConfigLoader loader, ApiKeyProtector apiKeyProtector, ILogger<ConfigImporter> logger)
{
    public void ImportIfNeeded(AppDbContext db, string configPath)
    {
        var reimport = IsTruthy(Environment.GetEnvironmentVariable("IMMICHFRAME_REIMPORT"));
        var alreadyInitialized = db.GeneralSettings.Any() || db.Accounts.Any();

        if (alreadyInitialized && !reimport)
        {
            return;
        }

        IServerSettings? loaded = null;
        try
        {
            loaded = loader.LoadConfig(configPath);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                "No file/environment configuration found to import ({errorMessage}). Starting with empty configuration; configure ImmichFrame in the admin UI.",
                e.Message);
        }

        if (loaded == null)
        {
            // Leave the database empty so a Settings file added later can still be imported.
            return;
        }

        using var transaction = db.Database.BeginTransaction();

        // On reimport, remember the accounts being replaced so existing slideshow links can be
        // remapped to the freshly-imported accounts (which get new Ids) instead of being orphaned.
        var oldAccountsById = alreadyInitialized
            ? db.Accounts.AsNoTracking().ToDictionary(a => a.Id)
            : new Dictionary<Guid, AccountEntity>();

        if (alreadyInitialized)
        {
            // IMMICHFRAME_REIMPORT path: clear and overwrite from the file/env source.
            db.Accounts.RemoveRange(db.Accounts);
            db.GeneralSettings.RemoveRange(db.GeneralSettings);
        }

        var general = GeneralSettingsEntity.From(loaded.GeneralSettings);
        general.Id = 1;
        db.GeneralSettings.Add(general);

        var accountCount = 0;
        var newAccounts = new List<AccountEntity>();
        foreach (var account in loaded.Accounts)
        {
            var entity = AccountEntity.From(account);
            entity.ApiKey = apiKeyProtector.Protect(entity.ApiKey);
            db.Accounts.Add(entity);
            newAccounts.Add(entity);
            accountCount++;
        }

        if (alreadyInitialized)
        {
            RemapSlideshowLinks(db, oldAccountsById, newAccounts);
        }

        db.SaveChanges();
        transaction.Commit();
        logger.LogInformation(
            "{action} existing configuration into the database ({count} account(s)).",
            reimport ? "Re-imported" : "Imported",
            accountCount);
    }

    /// <summary>
    /// Reimport replaces every account with a new row (fresh Id), which would orphan existing
    /// slideshow links. Remap each link to the newly-imported account with the same Immich server
    /// URL; links that can't be matched are disabled (never silently orphaned) with a warning.
    /// </summary>
    private void RemapSlideshowLinks(
        AppDbContext db,
        IReadOnlyDictionary<Guid, AccountEntity> oldAccountsById,
        IReadOnlyList<AccountEntity> newAccounts)
    {
        var links = db.SlideshowLinks.ToList();
        if (links.Count == 0) return;

        foreach (var link in links)
        {
            if (!oldAccountsById.TryGetValue(link.AccountId, out var oldAccount))
            {
                // Link already pointed at a missing account; leave it as-is.
                continue;
            }

            var replacement = newAccounts.FirstOrDefault(a =>
                string.Equals(a.ImmichServerUrl, oldAccount.ImmichServerUrl, StringComparison.OrdinalIgnoreCase));

            if (replacement is not null)
            {
                link.AccountId = replacement.Id;
            }
            else if (link.Enabled)
            {
                link.Enabled = false;
                logger.LogWarning(
                    "Slideshow link '{slug}' pointed at a re-imported account ({url}) that no longer exists; disabling it. Reassign it in the admin UI.",
                    link.Slug, oldAccount.ImmichServerUrl);
            }
        }
    }

    private static bool IsTruthy(string? value) =>
        value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
