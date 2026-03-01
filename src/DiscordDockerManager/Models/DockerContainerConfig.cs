namespace DiscordDockerManager.Models;

/// <summary>
///     Configuration record for a managed Docker container / game server.
/// </summary>
public class DockerContainerConfig
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Friendly name used in Discord commands (e.g. "minecraft").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Docker container name or ID used by the Docker API.</summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>Human-readable description of the server.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Game type label (e.g. Minecraft, Valheim).</summary>
    public string Game { get; set; } = string.Empty;

    /// <summary>Whether this container is actively monitored / manageable.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Regex pattern to detect a player joining. Group 1 must capture the player name.</summary>
    public string? PlayerJoinPattern { get; set; }

    /// <summary>Regex pattern to detect a player leaving. Group 1 must capture the player name.</summary>
    public string? PlayerLeavePattern { get; set; }

    /// <summary>Timestamp when this record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User-container permission links.</summary>
    public ICollection<UserPermissionContainer> UserPermissions { get; set; } = [];

    /// <summary>Player events recorded for this container.</summary>
    public ICollection<PlayerEvent> PlayerEvents { get; set; } = [];
}