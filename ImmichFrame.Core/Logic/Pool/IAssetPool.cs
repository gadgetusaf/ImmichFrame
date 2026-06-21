using ImmichFrame.Core.Api;

namespace ImmichFrame.Core.Logic.Pool;

public interface IAssetPool
{
    Task<long> GetAssetCount(CancellationToken ct = default);
    Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default);

    /// <summary>
    /// Whether the given asset id falls within this pool's scope. Used to enforce per-link scope on
    /// by-id lookups (asset info / album info / asset bytes) so a viewer of a narrowly scoped link
    /// cannot read arbitrary assets in the underlying account.
    /// </summary>
    Task<bool> ContainsAsset(Guid id, CancellationToken ct = default);

    protected static async Task<IEnumerable<AssetResponseDto>> WaitAssets(
        int requested,
        Func<CancellationToken, Task<AssetResponseDto?>> supplier,
        CancellationToken? cancellationToken = null)
    {
        //allow up to one minute
        var ct = cancellationToken ?? new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;

        var itemsRead = new List<AssetResponseDto>(requested > 0 ? requested : 0);

        for (var i = 0; i < requested; i++)
        {
            var asset = await supplier(ct);

            if (asset != null)
            {
                itemsRead.Add(asset);
            }
            else
            {
                break;
            }
        }

        return itemsRead;
    }
}