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
    private readonly DockerService _dockerService;
    private readonly ILogger<ContainerSyncService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public ContainerSyncService(
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<List<ContainerConfigEntry>> containerEntries,
        IOptions<DockerConfig> dockerConfig,
        DockerService dockerService,
        ILogger<ContainerSyncService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _dockerService = dockerService;
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
        await AppendManagedContainersFromCsvAsync(allEntries, ct);

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

    private async Task AppendManagedContainersFromCsvAsync(List<ContainerConfigEntry> entries, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_managedContainersCsv)) return;

        var tokens = _managedContainersCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Detect the bot's own container name so it can be excluded.
        var selfContainerName = await _dockerService.GetSelfContainerNameAsync(ct);

        foreach (var token in tokens)
        {
            // Skip the bot's own container
            if (selfContainerName is not null && string.Equals(token, selfContainerName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Skipping CSV container '{Token}' — it is the bot's own container.", token);
                continue;
            }

            var existing = entries.FirstOrDefault(e =>
                string.Equals(e.Name, token, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                continue;
            }

            // Skip if another entry already manages the same Docker container
            var existingByContainerId = entries.FirstOrDefault(e =>
                string.Equals(e.ContainerId, token, StringComparison.OrdinalIgnoreCase));
            if (existingByContainerId is not null)
            {
                _logger.LogInformation("Skipping CSV container '{Token}' — already configured as '{Name}'.", token, existingByContainerId.Name);
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