using Discord.Interactions;
using Discord.WebSocket;
using DiscordDockerManager.Config;
using DiscordDockerManager.Data;
using DiscordDockerManager.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        cfg.SetBasePath(AppContext.BaseDirectory)
           .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
           .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true)
           .AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // --- Configuration sections ---
        services.Configure<DiscordConfig>(config.GetSection("Discord"));
        services.Configure<DockerConfig>(config.GetSection("Docker"));
        services.Configure<DatabaseConfig>(config.GetSection("Database"));
        services.Configure<OllamaConfig>(config.GetSection("Ollama"));
        services.Configure<List<ContainerConfigEntry>>(config.GetSection("Containers"));

        // --- Database ---
        var dbConnectionString = config.GetSection("Database")["ConnectionString"] ?? "Data Source=gamemanager.db";
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(dbConnectionString));

        // --- Discord.Net ---
        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = Discord.GatewayIntents.Guilds | Discord.GatewayIntents.GuildMessages
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
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

// Apply EF Core migrations and sync containers
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
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

// Needed for test project to reference as partial class
public partial class Program { }
