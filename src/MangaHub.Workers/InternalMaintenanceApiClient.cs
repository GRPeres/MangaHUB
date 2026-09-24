using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

/// <summary>
/// The worker schedules maintenance; the API owns all provider calls and rate limiting.
/// </summary>
public sealed class InternalMaintenanceApiClient(HttpClient httpClient, IOptions<MangaHubOptions> options)
{
    public Task QueueAsync(string type, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, $"internal/maintenance/{Uri.EscapeDataString(type)}/queue", cancellationToken);

    public Task RunWatchdogAsync(CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, "internal/maintenance/watchdog", cancellationToken);

    public async Task RunAsync(string type, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post, $"internal/maintenance/{Uri.EscapeDataString(type)}", cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var token = options.Value.InternalWorkerToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("MangaHub:InternalWorkerToken must be configured for worker dispatch.");
        }

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-MangaHub-Worker-Token", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
