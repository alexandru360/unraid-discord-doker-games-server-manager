namespace DiscordDockerManager.Config;

/// <summary>
/// Configuration section for the Discord bot.
/// </summary>
public class DiscordConfig
{
    /// <summary>Bot token obtained from the Discord Developer Portal.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Guild (server) ID to register slash commands instantly.
    /// Set to 0 to register globally (takes up to 1 hour to propagate).
    /// </summary>
    public ulong GuildId { get; set; }
}

/// <summary>
/// Configuration section for Docker daemon connectivity.
/// </summary>
public class DockerConfig
{
    /// <summary>
    /// Docker socket or TCP endpoint.
    /// Default is the Unix socket: <c>unix:///var/run/docker.sock</c>.
    /// </summary>
    public string Endpoint { get; set; } = "unix:///var/run/docker.sock";
}

/// <summary>
/// Configuration section for the SQLite database.
/// </summary>
public class DatabaseConfig
{
    /// <summary>EF Core connection string (e.g. <c>Data Source=gamemanager.db</c>).</summary>
    public string ConnectionString { get; set; } = "Data Source=/data/gamemanager.db";
}

/// <summary>
/// Configuration section for the Ollama AI integration.
/// </summary>
public class OllamaConfig
{
    /// <summary>Whether Ollama log parsing is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base URL of the Ollama server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model name to use for log parsing (e.g. <c>llama3</c>).</summary>
    public string Model { get; set; } = "llama3";
}

/// <summary>
/// Per-container configuration entry from <c>appsettings.json</c>.
/// </summary>
public class ContainerConfigEntry
{
    /// <summary>Friendly name used in Discord commands.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Docker container name or ID.</summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>Human-readable description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Game type label.</summary>
    public string Game { get; set; } = string.Empty;

    /// <summary>Whether this container is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Regex for detecting a player joining (group 1 = player name).</summary>
    public string? PlayerJoinPattern { get; set; }

    /// <summary>Regex for detecting a player leaving (group 1 = player name).</summary>
    public string? PlayerLeavePattern { get; set; }
}
