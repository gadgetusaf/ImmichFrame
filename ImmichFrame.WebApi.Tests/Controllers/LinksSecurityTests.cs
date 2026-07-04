using System.Net;
using System.Net.Http.Json;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers;

/// <summary>
/// Integration tests for the public per-link slideshow surface (/slideshow/{slug}/api/*) and its admin
/// CRUD (/api/admin/links). These pin the FIXED security behaviour: slug-bound cookies, security-stamp
/// rotation revoking issued cookies, PIN unlock, and the scoped-IDOR guard (out-of-scope asset ids 404).
///
/// Each test boots the real Program against an isolated in-memory SQLite database and overrides the
/// per-account logic factory with a Moq stub, so the link's <see cref="IAccountImmichFrameLogic"/> scope
/// checks are deterministic without needing a live Immich server.
/// </summary>
[TestFixture]
public class LinksSecurityTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private SqliteConnection _connection = null!;

    // The stub logic every scoped link resolves to. IsInScope is driven per-test.
    private Mock<IAccountImmichFrameLogic> _logic = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _logic = new Mock<IAccountImmichFrameLogic>();
        // Default: nothing is in scope unless a test opts an id in. Keeps the IDOR guard the star.
        _logic.Setup(l => l.IsInScope(It.IsAny<Guid>())).ReturnsAsync(false);
        _logic.Setup(l => l.GetAssets()).ReturnsAsync(Array.Empty<AssetResponseDto>());
        _logic.Setup(l => l.GetAssetInfoById(It.IsAny<Guid>())).ReturnsAsync(new AssetResponseDto());
        _logic.Setup(l => l.GetScopedAlbumInfoById(It.IsAny<Guid>())).ReturnsAsync(Array.Empty<AlbumResponseDto>());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

                // Every scoped link's logic is our stub, so scope checks are deterministic and no real
                // Immich HTTP happens. The SlideshowLinkManager still exercises ApiKeyProtector.Unprotect
                // on the (real, encrypted) stored key before calling this factory.
                services.RemoveAll<Func<IAccountSettings, IAccountImmichFrameLogic>>();
                services.AddTransient<Func<IAccountSettings, IAccountImmichFrameLogic>>(_ => _ => _logic.Object);
            });
        });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    private sealed record AccountResponse(Guid Id, string ImmichServerUrl, bool HasApiKey, string? ApiKey, List<string> Tags);
    private sealed record AccountSaveResponse(AccountResponse Account, List<string> Warnings);
    private sealed record LinkResponse(Guid Id, string Slug, string Name, Guid AccountId, string AccessPolicy, bool HasPin, bool Enabled);
    private sealed record ResolveResponse(string Name, bool RequiresPin, bool RequiresAuth);

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/admin/setup", new { username = "admin", password = "pw" });
        res.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Guid> CreateAccountAsync(HttpClient admin)
    {
        var create = await admin.PostAsJsonAsync("/api/admin/accounts", new
        {
            immichServerUrl = "http://immich.test.invalid",
            apiKey = "secret-key"
        });
        create.EnsureSuccessStatusCode();
        var body = await create.Content.ReadFromJsonAsync<AccountSaveResponse>();
        return body!.Account.Id;
    }

    private async Task<LinkResponse> CreateLinkAsync(HttpClient admin, Guid accountId, string slug,
        string accessPolicy = SlideshowAccess.None, string? pin = null, string name = "A Link")
    {
        var res = await admin.PostAsJsonAsync("/api/admin/links", new
        {
            slug,
            name,
            accountId,
            accessPolicy,
            pin
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<LinkResponse>())!;
    }

    /// <summary>
    /// Seeds an account + PIN link directly into the DB and reloads the live manager, WITHOUT touching
    /// the rate-limited admin endpoints. Used by the rate-limit test so its "auth" partition counter
    /// starts clean (admin setup/login also live under the "auth" policy).
    /// </summary>
    private void SeedPinLink(string slug, string pin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PinHasher>();

        var account = new AccountEntity { Id = Guid.NewGuid(), ImmichServerUrl = "http://immich.test.invalid", ApiKey = "seed-key" };
        db.Accounts.Add(account);
        db.SlideshowLinks.Add(new SlideshowLinkEntity
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Name = "Seeded",
            AccountId = account.Id,
            AccessPolicy = SlideshowAccess.Pin,
            PinHash = hasher.Hash(pin),
            Enabled = true
        });
        db.SaveChanges();
        scope.ServiceProvider.GetRequiredService<SlideshowLinkManager>().Reload(db);
    }

    /// <summary>Resolves a public (None) link and returns the client carrying its path-scoped cookie.</summary>
    private async Task<HttpClient> PublicClientWithCookieAsync(string slug)
    {
        var client = _factory.CreateClient();
        var resolve = await client.GetAsync($"/api/slideshow/{slug}");
        resolve.EnsureSuccessStatusCode();
        return client;
    }

    // -------- public (None) link: cookie issuance, slug binding, no-cookie --------

    [Test]
    public async Task Content_WithoutCookie_Returns401()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var anon = _factory.CreateClient();
        var res = await anon.GetAsync("/slideshow/family/api/Asset");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Resolve_PublicLink_IssuesCookie_ThatUnlocksContent()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var client = await PublicClientWithCookieAsync("family");
        var res = await client.GetAsync("/slideshow/family/api/Asset");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Cookie_ForOneSlug_DoesNotUnlockAnotherSlug()
    {
        // The core cross-link isolation guarantee: a token minted for "family" must not authorize
        // "vacation" even though the token itself is otherwise valid and unexpired. We forge the exact
        // cookie the server would issue for family (via the real SlideshowTokenService bound to family's
        // current stamp) and replay it against /vacation to prove the slug binding — not just cookie
        // path-scoping — rejects it.
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");
        await CreateLinkAsync(admin, accountId, "vacation");

        string familyToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<SlideshowTokenService>();
            var links = scope.ServiceProvider.GetRequiredService<SlideshowLinkManager>();
            var familyStamp = links.Get("family")!.Link.SecurityStamp;
            familyToken = tokens.Issue("family", familyStamp);
        }

        // Sanity: the token is genuinely valid for its own slug.
        var ownClient = _factory.CreateClient();
        ownClient.DefaultRequestHeaders.Add("Cookie", $"immichframe_slideshow={familyToken}");
        Assert.That((await ownClient.GetAsync("/slideshow/family/api/Asset")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "the forged token must be valid for its own slug");

        // But it must be rejected on a different slug.
        var forged = _factory.CreateClient();
        forged.DefaultRequestHeaders.Add("Cookie", $"immichframe_slideshow={familyToken}");
        var res = await forged.GetAsync("/slideshow/vacation/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    // -------- PIN links --------

    [Test]
    public async Task Resolve_PinLink_RequiresPin_AndIssuesNoCookie()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "secret", SlideshowAccess.Pin, pin: "1234");

        var client = _factory.CreateClient();
        var resolve = await client.GetFromJsonAsync<ResolveResponse>("/api/slideshow/secret");
        Assert.That(resolve!.RequiresPin, Is.True);

        // Resolve must not have handed out a cookie for a PIN link.
        var res = await client.GetAsync("/slideshow/secret/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Unlock_WithCorrectPin_IssuesCookie()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "secret", SlideshowAccess.Pin, pin: "1234");

        var client = _factory.CreateClient();
        var unlock = await client.PostAsJsonAsync("/api/slideshow/secret/unlock", new { pin = "1234" });
        unlock.EnsureSuccessStatusCode();

        var res = await client.GetAsync("/slideshow/secret/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Unlock_WithWrongPin_Returns401_AndIssuesNoCookie()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "secret", SlideshowAccess.Pin, pin: "1234");

        var client = _factory.CreateClient();
        var unlock = await client.PostAsJsonAsync("/api/slideshow/secret/unlock", new { pin = "0000" });
        Assert.That(unlock.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var res = await client.GetAsync("/slideshow/secret/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Unlock_OnPublicLink_IsRejected()
    {
        // A None link must never mint a cookie through /unlock — that path is PIN-only.
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var client = _factory.CreateClient();
        var unlock = await client.PostAsJsonAsync("/api/slideshow/family/unlock", new { pin = "whatever" });
        Assert.That(unlock.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // -------- scoped IDOR guard --------

    [Test]
    public async Task AssetInfo_ForOutOfScopeId_Returns404()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var outOfScope = Guid.NewGuid();
        // Default stub already returns IsInScope=false for everything.
        var client = await PublicClientWithCookieAsync("family");

        var res = await client.GetAsync($"/slideshow/family/api/Asset/{outOfScope}/AssetInfo");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        _logic.Verify(l => l.GetAssetInfoById(outOfScope), Times.Never,
            "an out-of-scope id must be rejected before any metadata lookup");
    }

    [Test]
    public async Task AlbumInfo_ForOutOfScopeId_Returns404()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var outOfScope = Guid.NewGuid();
        var client = await PublicClientWithCookieAsync("family");

        var res = await client.GetAsync($"/slideshow/family/api/Asset/{outOfScope}/AlbumInfo");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        _logic.Verify(l => l.GetScopedAlbumInfoById(outOfScope), Times.Never);
    }

    [Test]
    public async Task AssetInfo_ForInScopeId_Returns200()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var inScope = Guid.NewGuid();
        _logic.Setup(l => l.IsInScope(inScope)).ReturnsAsync(true);
        _logic.Setup(l => l.GetAssetInfoById(inScope)).ReturnsAsync(new AssetResponseDto());

        var client = await PublicClientWithCookieAsync("family");
        var res = await client.GetAsync($"/slideshow/family/api/Asset/{inScope}/AssetInfo");

        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // -------- security-stamp rotation (revokes issued cookies) --------

    [Test]
    public async Task ChangingPin_RotatesStamp_AndRevokesIssuedCookie()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, accountId, "secret", SlideshowAccess.Pin, pin: "1234");

        // Hold a valid cookie for the current stamp.
        var viewer = _factory.CreateClient();
        var unlock = await viewer.PostAsJsonAsync("/api/slideshow/secret/unlock", new { pin = "1234" });
        unlock.EnsureSuccessStatusCode();
        Assert.That((await viewer.GetAsync("/slideshow/secret/api/Asset")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "cookie should work before the PIN change");

        // Admin changes the PIN -> stamp rotates -> the old cookie must stop validating.
        var update = await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "secret",
            name = "A Link",
            accountId,
            accessPolicy = SlideshowAccess.Pin,
            pin = "5678"
        });
        update.EnsureSuccessStatusCode();

        var res = await viewer.GetAsync("/slideshow/secret/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "rotating the stamp must revoke the previously-issued 30-day cookie");
    }

    [Test]
    public async Task Rename_DoesNotRotateStamp_AndKeepsCookieValid()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, accountId, "family", name: "Original");

        var viewer = await PublicClientWithCookieAsync("family");
        Assert.That((await viewer.GetAsync("/slideshow/family/api/Asset")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        // Renaming (no policy/PIN/enabled change) must NOT rotate the stamp, so the cookie still works.
        var update = await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "family",
            name = "Renamed",
            accountId,
            accessPolicy = SlideshowAccess.None
        });
        update.EnsureSuccessStatusCode();

        var res = await viewer.GetAsync("/slideshow/family/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "a pure rename must not revoke existing cookies");
    }

    [Test]
    public async Task DisablingLink_MakesItUnresolvable_AndRevokesCookie()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, accountId, "family");

        var viewer = await PublicClientWithCookieAsync("family");
        Assert.That((await viewer.GetAsync("/slideshow/family/api/Asset")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        // Disable the link: it drops out of the live manager AND the stamp rotates.
        var update = await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "family",
            name = "A Link",
            accountId,
            accessPolicy = SlideshowAccess.None,
            enabled = false
        });
        update.EnsureSuccessStatusCode();

        // The disabled link is no longer served at all.
        Assert.That((await viewer.GetAsync("/slideshow/family/api/Asset")).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await _factory.CreateClient().GetAsync("/api/slideshow/family")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DisableThenReEnable_RotatesStamp_SoOldCookieStaysRevoked()
    {
        // The disable->re-enable case: rotation-on-disable is what keeps a pre-disable cookie dead even
        // after the link comes back (the manager alone would otherwise re-admit the old cookie).
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, accountId, "family");

        var viewer = await PublicClientWithCookieAsync("family");

        await (await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "family", name = "A Link", accountId, accessPolicy = SlideshowAccess.None, enabled = false
        })).Content.ReadAsStringAsync();

        var reEnable = await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "family", name = "A Link", accountId, accessPolicy = SlideshowAccess.None, enabled = true
        });
        reEnable.EnsureSuccessStatusCode();

        // Link is resolvable again...
        Assert.That((await _factory.CreateClient().GetAsync("/api/slideshow/family")).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        // ...but the pre-disable cookie stays revoked because the stamp rotated on disable.
        var res = await viewer.GetAsync("/slideshow/family/api/Asset");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task StampInDb_ChangesOnPinChange_ButNotOnRename()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, accountId, "secret", SlideshowAccess.Pin, pin: "1234");

        string StampFor(Guid id)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return db.SlideshowLinks.AsNoTracking().Single(l => l.Id == id).SecurityStamp;
        }

        var original = StampFor(link.Id);

        // Rename only: stamp stable.
        (await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "secret", name = "Renamed", accountId, accessPolicy = SlideshowAccess.Pin
        })).EnsureSuccessStatusCode();
        Assert.That(StampFor(link.Id), Is.EqualTo(original), "rename must not rotate the stamp");

        // PIN change: stamp rotates.
        (await admin.PutAsJsonAsync($"/api/admin/links/{link.Id}", new
        {
            slug = "secret", name = "Renamed", accountId, accessPolicy = SlideshowAccess.Pin, pin = "9999"
        })).EnsureSuccessStatusCode();
        Assert.That(StampFor(link.Id), Is.Not.EqualTo(original), "changing the PIN must rotate the stamp");
    }

    // -------- admin CRUD validation --------

    [Test]
    public async Task CreateLink_WithoutAuth_Returns401()
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/admin/links", new { slug = "x", name = "X", accountId = Guid.NewGuid() });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task CreateLink_PinPolicyWithoutPin_Returns400()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var res = await admin.PostAsJsonAsync("/api/admin/links", new
        {
            slug = "secret", name = "Secret", accountId, accessPolicy = SlideshowAccess.Pin
        });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateLink_DuplicateSlug_Returns409()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        await CreateLinkAsync(admin, accountId, "family");

        var res = await admin.PostAsJsonAsync("/api/admin/links", new
        {
            slug = "family", name = "Dup", accountId, accessPolicy = SlideshowAccess.None
        });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task CreateLink_NormalizesSlug()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);

        var created = await CreateLinkAsync(admin, accountId, "My Family  Photos!");
        Assert.That(created.Slug, Is.EqualTo("my-family-photos"),
            "slug must be lowercased, space/underscore->dash, collapsed, and stripped of non-[a-z0-9-]");
    }

    [Test]
    public async Task CreateLink_UnknownAccessPolicy_Returns400()
    {
        var admin = await AdminClientAsync();
        var accountId = await CreateAccountAsync(admin);
        var res = await admin.PostAsJsonAsync("/api/admin/links", new
        {
            slug = "weird", name = "Weird", accountId, accessPolicy = "Superuser"
        });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "an unknown policy must fail closed, not silently downgrade to public");
    }

    // -------- rate limiting on PIN unlock --------

    [Test]
    public async Task Unlock_Over10AttemptsInWindow_Returns429()
    {
        // Seed directly rather than via the admin API: /api/admin/setup + /login also use the "auth"
        // rate-limit policy and share the (single, socket-less) test partition, which would eat permits
        // and make the 11th-attempt boundary flaky.
        SeedPinLink("secret", "1234");

        var client = _factory.CreateClient();

        // The "auth" policy permits 10 requests per 5-minute window per client IP.
        for (var i = 0; i < 10; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/slideshow/secret/unlock", new { pin = "0000" });
            Assert.That(attempt.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                $"attempt {i + 1} should be a normal wrong-PIN 401, not throttled yet");
        }

        var eleventh = await client.PostAsJsonAsync("/api/slideshow/secret/unlock", new { pin = "0000" });
        Assert.That(eleventh.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests),
            "the 11th unlock attempt in the window must be rate-limited");
    }
}
