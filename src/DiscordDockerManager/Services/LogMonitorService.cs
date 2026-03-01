using System.Text.RegularExpressions;
using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordDockerManager.Services;

/// <summary>
///     Background service that tails Docker container logs and records
///     player join/leave events into the database.
/// </summary>
public class LogMonitorService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly DockerService _dockerService;
    private readonly ILogger<LogMonitorService> _logger;
    private readonly OllamaService _ollamaService;

    /// <summary>Initialises the log monitor with required dependencies.</summary>
    public LogMonitorService(
        IDbContextFactory<AppDbContext> dbFactory,
        DockerService dockerService,
        OllamaService ollamaService,
        ILogger<LogMonitorService> logger)
    {
        _dbFactory = dbFactory;
        _dockerService = dockerService;
        _ollamaService = ollamaService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogMonitorService starting.");

        // Small initial delay to let the app fully start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            List<DockerContainerConfig> configs;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
                configs = await db.DockerContainerConfigs
                    .Where(c => c.IsEnabled)
                    .ToListAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load container configs for log monitoring.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            // Launch a tail task for each container; restart any that exit
            var tailTasks = configs.Select(config => TailContainerAsync(config, stoppingToken)).ToList();
            await Task.WhenAll(tailTasks);

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }

        _logger.LogInformation("LogMonitorService stopping.");
    }

    private async Task TailContainerAsync(DockerContainerConfig config, CancellationToken ct)
    {
        _logger.LogDebug("Starting log tail for container '{Container}'.", config.Name);

        var joinRegex = string.IsNullOrWhiteSpace(config.PlayerJoinPattern)
            ? null
            : new Regex(config.PlayerJoinPattern, RegexOptions.Compiled);
        var leaveRegex = string.IsNullOrWhiteSpace(config.PlayerLeavePattern)
            ? null
            : new Regex(config.PlayerLeavePattern, RegexOptions.Compiled);

        await _dockerService.TailLogsAsync(config.ContainerId, async line =>
        {
            var (eventType, playerName) = ParseLine(line, joinRegex, leaveRegex);

            if (eventType == PlayerEventType.Unknown && _ollamaService.IsEnabled)
            {
                var aiResult = await _ollamaService.ParseLogLineAsync(line, ct);
                if (aiResult.HasValue)
                {
                    eventType = aiResult.Value.EventType;
                    playerName = aiResult.Value.PlayerName;
                }
            }

            if (eventType == PlayerEventType.Unknown) return;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                db.PlayerEvents.Add(new PlayerEvent
                {
                    ContainerName = config.Name,
                    PlayerName = playerName,
                    EventType = eventType,
                    RawLogLine = line.Length > 1000 ? line[..1000] : line,
                    DockerContainerConfigId = config.Id
                });
                await db.SaveChangesAsync(ct);
                _logger.LogDebug("Player {PlayerName} {EventType} on '{Container}'.", playerName, eventType,
                    config.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save player event for container '{Container}'.", config.Name);
            }
        }, ct);
    }

    /// <summary>
    ///     Attempts to match a log line against the join/leave regexes.
    ///     Returns (<see cref="PlayerEventType.Unknown" />, "") if no match.
    /// </summary>
    public static (PlayerEventType EventType, string PlayerName) ParseLine(
        string line, Regex? joinRegex, Regex? leaveRegex)
    {
        if (joinRegex is not null)
        {
            var m = joinRegex.Match(line);
            if (m.Success && m.Groups.Count > 1)
                return (PlayerEventType.Joined, m.Groups[1].Value);
        }

        if (leaveRegex is not null)
        {
            var m = leaveRegex.Match(line);
            if (m.Success && m.Groups.Count > 1)
                return (PlayerEventType.Left, m.Groups[1].Value);
        }

        return (PlayerEventType.Unknown, string.Empty);
    }
}