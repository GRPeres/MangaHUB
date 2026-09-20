using MangaHub.Core.Services;
using MangaHub.Core.Sources;

namespace MangaHub.Api.Services;

/// <summary>
/// Finds a conservative MangaDex match for catalog entries that do not have a MAL identity.
/// </summary>
public sealed class MangaDexTitleMatchService(IEnumerable<IMangaSource> sources)
{
    public async Task<MangaDexCatalogMatch?> FindAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var source = sources.FirstOrDefault(item => string.Equals(item.Name, "mangadex", StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return null;
        }

        try
        {
            var normalizedTitle = NormalizeTitle(title);
            var match = (await source.SearchAsync(title, cancellationToken))
                .FirstOrDefault(result => string.Equals(NormalizeTitle(result.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase));
            return match is null ? null : new MangaDexCatalogMatch(match.Id, match.Title);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string NormalizeTitle(string title) =>
        new(title.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
