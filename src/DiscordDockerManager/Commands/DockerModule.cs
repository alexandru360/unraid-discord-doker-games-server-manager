using Discord;
using Discord.Interactions;
using DiscordDockerManager.Data;
using DiscordDockerManager.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiscordDockerManager.Commands;

/// <summary>
///     Discord slash commands for Docker container management (<c>/docker</c>).
/// </summary>
[Group("docker", "Manage Docker game server containers")]
public class DockerModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly DockerService _dockerService;
    private readonly ILogger<DockerModule> _logger;
    private readonly PermissionService _permissionService;

    /// <summary>Initialises the module with injected services.</summary>
    public DockerModule(
        DockerService dockerService,
        PermissionService permissionService,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<DockerModule> logger)
    {
        _dockerService = dockerService;
        _permissionService = permissionService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Restarts a configured Docker container.</summary>
    [SlashCommand("restart", "Restart a Docker container")]
    public async Task RestartAsync(
        [Summary("container", "Friendly name of the container")]
        string containerName)
    {
        if (!Context.Interaction.HasResponded)
            await DeferAsync();

        if (!await _permissionService.HasPermissionAsync(Context.User.Id, containerName))
        {
            await FollowupAsync("❌ You do not have permission to manage this container.", ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.DockerContainerConfigs
            .FirstOrDefaultAsync(c => c.Name == containerName && c.IsEnabled);

        if (config is null)
        {
            await FollowupAsync($"❌ Container `{containerName}` is not configured or is disabled.", ephemeral: true);
            return;
        }

        _logger.LogInformation("User {User} is restarting container '{Container}'.", Context.User.Username,
            containerName);
        var result = await _dockerService.RestartContainerAsync(config.ContainerId);

        var icon = result.Success ? "✅" : "❌";
        await FollowupAsync($"{icon} {result.Message}");
    }

    /// <summary>Shows the current status of a configured container.</summary>
    [SlashCommand("status", "Show status of a Docker container")]
    public async Task StatusAsync(
        [Summary("container", "Friendly name of the container")]
        string containerName)
    {
        if (!Context.Interaction.HasResponded)
            await DeferAsync();

        if (!await _permissionService.HasPermissionAsync(Context.User.Id, containerName))
        {
            await FollowupAsync("❌ You do not have permission to view this container.", ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == containerName);

        if (config is null)
        {
            await FollowupAsync($"❌ Container `{containerName}` is not configured.", ephemeral: true);
            return;
        }

        var result = await _dockerService.GetContainerStatusAsync(config.ContainerId);
        var icon = result.Success ? "ℹ️" : "❌";
        await FollowupAsync($"{icon} {result.Message}");
    }

    /// <summary>Lists all enabled configured containers.</summary>
    [SlashCommand("list", "List all configured Docker containers")]
    public async Task ListAsync()
    {
        // Acknowledge quickly to keep the interaction token valid.
        await RespondAsync("Listing containers...", ephemeral: true);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var containers = await db.DockerContainerConfigs.Where(c => c.IsEnabled).ToListAsync();

        if (containers.Count == 0)
        {
            await FollowupAsync("No containers are configured.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🐳 Configured Docker Containers")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        foreach (var c in containers)
            embed.AddField(
                $"{c.Name} ({c.Game})",
                $"Container ID: `{c.ContainerId}`\n{c.Description}",
                false);

        await FollowupAsync(embed: embed.Build());
    }

    /// <summary>Shows the last N log lines from a container.</summary>
    [SlashCommand("logs", "Show recent log lines from a Docker container")]
    public async Task LogsAsync(
        [Summary("container", "Friendly name of the container")]
        string containerName,
        [Summary("lines", "Number of lines to show (default: 30)")]
        int lines = 30)
    {
        if (!Context.Interaction.HasResponded)
            await DeferAsync();

        if (!await _permissionService.HasPermissionAsync(Context.User.Id, containerName))
        {
            await FollowupAsync("❌ You do not have permission to view this container.", ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == containerName);

        if (config is null)
        {
            await FollowupAsync($"❌ Container `{containerName}` is not configured.", ephemeral: true);
            return;
        }

        lines = Math.Clamp(lines, 1, 100);
        var result = await _dockerService.GetContainerLogsAsync(config.ContainerId, lines);
        var icon = result.Success ? "📋" : "❌";
        await FollowupAsync($"{icon} **Logs for `{containerName}` (last {lines} lines):**\n{result.Message}");
    }
}