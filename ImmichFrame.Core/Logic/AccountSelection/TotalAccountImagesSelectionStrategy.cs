using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.Core.Logic.AccountSelection;

public class TotalAccountImagesSelectionStrategy(ILogger<TotalAccountImagesSelectionStrategy> _logger, IAssetAccountTracker _tracker) : IAccountSelectionStrategy
{
    // Swapped wholesale by Initialize(); volatile so a concurrent GetNextAsset/GetAssets reads a
    // consistent reference. Starts empty so callers never deref null before the first Initialize.
    private volatile IList<IAccountImmichFrameLogic> _accounts = Array.Empty<IAccountImmichFrameLogic>();

    public void Initialize(IList<IAccountImmichFrameLogic> accounts)
    {
        _accounts = accounts;
    }

    public async Task<(IAccountImmichFrameLogic, AssetResponseDto)?> GetNextAsset()
    {
        var accounts = _accounts;
        if (accounts.Count == 0)
        {
            _logger.LogDebug("No accounts configured; no next asset");
            return null;
        }

        var chosen = await accounts.ChooseOne(logic => logic.GetTotalAssets());
        if (chosen == null)
        {
            _logger.LogDebug("No account chosen; no next asset");
            return null;
        }

        var asset = await chosen.GetNextAsset();
        if (asset != null)
        {
            await _tracker.RecordAssetLocation(chosen, asset.Id.ToString());
            return (chosen, asset);
        }

        _logger.LogDebug("No next asset found");
        return null;
    }

    private async Task<(IList<long>, long)> GetWeights(IList<IAccountImmichFrameLogic> accounts)
    {
        var weights = await Task.WhenAll(accounts.Select(GetTotalForAccount));
        return (weights, weights.Sum());
    }

    private async Task<IList<double>> GetProportions(IList<IAccountImmichFrameLogic> accounts)
    {
        var (totals, sum) = await GetWeights(accounts);
        return totals.Select(t => (double)t / sum).ToList();
    }

    private async Task<long> GetTotalForAccount(IAccountImmichFrameLogic account)
    {
        try
        {
            return await account.GetTotalAssets();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to get total assets for account [{account}]; treating as 0.", account);
            return 0;
        }
    }

    public async Task<IEnumerable<(IAccountImmichFrameLogic, AssetResponseDto)>> GetAssets()
    {
        var accounts = _accounts;
        if (accounts.Count == 0)
        {
            // Nothing to draw from (e.g. the last account was deleted mid-slideshow). Avoid
            // .Max() on an empty proportions sequence and just return no assets.
            _logger.LogDebug("No accounts configured; returning no assets");
            return Enumerable.Empty<(IAccountImmichFrameLogic, AssetResponseDto)>();
        }

        var proportions = await GetProportions(accounts);
        var maxAccount = proportions.Max();
        var adjustedProportions = proportions.Select(x => x / maxAccount).ToList();
        var assetLists = accounts.Select(account => account.GetAssets()).ToList();

        var taskList = assetLists
            .Zip(accounts, adjustedProportions)
            .Select(async tuple =>
            {
                var (task, account, proportion) = tuple;
                try
                {
                    var assets = (await task).ToList();
                    _logger.LogDebug("Retrieved {total} asset(s) for account [{account}], will take {proportion}%", assets.Count(), account, proportion * 100);
                    return (account, (IEnumerable<AssetResponseDto>)assets.Shuffle().TakeProportional(proportion));
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Failed to retrieve assets for account [{account}]; skipping it.", account);
                    return (account, Enumerable.Empty<AssetResponseDto>());
                }
            });

        var accountAssetTupleList = await Task.WhenAll(taskList);

        _logger.LogDebug("Processing {} list(s) of asset(s) of length {}", accountAssetTupleList.Length, string.Join(",", accountAssetTupleList.Select(a => a.Item2.Count())));

        foreach (var accountAssetTuple in accountAssetTupleList)
        {
            foreach (var asset in accountAssetTuple.Item2)
            {
                await _tracker.RecordAssetLocation(accountAssetTuple.account, asset.Id.ToString());
            }
        }

        var assets = accountAssetTupleList.SelectMany(tuple => tuple.Item2.Select(asset => (tuple.account, asset))).ToList();

        _logger.LogDebug("Returning {count} asset(s)", assets.Count);

        return assets;
    }

    public Task<T> ForAsset<T>(Guid assetId, Func<IAccountImmichFrameLogic, Task<T>> f)
        => _tracker.ForAsset(assetId.ToString(), f);
}