using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.AccountSelection;

public interface IAssetAccountTracker
{
    ValueTask<bool> RecordAssetLocation(IAccountImmichFrameLogic account, string assetId);
    T ForAsset<T>(string assetId, Func<IAccountImmichFrameLogic, T> f);

    /// <summary>Clears all asset-to-account mappings. Called when the account set is reloaded.</summary>
    void Reset();
}