using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace DiscordDockerManager.Services;

/// <summary>
///     Service that resolves the server's external (WAN) IP address by querying a public IP-echo endpoint.
/// </summary>
public class ExternalIpService
{
    /// <summary>Named HTTP client used for external IP lookups.</summary>
    public const string HttpClientName = "ExternalIp";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalIpService> _logger;

    /// <summary>Initialises the service.</summary>
    public ExternalIpService(IHttpClientFactory httpClientFactory, ILogger<ExternalIpService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    ///     Fetches the external IP address of the host machine.
    ///     Returns <c>null</c> if the lookup fails.
    /// </summary>
    public async Task<string?> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var ip = await client.GetStringAsync("https://ipinfo.io/ip", cancellationToken);
            return ip.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch external IP address.");
            return null;
        }
    }
}
