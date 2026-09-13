namespace MangaHub.Web.Services;

[Flags]
public enum AppRefreshScope
{
    None = 0,
    Shelf = 1,
    Catalog = 2,
    Notifications = 4,
    Analytics = 8
}

/// <summary>
/// Coordinates data refreshes between independently mounted Blazor components
/// after a successful server-side mutation.
/// </summary>
public sealed class AppRefreshService
{
    public event Action<AppRefreshScope>? Changed;

    public void Notify(AppRefreshScope scope)
    {
        if (scope != AppRefreshScope.None)
        {
            Changed?.Invoke(scope);
        }
    }
}
