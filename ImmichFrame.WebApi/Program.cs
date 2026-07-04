using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using System.Reflection;
using ImmichFrame.Core.Logic;
using ImmichFrame.Core.Logic.AccountSelection;
using ImmichFrame.WebApi.Helpers.Config;
using ImmichFrame.WebApi.Persistence;
using ImmichFrame.WebApi.Persistence.Entities;
using ImmichFrame.WebApi.Helpers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// In Development, load docker/.env before any environment variable is read below (config paths,
// LOG_LEVEL, trusted proxies, first-boot config import and the admin bootstrap all consume env vars).
if (builder.Environment.IsDevelopment())
{
    var dotenv = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "docker", ".env"));
    DotEnv.Load(dotenv);
}

//log the version number
var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
Console.WriteLine($@"
 _                     _      _    ______                        
(_)                   (_)    | |   |  ___|                       
 _ _ __ ___  _ __ ___  _  ___| |__ | |_ _ __ __ _ _ __ ___   ___ 
| | '_ ` _ \| '_ ` _ \| |/ __| '_ \|  _| '__/ _` | '_ ` _ \ / _ \
| | | | | | | | | | | | | (__| | | | | | | | (_| | | | | | |  __/
|_|_| |_| |_|_| |_| |_|_|\___|_| |_\_| |_|  \__,_|_| |_| |_|\___| Version {version}");
Console.WriteLine();

// Add services to the container.
builder.Services.AddLogging(builder =>
{
    LogLevel level = LogLevel.Information;
    var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
    if (!string.IsNullOrWhiteSpace(logLevel))
    {
        if (!Enum.TryParse(logLevel, true, out level) || !Enum.IsDefined(level))
            level = LogLevel.Information;
    }

    Console.WriteLine($"LogLevel: {level}");
    builder.SetMinimumLevel(level);
    builder.AddSimpleConsole(options =>
    {
        // Customizing the log output format
        options.TimestampFormat = "yy-MM-dd HH:mm:ss "; // Custom timestamp format
        options.SingleLine = true;
    });

    // Disable SpaProxy info logs
    builder.AddFilter("Microsoft.AspNetCore.SpaProxy", LogLevel.Warning);
    // Disable AspNetCore info logs
    builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
});


// Setup Config
var configPath = Environment.GetEnvironmentVariable("IMMICHFRAME_CONFIG_PATH") ??
        Directory.EnumerateDirectories(AppDomain.CurrentDomain.BaseDirectory, "*", SearchOption.TopDirectoryOnly)
        .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Config", StringComparison.OrdinalIgnoreCase))
        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");

// Runtime configuration now lives in a SQLite database. By default it sits inside the Config
// directory so existing config-volume mounts persist it; override with IMMICHFRAME_DB_PATH.
var dbPath = Environment.GetEnvironmentVariable("IMMICHFRAME_DB_PATH") ?? Path.Combine(configPath, "immichframe.db");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

// Encrypt Immich API keys before storing them in the database. The Data Protection key ring persists
// unencrypted in the Config volume (alongside immichframe.db) so ciphertext stays decryptable across
// restarts — this hides keys from a casual DB dump, but anyone who can read the whole Config volume
// holds both the ciphertext and the keys. Filesystem permissions on that volume are the real
// protection for stored credentials and slideshow tokens.
var dataProtectionDir = Path.Combine(configPath, "dataprotection-keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDir))
    .SetApplicationName("ImmichFrame");
builder.Services.AddSingleton<ApiKeyProtector>();

builder.Services.AddTransient<ConfigLoader>();
builder.Services.AddSingleton<ConfigImporter>();

// Database-backed settings. The snapshot is populated during startup (see below) before any
// request is served, preserving the legacy "config is loaded once at startup" behaviour.
builder.Services.AddSingleton<DatabaseServerSettings>();
builder.Services.AddSingleton<IServerSettings>(srv => srv.GetRequiredService<DatabaseServerSettings>());

// Register sub-settings as a live façade over the current snapshot so saved changes apply without a restart.
builder.Services.AddSingleton<IGeneralSettings, LiveGeneralSettings>();

// Admin authentication for the configuration UI.
builder.Services.AddSingleton<IPasswordHasher<UserEntity>, PasswordHasher<UserEntity>>();
builder.Services.AddScoped<AdminAuthService>();

// Applies account/config changes to the running app without a restart.
builder.Services.AddScoped<ConfigReloadService>();

// Lists albums/people from an Immich server for the account editor's pickers.
builder.Services.AddTransient<ImmichBrowseService>();

// Public per-link slideshows.
builder.Services.AddSingleton<PinHasher>();
builder.Services.AddSingleton<SlideshowTokenService>();
builder.Services.AddSingleton<SlideshowLinkManager>();

// Register services
builder.Services.AddSingleton<IWeatherService, OpenWeatherMapService>();
builder.Services.AddSingleton<ICalendarService, IcalCalendarService>();
builder.Services.AddSingleton<IAssetAccountTracker, BloomFilterAssetAccountTracker>();
builder.Services.AddSingleton<IAccountSelectionStrategy, TotalAccountImagesSelectionStrategy>();
builder.Services.AddHttpClient(); // Ensures IHttpClientFactory is available

builder.Services.AddTransient<Func<IAccountSettings, IAccountImmichFrameLogic>>(srv =>
    account => ActivatorUtilities.CreateInstance<PooledImmichFrameLogic>(srv, account));

// Registered as a concrete singleton too so the reload service can rebuild it on account changes.
builder.Services.AddSingleton<MultiImmichFrameLogicDelegate>();
builder.Services.AddSingleton<IImmichFrameLogic>(srv => srv.GetRequiredService<MultiImmichFrameLogicDelegate>());

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthConstants.AdminPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.AdminCookieScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(UserRoles.Admin);
    });
    options.AddPolicy(AuthConstants.ViewerPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.ViewerCookieScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(UserRoles.Viewer);
    });
});

