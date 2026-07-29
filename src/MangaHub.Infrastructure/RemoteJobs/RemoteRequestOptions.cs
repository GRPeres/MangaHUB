namespace MangaHub.Infrastructure.RemoteJobs;

public enum RemoteProvider
{
    MangaDexApi,
    MangaDexPages,
    MangaUpdates,
    MyAnimeList,
    OpenLibrary
}

public enum RemoteJobPriority
{
    UserBlocking = 0,
    ReaderAhead = 5,
    Interactive = 10,
    ReleaseSync = 20,
    Prefetch = 30,
    Maintenance = 40,
    Backfill = 50
}

public sealed class RemoteRequestLimitsOptions
{
    public RemoteProviderLimitOptions MangaDexApi { get; set; } = new(2, 2);
    public RemoteProviderLimitOptions MangaDexPages { get; set; } = new(1, 2);
    public RemoteProviderLimitOptions MangaUpdates { get; set; } = new(0.5, 1);
    public RemoteProviderLimitOptions MyAnimeList { get; set; } = new(1, 1);
    public RemoteProviderLimitOptions OpenLibrary { get; set; } = new(0.5, 1);

    public RemoteProviderLimitOptions Get(RemoteProvider provider) => provider switch
    {
        RemoteProvider.MangaDexApi => MangaDexApi,
        RemoteProvider.MangaDexPages => MangaDexPages,
        RemoteProvider.MangaUpdates => MangaUpdates,
        RemoteProvider.MyAnimeList => MyAnimeList,
        RemoteProvider.OpenLibrary => OpenLibrary,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };
}

public sealed class RemoteProviderLimitOptions
{
    public RemoteProviderLimitOptions()
    {
    }

    public RemoteProviderLimitOptions(double requestsPerSecond, int maxConcurrency)
    {
        RequestsPerSecond = requestsPerSecond;
        MaxConcurrency = maxConcurrency;
    }

    public double RequestsPerSecond { get; set; } = 1;
    public int MaxConcurrency { get; set; } = 1;
}
