using Discord;
using Discord.WebSocket;
using DiscordDockerManager.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppDiscordConfig = DiscordDockerManager.Config.DiscordConfig;

namespace DiscordDockerManager.Services;

/// <summary>
///     Hosted background service that connects to Discord and posts
///     running container status every 10 minutes.
/// </summary>
public class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly AppDiscordConfig _config;
    private readonly ExternalIpService _externalIpService;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DiscordBotService(
        DiscordSocketClient client,
        IOptions<AppDiscordConfig> config,
        ILogger<DiscordBotService> logger,
        IServiceScopeFactory scopeFactory,
        ExternalIpService externalIpService)
    {
        _client = client;
        _config = config.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _externalIpService = externalIpService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Log += LogAsync;
        _client.Ready += OnReadyAsync;

        await _client.LoginAsync(TokenType.Bot, _config.Token);
        await _client.StartAsync();

        // Wait until the bot is connected
        await _readyTcs.Task;

        // Post status immediately on startup, then every 10 minutes
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PostContainerStatusAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post container status.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await _client.StopAsync();
    }

    private Task OnReadyAsync()
    {
        _logger.LogInformation("Discord bot is connected as {Username}.", _client.CurrentUser.Username);
        _readyTcs.TrySetResult();
        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger.Log(level, msg.Exception, "[Discord] {Source}: {Message}", msg.Source, msg.Message);
        return Task.CompletedTask;
    }

    private async Task PostContainerStatusAsync()
    {
        if (_config.HelpChannelId == 0)
        {
            _logger.LogWarning("HelpChannelId not set; cannot post container status.");
            return;
        }

        var channel = _client.GetChannel(_config.HelpChannelId) as IMessageChannel;
        if (channel == null)
        {
            _logger.LogWarning("HelpChannelId {HelpChannelId} not found or not a text channel.", _config.HelpChannelId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var dockerService = scope.ServiceProvider.GetRequiredService<DockerService>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var selfContainerName = await dockerService.GetSelfContainerNameAsync();
        var allContainers = await db.DockerContainerConfigs
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var containers = selfContainerName is not null
            ? allContainers.Where(c =>
                !string.Equals(c.ContainerId, selfContainerName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(c.Name, selfContainerName, StringComparison.OrdinalIgnoreCase)).ToList()
            : allContainers;

        if (containers.Count == 0)
        {
            await channel.SendMessageAsync("No containers configured.");
            return;
        }

        var statusLookup = await dockerService.GetContainerStatusesAsync(containers.Select(c => c.ContainerId));
        var externalIp = await _externalIpService.GetExternalIpAsync();

        var runningLines = new List<string>();
        var offlineLines = new List<string>();

        foreach (var c in containers)
        {
            var status = statusLookup.TryGetValue(c.ContainerId, out var s)
                ? s
                : (Found: false, State: string.Empty, Status: string.Empty);

            var gameName = string.IsNullOrWhiteSpace(c.Game) ? c.Name : c.Game;

            if (!status.Found)
                offlineLines.Add($"⚠️ {gameName} - not found");
            else if (status.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                runningLines.Add($"🟢 {gameName} - {FormatUptime(status.Status)}");
            else
                offlineLines.Add($"🔴 {gameName} - {FormatOfflineTime(status.Status)}");
        }

        var ipDescription = externalIp != null ? $"🌐 IP: {externalIp}" : string.Empty;

        var embed = new EmbedBuilder()
            .WithTitle("HubdexServer")
            .WithDescription(ipDescription)
            .WithColor(runningLines.Count > 0 ? Color.Green : Color.Red)
            .WithCurrentTimestamp();

        if (runningLines.Count > 0)
            embed.AddField("Running:", string.Join("\n", runningLines), inline: false);

        if (offlineLines.Count > 0)
            embed.AddField("Offline:", string.Join("\n", offlineLines), inline: false);

        await channel.SendMessageAsync(embed: embed.Build());
        _logger.LogInformation("Posted container status to channel {ChannelId}. Running: {Count}", _config.HelpChannelId, runningLines.Count);
    }

    private static string FormatUptime(string statusStr)
    {
        if (statusStr.StartsWith("Up ", StringComparison.OrdinalIgnoreCase))
        {
            var part = statusStr[3..];
            var parenIdx = part.IndexOf('(');
            if (parenIdx >= 0) part = part[..parenIdx].TrimEnd();
            return $"online {part}";
        }
        return "online";
    }

    private static string FormatOfflineTime(string statusStr)
    {
        var agoIdx = statusStr.IndexOf(" ago", StringComparison.OrdinalIgnoreCase);
        if (agoIdx >= 0)
        {
            var parenEnd = statusStr.IndexOf(')');
            if (parenEnd >= 0)
            {
                var timePart = statusStr[(parenEnd + 1)..agoIdx].Trim();
                return $"offline ({timePart})";
            }
        }
        return "offline";
    }
}