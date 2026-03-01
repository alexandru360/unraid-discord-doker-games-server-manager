using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiscordDockerManager.Tests;

/// <summary>
/// Tests for <see cref="AppDbContext"/> entity relationships and constraints.
/// </summary>
public class AppDbContextTests : IAsyncDisposable
{
    private readonly AppDbContext _db;

    public AppDbContextTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Can_Add_And_Retrieve_DockerContainerConfig()
    {
        _db.DockerContainerConfigs.Add(new DockerContainerConfig
        {
            Name = "minecraft",
            ContainerId = "mc-server",
            Game = "Minecraft"
        });
        await _db.SaveChangesAsync();

        var config = await _db.DockerContainerConfigs.FirstOrDefaultAsync(c => c.Name == "minecraft");
        Assert.NotNull(config);
        Assert.Equal("mc-server", config.ContainerId);
    }

    [Fact]
    public async Task Can_Add_UserPermission_With_Container_Link()
    {
        var container = new DockerContainerConfig { Name = "valheim", ContainerId = "vh-server", Game = "Valheim" };
        _db.DockerContainerConfigs.Add(container);
        await _db.SaveChangesAsync();

        var user = new UserPermission { DiscordUserId = 1234UL, Username = "testuser" };
        _db.UserPermissions.Add(user);
        await _db.SaveChangesAsync();

        _db.UserPermissionContainers.Add(new UserPermissionContainer
        {
            UserPermissionId = user.Id,
            DockerContainerConfigId = container.Id
        });
        await _db.SaveChangesAsync();

        var loaded = await _db.UserPermissions
            .Include(u => u.ContainerPermissions)
                .ThenInclude(cp => cp.DockerContainerConfig)
            .FirstAsync(u => u.DiscordUserId == 1234UL);

        Assert.Single(loaded.ContainerPermissions);
        Assert.Equal("valheim", loaded.ContainerPermissions.First().DockerContainerConfig.Name);
    }

    [Fact]
    public async Task Can_Add_And_Retrieve_PlayerEvent()
    {
        var container = new DockerContainerConfig { Name = "rust", ContainerId = "rust-server", Game = "Rust" };
        _db.DockerContainerConfigs.Add(container);
        await _db.SaveChangesAsync();

        _db.PlayerEvents.Add(new PlayerEvent
        {
            ContainerName = "rust",
            PlayerName = "Facepunch",
            EventType = PlayerEventType.Joined,
            RawLogLine = "Facepunch joined the game",
            DockerContainerConfigId = container.Id
        });
        await _db.SaveChangesAsync();

        var events = await _db.PlayerEvents.Where(e => e.ContainerName == "rust").ToListAsync();
        Assert.Single(events);
        Assert.Equal("Facepunch", events[0].PlayerName);
        Assert.Equal(PlayerEventType.Joined, events[0].EventType);
    }

    [Fact]
    public async Task Deleting_Container_Cascades_To_PlayerEvents()
    {
        var container = new DockerContainerConfig { Name = "ark", ContainerId = "ark-server", Game = "ARK" };
        _db.DockerContainerConfigs.Add(container);
        await _db.SaveChangesAsync();

        _db.PlayerEvents.Add(new PlayerEvent
        {
            ContainerName = "ark",
            PlayerName = "Dino",
            EventType = PlayerEventType.Joined,
            DockerContainerConfigId = container.Id
        });
        await _db.SaveChangesAsync();

        _db.DockerContainerConfigs.Remove(container);
        await _db.SaveChangesAsync();

        var events = await _db.PlayerEvents.Where(e => e.ContainerName == "ark").ToListAsync();
        Assert.Empty(events);
    }
}
