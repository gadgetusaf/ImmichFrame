using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public abstract class CachingApiAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        return (await AllAssets(ct)).Count;
    }

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        return (await AllAssets(ct)).OrderBy(_ => Random.Shared.Next()).Take(requested);
    }

    // Membership is checked against the same cached, filtered asset set the slideshow serves from,
    // so an in-scope id matches and any other id (a different album/person/tag, or excluded) does not.
    // No extra Immich call: AllAssetIds() reuses the existing cache entry.
    public async Task<bool> ContainsAsset(Guid id, CancellationToken ct = default)
    {
        return (await AllAssetIds(ct)).Contains(id);
    }

    private async Task<IReadOnlyList<AssetResponseDto>> AllAssets(CancellationToken ct = default)
    {
        var excludedAlbumAssets = await apiCache.GetOrAddAsync($"{GetType().FullName}_ExcludedAlbums", () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings));

        return await apiCache.GetOrAddAsync(GetType().FullName!,
            async () => (IReadOnlyList<AssetResponseDto>)(await LoadAssets().ApplyAccountFilters(accountSettings, excludedAlbumAssets)).ToList());
    }

    private async Task<HashSet<Guid>> AllAssetIds(CancellationToken ct = default)
    {
        return await apiCache.GetOrAddAsync($"{GetType().FullName}_Ids",
            async () => (await AllAssets(ct)).Select(a => a.Id).ToHashSet());
    }

    protected abstract Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default);
}