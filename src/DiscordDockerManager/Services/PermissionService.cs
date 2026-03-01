using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiscordDockerManager.Services;

/// <summary>
///     Handles checking, granting, and revoking user permissions for Docker containers.
/// </summary>
public class PermissionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PermissionService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public PermissionService(IDbContextFactory<AppDbContext> dbFactory, ILogger<PermissionService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    ///     Returns <c>true</c> if the user has admin rights or is explicitly permitted to manage
    ///     the specified container.
    /// </summary>
    public async Task<bool> HasPermissionAsync(ulong discordUserId, string containerName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.UserPermissions
            .Include(u => u.ContainerPermissions)
            .ThenInclude(cp => cp.DockerContainerConfig)
            .FirstOrDefaultAsync(u => u.DiscordUserId == discordUserId);

        if (user is null) return false;
        if (user.IsAdmin) return true;

        return user.ContainerPermissions.Any(cp =>
            cp.DockerContainerConfig.Name.Equals(containerName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns <c>true</c> if the user has admin rights.</summary>
    public async Task<bool> IsAdminAsync(ulong discordUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.UserPermissions.FirstOrDefaultAsync(u => u.DiscordUserId == discordUserId);
        return user?.IsAdmin ?? false;
    }

    /// <summary>
    ///     Grants <paramref name="targetDiscordUserId" /> permission to manage <paramref name="containerName" />.
    ///     Creates the user record if it does not exist.
    /// </summary>
    public async Task<string> GrantPermissionAsync(ulong targetDiscordUserId, string targetUsername,
        string containerName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var container = await db.DockerContainerConfigs
            .FirstOrDefaultAsync(c => c.Name == containerName);

        if (container is null)
            return $"Container `{containerName}` is not configured.";

        var user = await db.UserPermissions
            .Include(u => u.ContainerPermissions)
            .FirstOrDefaultAsync(u => u.DiscordUserId == targetDiscordUserId);

        if (user is null)
        {
            user = new UserPermission
            {
                DiscordUserId = targetDiscordUserId,
                Username = targetUsername
            };
            db.UserPermissions.Add(user);
            await db.SaveChangesAsync();
        }
        else
        {
            // Update username in case it changed
            user.Username = targetUsername;
        }

        var alreadyGranted = await db.UserPermissionContainers.AnyAsync(upc =>
            upc.UserPermissionId == user.Id && upc.DockerContainerConfigId == container.Id);

        if (alreadyGranted)
            return $"**{targetUsername}** already has permission for `{containerName}`.";

        db.UserPermissionContainers.Add(new UserPermissionContainer
        {
            UserPermissionId = user.Id,
            DockerContainerConfigId = container.Id
        });

        await db.SaveChangesAsync();
        _logger.LogInformation("Granted {Username} permission for container '{Container}'.", targetUsername,
            containerName);
        return $"Granted **{targetUsername}** permission to manage `{containerName}`.";
    }

    /// <summary>Revokes <paramref name="targetDiscordUserId" />'s permission for <paramref name="containerName" />.</summary>
    public async Task<string> RevokePermissionAsync(ulong targetDiscordUserId, string containerName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.UserPermissions.FirstOrDefaultAsync(u => u.DiscordUserId == targetDiscordUserId);
        if (user is null)
            return "User has no permissions on record.";

        var container = await db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == containerName);
        if (container is null)
            return $"Container `{containerName}` is not configured.";

        var link = await db.UserPermissionContainers
            .FirstOrDefaultAsync(upc => upc.UserPermissionId == user.Id && upc.DockerContainerConfigId == container.Id);

        if (link is null)
            return $"**{user.Username}** does not have permission for `{containerName}`.";

        db.UserPermissionContainers.Remove(link);
        await db.SaveChangesAsync();

        _logger.LogInformation("Revoked {Username}'s permission for container '{Container}'.", user.Username,
            containerName);
        return $"Revoked **{user.Username}**'s permission for `{containerName}`.";
    }

    /// <summary>Ensures a user record exists; updates their username if it has changed.</summary>
    public async Task EnsureUserExistsAsync(ulong discordUserId, string username)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.UserPermissions.FirstOrDefaultAsync(u => u.DiscordUserId == discordUserId);
        if (user is null)
        {
            db.UserPermissions.Add(new UserPermission { DiscordUserId = discordUserId, Username = username });
            await db.SaveChangesAsync();
        }
        else if (user.Username != username)
        {
            user.Username = username;
            await db.SaveChangesAsync();
        }
    }
}