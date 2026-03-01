using DiscordDockerManager.Models;
using Microsoft.EntityFrameworkCore;

namespace DiscordDockerManager.Data;

/// <summary>
/// Entity Framework Core database context for the Discord Docker Manager application.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>Initialises a new instance with the given options.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>Discord user permission records.</summary>
    public DbSet<UserPermission> UserPermissions { get; set; } = null!;

    /// <summary>Docker container configurations.</summary>
    public DbSet<DockerContainerConfig> DockerContainerConfigs { get; set; } = null!;

    /// <summary>User-to-container permission join table.</summary>
    public DbSet<UserPermissionContainer> UserPermissionContainers { get; set; } = null!;

    /// <summary>Player join/leave events parsed from logs.</summary>
    public DbSet<PlayerEvent> PlayerEvents { get; set; } = null!;

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // UserPermission
        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordUserId).IsUnique();
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
        });

        // DockerContainerConfig
        modelBuilder.Entity<DockerContainerConfig>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ContainerId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Game).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        // UserPermissionContainer (join table)
        modelBuilder.Entity<UserPermissionContainer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserPermissionId, e.DockerContainerConfigId }).IsUnique();

            entity.HasOne(e => e.UserPermission)
                  .WithMany(u => u.ContainerPermissions)
                  .HasForeignKey(e => e.UserPermissionId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DockerContainerConfig)
                  .WithMany(c => c.UserPermissions)
                  .HasForeignKey(e => e.DockerContainerConfigId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // PlayerEvent
        modelBuilder.Entity<PlayerEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ContainerName, e.Timestamp });
            entity.Property(e => e.ContainerName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PlayerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.RawLogLine).HasMaxLength(1000);
            entity.Property(e => e.EventType).HasConversion<string>();

            entity.HasOne(e => e.DockerContainerConfig)
                  .WithMany(c => c.PlayerEvents)
                  .HasForeignKey(e => e.DockerContainerConfigId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
