// ImmichFrame.Core/Helpers/AssetHelper.cs
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Helpers;

public static class AssetHelper
{
    public static async Task<IEnumerable<AssetResponseDto>> GetExcludedAlbumAssets(ImmichApi immichApi, IAccountSettings accountSettings, CancellationToken ct = default)
    {
        var excludedAlbumAssets = new List<AssetResponseDto>();

        foreach (var albumId in accountSettings?.ExcludedAlbums ?? new())
        {
            try
            {
                // Album responses no longer embed assets (Immich moved to metadata-only album info),
                // so page through the excluded album's assets via search.
                int page = 1;
                int batchSize = 1000;
                int itemsInPage;
                do
                {
                    var metadataBody = new MetadataSearchDto
                    {
                        Page = page,
                        Size = batchSize,
                        AlbumIds = [albumId]
                    };

                    var albumInfo = await immichApi.SearchAssetsAsync(metadataBody, ct);
                    itemsInPage = albumInfo.Assets.Items.Count;
                    excludedAlbumAssets.AddRange(albumInfo.Assets.Items);
                    page++;
                } while (itemsInPage == batchSize);
            }
            catch (ApiException)
            {
                // A stale/deleted/inaccessible excluded album must not disable the whole pool; skip it.
            }
        }

        return excludedAlbumAssets;
    }
}