using System.Text.RegularExpressions;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordDockerManager.Config;
using DiscordDockerManager.Data;
using DiscordDockerManager.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using DiscordConfig = DiscordDockerManager.Config.DiscordConfig;

var bundledConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var bootstrapConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var bootstrapStorageRoot = bootstrapConfig["Storage:Root"] ?? "/data";
var bootstrapConfigPath = bootstrapConfig["Storage:ConfigPath"] ?? Path.Combine(bootstrapStorageRoot, "config");
var externalConfigEnv = Environment.GetEnvironmentVariable("ExternalConfig");
var bootstrapExternalConfigPath = !string.IsNullOrWhiteSpace(externalConfigEnv)
    ? externalConfigEnv
    : Path.Combine(bootstrapConfigPath, "appsettings.json");

EnsureDirectory(Path.GetDirectoryName(bootstrapExternalConfigPath) ?? string.Empty);
SyncExternalConfigVersion(bundledConfigPath, bootstrapExternalConfigPath, logger: null);

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((ctx, services, loggerCfg) =>
    {
        loggerCfg
            .ReadFrom.Configuration(ctx.Configuration, sectionName: "Serilog")
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    })
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        var externalConfigEnv = Environment.GetEnvironmentVariable("ExternalConfig");
        var storageRoot = ctx.Configuration["Storage:Root"] ?? "/data"; // may be empty this early; fallback
        var configPath = ctx.Configuration["Storage:ConfigPath"] ?? Path.Combine(storageRoot, "config");
        var externalConfigPath = !string.IsNullOrWhiteSpace(externalConfigEnv)
            ? externalConfigEnv
            : Path.Combine(configPath, "appsettings.json");

        cfg.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", true, true)
            .AddEnvironmentVariables()
            .AddJsonFile(externalConfigPath, true, true);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;
        var storageRoot = config["Storage:Root"] ?? "/data";
        var dbPath = config["Storage:DbPath"] ?? Path.Combine(storageRoot, "db");
        var logsPath = config["Storage:LogsPath"] ?? Path.Combine(storageRoot, "logs");
        var configPath = config["Storage:ConfigPath"] ?? Path.Combine(storageRoot, "config");
        EnsureDirectory(dbPath);
        EnsureDirectory(logsPath);
        EnsureDirectory(configPath);

        // --- Configuration sections ---
        services.Configure<DiscordConfig>(config.GetSection("Discord"));
        services.Configure<DockerConfig>(config.GetSection("Docker"));
        services.Configure<DatabaseConfig>(config.GetSection("Database"));
        services.Configure<OllamaConfig>(config.GetSection("Ollama"));
        services.Configure<List<ContainerConfigEntry>>(config.GetSection("Containers"));

        // --- Database ---
        var dbConnectionString = config.GetSection("Database")["ConnectionString"] ??
                                 $"Data Source={Path.Combine(dbPath, "gamemanager.db")}";
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(dbConnectionString));

        // --- Discord.Net ---
        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
        };
        services.AddSingleton(socketConfig);
        services.AddSingleton<DiscordSocketClient>();
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Async }));

        // --- Application Services ---
        services.AddSingleton<DockerService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<ContainerSyncService>();
        services.AddSingleton<OllamaService>();

        // --- Hosted Services ---
        services.AddHostedService<DiscordBotService>();
        services.AddHostedService<LogMonitorService>();
    })
    .ConfigureLogging(logging => { logging.ClearProviders(); })
    .Build();

// Apply EF Core migrations and sync containers
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    // Ensure storage structure and seed external config file if missing
    var storageRoot = configuration["Storage:Root"] ?? "/data";
    var dbPath = configuration["Storage:DbPath"] ?? Path.Combine(storageRoot, "db");
    var logsPath = configuration["Storage:LogsPath"] ?? Path.Combine(storageRoot, "logs");
    var configPath = configuration["Storage:ConfigPath"] ?? Path.Combine(storageRoot, "config");
    EnsureDirectory(dbPath);
    EnsureDirectory(logsPath);
    EnsureDirectory(configPath);

    var bundledConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    var externalConfig = Path.Combine(configPath, "appsettings.json");
    EnsureConfigFile(bundledConfig, externalConfig, logger);

    externalConfigEnv = Environment.GetEnvironmentVariable("ExternalConfig");
    if (!string.IsNullOrWhiteSpace(externalConfigEnv) && !externalConfigEnv.Equals(externalConfig, StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("ExternalConfig env var set; using {ExternalConfigPath} which overrides environment variables and built-in appsettings.", externalConfigEnv);
    }
    else
    {
        logger.LogInformation("External config path resolved to {ExternalConfigPath}; it overrides environment variables when present.", externalConfig);
    }

    try
    {
        var dbConnectionString = configuration.GetSection("Database")["ConnectionString"] ??
                                 "Data Source=/data/db/gamemanager.db";
        EnsureSqliteDirectory(dbConnectionString, logger);

        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations.");
    }

    try
    {
        var syncService = scope.ServiceProvider.GetRequiredService<ContainerSyncService>();
        await syncService.SyncAsync();
        logger.LogInformation("Container configurations synced from appsettings.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to sync container configurations.");
    }
}

