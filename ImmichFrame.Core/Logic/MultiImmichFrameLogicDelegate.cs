using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.AccountSelection;
using ImmichFrame.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.Core.Logic;

public class MultiImmichFrameLogicDelegate : IImmichFrameLogic
{
    // Swapped atomically by Reload(); read into a local before use so in-flight requests stay consistent.
    private volatile IReadOnlyDictionary<IAccountSettings, IAccountImmichFrameLogic> _accountToDelegate
        = new Dictionary<IAccountSettings, IAccountImmichFrameLogic>();

    private readonly IServerSettings _serverSettings;
    private readonly Func<IAccountSettings, IAccountImmichFrameLogic> _logicFactory;
    private readonly IAccountSelectionStrategy _accountSelectionStrategy;
    private readonly IAssetAccountTracker _tracker;
    private readonly ILogger<MultiImmichFrameLogicDelegate> _logger;
    private readonly object _reloadLock = new();

    public MultiImmichFrameLogicDelegate(IServerSettings serverSettings,
        Func<IAccountSettings, IAccountImmichFrameLogic> logicFactory, ILogger<MultiImmichFrameLogicDelegate> logger,
        IAccountSelectionStrategy accountSelectionStrategy, IAssetAccountTracker tracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accountSelectionStrategy = accountSelectionStrategy;
        _serverSettings = serverSettings;
        _logicFactory = logicFactory;
        _tracker = tracker;
        Reload();
    }

    /// <summary>
    /// Rebuilds the account-to-logic map from the current settings snapshot and re-initializes the
    /// selection strategy. Safe to call at runtime after accounts change — no restart required.
    /// </summary>
    public void Reload()
    {
        lock (_reloadLock)
        {
            var map = _serverSettings.Accounts.ToDictionary(
                keySelector: a => a,
                elementSelector: _logicFactory);

            // Capture the outgoing logic instances so their owned caches can be disposed after the swap.
            var oldDelegates = _accountToDelegate.Values.ToList();

            // Drop stale asset->account mappings so changed credentials are not reused.
            _tracker.Reset();
            _accountToDelegate = map;
            _accountSelectionStrategy.Initialize(map.Values.ToList());
            _logger.LogInformation("Account configuration loaded ({count} account(s)).", map.Count);

            // Dispose the replaced instances' caches, but not synchronously: an in-flight request may
            // still hold an old logic. Defer briefly so it finishes before disposal. HttpClients are
            // owned by IHttpClientFactory and are intentionally not touched here.
            DisposeAfterDelay(oldDelegates);
        }
    }

    // Disposes the given (now-replaced) logic instances after a short grace period so any request
    // still using one can complete first. Fire-and-forget; disposal failures are swallowed since the
    // instance is already orphaned.
    private void DisposeAfterDelay(IReadOnlyCollection<IAccountImmichFrameLogic> oldDelegates)
    {
        if (oldDelegates.Count == 0) return;

        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
        {
            foreach (var old in oldDelegates)
            {
                try
                {
                    (old as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to dispose a replaced account logic instance.");
                }
            }
        });
    }

    public async Task<AssetResponseDto?> GetNextAsset()
        => _accountToDelegate.Count == 0 ? null : (await _accountSelectionStrategy.GetNextAsset())?.ToAsset();


    public async Task<IEnumerable<AssetResponseDto>> GetAssets()
        => _accountToDelegate.Count == 0
            ? Enumerable.Empty<AssetResponseDto>()
            : (await _accountSelectionStrategy.GetAssets()).Shuffle().Select(it => it.ToAsset());


    public Task<AssetResponseDto> GetAssetInfoById(Guid assetId)
        => ForAsset(assetId, async logic => (await logic.GetAssetInfoById(assetId)).WithAccount(logic));


    public Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId)
        => ForAsset(assetId, logic => logic.GetAlbumInfoById(assetId));


    public Task<AssetResponse> GetAsset(Guid assetId, AssetTypeEnum? assetType = null, string? rangeHeader = null)
        => ForAsset(assetId, logic => logic.GetAsset(assetId, assetType, rangeHeader));

    // Runs f against the account that owns the asset. The tracker resolves it from recorded mappings;
    // on a miss (e.g. the tracker was reset by a Reload while frames still hold ids from an earlier
    // batch) we probe the current accounts, re-record the owner, and invoke f directly so the request
    // isn't served a spurious not-found.
    private async Task<T> ForAsset<T>(Guid assetId, Func<IAccountImmichFrameLogic, Task<T>> f)
    {
        try
        {
            return await _accountSelectionStrategy.ForAsset(assetId, f);
        }
        catch (AssetNotFoundException)
        {
            foreach (var logic in _accountToDelegate.Values.ToList())
            {
                bool inScope;
                try
                {
                    inScope = await logic.IsInScope(assetId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to probe account for asset {assetId}; skipping.", assetId);
                    continue;
                }

                if (!inScope) continue;

                await _tracker.RecordAssetLocation(logic, assetId.ToString());
                return await f(logic);
            }

            throw;
        }
    }

    public async Task<long> GetTotalAssets()
    {
        if (_accountToDelegate.Count == 0) return 0;
        var allInts = await Task.WhenAll(_accountToDelegate.Values.Select(account => account.GetTotalAssets()));
        return allInts.Sum();
    }

    public Task SendWebhookNotification(IWebhookNotification notification) =>
        WebhookHelper.SendWebhookNotification(notification, _serverSettings.GeneralSettings.Webhook);
}

public static class AccountAndAssetExtensions
{
    public static AssetResponseDto ToAsset(this (IAccountImmichFrameLogic, AssetResponseDto) accountAndAsset)
    {
        var (account, asset) = accountAndAsset;
        return asset.WithAccount(account);
    }

    public static AssetResponseDto WithAccount(this AssetResponseDto asset, IAccountImmichFrameLogic account)
    {
        asset.ImmichServerUrl = account.AccountSettings.ImmichServerUrl;
        return asset;
    }
}
