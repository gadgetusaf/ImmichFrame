using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AlbumAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var albumAssets = new List<AssetResponseDto>();
        var seenIds = new HashSet<Guid>();

        var albums = accountSettings.Albums;
        if (albums != null)
        {
            // Immich no longer embeds assets in the album response (GET /albums/{id} returns metadata
            // only), so page through the album's assets via metadata search.
            foreach (var albumId in albums)
            {
                int page = 1;
                int batchSize = 1000;
                int itemsInPage;
                do
                {
                    var metadataBody = new MetadataSearchDto
                    {
                        Page = page,
                        Size = batchSize,
                        AlbumIds = [albumId],
                        WithExif = true,
                        WithPeople = true
                    };

                    if (!accountSettings.ShowVideos)
                    {
                        metadataBody.Type = AssetTypeEnum.IMAGE;
                    }

                    var albumInfo = await immichApi.SearchAssetsAsync(metadataBody, ct);
                    itemsInPage = albumInfo.Assets.Items.Count;

                    foreach (var asset in albumInfo.Assets.Items)
                    {
                        if (seenIds.Add(asset.Id))
                        {
                            albumAssets.Add(asset);
                        }
                    }
                    page++;
                } while (itemsInPage == batchSize);
            }
        }

        return albumAssets;
    }
}