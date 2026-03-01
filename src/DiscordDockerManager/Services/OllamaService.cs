using DiscordDockerManager.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using DiscordDockerManager.Config;

namespace DiscordDockerManager.Services;

/// <summary>
/// Optional service that uses an Ollama LLM to parse unusual container log lines
/// and extract player join/leave events.
/// </summary>
public class OllamaService
{
    private readonly OllamaConfig _config;
    private readonly ILogger<OllamaService> _logger;
    private OllamaApiClient? _client;

    /// <summary>Whether the Ollama integration is enabled in configuration.</summary>
    public bool IsEnabled => _config.Enabled;

    /// <summary>Initialises the service.</summary>
    public OllamaService(IOptions<OllamaConfig> config, ILogger<OllamaService> logger)
    {
        _config = config.Value;
        _logger = logger;

        if (_config.Enabled)
        {
            try
            {
                _client = new OllamaApiClient(new Uri(_config.BaseUrl), _config.Model);
                _logger.LogInformation("OllamaService initialised with model '{Model}' at {BaseUrl}.",
                    _config.Model, _config.BaseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialise Ollama client; AI log parsing will be disabled.");
                _client = null;
            }
        }
    }

    /// <summary>
    /// Asks the LLM to determine whether a log line represents a player join or leave event.
    /// Returns <c>null</c> if the line is not a player event or Ollama is unavailable.
    /// </summary>
    public async Task<(PlayerEventType EventType, string PlayerName)?> ParseLogLineAsync(
        string logLine, CancellationToken ct = default)
    {
        if (_client is null || !_config.Enabled)
            return null;

        try
        {
            var request = new OllamaSharp.Models.GenerateRequest
            {
                Model = _config.Model,
                Prompt =
                    $"Analyze this game server log line and determine if it is a player join or leave event.\n" +
                    $"Log line: {logLine}\n" +
                    $"Respond in the format: EVENT_TYPE|PLAYER_NAME\n" +
                    $"Where EVENT_TYPE is one of: JOINED, LEFT, NONE\n" +
                    $"If the player name is unknown, use UNKNOWN.\n" +
                    $"Examples:\n" +
                    $"  JOINED|Steve\n" +
                    $"  LEFT|Alex\n" +
                    $"  NONE|\n" +
                    $"Only respond with the formatted result, nothing else.",
                Stream = true
            };

            var response = string.Empty;

            await foreach (var chunk in _client.GenerateAsync(request, ct))
            {
                response += chunk?.Response ?? string.Empty;
            }

            response = response.Trim();
            var parts = response.Split('|', 2);
            if (parts.Length != 2) return null;

            var eventStr = parts[0].Trim().ToUpperInvariant();
            var playerName = parts[1].Trim();

            if (eventStr == "NONE" || string.IsNullOrEmpty(playerName) || playerName == "UNKNOWN")
                return null;

            var eventType = eventStr switch
            {
                "JOINED" => PlayerEventType.Joined,
                "LEFT" => PlayerEventType.Left,
                _ => PlayerEventType.Unknown
            };

            if (eventType == PlayerEventType.Unknown) return null;

            return (eventType, playerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama parsing failed for log line.");
            return null;
        }
    }
}
