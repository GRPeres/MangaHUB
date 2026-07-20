namespace MangaHub.Core.Models;

public sealed class SiteActivity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
}
