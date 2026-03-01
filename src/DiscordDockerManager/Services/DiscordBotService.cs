using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
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

    /// <summary>Initialises the service.</summary>
    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider serviceProvider,
        IOptions<AppDiscordConfig> config,
        ILogger<DiscordBotService> logger)
    {
        _client = client;
        _interactions = interactions;
        _serviceProvider = serviceProvider;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Log += LogAsync;
        _interactions.Log += LogAsync;

        _client.Ready += OnReadyAsync;
        _client.InteractionCreated += OnInteractionAsync;

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
    }

    private async Task OnInteractionAsync(SocketInteraction interaction)
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
}