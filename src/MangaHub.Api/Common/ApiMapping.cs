using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Common;

public static class ApiMapping
{
    public static UserResponse ToUserResponse(MangaUser user, string sessionToken = "") =>
        new(user.Id, user.Username, user.Role, user.PreferredLanguage, sessionToken, user.Email, !string.IsNullOrWhiteSpace(user.PasswordHash), !string.IsNullOrWhiteSpace(user.GoogleSubject), user.EmailConfirmedAt is not null, user.PendingEmail, user.UsageAnalyticsEnabled);

    public static UserAdminResponse ToUserAdminResponse(MangaUser user) =>
        new(user.Id, user.Username, user.Role, user.CreatedAt);

    public static MangaEntryResponse ToMangaEntryResponse(MangaEntry entry, UserMangaEntry shelf) =>
        new(
            entry.Id,
            entry.Title,
            entry.Authors,
            entry.Category,
            entry.Description,
            entry.CoverUrl,
            entry.OpenLibraryKey,
            entry.FirstPublishYear,
            entry.MetadataSource,
            entry.MyAnimeListId,
            entry.MediaType,
            entry.PublishingStatus,
            entry.ChapterCount,
            entry.VolumeCount,
            shelf.ReadingStatus,
            entry.MangaDexId,
            entry.MangaDexLatestChapter,
            entry.MangaDexLastSyncedAt,
            entry.MangaUpdatesId,
            entry.MangaUpdatesLatestChapter,
            entry.MangaUpdatesStatus,
            entry.MangaUpdatesCompleted,
            entry.MangaUpdatesLastSyncedAt,
            entry.LocalSeriesId,
            shelf.CurrentChapter,
            shelf.Score,
            shelf.Category,
            shelf.Summary,
            shelf.Notes,
            entry.FallbackReaderUrl,
            entry.ReaderPreference,
            null,
            shelf.IsRead,
            false,
            shelf.LastExternalReaderVerifiedAt,
            shelf.ExternalReaderLatestChapter);

    public static CatalogMangaResponse ToCatalogMangaResponse(MangaEntry entry, bool isInMyShelf) =>
        new(
            entry.Id,
            entry.Title,
            entry.Authors,
            entry.Category,
            entry.Description,
            entry.CoverUrl,
            entry.OpenLibraryKey,
            entry.FirstPublishYear,
            entry.MetadataSource,
            entry.MyAnimeListId,
            entry.MediaType,
            entry.PublishingStatus,
            entry.ChapterCount,
            entry.VolumeCount,
            entry.MangaDexId,
            entry.MangaDexLatestChapter,
            entry.MangaDexLastSyncedAt,
            entry.MangaUpdatesId,
            entry.MangaUpdatesLatestChapter,
            entry.MangaUpdatesStatus,
            entry.MangaUpdatesCompleted,
            entry.MangaUpdatesLastSyncedAt,
            entry.LocalSeriesId,
            0,
            isInMyShelf,
            entry.FallbackReaderUrl,
            entry.ReaderPreference);
}
