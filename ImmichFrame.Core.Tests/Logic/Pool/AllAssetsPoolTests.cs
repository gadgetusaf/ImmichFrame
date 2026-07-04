using NUnit.Framework;
using Moq;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;

namespace ImmichFrame.Core.Tests.Logic.Pool;

[TestFixture]
public class AllAssetsPoolTests
{
    private Mock<IApiCache> _mockApiCache;
    private Mock<ImmichApi> _mockImmichApi;
    private Mock<IAccountSettings> _mockAccountSettings;
    private AllAssetsPool _allAssetsPool;

    [SetUp]
    public void Setup()
    {
        _mockApiCache = new Mock<IApiCache>();
        _mockImmichApi = new Mock<ImmichApi>(null, null);
        _mockAccountSettings = new Mock<IAccountSettings>();
        _allAssetsPool = new AllAssetsPool(_mockApiCache.Object, _mockImmichApi.Object, _mockAccountSettings.Object);

        // Default account settings
        _mockAccountSettings.SetupGet(s => s.ShowArchived).Returns(false);
        _mockAccountSettings.SetupGet(s => s.ImagesFromDate).Returns((DateTime?)null);
        _mockAccountSettings.SetupGet(s => s.ImagesUntilDate).Returns((DateTime?)null);
        _mockAccountSettings.SetupGet(s => s.ImagesFromDays).Returns((int?)null);
        _mockAccountSettings.SetupGet(s => s.Rating).Returns((int?)null);
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid>());
        _mockAccountSettings.SetupGet(s => s.Tags).Returns(new List<string>());

