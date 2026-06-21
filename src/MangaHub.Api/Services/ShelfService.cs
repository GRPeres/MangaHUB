using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Services;

public sealed class ShelfService(
    ShelfRepository shelf,
    CatalogRepository catalog,
    UserRepository users)
{
    public Task<List<MangaEntryResponse>> ListAsync(Guid targetUserId, string? status, CancellationToken cancellationToken) =>
        shelf.ListEntriesAsync(targetUserId, status, cancellationToken);

    public async Task<MangaEntryResponse?> AddAsync(Guid userId, AddToShelfRequest request, CancellationToken cancellationToken)
    {
        var manga = await catalog.GetByIdNoTrackingAsync(request.MangaEntryId, cancellationToken);
        if (manga is null)
        {
            return null;
        }

        var existingShelf = await shelf.GetAsync(userId, manga.Id, cancellationToken);
        if (existingShelf is null)
        {
            existingShelf = new UserMangaEntry
            {
                UserId = userId,
                MangaEntryId = manga.Id
            };
            TextRules.ApplyShelfRequest(existingShelf, request, manga);
            shelf.Add(existingShelf);
        }
        else
        {
            TextRules.ApplyShelfRequest(existingShelf, request, manga);
            existingShelf.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await shelf.SaveChangesAsync(cancellationToken);
        return ApiMapping.ToMangaEntryResponse(manga, existingShelf);
    }

    public async Task<MangaEntryResponse?> UpdateAsync(Guid targetUserId, Guid entryId, AddToShelfRequest request, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(targetUserId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        TextRules.ApplyShelfRequest(shelfEntry, request, shelfEntry.MangaEntry);
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
        return ApiMapping.ToMangaEntryResponse(shelfEntry.MangaEntry, shelfEntry);
    }

    public async Task<bool> RemoveAsync(Guid targetUserId, Guid entryId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetAsync(targetUserId, entryId, cancellationToken);
        if (shelfEntry is null)
        {
            return false;
        }

        shelf.Remove(shelfEntry);
        await shelf.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Guid?> ResolveShelfUserIdAsync(MangaUser currentUser, Guid? requestedUserId, CancellationToken cancellationToken)
    {
        if (requestedUserId is null || requestedUserId == currentUser.Id)
        {
            return currentUser.Id;
        }

        if (!CurrentUserService.IsAdmin(currentUser))
        {
            return null;
        }

        return await users.ExistsAsync(requestedUserId.Value, cancellationToken)
            ? requestedUserId.Value
            : null;
    }

    public async Task<ShelfImportResponse?> ImportAsync(Guid userId, bool canCreateCatalog, ShelfImportRequest import, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(import.CsvText))
        {
            return null;
        }

        var rows = TextRules.ParseCsv(import.CsvText);
        if (rows.Count == 0)
        {
            return null;
        }

        var headers = rows[0].Select(TextRules.NormalizeHeader).ToList();
        var messages = new List<string>();
        var imported = 0;
        var createdCatalog = 0;
        var updatedShelf = 0;
        var skipped = 0;
        var rowNumber = 1;

        foreach (var row in rows.Skip(1))
        {
            rowNumber++;
            try
            {
                var createdThisRow = false;
                var values = TextRules.RowToDictionary(headers, row);
                var title = TextRules.FirstValue(values, "name+link", "name", "title", "manga", "series");
                var link = TextRules.FirstValue(values, "link", "url", "mangadexurl", "mangadex", "sourceurl");
                var coverUrl = TextRules.FirstValue(values, "cover", "coverurl", "image", "imageurl", "thumbnail", "poster");
                if (Uri.TryCreate(title, UriKind.Absolute, out _))
                {
                    link = title;
                    title = TextRules.FirstValue(values, "title", "name", "manga", "series");
                }

                title = TextRules.CleanTitle(title);
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(link))
                {
                    skipped++;
                    continue;
                }

                var mangaDexId = TextRules.ExtractMangaDexId(link);
                MangaEntry? manga = null;
                if (!string.IsNullOrWhiteSpace(mangaDexId))
                {
                    manga = await catalog.FindByMangaDexIdAsync(mangaDexId, cancellationToken);
                }

                if (manga is null && !string.IsNullOrWhiteSpace(link))
                {
                    manga = await catalog.FindByMangaDexUrlAsync(link, cancellationToken);
                }

                if (manga is null && !string.IsNullOrWhiteSpace(title))
                {
                    manga = await catalog.FindByTitleAsync(title, cancellationToken);
                }

                if (manga is null)
                {
                    if (!canCreateCatalog)
                    {
                        skipped++;
                        messages.Add($"Skipped '{title}': not found in catalog.");
                        continue;
                    }

                    manga = new MangaEntry
                    {
                        CreatedByUserId = userId,
                        Title = string.IsNullOrWhiteSpace(title) ? link : title,
                        Category = TextRules.FirstValue(values, "tipo", "type", "category", "genre").Trim(),
                        Description = TextRules.FirstValue(values, "summary", "description").Trim(),
                        CoverUrl = coverUrl.Trim(),
                        MangaDexUrl = link.Trim(),
                        MangaDexId = mangaDexId
                    };
                    await catalog.AddAsync(manga, cancellationToken);
                    createdThisRow = true;
                }
                else if (!string.IsNullOrWhiteSpace(link) && string.IsNullOrWhiteSpace(manga.MangaDexUrl))
                {
                    manga.MangaDexUrl = link.Trim();
                    manga.MangaDexId = mangaDexId;
                    manga.UpdatedAt = DateTimeOffset.UtcNow;
                }

                if (!string.IsNullOrWhiteSpace(coverUrl) && string.IsNullOrWhiteSpace(manga.CoverUrl))
                {
                    manga.CoverUrl = coverUrl.Trim();
                    manga.UpdatedAt = DateTimeOffset.UtcNow;
                }

                var shelfEntry = await shelf.GetAsync(userId, manga.Id, cancellationToken);
                if (shelfEntry is null)
                {
                    shelfEntry = new UserMangaEntry
                    {
                        UserId = userId,
                        MangaEntryId = manga.Id
                    };
                    shelf.Add(shelfEntry);
                }
                else
                {
                    updatedShelf++;
                }

                shelfEntry.ReadingStatus = TextRules.NormalizeShelfStatus(TextRules.FirstValue(values, "status", "readingstatus"));
                shelfEntry.CurrentChapter = TextRules.FirstValue(values, "chapter", "currentchapter", "chapters").Trim();
                shelfEntry.Score = TextRules.ParseScore(TextRules.FirstValue(values, "rating", "score"));
                shelfEntry.Category = TextRules.FirstValue(values, "tipo", "type", "category", "genre").Trim();
                shelfEntry.Summary = TextRules.FirstValue(values, "summary", "description").Trim();
                shelfEntry.Notes = TextRules.FirstValue(values, "notes", "note").Trim();
                shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
                await shelf.SaveChangesAsync(cancellationToken);

                if (createdThisRow)
                {
                    createdCatalog++;
                }
                imported++;
            }
            catch (Exception ex)
            {
                skipped++;
                shelf.ClearTracking();
                messages.Add($"Skipped row {rowNumber}: {TextRules.DescribeImportException(ex)}.");
            }
        }

        if (!canCreateCatalog && import.CreateMissingCatalogEntries)
        {
            messages.Add("Missing catalog entries were not created because only admins can create catalog manga.");
        }

        return new ShelfImportResponse(imported, createdCatalog, updatedShelf, skipped, messages.Take(20).ToList());
    }
}
