using System.Collections;
using System.Collections.Concurrent;
using BloomFilter;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.Core.Logic.AccountSelection;

public class BloomFilterAssetAccountTracker(ILogger<BloomFilterAssetAccountTracker> _logger) : IAssetAccountTracker
{
    // Concurrent: RecordAssetLocation runs on every asset served and may race ForAsset's enumeration.
    // A ConcurrentDictionary makes both the create path and the read-side iteration safe under load.
    private volatile ConcurrentDictionary<IAccountImmichFrameLogic, IBloomFilter> logicToFilter = new();

    public void Reset() => logicToFilter = new ConcurrentDictionary<IAccountImmichFrameLogic, IBloomFilter>();

    public async ValueTask<bool> RecordAssetLocation(IAccountImmichFrameLogic account, string assetId)
    {
        // Snapshot the reference so a concurrent Reset() can't leave us adding into a swapped-out map mid-call.
        var map = logicToFilter;
        if (!map.TryGetValue(account, out var filter))
        {
            // Build outside the lock-free GetOrAdd: the factory does async I/O (GetTotalAssets), which
            // GetOrAdd's synchronous factory can't await. A redundant build under a race is harmless —
            // GetOrAdd keeps a single winner and the loser's filter is discarded.
            var built = await NewFilter(account);
            filter = map.GetOrAdd(account, built);
        }
        return await filter.AddAsync(assetId);
    }

    private async Task<IBloomFilter> NewFilter(IImmichFrameLogic account)
    {
        return FilterBuilder.Build(await account.GetTotalAssets());
    }

    public async Task<T> ForAsset<T>(string assetId, Func<IAccountImmichFrameLogic, Task<T>> f)
    {
        // Snapshot the reference: ConcurrentDictionary enumeration is safe against concurrent writes,
        // and pinning it locally keeps a concurrent Reset() from swapping the map out mid-iteration.
        foreach (var entry in logicToFilter)
        {
            if (entry.Value.Contains(assetId))
            {
                try
                {
                    return await f(entry.Key);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to locate asset {assetId} in {entry.Key}. Must be false positive, trying next account.", assetId, entry.Key);
                }
            }
        }
        
        _logger.LogError("Failed to locate account for asset {assetId}", assetId);
        throw new AssetNotFoundException();
    }
}