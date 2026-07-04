using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace ImmichFrame.Core.Logic.Pool;

public class MemoryAssetsPool : CachingApiAssetsPool
{
    private readonly ImmichApi immichApi;
    private readonly IAccountSettings accountSettings;

    // Owns a private DailyApiCache. Used by tests / standalone construction.
    public MemoryAssetsPool(ImmichApi immichApi, IAccountSettings accountSettings)
        : this(new DailyApiCache(), immichApi, accountSettings) { }

    // Preferred: the caller supplies (and is responsible for disposing) the daily cache, so it can be
    // released when the owning PooledImmichFrameLogic is disposed on a config reload.
    public MemoryAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings)
        : base(apiCache, immichApi, accountSettings)
    {
        this.immichApi = immichApi;
        this.accountSettings = accountSettings;
    }

    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var searchDate = DateTimeOffset.Now;
        var memories = await immichApi.SearchMemoriesAsync(null, searchDate, null, null, null, null, ct);

        var memoryAssets = new List<AssetResponseDto>();
        foreach (var memory in memories)
        {
            var assets = memory.Assets.ToList();
            var yearsAgo = searchDate.Year - memory.Data.Year;

            if (!accountSettings.ShowVideos)
            {
                assets = assets.Where(a => a.Type == AssetTypeEnum.IMAGE).ToList();
            }

            foreach (var asset in assets)
            {
                if (asset.ExifInfo == null)
                {
                    var assetInfo = await immichApi.GetAssetInfoAsync(asset.Id, null, null, ct);
                    asset.ExifInfo = assetInfo.ExifInfo;
                    asset.People = assetInfo.People;
                }

                asset.ExifInfo ??= new ExifResponseDto();
                asset.ExifInfo.Description = $"{yearsAgo} {(yearsAgo == 1 ? "year" : "years")} ago";
            }

            memoryAssets.AddRange(assets);
        }

        return memoryAssets;
    }
}

class DailyApiCache : ApiCache
{
    public DailyApiCache() : base(() => new MemoryCacheEntryOptions
    {
        AbsoluteExpiration = DateTimeOffset.Now.Date.AddDays(1)
    }
    )
    {
    }
}