builder.Services.AddAuthentication("ImmichFrameScheme")
    .AddScheme<AuthenticationSchemeOptions, ImmichFrameAuthenticationHandler>("ImmichFrameScheme", options => { })
    .AddCookie(AuthConstants.AdminCookieScheme, options =>
    {
        options.Cookie.Name = "immichframe_admin";
        options.Cookie.HttpOnly = true;
        // Strict: the admin UI is a same-origin SPA, so the cookie never needs to ride cross-site requests (extra CSRF defence).
        options.Cookie.SameSite = SameSiteMode.Strict;
        // SameAsRequest works behind a TLS-terminating proxy (with UseForwardedHeaders) and on plain HTTP locally.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // This is an API: return status codes rather than redirecting to a login page.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    })
    .AddCookie(AuthConstants.ViewerCookieScheme, options =>
    {
        options.Cookie.Name = "immichframe_viewer";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

// Trust the reverse proxy's X-Forwarded-* headers (TLS terminates upstream).
//
// X-Forwarded-For is only honoured when the request actually arrives from a configured trusted
// proxy. Set IMMICHFRAME_TRUSTED_PROXIES to a comma-separated list of the proxy's own IPs and/or
// CIDR networks (e.g. "10.0.0.5,172.18.0.0/16"). Without this allow-list ASP.NET would trust the
// header from any caller, letting an attacker spoof X-Forwarded-For to mint unlimited rate-limit
// partitions. If the variable is unset/empty we trust nothing and fall back to the real socket
// RemoteIpAddress (the safe default), and UseForwardedHeaders is skipped entirely below.
// IPNetwork is fully qualified to the HttpOverrides type that ForwardedHeadersOptions.KnownNetworks
// expects (System.Net also defines an IPNetwork, so the bare name would be ambiguous here).
var trustedProxies = new List<IPAddress>();
var trustedNetworks = new List<Microsoft.AspNetCore.HttpOverrides.IPNetwork>();
var trustedProxiesEnv = Environment.GetEnvironmentVariable("IMMICHFRAME_TRUSTED_PROXIES");
if (!string.IsNullOrWhiteSpace(trustedProxiesEnv))
{
    foreach (var entry in trustedProxiesEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var slash = entry.IndexOf('/');
        if (slash >= 0)
        {
            // CIDR network, e.g. "172.18.0.0/16".
            if (IPAddress.TryParse(entry[..slash], out var network) &&
                int.TryParse(entry[(slash + 1)..], out var prefixLength))
            {
                trustedNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(network, prefixLength));
            }
            else
            {
                Console.WriteLine($"Ignoring invalid IMMICHFRAME_TRUSTED_PROXIES entry: {entry}");
            }
        }
        else if (IPAddress.TryParse(entry, out var proxy))
        {
            trustedProxies.Add(proxy);
        }
        else
        {
            Console.WriteLine($"Ignoring invalid IMMICHFRAME_TRUSTED_PROXIES entry: {entry}");
        }
    }
}
var trustForwardedHeaders = trustedProxies.Count > 0 || trustedNetworks.Count > 0;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Replace the framework defaults (loopback) with only the explicitly trusted proxies/networks.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in trustedProxies)
        options.KnownProxies.Add(proxy);
    foreach (var network in trustedNetworks)
        options.KnownNetworks.Add(network);
    // Single proxy hop: only the proxy nearest to us may set the client IP.
    options.ForwardLimit = 1;
});

