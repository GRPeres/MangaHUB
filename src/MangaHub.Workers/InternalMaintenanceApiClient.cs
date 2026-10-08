using MangaHub.Infrastructure;
using MangaHub.Core.Services;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace MangaHub.Workers;

/// <summary>
/// The worker schedules maintenance; the API owns all provider calls and rate limiting.
/// </summary>
public sealed class InternalMaintenanceApiClient(HttpClient httpClient, IOptions<MangaHubOptions> options)
{
    public Task QueueAsync(string type, CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, $"internal/maintenance/{Uri.EscapeDataString(type)}/queue", cancellationToken);

    public Task RunWatchdogAsync(CancellationToken cancellationToken) => SendAsync(HttpMethod.Post, "internal/maintenance/watchdog", cancellationToken);

    public async Task<MaintenanceRunResult> RunAsync(string type, CancellationToken cancellationToken)
    {
        var token = options.Value.InternalWorkerToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("MangaHub:InternalWorkerToken must be configured for worker dispatch.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"internal/maintenance/{Uri.EscapeDataString(type)}");
        request.Headers.Add("X-MangaHub-Worker-Token", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<MaintenanceRunResult>(cancellationToken: cancellationToken)
            ?? new MaintenanceRunResult();
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
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Maintenance API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "unknown status"}): {DescribeProblem(body)}",
            null,
            response.StatusCode);
    }

    private static string DescribeProblem(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The API did not provide an error detail.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            foreach (var property in new[] { "detail", "title" })
            {
                if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return Truncate(value.GetString()!);
                }
            }
        }
        catch (JsonException)
        {
            // A plain-text upstream error is still more useful than a bare status code.
        }

        return Truncate(string.Join(" ", body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }

    private static string Truncate(string value) => value.Length <= 700 ? value : value[..700] + "...";
}