await host.RunAsync();

static void EnsureSqliteDirectory(string connectionString, ILogger<Program> logger)
{
    try
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            logger.LogWarning("SQLite connection string has no DataSource; skipping directory creation.");
            return;
        }

        var rootedPath = Path.IsPathRooted(dataSource)
            ? dataSource
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, dataSource));

        var directory = Path.GetDirectoryName(rootedPath);
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        logger.LogInformation("Ensured SQLite directory exists at {Directory}", directory);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Unable to ensure SQLite directory for connection string.");
    }
}

static void EnsureDirectory(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return;

    try
    {
        Directory.CreateDirectory(path);
    }
    catch
    {
        // Swallow exceptions here; detailed logging handled elsewhere if needed.
    }
}

static void EnsureConfigFile(string sourcePath, string targetPath, ILogger<Program> logger)
{
    try
    {
        if (!File.Exists(sourcePath))
        {
            logger.LogWarning("Bundled appsettings.json not found at {SourcePath}; skipping copy to external config.", sourcePath);
            return;
        }

        if (File.Exists(targetPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
        File.Copy(sourcePath, targetPath);
        logger.LogInformation("Seeded external config at {ConfigPath} from bundled appsettings.json", targetPath);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to seed external config file to {ConfigPath}", targetPath);
    }
}

static void SyncExternalConfigVersion(string bundledPath, string externalPath, ILogger<Program>? logger)
{
    try
    {
        var bundledVersion = ReadConfigVersion(bundledPath);
        var externalVersion = ReadConfigVersion(externalPath);

        // If external is missing, seed it with bundled config
        if (externalVersion < 0 && File.Exists(bundledPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(externalPath) ?? string.Empty);
            File.Copy(bundledPath, externalPath, true);
            logger?.LogInformation("Seeded external config {ExternalPath} (missing)", externalPath);
            return;
        }

        // If bundled is newer, back up external then replace
        if (bundledVersion > externalVersion && File.Exists(externalPath))
        {
            var backupPath = NextBackupPath(externalPath);
            File.Move(externalPath, backupPath);
            File.Copy(bundledPath, externalPath, true);
            logger?.LogInformation("External config upgraded from version {OldVersion} to {NewVersion}; backup saved to {BackupPath}", externalVersion, bundledVersion, backupPath);
        }
    }
    catch (Exception ex)
    {
        logger?.LogWarning(ex, "Failed to sync external config version for {ExternalPath}", externalPath);
    }
}

static int ReadConfigVersion(string path)
{
    if (!File.Exists(path))
    {
        return -1;
    }

    try
    {
        var cfg = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();

        var value = cfg["ConfigVersion"];
        return int.TryParse(value, out var version) ? version : 0;
    }
    catch
    {
        return 0;
    }
}

static string NextBackupPath(string externalPath)
{
    var dir = Path.GetDirectoryName(externalPath) ?? string.Empty;
    var file = Path.GetFileName(externalPath);
    var pattern = $"{Regex.Escape(file)}\\.(\\d+)\\.bak";

    var maxIndex = Directory.Exists(dir)
        ? Directory.GetFiles(dir, file + ".*.bak")
            .Select(f => Regex.Match(Path.GetFileName(f), pattern))
            .Where(m => m.Success && int.TryParse(m.Groups[1].Value, out _))
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max()
        : 0;

    var next = maxIndex + 1;
    return Path.Combine(dir, $"{file}.{next}.bak");
}

// Needed for test project to reference as partial class
public partial class Program
{
}