// Throttle credential/PIN brute-force per client IP. RemoteIpAddress is the real socket peer,
// or the forwarded client IP only when the request came through a trusted proxy (see above).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many attempts. Please wait a few minutes and try again." }, token);
    };
});

var app = builder.Build();

// Initialize the configuration database: apply migrations, import any existing file/env config on
// first boot, then load the in-memory settings snapshot before the app starts serving requests.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var fullDbPath = Path.GetFullPath(dbPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullDbPath)!);
    Directory.CreateDirectory(dataProtectionDir);

    var db = services.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    services.GetRequiredService<ConfigImporter>().ImportIfNeeded(db, configPath);
    services.GetRequiredService<DatabaseServerSettings>().Load(db);
    services.GetRequiredService<SlideshowLinkManager>().Reload(db);

    // Bootstrap the first admin from ADMIN_USERNAME/ADMIN_PASSWORD if no admin exists yet.
    var adminUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
    var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
    var adminAuth = services.GetRequiredService<AdminAuthService>();
    if (!adminAuth.AnyAdminExists() && !string.IsNullOrWhiteSpace(adminUsername) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        adminAuth.CreateAdmin(adminUsername.Trim(), adminPassword);
        app.Logger.LogInformation("Created admin user '{username}' from environment.", adminUsername.Trim());
    }
}

// Only rewrite RemoteIpAddress from X-Forwarded-For when at least one trusted proxy/network is
// configured; otherwise the real socket peer is used so the rate limiter can't be spoofed.
if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}
else if (!app.Environment.IsDevelopment())
{
    // Without a trusted proxy the backend can't see the edge's HTTPS scheme, so session cookies are
    // issued without the Secure flag and HSTS is suppressed behind a TLS-terminating reverse proxy.
    app.Logger.LogWarning(
        "IMMICHFRAME_TRUSTED_PROXIES is not set: X-Forwarded-Proto is ignored, so behind a TLS-terminating " +
        "reverse proxy session cookies will NOT be marked Secure. Set it to your proxy's IP/CIDR when serving over HTTPS.");
}
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();

// In Development, render full diagnostics for unhandled exceptions. ApiExceptionMiddleware rethrows
// unexpected errors in Development so this page (sitting just outside it) can handle them; in
// Production that middleware is the catch-all and returns a generic JSON 500 instead.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Turn unhandled Immich API errors (e.g. a missing-permission 403 on an asset) into a clean 502, and
// act as the production catch-all: any other unhandled exception becomes a generic JSON 500 (full
// detail logged server-side, never leaked to the client).
app.UseMiddleware<ApiExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

// app.UseHttpsRedirection();
app.UseMiddleware<CustomAuthenticationMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();

// Make Program public for WebApplicationFactory
public partial class Program { }
