namespace MangaHub.Web.Services;

using Microsoft.JSInterop;

public sealed class SessionTokenStore(IJSRuntime js)
{
    private const string StorageKey = "mangahub_session";
    private string token = "";
    private bool loaded;

    public async Task<string> GetAsync()
    {
        if (loaded)
        {
            return token;
        }

        try
        {
            token = await js.InvokeAsync<string>("localStorage.getItem", StorageKey) ?? "";
            if (string.IsNullOrWhiteSpace(token))
            {
                token = await js.InvokeAsync<string>("sessionStorage.getItem", StorageKey) ?? "";
            }
        }
        catch
        {
            token = "";
        }

        loaded = true;
        return token;
    }

    public async Task SetAsync(string value)
    {
        token = value;
        loaded = true;

        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
                await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
            }
            else
            {
                await js.InvokeVoidAsync("localStorage.setItem", StorageKey, value);
                await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, value);
            }
        }
        catch
        {
        }
    }
}
