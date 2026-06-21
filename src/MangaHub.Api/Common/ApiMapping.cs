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
            shelf.ReadingStatus,
            entry.MangaDexUrl,
            entry.MangaDexId,
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
            entry.MangaDexUrl,
            entry.MangaDexId,
            entry.LocalSeriesId,
            isInMyShelf);
}
