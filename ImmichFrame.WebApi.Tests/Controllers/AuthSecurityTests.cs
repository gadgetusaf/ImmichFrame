using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers
{
    /// <summary>
    /// Pins the FIXED behavior of the global auth gate and the "auth" brute-force rate limiter.
    ///
    /// Covers two findings that previously had zero coverage:
    ///  - C2: the global bearer-secret gate (CustomAuthenticationMiddleware bypass prefixes +
    ///        ImmichFrameAuthenticationHandler: right/wrong/missing Bearer, and the opt-in
    ///        IMMICHFRAME_REQUIRE_SECRET "links-only" mode).
    ///  - C25: the fixed-window per-IP "auth" rate-limit policy guarding credential endpoints
    ///        (10 attempts / 5 min => 11th request is 429).
    ///
    /// These are deliberately in a NEW fixture (not appended to AssetControllerTests /
    /// AdminEndpointsTests) to avoid collisions with concurrent test-writers.
    /// </summary>
    [TestFixture]
    public class AuthSecurityTests
    {
        // ---- shared settings builders -----------------------------------------------------------

        private static GeneralSettings BuildGeneralSettings(string? authenticationSecret)
        {
            return new GeneralSettings
            {
                ShowWeatherDescription = false,
                WeatherIconUrl = "https://openweathermap.org/img/wn/{IconId}.png",
                ShowClock = true,
                ClockFormat = "HH:mm",
                ClockDateFormat = "eee, MMM d",
                Language = "en",
                PhotoDateFormat = "MM/dd/yyyy",
                ImageLocationFormat = "City,State,Country",
                DownloadImages = false,
                RenewImagesDuration = 30,
                PrimaryColor = "#FFFFFF",
                SecondaryColor = "#000000",
                Style = "none",
                BaseFontSize = "16px",
                WeatherApiKey = "",
                UnitSystem = "imperial",
                WeatherLatLong = "0,0",
                AuthenticationSecret = authenticationSecret
            };
        }

        private static ServerSettings BuildServerSettings(string? authenticationSecret)
        {
            var general = BuildGeneralSettings(authenticationSecret);
            var account = new ServerAccountSettings
            {
                ImmichServerUrl = "http://mock-immich-server.com",
                ApiKey = "test-api-key",
                ShowMemories = false,
                ShowFavorites = true,
                ShowArchived = false,
                Albums = new List<Guid>(),
                ExcludedAlbums = new List<Guid>(),
                People = new List<Guid>()
            };
            return new ServerSettings
            {
                GeneralSettingsImpl = general,
                AccountsImpl = new List<ServerAccountSettings> { account }
            };
        }

        /// <summary>
        /// Boots Program with a concrete IServerSettings whose AuthenticationSecret is
        /// <paramref name="authenticationSecret"/>. The global bearer gate
        /// (ImmichFrameAuthenticationHandler) reads that value at construction time.
        /// </summary>
        private static WebApplicationFactory<Program> CreateFactory(string? authenticationSecret)
        {
            var serverSettings = BuildServerSettings(authenticationSecret);
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IServerSettings>(serverSettings);
                    services.AddSingleton<IGeneralSettings>(serverSettings.GeneralSettingsImpl!);
                });
            });
        }

        // =========================================================================================
        // C2 — global bearer-secret gate on the content API (/api/Asset, /api/Config/Version, ...)
        //
        // Target endpoint is GET /api/Config/Version: it is [Authorize]-protected (so the bearer
        // gate applies) yet needs no upstream Immich call or live DB, making it a clean probe of the
        // gate. NOTE: bare GET /api/Config is intentionally anonymous, so it is NOT a valid probe.
        // =========================================================================================

        [Test]
        public async Task ContentApi_WithSecretConfigured_And_CorrectBearer_IsAuthorized()
        {
            using var factory = CreateFactory("s3cr3t-value");
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "s3cr3t-value");

            // /api/Config/Version is [Authorize] and cheap (no upstream Immich call). A correct
            // bearer secret must pass the gate; anything other than 401 means the gate accepted us.
            var response = await client.GetAsync("/api/Config/Version");

            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized),
                "a correct Bearer secret must satisfy the global auth gate");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task ContentApi_WithSecretConfigured_And_WrongBearer_Returns401()
        {
            using var factory = CreateFactory("s3cr3t-value");
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "totally-wrong");

            var response = await client.GetAsync("/api/Config/Version");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "a wrong Bearer secret must be rejected by the global auth gate");
        }

        [Test]
        public async Task ContentApi_WithSecretConfigured_And_MissingAuthorizationHeader_Returns401()
        {
            using var factory = CreateFactory("s3cr3t-value");
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/Config/Version");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "the content API must not be anonymously reachable once a secret is configured");
        }

        [Test]
        public async Task ContentApi_WithSecretConfigured_And_NonBearerScheme_Returns401()
        {
            using var factory = CreateFactory("s3cr3t-value");
            var client = factory.CreateClient();
            // Presenting the secret under the wrong scheme (Basic) must NOT authenticate.
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", "s3cr3t-value");

            var response = await client.GetAsync("/api/Config/Version");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                "only the Bearer scheme is accepted for the AuthenticationSecret");
        }

        [Test]
        public async Task ContentApi_WithNoSecret_LegacyOpen_IsReachableWithoutAuth()
        {
            // Legacy default: no AuthenticationSecret and REQUIRE_SECRET unset => content API is open.
            Assert.That(Environment.GetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET"),
                Is.Null.Or.Empty, "test env must not have REQUIRE_SECRET set for the legacy path");

            using var factory = CreateFactory(null);
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/Config/Version");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "with no secret and REQUIRE_SECRET unset the content API stays anonymously open (legacy)");
        }

        // ---- bypass prefixes: cookie-authed / public-link surfaces must skip the bearer gate ----

        [Test]
        public async Task AdminSurface_IsExemptFromBearerGate_EvenWhenSecretConfigured()
        {
            // /api/admin uses cookie auth and MUST bypass the global bearer gate. With a secret
            // configured, an unauthenticated /api/admin/me must still return 401 from the COOKIE
            // policy (not be short-circuited by the bearer handler) — i.e. the endpoint is reachable.
            using var factory = CreateFactory("s3cr3t-value");
            var client = factory.CreateClient();

            var me = await client.GetAsync("/api/admin/me");
            Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            // setup-required is an anonymous admin endpoint; if the bypass works it returns 200
            // WITHOUT any Bearer header even though a secret is configured.
            var setupRequired = await client.GetAsync("/api/admin/setup-required");
            Assert.That(setupRequired.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "/api/admin/* must bypass the global bearer gate (cookie-auth surface)");
        }

        [Test]
        public async Task SlideshowSurface_IsExemptFromBearerGate_EvenWhenSecretConfigured()
        {
            // Public /api/slideshow/* links enforce their own slug-bound token and MUST bypass the
            // global bearer gate. Resolving an unknown slug should reach the controller (NotFound),
            // NOT be blocked with 401 by the bearer handler.
            using var factory = CreateFactory("s3cr3t-value");
            var client = factory.CreateClient();

            var response = await client.GetAsync("/api/slideshow/does-not-exist");

            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized),
                "/api/slideshow/* must bypass the global bearer gate (public-link surface)");
        }

        // ---- IMMICHFRAME_REQUIRE_SECRET links-only mode -----------------------------------------

        [Test]
        public async Task RequireSecretMode_LocksContentApi_ButKeepsBypassSurfacesReachable()
        {
            // The env var is read in the auth handler constructor, so it must be set BEFORE the host
            // is built. Set/restore around this test only.
            var previous = Environment.GetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET");
            Environment.SetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET", "true");
            try
            {
                // No AuthenticationSecret configured, but REQUIRE_SECRET flips the "open" default to
                // "locked": the global content API must now be refused.
                using var factory = CreateFactory(null);
                var client = factory.CreateClient();

                var content = await client.GetAsync("/api/Config/Version");
                Assert.That(content.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                    "REQUIRE_SECRET=true with no secret must lock the global content API");

                // Bypass surfaces stay reachable: /api/admin/setup-required (anonymous) still 200s.
                var admin = await client.GetAsync("/api/admin/setup-required");
                Assert.That(admin.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                    "/api/admin/* must remain reachable in links-only mode");

                // Public slideshow links stay reachable (unknown slug => not a 401 from the gate).
                var slideshow = await client.GetAsync("/api/slideshow/does-not-exist");
                Assert.That(slideshow.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized),
                    "/api/slideshow/* must remain reachable in links-only mode");
            }
            finally
            {
                Environment.SetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET", previous);
            }
        }

        [Test]
        public async Task RequireSecretMode_FalseValue_DoesNotLockContentApi()
        {
            // A non-truthy value must NOT engage links-only mode (guards against inverting the check).
            var previous = Environment.GetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET");
            Environment.SetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET", "false");
            try
            {
                using var factory = CreateFactory(null);
                var client = factory.CreateClient();

                var content = await client.GetAsync("/api/Config/Version");
                Assert.That(content.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                    "REQUIRE_SECRET=false must preserve the legacy open behavior");
            }
            finally
            {
                Environment.SetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET", previous);
            }
        }

        // =========================================================================================
        // C25 — "auth" fixed-window rate limiter on credential endpoints
        //        TestServer's RemoteIpAddress is null, so every request lands in the same "unknown"
        //        partition. PermitLimit = 10 => the 11th request in the window is rejected with 429.
        // =========================================================================================

        [Test]
        public async Task AdminLogin_EleventhAttempt_IsRateLimitedWith429()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
                });
            });

            var client = factory.CreateClient();

            // Create an admin so /api/admin/login validates credentials (wrong password => 401),
            // exercising the endpoint body rather than short-circuiting before the limiter.
            var setup = await client.PostAsJsonAsync("/api/admin/setup",
                new { username = "admin", password = "pw" });
            setup.EnsureSuccessStatusCode();

            // The setup POST above already consumed 1 permit from the shared "auth" window.
            // The window allows 10; so up to 9 more login attempts succeed (as 401), and the
            // 10th login attempt (11th credential POST overall) must be rejected with 429.
            HttpResponseMessage? rejected = null;
            var sawUnauthorized = false;
            for (var i = 0; i < 15; i++)
            {
                var res = await client.PostAsJsonAsync("/api/admin/login",
                    new { username = "admin", password = "wrong" });

                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rejected = res;
                    break;
                }

                Assert.That(res.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                    "before the window is exhausted, wrong-password logins return 401");
                sawUnauthorized = true;
            }

            Assert.That(sawUnauthorized, Is.True, "at least one attempt should pass the limiter and hit the endpoint");
            Assert.That(rejected, Is.Not.Null,
                "the 'auth' rate limiter must reject credential brute-force with 429 within 15 attempts");
            Assert.That(rejected!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        }

        [Test]
        public async Task SlideshowUnlock_IsRateLimited_WithinTheAuthWindow()
        {
            // /api/slideshow/{slug}/unlock also carries [EnableRateLimiting("auth")]. An unknown slug
            // returns 401/404 from the controller until the shared "auth" window is exhausted, then 429.
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<DbContextOptions>();
                    services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
                });
            });

            var client = factory.CreateClient();

            HttpResponseMessage? rejected = null;
            for (var i = 0; i < 15; i++)
            {
                var res = await client.PostAsJsonAsync("/api/slideshow/no-such-slug/unlock",
                    new { pin = "0000" });

                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rejected = res;
                    break;
                }
            }

            Assert.That(rejected, Is.Not.Null,
                "the unlock endpoint must be guarded by the same 'auth' rate-limit policy");
            Assert.That(rejected!.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        }
    }
}
