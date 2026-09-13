using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

/// <summary>
/// The worker schedules maintenance; the API owns all provider calls and rate limiting.
/// </summary>
public sealed class InternalMaintenanceApiClient(HttpClient httpClient, IOptions<MangaHubOptions> options)
{
    public async Task RunAsync(string type, CancellationToken cancellationToken)
    {
        var token = options.Value.InternalWorkerToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("MangaHub:InternalWorkerToken must be configured for worker dispatch.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"internal/maintenance/{Uri.EscapeDataString(type)}");
        request.Headers.Add("X-MangaHub-Worker-Token", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
