using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;

namespace ImmichFrame.Core.Logic.Pool;

public class MultiAssetPool(IEnumerable<IAssetPool> delegates) : AggregatingAssetPool
{
    public override async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        var counts = delegates.Select(pool => pool.GetAssetCount(ct));
        return (await Task.WhenAll(counts)).Sum();
    }

    // In scope if ANY child pool contains the asset (the union of all configured filters).
    public override async Task<bool> ContainsAsset(Guid id, CancellationToken ct = default)
    {
        foreach (var pool in delegates)
        {
            if (await pool.ContainsAsset(id, ct))
                return true;
        }

        return false;
    }

    protected override async Task<AssetResponseDto?> GetNextAsset(CancellationToken ct)
    {
        var pool = await delegates.ChooseOne(async @delegate=> await @delegate.GetAssetCount(ct));
        
        if (pool == null) return null;
        
        return (await pool.GetAssets(1, ct)).FirstOrDefault();
    }
}