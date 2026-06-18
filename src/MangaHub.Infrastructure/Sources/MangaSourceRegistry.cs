using MangaHub.Core.Sources;

namespace MangaHub.Infrastructure.Sources;

public sealed class MangaSourceRegistry(IEnumerable<IMangaSource> sources)
{
    public IMangaSource Get(string sourceName) =>
        sources.First(x => string.Equals(x.Name, sourceName, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<IMangaSource> All => sources;
}

