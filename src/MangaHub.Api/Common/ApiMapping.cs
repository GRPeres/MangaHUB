using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Common;

public static class ApiMapping
{
    public static UserResponse ToUserResponse(MangaUser user, string sessionToken = "") =>
        new(user.Id, user.Username, user.Role, sessionToken);

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
            entry.MangaDexUrl,
            entry.MangaDexId,
            entry.MangaDexLastSyncedAt,
            entry.LocalSeriesId,
            shelf.CurrentChapter,
            shelf.Score,
            shelf.Category,
            shelf.Summary,
            shelf.Notes);

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
            entry.MangaDexUrl,
            entry.MangaDexId,
            entry.MangaDexLastSyncedAt,
            entry.LocalSeriesId,
            0,
            isInMyShelf);
}
