using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// Holds one scoped <see cref="IAccountImmichFrameLogic"/> per enabled slideshow link (built from
/// the link's account + its content filters). Rebuilt whenever links or accounts change, mirroring
/// how <c>MultiImmichFrameLogicDelegate</c> manages the global account pools.
/// </summary>
public class SlideshowLinkManager(
    Func<IAccountSettings, IAccountImmichFrameLogic> logicFactory,
    ApiKeyProtector protector,
    ILogger<SlideshowLinkManager> logger)
{
    public record LinkEntry(SlideshowLinkEntity Link, IAccountImmichFrameLogic Logic);

    private volatile IReadOnlyDictionary<string, LinkEntry> _bySlug =
        new Dictionary<string, LinkEntry>(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

    public LinkEntry? Get(string slug) =>
        _bySlug.TryGetValue(slug, out var entry) ? entry : null;

    public void Reload(AppDbContext db)
    {
        lock (_lock)
        {
            var accounts = db.Accounts.AsNoTracking().ToList();
            var links = db.SlideshowLinks.AsNoTracking().Where(l => l.Enabled).ToList();

            var map = new Dictionary<string, LinkEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in links)
            {
                var account = accounts.FirstOrDefault(a => a.Id == link.AccountId);
                if (account is null)
                {
                    logger.LogWarning("Slideshow link '{slug}' references a missing account; skipping.", link.Slug);
                    continue;
                }

                var apiKey = protector.Unprotect(account.ApiKey);
                var scoped = new ScopedAccountSettings(account, apiKey, link);
                map[link.Slug] = new LinkEntry(link, logicFactory(scoped));
            }

            // Capture the outgoing logic instances so their owned caches can be disposed after the swap.
            var oldLogics = _bySlug.Values.Select(e => e.Logic).ToList();

            _bySlug = map;
            logger.LogInformation("Loaded {count} slideshow link(s).", map.Count);

            // Dispose the replaced instances' caches, but not synchronously: an in-flight slideshow
            // request may still hold an old logic. Defer briefly so it finishes before disposal.
            // HttpClients are owned by IHttpClientFactory and are intentionally not touched here.
            DisposeAfterDelay(oldLogics);
        }
    }

    // Fire-and-forget disposal after a short grace period, so an in-flight request using a replaced
    // logic can complete first. Disposal failures are swallowed since the instance is already orphaned.
    private void DisposeAfterDelay(IReadOnlyCollection<IAccountImmichFrameLogic> oldLogics)
    {
        if (oldLogics.Count == 0) return;

        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
        {
            foreach (var old in oldLogics)
            {
                try
                {
                    (old as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to dispose a replaced slideshow logic instance.");
                }
            }
        });
    }
}
