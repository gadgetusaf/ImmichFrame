using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AllAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        // Match the visibility the pool actually serves (see GetAssets) so archived assets are not
        // counted toward a Timeline-only pool, which would over-weight this account in multi-account
        // selection. Date/rating/excluded-album filters remain unaccounted for here.
        var visibility = accountSettings.ShowArchived ? AssetVisibility.Archive : AssetVisibility.Timeline;

        var stats = await apiCache.GetOrAddAsync(nameof(AllAssetsPool),
            () => immichApi.GetAssetStatisticsAsync(visibility, null, null, ct));

        if (accountSettings.ShowVideos)
        {
            return stats.Images + stats.Videos;
        }

        return stats.Images;
    }

    // A "whole-account" link can still carry content filters (excluded albums, date range, rating,
    // archived/video visibility). Validate the id against those same filters the slideshow serves
    // with, so a viewer cannot fetch an asset the link is not scoped to.
    public async Task<bool> ContainsAsset(Guid id, CancellationToken ct = default)
    {
        AssetResponseDto asset;
        try
        {
            asset = await immichApi.GetAssetInfoAsync(id, null, null, ct);
        }
        catch (ApiException)
        {
            return false;
        }

        var excludedAlbumAssets = await apiCache.GetOrAddAsync(
            $"{nameof(AllAssetsPool)}_ExcludedAlbums",
            () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings, ct));

        return new[] { asset }.ApplyAccountFilters(accountSettings, excludedAlbumAssets).Any();
    }

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        var searchDto = new RandomSearchDto
        {
            Size = requested,
            WithExif = true,
            WithPeople = true
        };

        if (!accountSettings.ShowVideos)
        {
            searchDto.Type = AssetTypeEnum.IMAGE;
        }

        if (accountSettings.ShowArchived)
        {
            searchDto.Visibility = AssetVisibility.Archive;
        }
        else
        {
            searchDto.Visibility = AssetVisibility.Timeline;
        }

        var takenBefore = accountSettings.ImagesUntilDate.HasValue ? accountSettings.ImagesUntilDate : null;
        if (takenBefore.HasValue)
        {
            searchDto.TakenBefore = takenBefore;
        }
        var takenAfter = accountSettings.ImagesFromDate.HasValue ? accountSettings.ImagesFromDate : accountSettings.ImagesFromDays.HasValue ? DateTime.Today.AddDays(-accountSettings.ImagesFromDays.Value) : null;

        if (takenAfter.HasValue)
        {
            searchDto.TakenAfter = takenAfter;
        }

        if (accountSettings.Rating is int rating)
        {
            searchDto.Rating = rating;
        }

        var excludedAlbumAssets = await apiCache.GetOrAddAsync(
            $"{nameof(AllAssetsPool)}_ExcludedAlbums",
            () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings, ct));

        // Immich applies every filter except excluded-album membership server-side, so a single random
        // draw of exactly `requested` can come back short once client-side exclusion runs. Over-fetch and
        // retry (bounded) until we have `requested` post-filter assets or the library is exhausted.
        const int maxAttempts = 5;
        var collected = new Dictionary<Guid, AssetResponseDto>();

        for (var attempt = 0; attempt < maxAttempts && collected.Count < requested; attempt++)
        {
            // Over-fetch to absorb client-side exclusion, but cap at the server's max page size (1000).
            searchDto.Size = Math.Min(requested * 2, 1000);

            var assets = await immichApi.SearchRandomAsync(searchDto, ct);
            var filtered = assets.ApplyAccountFilters(accountSettings, excludedAlbumAssets).ToList();

            foreach (var asset in filtered)
            {
                collected.TryAdd(asset.Id, asset);
            }

            // A draw that yielded no new assets means the qualifying set is effectively exhausted.
            if (filtered.Count == 0)
            {
                break;
            }
        }

        return collected.Values.Take(requested);
    }

}