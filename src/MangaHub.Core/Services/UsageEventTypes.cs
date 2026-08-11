namespace MangaHub.Core.Services;

public static class UsageEventTypes
{
    public const string SignIn = "auth.sign_in";
    public const string ShelfAdded = "shelf.added";
    public const string ShelfUpdated = "shelf.updated";
    public const string ShelfRemoved = "shelf.removed";
    public const string MangaStarted = "reader.manga_started";
    public const string ChapterCompleted = "reader.chapter_completed";
    public const string MangaCompleted = "reader.manga_completed";
    public const string ReaderSession = "reader.session";
    public const string CatalogCreated = "catalog.created";
    public const string CatalogUpdated = "catalog.updated";
    public const string Search = "search.performed";
    public const string NotificationOpened = "notification.opened";
}
