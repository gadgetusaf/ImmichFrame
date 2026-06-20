using ImmichFrame.Core.Logic;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// Applies configuration changes to the running app without a restart: refreshes the settings
/// snapshot from the database, then rebuilds the per-account image-pool graph.
/// </summary>
public class ConfigReloadService(DatabaseServerSettings provider, MultiImmichFrameLogicDelegate logic, SlideshowLinkManager links, AppDbContext db)
{
    public void ReloadFromDatabase()
    {
        provider.Load(db);
        logic.Reload();
        links.Reload(db);
    }
}
