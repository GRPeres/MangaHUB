using System.Text.Json;
using MangaHub.Core.Services;

namespace MangaHub.Infrastructure.Sources;

public sealed class OpenLibraryClient(HttpClient httpClient) : IOpenLibraryClient
{
    public async Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var fields = "key,title,author_name,cover_i,first_publish_year,subject,first_sentence";
        var url = $"/search.json?q={Uri.EscapeDataString(query)}&fields={fields}&limit=12";
        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var results = new List<OpenLibrarySearchResult>();

        foreach (var item in document.RootElement.GetProperty("docs").EnumerateArray())
        {
            var key = item.TryGetProperty("key", out var keyElement) ? keyElement.GetString() ?? "" : "";
            var title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? "" : "";
            var authors = item.TryGetProperty("author_name", out var authorElement)
                ? string.Join(", ", authorElement.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                : "";
            var coverUrl = item.TryGetProperty("cover_i", out var coverElement)
                ? $"https://covers.openlibrary.org/b/id/{coverElement.GetInt32()}-M.jpg"
                : "";
            var year = item.TryGetProperty("first_publish_year", out var yearElement) ? yearElement.GetInt32() : (int?)null;
            var category = item.TryGetProperty("subject", out var subjectElement)
                ? ChooseCategory(subjectElement.EnumerateArray().Select(x => x.GetString() ?? ""))
                : "";
            var description = item.TryGetProperty("first_sentence", out var sentenceElement)
                ? ReadStringOrArray(sentenceElement)
                : "";

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(title))
            {
                results.Add(new OpenLibrarySearchResult(key, title, authors, coverUrl, year, category, description));
            }
        }

        return results;
    }

    public async Task<OpenLibraryWorkDetails?> GetWorkAsync(string key, CancellationToken cancellationToken)
    {
        var normalizedKey = key.StartsWith('/') ? key : $"/works/{key}";
        var response = await httpClient.GetAsync($"{normalizedKey}.json", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var category = root.TryGetProperty("subjects", out var subjects)
            ? ChooseCategory(subjects.EnumerateArray().Select(x => x.GetString() ?? ""))
            : "";
        var description = root.TryGetProperty("description", out var descriptionElement)
            ? ReadStringOrObject(descriptionElement)
            : "";

        return new OpenLibraryWorkDetails(category, description);
    }

    private static string ChooseCategory(IEnumerable<string> subjects)
    {
        var candidates = subjects
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => x.Length <= 40)
            .Where(x => !x.Contains("protected daisy", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Contains("accessible book", StringComparison.OrdinalIgnoreCase))
            .Where(x => !x.Contains("juvenile", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var preferred = candidates.FirstOrDefault(x =>
            x.Contains("manga", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("comic", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("romance", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("fantasy", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("science fiction", StringComparison.OrdinalIgnoreCase));

        return preferred ?? candidates.FirstOrDefault() ?? "";
    }

    private static string ReadStringOrArray(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Array => element.EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "",
            _ => ""
        };
    }

    private static string ReadStringOrObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Object when element.TryGetProperty("value", out var value) => value.GetString() ?? "",
            _ => ""
        };
    }
}
