using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AllAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        // Retrieve total media count (images + videos); will update to query filtered stats from Immich
        var stats = await apiCache.GetOrAddAsync(nameof(AllAssetsPool),
            () => immichApi.GetAssetStatisticsAsync(null, false, null, ct));

        if (accountSettings.ShowVideos)
        {
            return stats.Images + stats.Videos;
        }

        return stats.Images;
    }

    // An unfiltered pool exposes the account's whole library by design, so any id owned by the
    // account is in scope. The Immich API key is already account-scoped, so "true" here is correct.
    public Task<bool> ContainsAsset(Guid id, CancellationToken ct = default) => Task.FromResult(true);

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

        var assets = await immichApi.SearchRandomAsync(searchDto, ct);
        var excludedAlbumAssets = await apiCache.GetOrAddAsync(
            $"{nameof(AllAssetsPool)}_ExcludedAlbums",
            () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings, ct));

        return assets.ApplyAccountFilters(accountSettings, excludedAlbumAssets);
    }

}