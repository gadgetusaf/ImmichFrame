using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using System.Reflection;
using ImmichFrame.Core.Logic;
using ImmichFrame.Core.Logic.AccountSelection;
using ImmichFrame.WebApi.Helpers.Config;
using ImmichFrame.WebApi.Persistence;
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

builder.Services.AddTransient<ConfigLoader>();
builder.Services.AddSingleton<ConfigImporter>();

// Database-backed settings. The snapshot is populated during startup (see below) before any
// request is served, preserving the legacy "config is loaded once at startup" behaviour.
builder.Services.AddSingleton<DatabaseServerSettings>();
builder.Services.AddSingleton<IServerSettings>(srv => srv.GetRequiredService<DatabaseServerSettings>());

// Register sub-settings
builder.Services.AddSingleton<IGeneralSettings>(srv => srv.GetRequiredService<IServerSettings>().GeneralSettings);

// Register services
builder.Services.AddSingleton<IWeatherService, OpenWeatherMapService>();
builder.Services.AddSingleton<ICalendarService, IcalCalendarService>();
builder.Services.AddSingleton<IAssetAccountTracker, BloomFilterAssetAccountTracker>();
builder.Services.AddSingleton<IAccountSelectionStrategy, TotalAccountImagesSelectionStrategy>();
builder.Services.AddHttpClient(); // Ensures IHttpClientFactory is available

builder.Services.AddTransient<Func<IAccountSettings, IAccountImmichFrameLogic>>(srv =>
    account => ActivatorUtilities.CreateInstance<PooledImmichFrameLogic>(srv, account));

builder.Services.AddSingleton<IImmichFrameLogic, MultiImmichFrameLogicDelegate>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthorization(options => { options.AddPolicy("AllowAnonymous", policy => policy.RequireAssertion(context => true)); });

builder.Services.AddAuthentication("ImmichFrameScheme")
    .AddScheme<AuthenticationSchemeOptions, ImmichFrameAuthenticationHandler>("ImmichFrameScheme", options => { });

var app = builder.Build();

// Initialize the configuration database: apply migrations, import any existing file/env config on
// first boot, then load the in-memory settings snapshot before the app starts serving requests.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var fullDbPath = Path.GetFullPath(dbPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullDbPath)!);

    var db = services.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    services.GetRequiredService<ConfigImporter>().ImportIfNeeded(db, configPath);
    services.GetRequiredService<DatabaseServerSettings>().Load(db);
}

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
