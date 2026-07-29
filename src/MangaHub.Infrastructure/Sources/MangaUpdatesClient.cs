using System.Net.Http.Json;
using System.Text.Json;
using MangaHub.Core.Services;

namespace MangaHub.Infrastructure.Sources;

public sealed class MangaUpdatesClient(HttpClient httpClient) : IMangaUpdatesClient
{
    public async Task<IReadOnlyList<MangaUpdatesSearchResult>> SearchSeriesAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var response = await httpClient.PostAsJsonAsync("v1/series/search", new { search = query, stype = "title" }, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray()
            .Select(item => item.TryGetProperty("record", out var record) ? ToSearchResult(record) : null)
            .Where(result => result is not null)
            .Cast<MangaUpdatesSearchResult>()
            .ToList();
    }

    public async Task<MangaUpdatesSeriesDetails?> GetSeriesAsync(string seriesId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
        {
            return null;
        }

        using var response = await httpClient.GetAsync($"v1/series/{Uri.EscapeDataString(seriesId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var id = ReadString(root, "series_id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new MangaUpdatesSeriesDetails(
            id,
            ReadString(root, "title"),
            ReadDecimal(root, "latest_chapter"),
            ReadString(root, "status"),
            ReadBool(root, "completed"));
    }

    private static MangaUpdatesSearchResult? ToSearchResult(JsonElement record)
    {
        var id = ReadString(record, "series_id");
        var title = ReadString(record, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var alternativeTitles = record.TryGetProperty("associated", out var associated) && associated.ValueKind == JsonValueKind.Array
            ? associated.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList()
            : [];
        return new MangaUpdatesSearchResult(id, title, ReadString(record, "type"), ReadInt(record, "year"), alternativeTitles);
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static bool ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
}
