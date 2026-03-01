namespace DiscordDockerManager.Models;

/// <summary>
/// Type of player event detected from container logs.
/// </summary>
public enum PlayerEventType
{
    /// <summary>Player joined the server.</summary>
    Joined,

    /// <summary>Player left the server.</summary>
    Left,

    /// <summary>Unknown / other parsed event.</summary>
    Unknown
}

/// <summary>
/// A recorded player join or leave event parsed from a container's log stream.
/// </summary>
public class PlayerEvent
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Friendly name of the container (matches <see cref="DockerContainerConfig.Name"/>).</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Name of the player as parsed from logs.</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>Whether the player joined or left.</summary>
    public PlayerEventType EventType { get; set; }

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Original raw log line that produced this event.</summary>
    public string RawLogLine { get; set; } = string.Empty;

    /// <summary>FK to the container configuration.</summary>
    public int DockerContainerConfigId { get; set; }

    /// <summary>Navigation property.</summary>
    public DockerContainerConfig DockerContainerConfig { get; set; } = null!;
}
