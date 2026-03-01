namespace DiscordDockerManager.Models;

/// <summary>
/// Join table linking a <see cref="UserPermission"/> to a <see cref="DockerContainerConfig"/>.
/// </summary>
public class UserPermissionContainer
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>FK to <see cref="UserPermission"/>.</summary>
    public int UserPermissionId { get; set; }

    /// <summary>Navigation property.</summary>
    public UserPermission UserPermission { get; set; } = null!;

    /// <summary>FK to <see cref="DockerContainerConfig"/>.</summary>
    public int DockerContainerConfigId { get; set; }

    /// <summary>Navigation property.</summary>
    public DockerContainerConfig DockerContainerConfig { get; set; } = null!;

    /// <summary>When this permission was granted.</summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
