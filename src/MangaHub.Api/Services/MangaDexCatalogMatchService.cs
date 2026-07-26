using System.Text.Json;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class MangaDexCatalogMatchService(IMangaDexCatalogLookup mangaDexCatalog)
{
    public async Task<MangaDexCatalogMatch?> FindAsync(string myAnimeListId, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(myAnimeListId) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        try
        {
            return await mangaDexCatalog.FindByMyAnimeListIdAsync(myAnimeListId, title, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
