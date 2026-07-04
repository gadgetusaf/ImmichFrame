using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic;
using ImmichFrame.Core.Logic.AccountSelection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Logic
{
    /// <summary>
    /// Pins the live-reload behaviour of <see cref="MultiImmichFrameLogicDelegate"/>: the atomic
    /// account-map rebuild, the tracker reset that drops stale asset->account routing after a
    /// credential change, and the zero-account guard on <c>GetNextAsset</c>/<c>GetAssets</c>. These
    /// guard the core runtime-reconfiguration seam of the fork; a regression such as forgetting
    /// <c>_tracker.Reset()</c> or dropping the zero-account guard would otherwise pass the suite
    /// silently and only surface as production 401s / stale content.
    /// </summary>
    [TestFixture]
    public class ReloadSeamTests
    {
        private Mock<IServerSettings> _serverSettings = null!;
        private Mock<IAccountSelectionStrategy> _strategy = null!;
        private Mock<IAssetAccountTracker> _tracker = null!;
        private List<IAccountSettings> _accounts = null!;
        private Dictionary<IAccountSettings, Mock<IAccountImmichFrameLogic>> _logicByAccount = null!;

        [SetUp]
        public void Setup()
        {
            _accounts = new List<IAccountSettings>();
            _logicByAccount = new Dictionary<IAccountSettings, Mock<IAccountImmichFrameLogic>>();

            _serverSettings = new Mock<IServerSettings>();
            _serverSettings.SetupGet(s => s.Accounts).Returns(() => _accounts);
            _serverSettings.SetupGet(s => s.GeneralSettings).Returns(new Mock<IGeneralSettings>().Object);

            _strategy = new Mock<IAccountSelectionStrategy>();
            _tracker = new Mock<IAssetAccountTracker>();
        }

        private IAccountSettings NewAccount(string url)
        {
            var account = new Mock<IAccountSettings>();
            account.SetupGet(a => a.ImmichServerUrl).Returns(url);
            var logic = new Mock<IAccountImmichFrameLogic>();
            logic.SetupGet(l => l.AccountSettings).Returns(account.Object);
            _logicByAccount[account.Object] = logic;
            return account.Object;
        }

        // The delegate's constructor takes Func<IAccountSettings, IAccountImmichFrameLogic>; hand back
        // the mock logic pre-registered for the account so we can assert per-account wiring.
        private MultiImmichFrameLogicDelegate CreateDelegate()
        {
            return new MultiImmichFrameLogicDelegate(
                _serverSettings.Object,
                account => _logicByAccount[account].Object,
                NullLogger<MultiImmichFrameLogicDelegate>.Instance,
                _strategy.Object,
                _tracker.Object);
        }

        [Test]
        public void Reload_RebuildsSelectionStrategy_WithCurrentAccounts()
        {
            var a1 = NewAccount("http://one.invalid");
            _accounts.Add(a1);
            var sut = CreateDelegate();

            // Add a second account and reload — the strategy must be re-initialized with both logics.
            var a2 = NewAccount("http://two.invalid");
            _accounts.Add(a2);
            sut.Reload();

            _strategy.Verify(s => s.Initialize(
                It.Is<IList<IAccountImmichFrameLogic>>(list =>
                    list.Count == 2 &&
                    list.Contains(_logicByAccount[a1].Object) &&
                    list.Contains(_logicByAccount[a2].Object))),
                Times.AtLeastOnce);
        }

        [Test]
        public void Reload_ResetsTracker_SoStaleRoutingIsDropped()
        {
            var a1 = NewAccount("http://one.invalid");
            _accounts.Add(a1);
            var sut = CreateDelegate();

            _tracker.Invocations.Clear();

            // A credential/account change followed by Reload must clear recorded asset->account
            // mappings, otherwise a changed API key would be reused against the wrong account.
            sut.Reload();

            _tracker.Verify(t => t.Reset(), Times.Once);
        }

        [Test]
        public async Task GetNextAsset_WithZeroAccounts_ReturnsNull_WithoutQueryingStrategy()
        {
            // No accounts configured. The zero-account guard must short-circuit before touching the
            // selection strategy so a de-configured instance yields null rather than throwing.
            var sut = CreateDelegate();

            var result = await sut.GetNextAsset();

            Assert.That(result, Is.Null);
            _strategy.Verify(s => s.GetNextAsset(), Times.Never);
        }

        [Test]
        public async Task GetAssets_WithZeroAccounts_ReturnsEmpty_WithoutQueryingStrategy()
        {
            var sut = CreateDelegate();

            var result = await sut.GetAssets();

            Assert.That(result, Is.Empty);
            _strategy.Verify(s => s.GetAssets(), Times.Never);
        }

        [Test]
        public async Task GetTotalAssets_WithZeroAccounts_ReturnsZero()
        {
            var sut = CreateDelegate();

            Assert.That(await sut.GetTotalAssets(), Is.EqualTo(0));
        }

        [Test]
        public async Task GetNextAsset_WithAnAccount_DelegatesToStrategy()
        {
            var a1 = NewAccount("http://one.invalid");
            _accounts.Add(a1);
            var sut = CreateDelegate();

            var dto = new AssetResponseDto { Id = Guid.NewGuid(), OriginalPath = "/p.jpg", Type = AssetTypeEnum.IMAGE, ExifInfo = new ExifResponseDto() };
            (IAccountImmichFrameLogic, AssetResponseDto)? next = (_logicByAccount[a1].Object, dto);
            _strategy.Setup(s => s.GetNextAsset()).Returns(Task.FromResult(next));

            var result = await sut.GetNextAsset();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Id, Is.EqualTo(dto.Id));
            // ToAsset stamps the owning account's server url onto the returned asset.
            Assert.That(result.ImmichServerUrl, Is.EqualTo("http://one.invalid"));
        }

        [Test]
        public void Reload_ReconfiguringFromOneToZeroAccounts_IsGuarded()
        {
            var a1 = NewAccount("http://one.invalid");
            _accounts.Add(a1);
            var sut = CreateDelegate();

            // Remove all accounts then reload: the strategy must be re-initialized with an empty list
            // (the deferred-disposal path must not throw), and the map is empty afterwards.
            _accounts.Clear();
            Assert.DoesNotThrow(() => sut.Reload());

            _strategy.Verify(s => s.Initialize(
                It.Is<IList<IAccountImmichFrameLogic>>(list => list.Count == 0)),
                Times.AtLeastOnce);
        }
    }
}
