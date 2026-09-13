namespace MangaHub.Web.Components.Design;

public static class CoverImageRules
{
    public static string DisplayUrl(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl) || !Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri)) return "";
        return uri.Host.EndsWith("mangadex.org", StringComparison.OrdinalIgnoreCase) ? "" : coverUrl;
    }
}
