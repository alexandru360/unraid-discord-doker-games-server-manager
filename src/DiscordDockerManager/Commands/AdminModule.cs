using Discord;
using Discord.Interactions;
using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using DiscordDockerManager.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiscordDockerManager.Commands;

/// <summary>
/// Admin-only Discord slash commands for managing permissions and container configs (<c>/admin</c>).
/// </summary>
[Group("admin", "Admin commands for managing permissions and containers")]
public class AdminModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly PermissionService _permissionService;
    private readonly ContainerSyncService _containerSyncService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AdminModule> _logger;

    /// <summary>Initialises the module.</summary>
    public AdminModule(
        PermissionService permissionService,
        ContainerSyncService containerSyncService,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<AdminModule> logger)
    {
        _permissionService = permissionService;
        _containerSyncService = containerSyncService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Grants a Discord user permission to manage a container.</summary>
    [SlashCommand("grant", "Grant a user permission to manage a container")]
    public async Task GrantAsync(
        [Summary("user", "The Discord user to grant permission to")] IUser user,
        [Summary("container", "Friendly name of the container")] string containerName)
    {
        await DeferAsync(ephemeral: true);

        if (!await _permissionService.IsAdminAsync(Context.User.Id))
        {
            await FollowupAsync("❌ You must be an admin to use this command.", ephemeral: true);
            return;
        }

        var message = await _permissionService.GrantPermissionAsync(user.Id, user.Username, containerName);
        await FollowupAsync(message, ephemeral: true);
    }

    /// <summary>Revokes a Discord user's permission to manage a container.</summary>
    [SlashCommand("revoke", "Revoke a user's permission for a container")]
    public async Task RevokeAsync(
        [Summary("user", "The Discord user to revoke permission from")] IUser user,
        [Summary("container", "Friendly name of the container")] string containerName)
    {
        await DeferAsync(ephemeral: true);

        if (!await _permissionService.IsAdminAsync(Context.User.Id))
        {
            await FollowupAsync("❌ You must be an admin to use this command.", ephemeral: true);
            return;
        }

        var message = await _permissionService.RevokePermissionAsync(user.Id, containerName);
        await FollowupAsync(message, ephemeral: true);
    }

    /// <summary>Adds a new container configuration to the database.</summary>
    [SlashCommand("addcontainer", "Add a new Docker container configuration")]
    public async Task AddContainerAsync(
        [Summary("name", "Friendly name for Discord commands")] string name,
        [Summary("containerid", "Docker container name or ID")] string containerId,
        [Summary("game", "Game type (e.g. Minecraft)")] string game,
        [Summary("description", "Optional description")] string description = "")
    {
        await DeferAsync(ephemeral: true);

        if (!await _permissionService.IsAdminAsync(Context.User.Id))
        {
            await FollowupAsync("❌ You must be an admin to use this command.", ephemeral: true);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        if (await db.DockerContainerConfigs.AnyAsync(c => c.Name == name))
        {
            await FollowupAsync($"❌ A container named `{name}` already exists.", ephemeral: true);
            return;
        }

        db.DockerContainerConfigs.Add(new DockerContainerConfig
        {
            Name = name,
            ContainerId = containerId,
            Game = game,
            Description = description,
            IsEnabled = true
        });

        await db.SaveChangesAsync();
        _logger.LogInformation("Admin {User} added container config '{Name}'.", Context.User.Username, name);
        await FollowupAsync($"✅ Container `{name}` ({game}) added successfully.", ephemeral: true);
    }

    /// <summary>
    /// Re-syncs container configurations from <c>appsettings.json</c> into the database.
    /// </summary>
    [SlashCommand("sync", "Sync containers from appsettings.json into the database")]
    public async Task SyncAsync()
    {
        await DeferAsync(ephemeral: true);

        if (!await _permissionService.IsAdminAsync(Context.User.Id))
        {
            await FollowupAsync("❌ You must be an admin to use this command.", ephemeral: true);
            return;
        }

        try
        {
            await _containerSyncService.SyncAsync();
            await FollowupAsync("✅ Container configurations synced from appsettings.", ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed.");
            await FollowupAsync($"❌ Sync failed: {ex.Message}", ephemeral: true);
        }
    }
}
