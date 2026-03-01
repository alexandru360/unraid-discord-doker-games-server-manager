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
    private readonly string _managedContainersCsv;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ContainerSyncService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public ContainerSyncService(
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<List<ContainerConfigEntry>> containerEntries,
        IOptions<DockerConfig> dockerConfig,
        ILogger<ContainerSyncService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _containerEntries = containerEntries.Value;
        _managedContainersCsv = dockerConfig.Value.ManagedContainers;
    }

    /// <summary>
    ///     Inserts or updates container records from configuration.
    ///     Existing entries are updated; entries not in config are left unchanged.
    /// </summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        var allEntries = new List<ContainerConfigEntry>(_containerEntries);
        AppendManagedContainersFromCsv(allEntries);

        if (allEntries.Count == 0)
        {
            _logger.LogInformation("No container entries found in configuration to sync.");
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var allowedNames = new HashSet<string>(allEntries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in allEntries)
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

        // Disable configs no longer present in appsettings/managed CSV to avoid stale duplicates.
        var stale = await db.DockerContainerConfigs
            .Where(c => !allowedNames.Contains(c.Name))
            .ToListAsync(ct);

        foreach (var entry in stale)
        {
            if (entry.IsEnabled)
            {
                entry.IsEnabled = false;
                _logger.LogInformation("Disabled stale container config '{Name}' not present in current configuration.", entry.Name);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private void AppendManagedContainersFromCsv(List<ContainerConfigEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(_managedContainersCsv)) return;

        var tokens = _managedContainersCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var token in tokens)
        {
            var existing = entries.FirstOrDefault(e =>
                string.Equals(e.Name, token, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                continue;
            }

            entries.Add(new ContainerConfigEntry
            {
                Name = token,
                ContainerId = token,
                Description = "Managed via Docker.ManagedContainers",
                Game = "Generic",
                IsEnabled = true
            });
            _logger.LogInformation("Added managed container from CSV: {Container}", token);
        }
    }
}