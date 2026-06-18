using System.Text.Json;
using MangaHub.Core.Services;

namespace MangaHub.Infrastructure.Sources;

public sealed class OpenLibraryClient(HttpClient httpClient) : IOpenLibraryClient
{
    public async Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var fields = "key,title,author_name,cover_i,first_publish_year";
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

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(title))
            {
                results.Add(new OpenLibrarySearchResult(key, title, authors, coverUrl, year));
            }
        }

        return results;
    }
}

