using System.Text;
using DiscordDockerManager.Config;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordDockerManager.Services;

/// <summary>
///     Result object returned by Docker operations.
/// </summary>
public record DockerOperationResult(bool Success, string Message);

/// <summary>
///     Wraps the Docker.DotNet client to provide container management operations.
/// </summary>
public class DockerService : IDisposable
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerService> _logger;

    /// <summary>Initialises the service using the configured Docker endpoint.</summary>
    public DockerService(IOptions<DockerConfig> config, ILogger<DockerService> logger)
    {
        _logger = logger;
        var endpoint = config.Value.Endpoint;

        Uri uri;
        try
        {
            uri = new Uri(endpoint);
        }
        catch (UriFormatException)
        {
            _logger.LogWarning("Invalid Docker endpoint '{Endpoint}', falling back to default socket.", endpoint);
            uri = new Uri("unix:///var/run/docker.sock");
        }

        _client = new DockerClientConfiguration(uri).CreateClient();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }

    /// <summary>
    ///     Returns the Docker container name of the bot itself (if running inside Docker),
    ///     so it can be excluded from listings. Returns <c>null</c> when not detected.
    /// </summary>
    public async Task<string?> GetSelfContainerNameAsync(CancellationToken ct = default)
    {
        try
        {
            var hostname = Environment.MachineName;
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true }, ct);

            var self = containers.FirstOrDefault(c =>
                c.ID.StartsWith(hostname, StringComparison.OrdinalIgnoreCase));

            if (self is not null)
            {
                var name = self.Names.FirstOrDefault()?.TrimStart('/');
                _logger.LogInformation("Detected bot's own container: '{Name}' (ID: {Id}).", name, self.ID[..12]);
                return name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect bot's own container.");
        }

        return null;
    }

    /// <summary>
    ///     Restarts a container by its name or ID.
    /// </summary>
    public async Task<DockerOperationResult> RestartContainerAsync(string containerIdOrName,
        CancellationToken ct = default)
    {
        try
        {
            await _client.Containers.RestartContainerAsync(containerIdOrName,
                new ContainerRestartParameters { WaitBeforeKillSeconds = 5 }, ct);
            _logger.LogInformation("Container '{Container}' restarted successfully.", containerIdOrName);
            return new DockerOperationResult(true, $"Container `{containerIdOrName}` restarted successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart container '{Container}'.", containerIdOrName);
            return new DockerOperationResult(false, $"Failed to restart container `{containerIdOrName}`: {ex.Message}");
        }
    }

    /// <summary>
    ///     Gets the status of a container.
    /// </summary>
    public async Task<DockerOperationResult> GetContainerStatusAsync(string containerIdOrName,
        CancellationToken ct = default)
    {
        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true }, ct);

            var container = containers.FirstOrDefault(c =>
                c.ID.StartsWith(containerIdOrName, StringComparison.OrdinalIgnoreCase) ||
                c.Names.Any(n => n.TrimStart('/').Equals(containerIdOrName, StringComparison.OrdinalIgnoreCase)));

            if (container is null)
                return new DockerOperationResult(false, $"Container `{containerIdOrName}` not found.");

            var info = $"**{containerIdOrName}** — Status: `{container.State}` ({container.Status})";
            return new DockerOperationResult(true, info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get status for container '{Container}'.", containerIdOrName);
            return new DockerOperationResult(false, $"Error retrieving status: {ex.Message}");
        }
    }

    /// <summary>
    ///     Gets statuses for multiple containers in one Docker API call.
    /// </summary>
    public async Task<Dictionary<string, (bool Found, string State, string Status)>> GetContainerStatusesAsync(
        IEnumerable<string> containerIdsOrNames,
        CancellationToken ct = default)
    {
        var identifiers = containerIdsOrNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = identifiers.ToDictionary(id => id, _ => (Found: false, State: string.Empty, Status: string.Empty),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);

            foreach (var id in identifiers)
            {
                var match = containers.FirstOrDefault(c =>
                    c.ID.StartsWith(id, StringComparison.OrdinalIgnoreCase) ||
                    c.Names.Any(n => n.TrimStart('/').Equals(id, StringComparison.OrdinalIgnoreCase)));

                if (match is not null)
                {
                    result[id] = (true, match.State, match.Status);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve bulk container statuses.");
        }

        return result;
    }

    /// <summary>
    ///     Retrieves the last <paramref name="lines" /> lines from a container's logs.
    /// </summary>
    public async Task<DockerOperationResult> GetContainerLogsAsync(string containerIdOrName, int lines = 50,
        CancellationToken ct = default)
    {
        try
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Tail = lines.ToString(),
                Timestamps = true
            };

            using var logStream =
                await _client.Containers.GetContainerLogsAsync(containerIdOrName, false, parameters, ct);
            var (stdout, stderr) = await logStream.ReadOutputToEndAsync(ct);
            var combined = (stdout + stderr).Trim();

            if (string.IsNullOrWhiteSpace(combined))
                return new DockerOperationResult(true, "No log output.");

            // Discord has a 2000-character message limit; truncate if needed
            if (combined.Length > 1800)
                combined = combined[^1800..];

            return new DockerOperationResult(true, $"```\n{combined}\n```");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve logs for container '{Container}'.", containerIdOrName);
            return new DockerOperationResult(false, $"Error retrieving logs: {ex.Message}");
        }
    }

    /// <summary>
    ///     Opens a log stream for a container, yielding each line to the provided callback.
    ///     Runs until <paramref name="ct" /> is cancelled.
    /// </summary>
    public async Task TailLogsAsync(string containerIdOrName, Func<string, Task> onLine, CancellationToken ct)
    {
        try
        {
            var parameters = new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = true,
                Tail = "0",
                Timestamps = false
            };

            using var logStream =
                await _client.Containers.GetContainerLogsAsync(containerIdOrName, false, parameters, ct);

            var buffer = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                var result = await logStream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
                if (result.Count == 0) break;

                var text = Encoding.UTF8.GetString(buffer, 0, result.Count).TrimEnd();
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    await onLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tailing logs for container '{Container}'.", containerIdOrName);
        }
    }
}