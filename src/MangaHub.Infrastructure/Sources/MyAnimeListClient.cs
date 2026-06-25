using System.Text.Json;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Sources;

public sealed class MyAnimeListClient(HttpClient httpClient, IOptions<MangaHubOptions> options) : IMyAnimeListClient
{
    public async Task<IReadOnlyList<MetadataResult>> SearchMangaAsync(string query, CancellationToken cancellationToken)
    {
        var clientId = options.Value.MyAnimeListClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildSearchUrl(query));
        request.Headers.TryAddWithoutValidation("X-MAL-CLIENT-ID", clientId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data))
        {
            return [];
        }

        var results = new List<MetadataResult>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("node", out var node))
            {
                continue;
            }

            var id = ReadInt(node, "id")?.ToString() ?? "";
            var title = ReadString(node, "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            results.Add(new MetadataResult(
                "myanimelist",
                id,
                title,
                ReadAuthors(node),
                ReadPicture(node),
                ReadYear(node),
                ReadCategory(node),
                ReadString(node, "synopsis"),
                ReadString(node, "media_type"),
                ReadString(node, "status"),
                ReadInt(node, "num_chapters"),
                ReadInt(node, "num_volumes"),
                "",
                id));
        }

        return results;
    }

    private static string BuildSearchUrl(string query)
    {
        const string fields = "id,title,main_picture,start_date,synopsis,media_type,status,genres,authors,num_volumes,num_chapters";
        return $"manga?q={Uri.EscapeDataString(query)}&limit=12&nsfw=true&fields={Uri.EscapeDataString(fields)}";
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static int? ReadYear(JsonElement node)
    {
        var startDate = ReadString(node, "start_date");
        return startDate.Length >= 4 && int.TryParse(startDate[..4], out var year) ? year : null;
    }

    private static string ReadPicture(JsonElement node)
    {
        if (!node.TryGetProperty("main_picture", out var picture))
        {
            return "";
        }

        return ReadString(picture, "large") is { Length: > 0 } large ? large : ReadString(picture, "medium");
    }

    private static string ReadCategory(JsonElement node)
    {
        if (!node.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(", ", genres.EnumerateArray()
            .Select(x => ReadString(x, "name"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(3));
    }

    private static string ReadAuthors(JsonElement node)
    {
        if (!node.TryGetProperty("authors", out var authorsElement) || authorsElement.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(", ", authorsElement.EnumerateArray()
            .Select(ReadAuthor)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .Take(4));
    }

    private static string ReadAuthor(JsonElement author)
    {
        if (!author.TryGetProperty("node", out var node))
        {
            return "";
        }

        var first = ReadString(node, "first_name");
        var last = ReadString(node, "last_name");
        return string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
    }
}
