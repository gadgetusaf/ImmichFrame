using System.Net;
using System.Net.Http.Json;
using ImmichFrame.WebApi.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers;

/// <summary>
/// Integration tests for the admin/config API. Each test boots the real Program against an isolated
/// in-memory SQLite database (one shared open connection per test) and exercises the HTTP surface.
/// </summary>
[TestFixture]
public class AdminEndpointsTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private SqliteConnection _connection = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
            });
        });
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        _connection.Dispose();
    }

    private sealed record SetupRequiredResponse(bool SetupRequired);
    private sealed record UserResponse(string Username);
    private sealed record AccountResponse(Guid Id, string ImmichServerUrl, bool HasApiKey, string? ApiKey, List<string> Tags);
    private sealed record AccountSaveResponse(AccountResponse Account, List<string> Warnings);

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/admin/setup", new { username = "admin", password = "pw" });
        res.EnsureSuccessStatusCode();
        return client;
    }

    [Test]
    public async Task SetupRequired_IsTrue_OnEmptyDatabase()
    {
        var client = _factory.CreateClient();
        var body = await client.GetFromJsonAsync<SetupRequiredResponse>("/api/admin/setup-required");
        Assert.That(body!.SetupRequired, Is.True);
    }

    [Test]
    public async Task Me_WithoutAuthentication_Returns401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/admin/me");
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Setup_CreatesAdmin_AndAuthenticatesViaCookie()
    {
        var client = await AuthenticatedClientAsync();

        var me = await client.GetFromJsonAsync<UserResponse>("/api/admin/me");
        Assert.That(me!.Username, Is.EqualTo("admin"));

        var setupRequired = await client.GetFromJsonAsync<SetupRequiredResponse>("/api/admin/setup-required");
        Assert.That(setupRequired!.SetupRequired, Is.False);
    }

    [Test]
    public async Task Setup_WhenAdminAlreadyExists_ReturnsConflict()
    {
        await AuthenticatedClientAsync();
        var second = _factory.CreateClient();
        var res = await second.PostAsJsonAsync("/api/admin/setup", new { username = "other", password = "pw2" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await AuthenticatedClientAsync();
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/admin/login", new { username = "admin", password = "wrong" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_WithCorrectPassword_Succeeds()
    {
        await AuthenticatedClientAsync();
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/admin/login", new { username = "admin", password = "pw" });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PutGeneral_WithoutAuthentication_Returns401()
    {
        var client = _factory.CreateClient();
        var res = await client.PutAsJsonAsync("/api/admin/general", new { interval = 5 });
        Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task PutGeneral_PersistsAndIsReflectedInPublicConfig()
    {
        var client = await AuthenticatedClientAsync();

        var current = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/admin/general");
        current!["interval"] = 99;
        var put = await client.PutAsJsonAsync("/api/admin/general", current);
        put.EnsureSuccessStatusCode();

        // The public, unauthenticated config endpoint should reflect the change live.
        var publicConfig = await client.GetFromJsonAsync<Dictionary<string, object>>("/api/Config");
        Assert.That(publicConfig!["interval"].ToString(), Is.EqualTo("99"));
    }

    [Test]
    public async Task CreateAccount_EncryptsKeyAtRest_AndMasksOnRead()
    {
        var client = await AuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/admin/accounts", new
        {
            immichServerUrl = "http://immich.test.invalid",
            apiKey = "super-secret-key",
            showFavorites = true,
            tags = new[] { "holidays" }
        });
        create.EnsureSuccessStatusCode();

        // Read back: key must be masked.
        var accounts = await client.GetFromJsonAsync<List<AccountResponse>>("/api/admin/accounts");
        Assert.That(accounts!, Has.Count.EqualTo(1));
        Assert.That(accounts[0].HasApiKey, Is.True);
        Assert.That(accounts[0].ApiKey, Is.Null);
        Assert.That(accounts[0].Tags, Does.Contain("holidays"));

        // Stored value must be ciphertext, not the plaintext key.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = db.Accounts.Single();
        Assert.That(stored.ApiKey, Is.Not.EqualTo("super-secret-key"));
        Assert.That(stored.ApiKey, Does.StartWith("CfDJ8"), "expected an ASP.NET Data Protection payload");
    }

    [Test]
    public async Task UpdateAccount_WithBlankKey_KeepsExistingKey()
    {
        var client = await AuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/admin/accounts", new
        {
            immichServerUrl = "http://immich.test.invalid",
            apiKey = "original-key"
        });
        var created = (await create.Content.ReadFromJsonAsync<AccountSaveResponse>())!.Account;

        string ciphertextBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            ciphertextBefore = scope.ServiceProvider.GetRequiredService<AppDbContext>().Accounts.Single().ApiKey;
        }

        var update = await client.PutAsJsonAsync($"/api/admin/accounts/{created!.Id}", new
        {
            immichServerUrl = "http://immich.renamed.invalid",
            apiKey = "" // blank => keep existing
        });
        update.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var stored = scope.ServiceProvider.GetRequiredService<AppDbContext>().Accounts.Single();
            Assert.That(stored.ImmichServerUrl, Is.EqualTo("http://immich.renamed.invalid"));
            Assert.That(stored.ApiKey, Is.EqualTo(ciphertextBefore), "blank key on update must preserve the stored key");
        }
    }

    [Test]
    public async Task DeleteAccount_RemovesIt()
    {
        var client = await AuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/admin/accounts", new
        {
            immichServerUrl = "http://immich.test.invalid",
            apiKey = "k"
        });
        var created = (await create.Content.ReadFromJsonAsync<AccountSaveResponse>())!.Account;

        var del = await client.DeleteAsync($"/api/admin/accounts/{created!.Id}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var accounts = await client.GetFromJsonAsync<List<AccountResponse>>("/api/admin/accounts");
        Assert.That(accounts!, Is.Empty);
    }
}
