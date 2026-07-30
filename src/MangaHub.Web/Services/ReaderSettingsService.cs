using Microsoft.JSInterop;

namespace MangaHub.Web.Services;

public sealed record ReaderSettings(string PreferredLanguage = "en", int PreloadPageCount = 3)
{
    public static ReaderSettings Normalize(ReaderSettings? settings) => new(
        string.IsNullOrWhiteSpace(settings?.PreferredLanguage) ? "en" : settings.PreferredLanguage,
        settings?.PreloadPageCount is 0 or 1 or 3 or 5 ? settings.PreloadPageCount : 3);
}

public sealed class ReaderSettingsService(IJSRuntime js)
{
    public async Task<ReaderSettings?> GetAsync()
    {
        try
        {
            var settings = await js.InvokeAsync<ReaderSettings?>("mangaHubReader.getSettings");
            return settings is null ? null : ReaderSettings.Normalize(settings);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetAsync(ReaderSettings settings)
    {
        try
        {
            await js.InvokeVoidAsync("mangaHubReader.setSettings", ReaderSettings.Normalize(settings));
        }
        catch
        {
            // Reader preferences are optional and must not interrupt reading.
        }
    }
}
