using DiscordDockerManager.Models;

namespace DiscordDockerManager.Tests;

/// <summary>
///     Tests for <see cref="DockerContainerConfig" /> model validation logic.
/// </summary>
public class DockerContainerConfigTests
{
    [Fact]
    public void Default_IsEnabled_Is_True()
    {
        var config = new DockerContainerConfig();
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void Default_CreatedAt_Is_Recent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var config = new DockerContainerConfig();
        Assert.True(config.CreatedAt >= before);
        Assert.True(config.CreatedAt <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Properties_Can_Be_Set()
    {
        var config = new DockerContainerConfig
        {
            Name = "minecraft",
            ContainerId = "mc-container",
            Description = "Minecraft server",
            Game = "Minecraft",
            IsEnabled = false,
            PlayerJoinPattern = @"(.+) joined the game",
            PlayerLeavePattern = @"(.+) left the game"
        };

        Assert.Equal("minecraft", config.Name);
        Assert.Equal("mc-container", config.ContainerId);
        Assert.Equal("Minecraft", config.Game);
        Assert.False(config.IsEnabled);
        Assert.NotNull(config.PlayerJoinPattern);
        Assert.NotNull(config.PlayerLeavePattern);
    }

    [Fact]
    public void Navigation_Collections_Are_Empty_By_Default()
    {
        var config = new DockerContainerConfig();
        Assert.Empty(config.UserPermissions);
        Assert.Empty(config.PlayerEvents);
    }
}