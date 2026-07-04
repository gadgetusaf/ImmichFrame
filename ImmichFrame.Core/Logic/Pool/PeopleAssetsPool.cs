using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class PersonAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var personAssets = new List<AssetResponseDto>();
        var seenIds = new HashSet<Guid>();

        var people = accountSettings.People;
        if (people == null)
        {
            return personAssets;
        }

        foreach (var personId in people)
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
                    PersonIds = [personId],
                    WithExif = true,
                    WithPeople = true
                };

                if (!accountSettings.ShowVideos)
                {
                    metadataBody.Type = AssetTypeEnum.IMAGE;
                }

                var personInfo = await immichApi.SearchAssetsAsync(metadataBody, ct);

                itemsInPage = personInfo.Assets.Items.Count;

                foreach (var asset in personInfo.Assets.Items)
                {
                    if (seenIds.Add(asset.Id))
                    {
                        personAssets.Add(asset);
                    }
                }
                page++;
            } while (itemsInPage == batchSize);
        }

        return personAssets;
    }
}