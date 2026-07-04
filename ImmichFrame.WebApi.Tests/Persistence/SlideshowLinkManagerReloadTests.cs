using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Persistence;

/// <summary>
/// Pins <see cref="SlideshowLinkManager.Reload"/>: the per-link scoped-logic rebuild, the
/// missing-account skip (SlideshowLinkManager.cs:38-43), the disabled-link filter, the API-key
/// decryption feeding <see cref="ScopedAccountSettings"/>, and atomic replacement of the old slug map.
/// This branch never executed in any prior test, so a regression (e.g. serving a link whose account
/// was deleted, or leaking a disabled link) would pass the suite silently.
/// </summary>
[TestFixture]
public class SlideshowLinkManagerReloadTests
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AppDbContext> _options = null!;
    private ApiKeyProtector _protector = null!;

    // Records which scoped account settings the factory was asked to build a logic for, so we can
    // assert the manager wired each surviving link to its account's decrypted key + link filters.
    private sealed class FakeAccountLogic : IAccountImmichFrameLogic
    {
        public FakeAccountLogic(IAccountSettings settings) => AccountSettings = settings;
        public IAccountSettings AccountSettings { get; }
        public Task<bool> IsInScope(Guid assetId) => Task.FromResult(false);
        public Task<IEnumerable<AlbumResponseDto>> GetScopedAlbumInfoById(Guid assetId) => Task.FromResult(Enumerable.Empty<AlbumResponseDto>());
        public Task<AssetResponseDto?> GetNextAsset() => Task.FromResult<AssetResponseDto?>(null);
        public Task<IEnumerable<AssetResponseDto>> GetAssets() => Task.FromResult(Enumerable.Empty<AssetResponseDto>());
        public Task<AssetResponseDto> GetAssetInfoById(Guid assetId) => throw new NotImplementedException();
        public Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId) => throw new NotImplementedException();
        public Task<AssetResponse> GetAsset(Guid assetId, AssetTypeEnum? assetType = null, string? rangeHeader = null) => throw new NotImplementedException();
        public Task<long> GetTotalAssets() => Task.FromResult(0L);
        public Task SendWebhookNotification(IWebhookNotification notification) => Task.CompletedTask;
    }

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var db = new AppDbContext(_options))
        {
            db.Database.EnsureCreated();
        }

        var dp = DataProtectionProvider.Create("ImmichFrame.Tests");
        _protector = new ApiKeyProtector(dp, NullLogger<ApiKeyProtector>.Instance);
    }

    [TearDown]
    public void TearDown() => _connection.Dispose();

    private AppDbContext NewDb() => new(_options);

    private (SlideshowLinkManager mgr, List<IAccountSettings> built) NewManager()
    {
        var built = new List<IAccountSettings>();
        var mgr = new SlideshowLinkManager(
            settings => { built.Add(settings); return new FakeAccountLogic(settings); },
            _protector,
            NullLogger<SlideshowLinkManager>.Instance);
        return (mgr, built);
    }

    private AccountEntity SeedAccount(AppDbContext db, string url, string plaintextKey)
    {
        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            ImmichServerUrl = url,
            ApiKey = _protector.Protect(plaintextKey)
        };
        db.Accounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private SlideshowLinkEntity SeedLink(AppDbContext db, string slug, Guid accountId, bool enabled = true, bool showFavorites = false)
    {
        var link = new SlideshowLinkEntity
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = slug,
            AccountId = accountId,
            Enabled = enabled,
            ShowFavorites = showFavorites
        };
        db.SlideshowLinks.Add(link);
        db.SaveChanges();
        return link;
    }

    [Test]
    public void Reload_EnabledLinkWithAccount_IsServable_WithDecryptedKeyAndLinkFilters()
    {
        using var db = NewDb();
        var account = SeedAccount(db, "http://immich.invalid", "plaintext-key");
        SeedLink(db, "family", account.Id, enabled: true, showFavorites: true);

        var (mgr, built) = NewManager();
        mgr.Reload(db);

        var entry = mgr.Get("family");
        Assert.That(entry, Is.Not.Null, "enabled link with a live account must be servable after reload");
        Assert.That(entry!.Logic.AccountSettings.ImmichServerUrl, Is.EqualTo("http://immich.invalid"));
        // API key must be decrypted for the scoped logic, not handed the stored ciphertext.
        Assert.That(entry.Logic.AccountSettings.ApiKey, Is.EqualTo("plaintext-key"));
        // Content filters come from the link, not the account.
        Assert.That(entry.Logic.AccountSettings.ShowFavorites, Is.True);
        Assert.That(built, Has.Count.EqualTo(1));
    }

    [Test]
    public void Reload_LinkReferencingMissingAccount_IsSkipped()
    {
        using var db = NewDb();
        // Link points at an account id that does not exist (e.g. the account was deleted).
        SeedLink(db, "orphan", Guid.NewGuid(), enabled: true);

        var (mgr, built) = NewManager();
        mgr.Reload(db);

        Assert.That(mgr.Get("orphan"), Is.Null, "a link with no backing account must not be servable");
        Assert.That(built, Is.Empty, "no scoped logic should be built for an orphan link");
    }

    [Test]
    public void Reload_DisabledLink_IsNotServable()
    {
        using var db = NewDb();
        var account = SeedAccount(db, "http://immich.invalid", "k");
        SeedLink(db, "hidden", account.Id, enabled: false);

        var (mgr, _) = NewManager();
        mgr.Reload(db);

        Assert.That(mgr.Get("hidden"), Is.Null, "disabled links must be excluded from the slug map");
    }

    [Test]
    public void Reload_LookupIsCaseInsensitive()
    {
        using var db = NewDb();
        var account = SeedAccount(db, "http://immich.invalid", "k");
        SeedLink(db, "family", account.Id);

        var (mgr, _) = NewManager();
        mgr.Reload(db);

        Assert.That(mgr.Get("FAMILY"), Is.Not.Null);
    }

    [Test]
    public void Reload_ReplacesOldMap_RemovedLinkNoLongerServable_NewLinkServable()
    {
        using var db = NewDb();
        var account = SeedAccount(db, "http://immich.invalid", "k");
        var first = SeedLink(db, "first", account.Id);

        var (mgr, _) = NewManager();
        mgr.Reload(db);
        Assert.That(mgr.Get("first"), Is.Not.Null);

        // Remove the first link, add a second, and reload: the old entry must be gone and the new
        // entry present after the atomic swap.
        db.SlideshowLinks.Remove(db.SlideshowLinks.Single(l => l.Id == first.Id));
        db.SaveChanges();
        SeedLink(db, "second", account.Id);

        using var db2 = NewDb();
        mgr.Reload(db2);

        Assert.That(mgr.Get("first"), Is.Null, "the removed link must no longer be servable after reload");
        Assert.That(mgr.Get("second"), Is.Not.Null, "the newly added link must be servable after reload");
    }

    [Test]
    public void Reload_NewAccountAndLink_BecomesServable_WithoutRestart()
    {
        // Start empty: no links at all.
        var (mgr, _) = NewManager();
        using (var db = NewDb())
        {
            mgr.Reload(db);
        }
        Assert.That(mgr.Get("later"), Is.Null);

        // Add an account + enabled link, reload against a fresh context (as ConfigReloadService does).
        using (var db = NewDb())
        {
            var account = SeedAccount(db, "http://new.invalid", "k2");
            SeedLink(db, "later", account.Id);
            mgr.Reload(db);
        }

        Assert.That(mgr.Get("later"), Is.Not.Null, "a link added at runtime must be servable after reload, no restart");
    }
}
