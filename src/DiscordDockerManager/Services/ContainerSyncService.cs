using DiscordDockerManager.Config;
using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordDockerManager.Services;

/// <summary>
///     Synchronises container configurations from <c>appsettings.json</c> into the database
///     so that the database is the single source of truth at runtime.
/// </summary>
public class ContainerSyncService
{
    private readonly List<ContainerConfigEntry> _containerEntries;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ContainerSyncService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public ContainerSyncService(
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<List<ContainerConfigEntry>> containerEntries,
        ILogger<ContainerSyncService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _containerEntries = containerEntries.Value;
    }

    /// <summary>
    ///     Inserts or updates container records from configuration.
    ///     Existing entries are updated; entries not in config are left unchanged.
    /// </summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (_containerEntries.Count == 0)
        {
            _logger.LogInformation("No container entries found in configuration to sync.");
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        foreach (var entry in _containerEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                _logger.LogWarning("Container entry with empty name skipped.");
                continue;
            }

            var existing = await db.DockerContainerConfigs
                .FirstOrDefaultAsync(c => c.Name == entry.Name, ct);

            if (existing is null)
            {
                db.DockerContainerConfigs.Add(new DockerContainerConfig
                {
                    Name = entry.Name,
                    ContainerId = entry.ContainerId,
                    Description = entry.Description,
                    Game = entry.Game,
                    IsEnabled = entry.IsEnabled,
                    PlayerJoinPattern = entry.PlayerJoinPattern,
                    PlayerLeavePattern = entry.PlayerLeavePattern
                });
                _logger.LogInformation("Added container config '{Name}' from appsettings.", entry.Name);
            }
            else
            {
                existing.ContainerId = entry.ContainerId;
                existing.Description = entry.Description;
                existing.Game = entry.Game;
                existing.IsEnabled = entry.IsEnabled;
                existing.PlayerJoinPattern = entry.PlayerJoinPattern;
                existing.PlayerLeavePattern = entry.PlayerLeavePattern;
                _logger.LogInformation("Updated container config '{Name}' from appsettings.", entry.Name);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}