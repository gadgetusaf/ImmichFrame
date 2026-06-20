using ImmichFrame.Core.Api;
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

            // Drop stale asset->account mappings so changed credentials are not reused.
            _tracker.Reset();
            _accountToDelegate = map;
            _accountSelectionStrategy.Initialize(map.Values.ToList());
            _logger.LogInformation("Account configuration loaded ({count} account(s)).", map.Count);
        }
    }

    public async Task<AssetResponseDto?> GetNextAsset()
        => _accountToDelegate.Count == 0 ? null : (await _accountSelectionStrategy.GetNextAsset())?.ToAsset();


    public async Task<IEnumerable<AssetResponseDto>> GetAssets()
        => _accountToDelegate.Count == 0
            ? Enumerable.Empty<AssetResponseDto>()
            : (await _accountSelectionStrategy.GetAssets()).Shuffle().Select(it => it.ToAsset());


    public Task<AssetResponseDto> GetAssetInfoById(Guid assetId)
        => _accountSelectionStrategy.ForAsset(assetId, async logic => (await logic.GetAssetInfoById(assetId)).WithAccount(logic));


    public Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId)
        => _accountSelectionStrategy.ForAsset(assetId, logic => logic.GetAlbumInfoById(assetId));


    public Task<AssetResponse> GetAsset(Guid assetId, AssetTypeEnum? assetType = null, string? rangeHeader = null)
        => _accountSelectionStrategy.ForAsset(assetId, logic => logic.GetAsset(assetId, assetType, rangeHeader));

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
