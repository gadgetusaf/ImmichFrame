using ImmichFrame.Core.Api;
using ImmichFrame.Core.Logic.Pool;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Logic.Pool
{
    /// <summary>
    /// Tests the abstract <see cref="AggregatingAssetPool"/> base contract directly (previously this
    /// fixture just re-ran the <see cref="MultiAssetPool"/> tests and never exercised the base class).
    /// The base implements <c>GetAssets(requested)</c> by pumping the protected <c>GetNextAsset</c>
    /// up to <c>requested</c> times, stopping at the first null.
    /// </summary>
    [TestFixture]
    public class AggregatingAssetPoolTests
    {
        // Minimal concrete pool: GetNextAsset drains a fixed queue, returning null when empty. This
        // isolates the base-class GetAssets pumping loop from any MultiAssetPool selection logic.
        private sealed class QueueBackedPool : AggregatingAssetPool
        {
            private readonly Queue<AssetResponseDto> _queue;
            public int NextAssetCalls { get; private set; }

            public QueueBackedPool(IEnumerable<AssetResponseDto> assets) => _queue = new Queue<AssetResponseDto>(assets);

            public override Task<long> GetAssetCount(CancellationToken ct = default) => Task.FromResult((long)_queue.Count);
            public override Task<bool> ContainsAsset(Guid id, CancellationToken ct = default)
                => Task.FromResult(_queue.Any(a => a.Id == id));

            protected override Task<AssetResponseDto?> GetNextAsset(CancellationToken ct)
            {
                NextAssetCalls++;
                return Task.FromResult(_queue.Count > 0 ? _queue.Dequeue() : null);
            }
        }

        private static AssetResponseDto CreateAsset(string id) =>
            new() { Id = TestIds.From(id), OriginalPath = $"/path/{id}.jpg", Type = AssetTypeEnum.IMAGE, ExifInfo = new ExifResponseDto() };

        [Test]
        public async Task GetAssets_RequestZero_ReturnsEmpty_AndNeverCallsGetNextAsset()
        {
            var pool = new QueueBackedPool(new[] { CreateAsset("a1") });

            var result = await pool.GetAssets(0, CancellationToken.None);

            Assert.That(result, Is.Empty);
            Assert.That(pool.NextAssetCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task GetAssets_RequestFewerThanAvailable_ReturnsExactlyRequested_InOrder()
        {
            var a1 = CreateAsset("a1");
            var a2 = CreateAsset("a2");
            var a3 = CreateAsset("a3");
            var pool = new QueueBackedPool(new[] { a1, a2, a3 });

            var result = (await pool.GetAssets(2, CancellationToken.None)).ToList();

            Assert.That(result, Is.EqualTo(new[] { a1, a2 }));
            // Stops pumping once the requested count is met — does not drain the whole queue.
            Assert.That(pool.NextAssetCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task GetAssets_RequestMoreThanAvailable_StopsAtFirstNull_ReturnsAllAvailable()
        {
            var a1 = CreateAsset("a1");
            var a2 = CreateAsset("a2");
            var pool = new QueueBackedPool(new[] { a1, a2 });

            var result = (await pool.GetAssets(5, CancellationToken.None)).ToList();

            Assert.That(result, Is.EqualTo(new[] { a1, a2 }));
            // 2 successful pumps + 1 that returns null and breaks the loop.
            Assert.That(pool.NextAssetCalls, Is.EqualTo(3));
        }

        [Test]
        public async Task GetAssets_EmptyPool_ReturnsEmpty_AfterASingleProbe()
        {
            var pool = new QueueBackedPool(Array.Empty<AssetResponseDto>());

            var result = await pool.GetAssets(3, CancellationToken.None);

            Assert.That(result, Is.Empty);
            Assert.That(pool.NextAssetCalls, Is.EqualTo(1));
        }
    }
}
