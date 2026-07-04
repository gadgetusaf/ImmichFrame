using NUnit.Framework;
using Moq;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;

namespace ImmichFrame.Core.Tests.Logic.Pool;

[TestFixture]
public class AlbumAssetsPoolTests
{
    private Mock<IApiCache> _mockApiCache;
    private Mock<ImmichApi> _mockImmichApi;
    private Mock<IAccountSettings> _mockAccountSettings;
    private AlbumAssetsPool _albumAssetsPool;

    [SetUp]
    public void Setup()
    {
        _mockApiCache = new Mock<IApiCache>();

        _mockApiCache
            .Setup(m => m.GetOrAddAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<IEnumerable<AssetResponseDto>>>>()))
            .Returns<string, Func<Task<IEnumerable<AssetResponseDto>>>>((_, factory) => factory());

        // The CachingApiAssetsPool base caches the filtered asset set as IReadOnlyList and its
        // id set as HashSet<Guid>; both closed generics must be stubbed or Moq returns null.
        _mockApiCache
            .Setup(m => m.GetOrAddAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<IReadOnlyList<AssetResponseDto>>>>()))
            .Returns<string, Func<Task<IReadOnlyList<AssetResponseDto>>>>((_, factory) => factory());

        _mockApiCache
            .Setup(m => m.GetOrAddAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<HashSet<Guid>>>>()))
            .Returns<string, Func<Task<HashSet<Guid>>>>((_, factory) => factory());

        _mockImmichApi = new Mock<ImmichApi>("", null);
        _mockAccountSettings = new Mock<IAccountSettings>();
        _albumAssetsPool = new AlbumAssetsPool(_mockApiCache.Object, _mockImmichApi.Object, _mockAccountSettings.Object);

        _mockAccountSettings.SetupGet(s => s.Albums).Returns(new List<Guid>());
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid>());
    }

    private AssetResponseDto CreateAsset(string id) => new AssetResponseDto { Id = TestIds.From(id), Type = AssetTypeEnum.IMAGE };

    // Immich no longer embeds assets in the album response; both included and excluded albums
    // load their assets via SearchAssetsAsync(MetadataSearchDto) with AlbumIds set. This stubs a
    // single page (< 1000 items) so the pool's paging loop ends naturally after one call.
    private void SetupAlbumSearch(Guid albumId, params AssetResponseDto[] assets)
    {
        _mockImmichApi
            .Setup(api => api.SearchAssetsAsync(
                It.Is<MetadataSearchDto>(d => d.AlbumIds != null && d.AlbumIds.Contains(albumId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto
            {
                Assets = new SearchAssetResponseDto
                {
                    Items = new List<AssetResponseDto>(assets),
                    Total = assets.Length
                }
            });
    }

    [Test]
    public async Task LoadAssets_ReturnsAssetsPresentIIncludedNotExcludedAlbums()
    {
        // Arrange
        var album1Id = Guid.NewGuid();
        var excludedAlbumId = Guid.NewGuid();

        var assetA = CreateAsset("A"); // In album1
        var assetB = CreateAsset("B"); // In album1 and excludedAlbum
        var assetC = CreateAsset("C"); // In excludedAlbum only
        var assetD = CreateAsset("D"); // In album1 only

        _mockAccountSettings.SetupGet(s => s.Albums).Returns(new List<Guid> { album1Id });
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid> { excludedAlbumId });

        SetupAlbumSearch(album1Id, assetA, assetB, assetD);
        SetupAlbumSearch(excludedAlbumId, assetB, assetC);

        // Act
        var result = (await _albumAssetsPool.GetAssets(25)).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Any(a => a.Id == TestIds.From("A")));
        Assert.That(result.Any(a => a.Id == TestIds.From("D")));
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(
            It.Is<MetadataSearchDto>(d => d.AlbumIds != null && d.AlbumIds.Contains(album1Id)),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(
            It.Is<MetadataSearchDto>(d => d.AlbumIds != null && d.AlbumIds.Contains(excludedAlbumId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoadAssets_NoIncludedAlbums_ReturnsEmpty()
    {
        var excludedAlbumId = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.Albums).Returns(new List<Guid>());
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid> { excludedAlbumId });
        SetupAlbumSearch(excludedAlbumId, CreateAsset("excluded_only"));

        var result = (await _albumAssetsPool.GetAssets(25)).ToList();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task LoadAssets_NoExcludedAlbums_ReturnsAlbums()
    {
        var album1Id = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.Albums).Returns(new List<Guid> { album1Id });
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid>()); // Empty excluded

        SetupAlbumSearch(album1Id, CreateAsset("A"));

        var result = (await _albumAssetsPool.GetAssets(25)).ToList();
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result.Any(a => a.Id == TestIds.From("A")));
    }

    [Test]
    public async Task LoadAssets_NullAlbums_ReturnsEmpty()
    {
        _mockAccountSettings.SetupGet(s => s.Albums).Returns((List<Guid>)null);

        var result = (await _albumAssetsPool.GetAssets(25)).ToList();
        Assert.That(result, Is.Empty);

        // the absence of an error, whereas before a null pointer exception would be thrown, indicates success.
    }

    [Test]
    public async Task LoadAssets_NullExcludedAlbums_Succeeds()
    {
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns((List<Guid>)null);

        var result = (await _albumAssetsPool.GetAssets(25)).ToList();
        Assert.That(result, Is.Empty);

        // the absence of an error, whereas before a null pointer exception would be thrown, indicates success.
    }
}
