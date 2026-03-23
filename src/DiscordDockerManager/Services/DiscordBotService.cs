using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordDockerManager.Data;
using DiscordDockerManager.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppDiscordConfig = DiscordDockerManager.Config.DiscordConfig;

namespace DiscordDockerManager.Services;

/// <summary>
///     Hosted background service that manages the Discord bot lifecycle:
///     connects, registers slash commands, and dispatches interactions.
/// </summary>
public class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly AppDiscordConfig _config;
    private readonly InteractionService _interactions;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initialises the service.</summary>
    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider serviceProvider,
        IOptions<AppDiscordConfig> config,
        ILogger<DiscordBotService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _client = client;
        _interactions = interactions;
        _serviceProvider = serviceProvider;
        _config = config.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Log += LogAsync;
        _interactions.Log += LogAsync;

        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionAsync;
        _client.MessageReceived += OnMessageReceivedAsync;

        await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _serviceProvider);

        await _client.LoginAsync(TokenType.Bot, _config.Token);
        await _client.StartAsync();

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            await _client.StopAsync();
        }
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Discord bot is connected as {Username}.", _client.CurrentUser.Username);

        if (_config.GuildId != 0)
        {
            await _interactions.RegisterCommandsToGuildAsync(_config.GuildId);
            _logger.LogInformation("Slash commands registered to guild {GuildId}.", _config.GuildId);
        }
        else
        {
            await _interactions.RegisterCommandsGloballyAsync();
            _logger.LogInformation("Slash commands registered globally.");
        }

        await PostStartupStatusAsync();
    }

    private Task OnInteractionAsync(SocketInteraction interaction)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var ctx = new SocketInteractionContext(_client, interaction);
                await _interactions.ExecuteCommandAsync(ctx, _serviceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while executing interaction.");
            }
        });

        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        // Ignore bots and system messages
        if (message.Author.IsBot || message.Author.IsWebhook) return;

        var prefix = string.IsNullOrWhiteSpace(_config.TextPrefix) ? "hbot" : _config.TextPrefix;
        if (!message.Content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        var parts = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return;

        var verb = parts[1].ToLowerInvariant();
        var args = parts.Skip(2).ToArray();

        switch (verb)
        {
            case "list":
                await HandleListContainersAsync(message.Channel);
                break;
            case "status":
                if (args.Length == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: hbot status <container>");
                    return;
                }
                await HandleStatusAsync(message.Channel, args[0]);
                break;
            case "restart":
                if (args.Length == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: hbot restart <container>");
                    return;
                }
                await HandleRestartAsync(message.Channel, args[0], message.Author);
                break;
            case "logs":
                if (args.Length == 0)
                {
                    await message.Channel.SendMessageAsync("Usage: hbot logs <container> [lines]");
                    return;
                }
                var lines = 30;
                if (args.Length >= 2 && int.TryParse(args[1], out var parsed)) lines = parsed;
                await HandleLogsAsync(message.Channel, args[0], lines, message.Author);
                break;
            case "help":
                await SendHelpAsync(message.Channel);
                break;
            default:
                await message.Channel.SendMessageAsync($"Unknown command. Try `{prefix} help`.");
                break;
        }
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

    private async Task PostStartupStatusAsync()
    {
        if (_config.HelpChannelId == 0)
        {
            _logger.LogInformation("HelpChannelId not set; skipping startup status post.");
            return;
        }

        var channel = _client.GetChannel(_config.HelpChannelId) as IMessageChannel;
        if (channel == null)
        {
            _logger.LogWarning("HelpChannelId {HelpChannelId} not found or not a text channel.", _config.HelpChannelId);
            return;
        }

        var prefix = string.IsNullOrWhiteSpace(_config.TextPrefix) ? "hbot" : _config.TextPrefix;
        var (embed, running) = await BuildContainersSummaryAsync();

        var runningText = running.Count > 0
            ? string.Join(", ", running)
            : "none";

        var content = ":wave: Hubdex online.\n"
                  + $"Prefix: `{prefix}` | Slash commands ready.\n"
                  + $"Running now: {runningText}. Try `{prefix} list`.";

        await channel.SendMessageAsync(content, embed: embed);
        _logger.LogInformation("Posted startup status to channel {ChannelId}.", _config.HelpChannelId);
    }

    private async Task HandleListContainersAsync(IMessageChannel channel)
    {
        var (embed, running) = await BuildContainersSummaryAsync();
        if (embed != null)
        {
            await channel.SendMessageAsync(embed: embed);
        }
        else
        {
            await channel.SendMessageAsync("No containers configured.");
        }
    }

    private async Task SendHelpAsync(IMessageChannel channel)
    {
        var prefix = string.IsNullOrWhiteSpace(_config.TextPrefix) ? "hbot" : _config.TextPrefix;
        var msg = $"**Hubdex commands**\n" +
                  $"- `{prefix} list` — list configured containers\n" +
                  $"- `{prefix} status <container>` — container status\n" +
                  $"- `{prefix} restart <container>` — restart container\n" +
                  $"- `{prefix} logs <container> [lines]` — recent logs (default 30)\n" +
                  $"- `{prefix} help` — this help\n" +
                  "Slash commands: /hbot list | status | restart | logs; /players list; /admin ... (admins only).";
        await channel.SendMessageAsync(msg);
    }

    private async Task<(Embed? Embed, List<string> RunningContainers)> BuildContainersSummaryAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var dockerService = scope.ServiceProvider.GetRequiredService<DockerService>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var selfContainerName = await dockerService.GetSelfContainerNameAsync();
        var allContainers = await db.DockerContainerConfigs
            .Where(c => c.IsEnabled)
            .OrderBy(c => c.Name)
            .ToListAsync();
        // Exclude the bot's own container
        var containers = selfContainerName is not null
            ? allContainers.Where(c =>
                !string.Equals(c.ContainerId, selfContainerName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(c.Name, selfContainerName, StringComparison.OrdinalIgnoreCase)).ToList()
            : allContainers;

        if (containers.Count == 0)
        {
            return (null, new List<string>());
        }

        var embed = new EmbedBuilder()
            .WithTitle("🐳 Hubdex Containers")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        var statusLookup = await dockerService.GetContainerStatusesAsync(containers.Select(c => c.ContainerId));
        var running = new List<string>();

        foreach (var c in containers)
        {
            (bool Found, string State, string Status) status = statusLookup.TryGetValue(c.ContainerId, out var s)
                ? s
                : (Found: false, State: string.Empty, Status: string.Empty);

            var (icon, stateText) = status switch
            {
                (false, _, _) => ("⚠️", "not found"),
                (_, var state, _) when state.Equals("running", StringComparison.OrdinalIgnoreCase) => ("🟢", "running"),
                (_, var state, _) when string.IsNullOrWhiteSpace(state) => ("⚪", "unknown"),
                (_, var state, _) => ("🔴", state)
            };

            if (status.Found && status.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                running.Add(c.Name);
            }

            embed.AddField(
                c.Name,
                $"{icon} Status: {stateText} ({status.Status})\nID: `{c.ContainerId}`\nGame: {c.Game}",
                inline: false);
        }

        return (embed.Build(), running);
    }

    private async Task HandleStatusAsync(IMessageChannel channel, string containerName)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var dockerService = scope.ServiceProvider.GetRequiredService<DockerService>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var config = await db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == containerName);
        if (config is null)
        {
            await channel.SendMessageAsync($"❌ Container `{containerName}` is not configured.");
            return;
        }

        var result = await dockerService.GetContainerStatusAsync(config.ContainerId);
        var icon = result.Success ? "ℹ️" : "❌";
        await channel.SendMessageAsync($"{icon} {result.Message}");
    }

    private async Task HandleRestartAsync(IMessageChannel channel, string containerName, IUser author)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var dockerService = scope.ServiceProvider.GetRequiredService<DockerService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (!await permissionService.HasPermissionAsync(author.Id, containerName))
        {
            await channel.SendMessageAsync("❌ You do not have permission to manage this container.");
            return;
        }

        var config = await db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == containerName && c.IsEnabled);
        if (config is null)
        {
            await channel.SendMessageAsync($"❌ Container `{containerName}` is not configured or is disabled.");
            return;
        }

        var result = await dockerService.RestartContainerAsync(config.ContainerId);
        var icon = result.Success ? "✅" : "❌";
        await channel.SendMessageAsync($"{icon} {result.Message}");
    }

    private async Task HandleLogsAsync(IMessageChannel channel, string containerName, int lines, IUser author)
    {
        lines = Math.Clamp(lines, 1, 100);
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var dockerService = scope.ServiceProvider.GetRequiredService<DockerService>();
        var permissionService = scope.ServiceProvider.GetRequiredService<PermissionService>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (!await permissionService.HasPermissionAsync(author.Id, containerName))
        {
            await channel.SendMessageAsync("❌ You do not have permission to view this container.");
            return;
        }

        var config = await db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == containerName);
        if (config is null)
        {
            await channel.SendMessageAsync($"❌ Container `{containerName}` is not configured.");
            return;
        }

        var result = await dockerService.GetContainerLogsAsync(config.ContainerId, lines);
        var icon = result.Success ? "📋" : "❌";
        await channel.SendMessageAsync($"{icon} **Logs for `{containerName}` (last {lines} lines):**\n{result.Message}");
    }
}