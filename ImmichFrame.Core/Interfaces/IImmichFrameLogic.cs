using ImmichFrame.Core.Api;
using ImmichFrame.Core.Models;


namespace ImmichFrame.Core.Interfaces
{
    public interface IImmichFrameLogic
    {
        public Task<AssetResponseDto?> GetNextAsset();
        public Task<IEnumerable<AssetResponseDto>> GetAssets();
        public Task<AssetResponseDto> GetAssetInfoById(Guid assetId);
        public Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId);
        public Task<AssetResponse> GetAsset(Guid id, AssetTypeEnum? assetType = null, string? rangeHeader = null);
        public Task<long> GetTotalAssets();
        public Task SendWebhookNotification(IWebhookNotification notification);
    }

    public interface IAccountImmichFrameLogic : IImmichFrameLogic
    {
        public IAccountSettings AccountSettings { get; }

        /// <summary>
        /// Whether the given asset id is within this account/link's configured scope. Used by the
        /// scoped slideshow serving path to reject by-id lookups for assets outside the link's pool.
        /// </summary>
        public Task<bool> IsInScope(Guid assetId);

        /// <summary>
        /// Albums containing the asset, filtered to this link's scope: only the granted albums for an
        /// album-scoped link, every album for a whole-account link, and none for a people/tag/favorite/
        /// memory link. Used by the scoped slideshow path so it can't leak the names of albums the link
        /// was never granted (whereas <see cref="IImmichFrameLogic.GetAlbumInfoById"/> returns them all).
        /// </summary>
        public Task<IEnumerable<AlbumResponseDto>> GetScopedAlbumInfoById(Guid assetId);
    }

    public interface IAccountSelectionStrategy
    {
        void Initialize(IList<IAccountImmichFrameLogic> accounts);
        Task<(IAccountImmichFrameLogic, AssetResponseDto)?> GetNextAsset();
        Task<IEnumerable<(IAccountImmichFrameLogic, AssetResponseDto)>> GetAssets();
        T ForAsset<T>(Guid assetId, Func<IAccountImmichFrameLogic, T> f);
    }
}
