namespace MangaHub.Core.Models;

public static class ReaderPreference
{
    public const string MangaHub = "mangahub";
    public const string External = "external";
    public const string Hybrid = "hybrid";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        External => External,
        Hybrid => Hybrid,
        _ => MangaHub
    };
}
