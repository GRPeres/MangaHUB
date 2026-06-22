using MangaHub.Core.Dto;

namespace MangaHub.Core.Services;

public interface IMyAnimeListClient
{
    Task<IReadOnlyList<MetadataResult>> SearchMangaAsync(string query, CancellationToken cancellationToken);
}
