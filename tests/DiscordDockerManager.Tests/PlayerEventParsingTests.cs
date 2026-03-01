using System.Text.RegularExpressions;
using DiscordDockerManager.Models;
using DiscordDockerManager.Services;

namespace DiscordDockerManager.Tests;

/// <summary>
///     Tests for player event log line parsing in <see cref="LogMonitorService" />.
/// </summary>
public class PlayerEventParsingTests
{
    private static readonly Regex MinecraftJoinRegex = new(
        @"\[.+\] \[Server thread/INFO\]: (.+) joined the game",
        RegexOptions.Compiled);

    private static readonly Regex MinecraftLeaveRegex = new(
        @"\[.+\] \[Server thread/INFO\]: (.+) left the game",
        RegexOptions.Compiled);

    [Theory]
    [InlineData("[12:34:56] [Server thread/INFO]: Steve joined the game", "Steve", PlayerEventType.Joined)]
    [InlineData("[01:00:00] [Server thread/INFO]: Alex joined the game", "Alex", PlayerEventType.Joined)]
    public void Parses_Minecraft_Join_Events(string line, string expectedPlayer, PlayerEventType expectedType)
    {
        var (eventType, playerName) = LogMonitorService.ParseLine(line, MinecraftJoinRegex, MinecraftLeaveRegex);
        Assert.Equal(expectedType, eventType);
        Assert.Equal(expectedPlayer, playerName);
    }

    [Theory]
    [InlineData("[12:34:56] [Server thread/INFO]: Steve left the game", "Steve", PlayerEventType.Left)]
    [InlineData("[23:59:59] [Server thread/INFO]: Notch left the game", "Notch", PlayerEventType.Left)]
    public void Parses_Minecraft_Leave_Events(string line, string expectedPlayer, PlayerEventType expectedType)
    {
        var (eventType, playerName) = LogMonitorService.ParseLine(line, MinecraftJoinRegex, MinecraftLeaveRegex);
        Assert.Equal(expectedType, eventType);
        Assert.Equal(expectedPlayer, playerName);
    }

    [Fact]
    public void Returns_Unknown_For_Non_Player_Log_Lines()
    {
        const string line = "[12:34:56] [Server thread/INFO]: Saving world...";
        var (eventType, playerName) = LogMonitorService.ParseLine(line, MinecraftJoinRegex, MinecraftLeaveRegex);
        Assert.Equal(PlayerEventType.Unknown, eventType);
        Assert.Equal(string.Empty, playerName);
    }

    [Fact]
    public void Returns_Unknown_When_Both_Regexes_Are_Null()
    {
        const string line = "[12:34:56] [Server thread/INFO]: Steve joined the game";
        var (eventType, playerName) = LogMonitorService.ParseLine(line, null, null);
        Assert.Equal(PlayerEventType.Unknown, eventType);
        Assert.Equal(string.Empty, playerName);
    }

    [Fact]
    public void Returns_Unknown_For_Empty_Line()
    {
        var (eventType, playerName) =
            LogMonitorService.ParseLine(string.Empty, MinecraftJoinRegex, MinecraftLeaveRegex);
        Assert.Equal(PlayerEventType.Unknown, eventType);
        Assert.Equal(string.Empty, playerName);
    }

    [Theory]
    [InlineData("PlayerName Notch_123 joined the game", "Notch_123")]
    [InlineData("PlayerName Steve_McQueen joined the game", "Steve_McQueen")]
    public void Custom_Join_Regex_With_Player_Names_Containing_Underscores(string line, string expectedPlayer)
    {
        var joinRegex = new Regex(@"PlayerName (.+) joined the game");
        var (eventType, playerName) = LogMonitorService.ParseLine(line, joinRegex, null);
        Assert.Equal(PlayerEventType.Joined, eventType);
        Assert.Equal(expectedPlayer, playerName);
    }

    [Fact]
    public void PlayerEvent_Default_Timestamp_Is_Recent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ev = new PlayerEvent { ContainerName = "test", PlayerName = "Player1", EventType = PlayerEventType.Joined };
        Assert.True(ev.Timestamp >= before);
    }
}