using System.Text.Json;
using MangaHub.Core.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Sources;

public sealed class MangaDexSource(HttpClient httpClient, IOptions<MangaHubOptions> options) : IMangaSource
{
    public string Name => "mangadex";

    public async Task<IReadOnlyList<MangaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled)
        {
            return [];
        }

        var response = await httpClient.GetAsync($"/manga?limit=12&title={Uri.EscapeDataString(query)}&includes[]=cover_art", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var results = new List<MangaSearchResult>();

        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? "";
            var attributes = item.GetProperty("attributes");
            var title = ReadLocalized(attributes.GetProperty("title"));
            var description = attributes.TryGetProperty("description", out var descriptions) ? ReadLocalized(descriptions) : "";
            var status = attributes.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "unknown" : "unknown";
            results.Add(new MangaSearchResult(id, title, description, "", status, Name));
        }

        return results;
    }

    public Task<MangaSourceSeries?> GetSeriesAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<MangaSourceSeries?>(null);

    public Task<IReadOnlyList<MangaSourceChapter>> GetChaptersAsync(string seriesId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaSourceChapter>>([]);

    public Task<IReadOnlyList<MangaPage>> GetPagesAsync(string chapterId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaPage>>([]);

    private static string ReadLocalized(JsonElement element)
    {
        if (element.TryGetProperty("en", out var en))
        {
            return en.GetString() ?? "";
        }

        var first = element.EnumerateObject().FirstOrDefault();
        return first.Value.ValueKind == JsonValueKind.String ? first.Value.GetString() ?? "" : "";
    }
}

