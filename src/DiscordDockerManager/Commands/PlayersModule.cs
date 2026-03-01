using Discord;
using Discord.Interactions;
using DiscordDockerManager.Data;
using DiscordDockerManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiscordDockerManager.Commands;

/// <summary>
/// Discord slash commands for player event history (<c>/players</c>).
/// </summary>
[Group("players", "View player activity on game servers")]
public class PlayersModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<PlayersModule> _logger;

    /// <summary>Initialises the module.</summary>
    public PlayersModule(IDbContextFactory<AppDbContext> dbFactory, ILogger<PlayersModule> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Shows recent player join/leave events for a container.</summary>
    [SlashCommand("list", "Show recent player events for a game server")]
    public async Task ListAsync(
        [Summary("container", "Friendly name of the container")] string containerName,
        [Summary("count", "How many recent events to show (default: 20)")] int count = 20)
    {
        await DeferAsync();
        count = Math.Clamp(count, 1, 50);

        await using var db = await _dbFactory.CreateDbContextAsync();

        var events = await db.PlayerEvents
            .Where(e => e.ContainerName == containerName)
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();

        if (events.Count == 0)
        {
            await FollowupAsync($"No player events recorded for `{containerName}`.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🎮 Player Events — {containerName}")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        foreach (var ev in events)
        {
            var icon = ev.EventType == PlayerEventType.Joined ? "➕" : "➖";
            embed.AddField(
                $"{icon} {ev.PlayerName}",
                $"`{ev.EventType}` at {ev.Timestamp:yyyy-MM-dd HH:mm:ss} UTC",
                inline: false);
        }

        await FollowupAsync(embed: embed.Build());
    }
}