        // Default ApiCache setup
        _mockApiCache.Setup(c => c.GetOrAddAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<AssetStatsResponseDto>>>() // For GetAssetCount
            ))
            .Returns<string, Func<Task<AssetStatsResponseDto>>>(async (key, factory) => await factory());

        _mockApiCache.Setup(c => c.GetOrAddAsync(
            It.IsAny<string>(),
            It.IsAny<Func<Task<IEnumerable<AssetResponseDto>>>>()
        ))
        .Returns<string, Func<Task<IEnumerable<AssetResponseDto>>>>(async (key, factory) => await factory());
    }

    private List<AssetResponseDto> CreateSampleAssets(int count, string idPrefix, AssetTypeEnum type, int? rating = null)
    {
        return Enumerable.Range(0, count)
            .Select(i => new AssetResponseDto { Id = TestIds.From($"{idPrefix}{i}"), Type = type, ExifInfo = new ExifResponseDto { Rating = rating } })
            .ToList();
    }

    private List<AssetResponseDto> CreateSampleImageAssets(int count, string idPrefix = "asset", int? rating = null)
    {
        return CreateSampleAssets(count, idPrefix, AssetTypeEnum.IMAGE, rating);
    }

    private List<AssetResponseDto> CreateSampleVideoAssets(int count, string idPrefix = "asset", int? rating = null)
    {
        return CreateSampleAssets(count, idPrefix, AssetTypeEnum.VIDEO, rating);
    }

    [Test]
    public async Task GetAssetCount_CallsApiAndCache_OnlyImages()
    {
        // Arrange
        var stats = new AssetStatsResponseDto { Images = 100, Videos = 40 };
        // ShowArchived is false by default, so the pool now counts only Timeline visibility
        // to match what the slideshow actually serves.
        _mockImmichApi.Setup(api => api.GetAssetStatisticsAsync(AssetVisibility.Timeline, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(stats);

        // Act
        var count = await _allAssetsPool.GetAssetCount();

        // Assert
        Assert.That(count, Is.EqualTo(100));
        _mockImmichApi.Verify(api => api.GetAssetStatisticsAsync(AssetVisibility.Timeline, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _mockApiCache.Verify(cache => cache.GetOrAddAsync(nameof(AllAssetsPool), It.IsAny<Func<Task<AssetStatsResponseDto>>>()), Times.Once);
    }

    [Test]
    public async Task GetAssetCount_CallsApiAndCache_WithVideos()
    {
        // Arrange
        var stats = new AssetStatsResponseDto { Images = 100, Videos = 40 };
        // ShowArchived is false by default, so the pool now counts only Timeline visibility.
        _mockImmichApi.Setup(api => api.GetAssetStatisticsAsync(AssetVisibility.Timeline, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(stats);

        _mockAccountSettings.SetupGet(s => s.ShowVideos).Returns(true);

        // Act
        var count = await _allAssetsPool.GetAssetCount();

        // Assert
        Assert.That(count, Is.EqualTo(140));
        _mockImmichApi.Verify(api => api.GetAssetStatisticsAsync(AssetVisibility.Timeline, null, null, It.IsAny<CancellationToken>()), Times.Once);
        _mockApiCache.Verify(cache => cache.GetOrAddAsync(nameof(AllAssetsPool), It.IsAny<Func<Task<AssetStatsResponseDto>>>()), Times.Once);
    }

    [Test]
    public async Task GetAssets_CallsSearchRandomAsync_WithCorrectParameters_OnlyImages()
    {
        // Arrange
        var requestedImageCount = 5;
        var requestedVideoCount = 8;
        var rating = 3;
        _mockAccountSettings.SetupGet(s => s.ShowArchived).Returns(true);
        _mockAccountSettings.SetupGet(s => s.Rating).Returns(3);
        var returnedAssets = CreateSampleImageAssets(requestedImageCount, rating: rating);
        returnedAssets.AddRange(CreateSampleVideoAssets(requestedVideoCount, rating: rating));
        _mockImmichApi.Setup(api => api.SearchRandomAsync(It.IsAny<RandomSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedAssets.Where(a => a.Type == AssetTypeEnum.IMAGE).ToList());

        // Act
        var assets = await _allAssetsPool.GetAssets(requestedImageCount);

        // Assert
        Assert.That(assets.Count(), Is.EqualTo(requestedImageCount));
        // The pool over-fetches (requested * 2) to compensate for client-side excluded-album filtering.
        _mockImmichApi.Verify(api => api.SearchRandomAsync(
            It.Is<RandomSearchDto>(dto =>
                dto.Size == requestedImageCount * 2 &&
                dto.Type == AssetTypeEnum.IMAGE &&
                dto.WithExif == true &&
                dto.WithPeople == true &&
                dto.Visibility == AssetVisibility.Archive && // ShowArchived = true
                dto.Rating == rating
            ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAssets_CallsSearchRandomAsync_WithCorrectParameters_ImagesAndVideos()
    {
        // Arrange
        var requestedImageCount = 5;
        var requestedVideoCount = 8;
        var rating = 3;
        _mockAccountSettings.SetupGet(s => s.ShowArchived).Returns(true);
        _mockAccountSettings.SetupGet(s => s.ShowVideos).Returns(true);
        _mockAccountSettings.SetupGet(s => s.Rating).Returns(3);
        // Use distinct id prefixes: the pool now dedups by asset id, and the image/video
        // helpers otherwise share the default "asset" prefix, producing colliding ids.
        var returnedAssets = CreateSampleImageAssets(requestedImageCount, idPrefix: "image", rating: rating);
        returnedAssets.AddRange(CreateSampleVideoAssets(requestedVideoCount, idPrefix: "video", rating: rating));
        _mockImmichApi.Setup(api => api.SearchRandomAsync(It.IsAny<RandomSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedAssets.ToList());

        // Act
        var assets = await _allAssetsPool.GetAssets(requestedImageCount + requestedVideoCount);

        // Assert
        Assert.That(assets.Count(), Is.EqualTo(requestedImageCount + requestedVideoCount));
        // The pool over-fetches (requested * 2) to compensate for client-side excluded-album filtering.
        _mockImmichApi.Verify(api => api.SearchRandomAsync(
            It.Is<RandomSearchDto>(dto =>
                dto.Size == (requestedImageCount + requestedVideoCount) * 2 &&
                dto.Type == null &&
                dto.WithExif == true &&
                dto.WithPeople == true &&
                dto.Visibility == AssetVisibility.Archive && // ShowArchived = true
                dto.Rating == rating
            ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAssets_AppliesDateFilters_FromDays()
    {
        _mockAccountSettings.SetupGet(s => s.ImagesFromDays).Returns(10);
        var expectedFromDate = DateTime.Today.AddDays(-10);
        _mockImmichApi.Setup(api => api.SearchRandomAsync(It.IsAny<RandomSearchDto>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new List<AssetResponseDto>());

        await _allAssetsPool.GetAssets(5);

        _mockImmichApi.Verify(api => api.SearchRandomAsync(
            It.Is<RandomSearchDto>(dto => dto.TakenAfter.HasValue && dto.TakenAfter.Value.Date == expectedFromDate.Date),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAssets_ExcludesAssetsFromExcludedAlbums()
    {
        // Arrange
        var mainAssets = CreateSampleImageAssets(3, "main"); // main0, main1, main2
        var excludedAsset = new AssetResponseDto { Id = TestIds.From("excluded1"), Type = AssetTypeEnum.IMAGE };
        var assetsToReturnFromSearch = new List<AssetResponseDto>(mainAssets) { excludedAsset };

        var excludedAlbumId = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid> { excludedAlbumId });

        _mockImmichApi.Setup(api => api.SearchRandomAsync(It.IsAny<RandomSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assetsToReturnFromSearch);
        // Album responses no longer embed assets; excluded-album membership is now loaded via a
        // metadata search scoped to the album id. A single page (< 1000 items) ends the paging loop.
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.AlbumIds != null && d.AlbumIds.Contains(excludedAlbumId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto { Assets = new SearchAssetResponseDto { Items = new List<AssetResponseDto> { excludedAsset }, Total = 1 } });

        // Act
        var result = (await _allAssetsPool.GetAssets(4)).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Any(a => a.Id == TestIds.From("excluded1")), Is.False);
        Assert.That(result.All(a => mainAssets.Any(m => m.Id == a.Id)));
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.AlbumIds != null && d.AlbumIds.Contains(excludedAlbumId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetAssets_NullExcludedAlbums_Succeeds()
    {
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns((List<Guid>)null);

        // Create a set of assets to verify that the code was actually exercised (minimize risk of false positives)
        var allAssets = CreateSampleImageAssets(5, "asset");

        _mockImmichApi.Setup(api => api.SearchRandomAsync(It.IsAny<RandomSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allAssets);

        // Act
        var result = (await _allAssetsPool.GetAssets(5)).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result, Is.EqualTo(allAssets));

        // Verify that no excluded-album search was issued since ExcludedAlbums is null
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ContainsAsset gates by-id access for filtered whole-account links (the IDOR guard): an id must
    // only be "in scope" if it survives the exact same account filters the slideshow serves with.
    [Test]
    public async Task ContainsAsset_ReturnsTrue_ForInScopeAsset()
    {
        var id = TestIds.From("in-scope");
        _mockImmichApi.Setup(api => api.GetAssetInfoAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetResponseDto { Id = id, Type = AssetTypeEnum.IMAGE });

        var result = await _allAssetsPool.ContainsAsset(id);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task ContainsAsset_ReturnsFalse_ForExcludedAlbumAsset()
    {
        var id = TestIds.From("excluded");
        var excludedAlbumId = Guid.NewGuid();
        _mockAccountSettings.SetupGet(s => s.ExcludedAlbums).Returns(new List<Guid> { excludedAlbumId });

        _mockImmichApi.Setup(api => api.GetAssetInfoAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetResponseDto { Id = id, Type = AssetTypeEnum.IMAGE });
        // The asset is a member of the excluded album, so it must be reported out of scope.
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.Is<MetadataSearchDto>(d => d.AlbumIds != null && d.AlbumIds.Contains(excludedAlbumId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResponseDto { Assets = new SearchAssetResponseDto { Items = new List<AssetResponseDto> { new AssetResponseDto { Id = id, Type = AssetTypeEnum.IMAGE } }, Total = 1 } });

        var result = await _allAssetsPool.ContainsAsset(id);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ContainsAsset_ReturnsFalse_WhenAssetFailsRatingFilter()
    {
        var id = TestIds.From("low-rating");
        _mockAccountSettings.SetupGet(s => s.Rating).Returns(5);
        _mockImmichApi.Setup(api => api.GetAssetInfoAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetResponseDto { Id = id, Type = AssetTypeEnum.IMAGE, ExifInfo = new ExifResponseDto { Rating = 1 } });

        var result = await _allAssetsPool.ContainsAsset(id);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ContainsAsset_ReturnsFalse_ForArchivedAssetWhenArchivedHidden()
    {
        var id = TestIds.From("archived");
        // ShowArchived is false by default in this fixture.
        _mockImmichApi.Setup(api => api.GetAssetInfoAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssetResponseDto { Id = id, Type = AssetTypeEnum.IMAGE, IsArchived = true });

        var result = await _allAssetsPool.ContainsAsset(id);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task ContainsAsset_ReturnsFalse_WhenAssetLookupThrows()
    {
        var id = TestIds.From("missing");
        // A deleted/inaccessible asset (ApiException) must fail closed, not leak access.
        _mockImmichApi.Setup(api => api.GetAssetInfoAsync(id, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("not found", 404, null, null, null));

        var result = await _allAssetsPool.ContainsAsset(id);

        Assert.That(result, Is.False);
    }
}
