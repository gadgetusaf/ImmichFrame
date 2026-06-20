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
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
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
        Enum.TryParse(logLevel, true, out level);
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

// Encrypt Immich API keys at rest. Protection keys persist in the Config volume so stored
// ciphertext stays decryptable across restarts.
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
    options.AddPolicy("AllowAnonymous", policy => policy.RequireAssertion(context => true));
    options.AddPolicy(AuthConstants.AdminPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(AuthConstants.AdminCookieScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireRole(UserRoles.Admin);
    });
});

builder.Services.AddAuthentication("ImmichFrameScheme")
    .AddScheme<AuthenticationSchemeOptions, ImmichFrameAuthenticationHandler>("ImmichFrameScheme", options => { })
    .AddCookie(AuthConstants.AdminCookieScheme, options =>
    {
        options.Cookie.Name = "immichframe_admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        // SameAsRequest works behind a TLS-terminating proxy (with UseForwardedHeaders) and on plain HTTP locally.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        // This is an API: return status codes rather than redirecting to a login page.
        options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

// Trust the reverse proxy's X-Forwarded-* headers (TLS terminates upstream).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
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

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
if (app.Environment.IsProduction())
{
    app.UseDefaultFiles();
}

if (app.Environment.IsDevelopment())
{
    var root = Directory.GetCurrentDirectory();
    var dotenv = Path.Combine(root, "..", "docker", ".env");

    dotenv = Path.GetFullPath(dotenv);
    DotEnv.Load(dotenv);
}

// app.UseHttpsRedirection();
app.UseMiddleware<CustomAuthenticationMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();

// Make Program public for WebApplicationFactory
public partial class Program { }
