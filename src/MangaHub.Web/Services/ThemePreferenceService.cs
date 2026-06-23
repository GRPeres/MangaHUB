using Microsoft.JSInterop;

namespace MangaHub.Web.Services;

public sealed class ThemePreferenceService(IJSRuntime js)
{
    private const string StorageKey = "mangahub_theme";

    public async Task<bool?> GetDarkModeAsync()
    {
        try
        {
            var value = await js.InvokeAsync<string>("localStorage.getItem", StorageKey);
            return value?.ToLowerInvariant() switch
            {
                "dark" => true,
                "light" => false,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task SetDarkModeAsync(bool darkMode)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, darkMode ? "dark" : "light");
        }
        catch
        {
        }
    }
}
