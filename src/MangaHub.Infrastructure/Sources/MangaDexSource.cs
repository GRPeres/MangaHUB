using System.Text.Json;
using MangaHub.Core.Sources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Sources;

public sealed class MangaDexSource(HttpClient httpClient, IOptions<MangaHubOptions> options, IMemoryCache cache) : IMangaSource
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

    public async Task<IReadOnlyList<MangaSourceChapter>> GetChaptersAsync(string seriesId, string? language, CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled || string.IsNullOrWhiteSpace(seriesId))
        {
            return [];
        }

        var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "all" : language.Trim().ToLowerInvariant();
        var cacheKey = $"mangadex:reader:chapters:{seriesId}:{normalizedLanguage}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<MangaSourceChapter>? cached) && cached is not null)
        {
            return cached;
        }

        var chapters = new List<MangaSourceChapter>();
        var offset = 0;
        var total = int.MaxValue;
        var maximum = Math.Max(100, options.Value.MangaDexReaderMaxChapters);

        while (offset < total && offset < maximum)
        {
            var limit = Math.Min(100, maximum - offset);
            var languageFilter = normalizedLanguage == "all" ? "" : $"&translatedLanguage[]={Uri.EscapeDataString(normalizedLanguage)}";
            var response = await httpClient.GetAsync(
                $"/manga/{Uri.EscapeDataString(seriesId)}/feed?limit={limit}&offset={offset}{languageFilter}&includeExternalUrl=0&order[chapter]=asc",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var data = document.RootElement.TryGetProperty("data", out var dataElement) ? dataElement : default;
            total = ReadInt(document.RootElement, "total") ?? 0;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var item in data.EnumerateArray())
            {
                var id = ReadString(item, "id");
                if (string.IsNullOrWhiteSpace(id) || !item.TryGetProperty("attributes", out var attributes))
                {
                    continue;
                }

                var number = ReadString(attributes, "chapter");
                chapters.Add(new MangaSourceChapter(
                    id,
                    string.IsNullOrWhiteSpace(number) ? "Extra" : number,
                    ReadString(attributes, "title"),
                    ReadInt(attributes, "pages") ?? 0,
                    ReadString(attributes, "translatedLanguage")));
            }

            offset += data.GetArrayLength();
        }

        var uniqueChapters = chapters
            .GroupBy(
                chapter => string.Equals(chapter.Number, "Extra", StringComparison.OrdinalIgnoreCase)
                    ? $"{chapter.Number}:{chapter.Title}:{chapter.Id}"
                    : $"{chapter.Number}:{chapter.Language}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(chapter => ChapterSortKey(chapter.Number))
            .ThenBy(chapter => chapter.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var expiration = TimeSpan.FromMinutes(Math.Max(1, options.Value.MangaDexReaderCacheMinutes));
        cache.Set(cacheKey, (IReadOnlyList<MangaSourceChapter>)uniqueChapters, expiration);
        return uniqueChapters;
    }

    public async Task<IReadOnlyList<MangaPage>> GetPagesAsync(string chapterId, CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled || string.IsNullOrWhiteSpace(chapterId))
        {
            return [];
        }

        var cacheKey = $"mangadex:reader:pages:{chapterId}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<MangaPage>? cached) && cached is not null)
        {
            return cached;
        }

        var response = await httpClient.GetAsync($"/at-home/server/{Uri.EscapeDataString(chapterId)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var baseUrl = ReadString(document.RootElement, "baseUrl").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !document.RootElement.TryGetProperty("chapter", out var chapter)
            || string.IsNullOrWhiteSpace(ReadString(chapter, "hash"))
            || !chapter.TryGetProperty("data", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hash = ReadString(chapter, "hash");
        var pages = files.EnumerateArray()
            .Select((file, index) => new MangaPage(index, $"{baseUrl}/data/{Uri.EscapeDataString(hash)}/{Uri.EscapeDataString(file.GetString() ?? string.Empty)}"))
            .Where(page => !page.Url.EndsWith("/", StringComparison.Ordinal))
            .ToList();

        var expiration = TimeSpan.FromMinutes(Math.Max(1, options.Value.MangaDexReaderCacheMinutes));
        cache.Set(cacheKey, (IReadOnlyList<MangaPage>)pages, expiration);
        return pages;
    }

    private static string ReadLocalized(JsonElement element)
    {
        if (element.TryGetProperty("en", out var en))
        {
            return en.GetString() ?? "";
        }

        var first = element.EnumerateObject().FirstOrDefault();
        return first.Value.ValueKind == JsonValueKind.String ? first.Value.GetString() ?? "" : "";
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static decimal ChapterSortKey(string number) =>
        decimal.TryParse(number, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : decimal.MaxValue;
}
