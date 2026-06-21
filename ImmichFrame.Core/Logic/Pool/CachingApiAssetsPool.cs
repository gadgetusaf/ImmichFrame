using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public abstract class CachingApiAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    private readonly Random _random = new();

    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        return (await AllAssets(ct)).Count();
    }

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        return (await AllAssets(ct)).OrderBy(_ => _random.Next()).Take(requested);
    }

    // Membership is checked against the same cached, filtered asset set the slideshow serves from,
    // so an in-scope id matches and any other id (a different album/person/tag, or excluded) does not.
    // No extra Immich call: AllAssets() reuses the existing cache entry.
    public async Task<bool> ContainsAsset(Guid id, CancellationToken ct = default)
    {
        var target = id.ToString();
        return (await AllAssets(ct)).Any(a => string.Equals(a.Id, target, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IEnumerable<AssetResponseDto>> AllAssets(CancellationToken ct = default)
    {
        var excludedAlbumAssets = await apiCache.GetOrAddAsync($"{GetType().FullName}_ExcludedAlbums", () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings));

        return await apiCache.GetOrAddAsync(GetType().FullName!, () => LoadAssets().ApplyAccountFilters(accountSettings, excludedAlbumAssets));
    }

    protected abstract Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default);
}