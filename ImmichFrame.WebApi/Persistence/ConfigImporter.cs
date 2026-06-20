using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers.Config;
using ImmichFrame.WebApi.Persistence.Entities;

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
public class ConfigImporter(ConfigLoader loader, ILogger<ConfigImporter> logger)
{
    public void ImportIfNeeded(AppDbContext db, string configPath)
    {
        var reimport = IsTruthy(Environment.GetEnvironmentVariable("IMMICHFRAME_REIMPORT"));
        var alreadyInitialized = db.GeneralSettings.Any();

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

        if (alreadyInitialized)
        {
            // IMMICHFRAME_REIMPORT path: clear and overwrite from the file/env source.
            db.Accounts.RemoveRange(db.Accounts);
            db.GeneralSettings.RemoveRange(db.GeneralSettings);
            db.SaveChanges();
        }

        var general = GeneralSettingsEntity.From(loaded.GeneralSettings);
        general.Id = 1;
        db.GeneralSettings.Add(general);

        var accountCount = 0;
        foreach (var account in loaded.Accounts)
        {
            db.Accounts.Add(AccountEntity.From(account));
            accountCount++;
        }

        db.SaveChanges();
        logger.LogInformation(
            "{action} existing configuration into the database ({count} account(s)).",
            reimport ? "Re-imported" : "Imported",
            accountCount);
    }

    private static bool IsTruthy(string? value) =>
        value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
