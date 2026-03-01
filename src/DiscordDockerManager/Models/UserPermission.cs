namespace DiscordDockerManager.Models;

/// <summary>
///     Represents a Discord user with permissions to manage Docker containers.
/// </summary>
public class UserPermission
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Discord user snowflake ID.</summary>
    public ulong DiscordUserId { get; set; }

    /// <summary>Discord username for display purposes.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Whether this user has admin privileges (can grant/revoke permissions).</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Timestamp when this record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Container permissions granted to this user.</summary>
    public ICollection<UserPermissionContainer> ContainerPermissions { get; set; } = [];
}