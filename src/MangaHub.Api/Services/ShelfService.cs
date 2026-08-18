using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class ShelfService(
    ShelfRepository shelf,
    CatalogRepository catalog,
    UserRepository users,
    UsageTrackingService? usage = null)
{
    public async Task<List<MangaEntryResponse>> ListAsync(Guid targetUserId, string? status, string? section, int offset, int limit, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(targetUserId, cancellationToken);
        var languages = LanguagePreferences.Parse(user?.PreferredLanguage);
        return await shelf.ListEntriesAsync(targetUserId, status, section, languages, offset, limit, cancellationToken);
    }

    public async Task<ShelfSectionSummaryResponse> GetSectionSummaryAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(targetUserId, cancellationToken);
        var languages = LanguagePreferences.Parse(user?.PreferredLanguage);
        return await shelf.GetSectionSummaryAsync(targetUserId, languages, cancellationToken);
    }

    public async Task<List<MangaEntryResponse>> ExportAsync(Guid userId, string? section, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        var languages = LanguagePreferences.Parse(user?.PreferredLanguage);
        return await shelf.ListEntriesAsync(userId, null, section, languages, 0, int.MaxValue, cancellationToken);
    }

    public async Task<MangaEntryResponse?> AddAsync(Guid userId, AddToShelfRequest request, CancellationToken cancellationToken)
    {
        var manga = await catalog.GetByIdNoTrackingAsync(request.MangaEntryId, cancellationToken);
        if (manga is null)
        {
            return null;
        }

        var existingShelf = await shelf.GetAsync(userId, manga.Id, cancellationToken);
        var isNewShelfEntry = existingShelf is null;
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
            var currentChapterChanged = !string.Equals(existingShelf.CurrentChapter, request.CurrentChapter.Trim(), StringComparison.Ordinal);
            TextRules.ApplyShelfRequest(existingShelf, request, manga);
            if (currentChapterChanged && existingShelf.ReadingStatus != "done")
            {
                existingShelf.IsRead = false;
            }
            existingShelf.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await shelf.SaveChangesAsync(cancellationToken);
        if (usage is not null) await usage.TrackAsync(userId, isNewShelfEntry ? UsageEventTypes.ShelfAdded : UsageEventTypes.ShelfUpdated, manga.Id, cancellationToken);
        return ApiMapping.ToMangaEntryResponse(manga, existingShelf);
    }

    public async Task<MangaEntryResponse?> UpdateAsync(Guid targetUserId, Guid entryId, AddToShelfRequest request, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(targetUserId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        var currentChapterChanged = !string.Equals(shelfEntry.CurrentChapter, request.CurrentChapter.Trim(), StringComparison.Ordinal);
        TextRules.ApplyShelfRequest(shelfEntry, request, shelfEntry.MangaEntry);
        if (currentChapterChanged && shelfEntry.ReadingStatus != "done")
        {
            shelfEntry.IsRead = false;
        }
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
        if (usage is not null) await usage.TrackAsync(targetUserId, UsageEventTypes.ShelfUpdated, entryId, cancellationToken);
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
        if (usage is not null) await usage.TrackAsync(targetUserId, UsageEventTypes.ShelfRemoved, entryId, cancellationToken);
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
        var availableHeaders = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappedFields = import.ColumnMappings?
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Key) && !string.IsNullOrWhiteSpace(mapping.Value))
            .ToDictionary(mapping => TextRules.NormalizeHeader(mapping.Key), mapping => mapping.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (mappedFields.Count > 0 && !mappedFields.ContainsKey("title"))
        {
            return new ShelfImportResponse(0, 0, 0, 0, ["Map one CSV column to Title before importing."]);
        }

        var duplicatedMappings = mappedFields.Values
            .GroupBy(TextRules.NormalizeHeader, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.First())
            .ToList();
        if (duplicatedMappings.Count > 0)
        {
            return new ShelfImportResponse(0, 0, 0, 0, [$"Each CSV column can be mapped once. Duplicate mapping: {string.Join(", ", duplicatedMappings)}."]);
        }

        foreach (var (_, sourceHeader) in mappedFields)
        {
            if (!availableHeaders.Contains(TextRules.NormalizeHeader(sourceHeader)))
            {
                return new ShelfImportResponse(0, 0, 0, 0, [$"Mapped column '{sourceHeader}' was not found in the CSV header."]);
            }
        }

        // Validate all client-provided values before changing the catalog or shelf. This gives
        // users actionable row errors and keeps the import atomic even before database work starts.
        var preflightErrors = new List<string>();
        var preflightRowNumber = 1;
        foreach (var row in rows.Skip(1))
        {
            preflightRowNumber++;
            var values = TextRules.RowToDictionary(headers, row);
            if (!TextRules.TryApplyColumnMappings(values, mappedFields, availableHeaders, out var mappingError))
            {
                preflightErrors.Add($"Row {preflightRowNumber}: {mappingError}");
                continue;
            }

            var importValues = ReadImportValues(values);
            var title = importValues.Title;
            if (Uri.TryCreate(title, UriKind.Absolute, out _))
            {
                title = TextRules.FirstValue(values, "title", "name", "manga", "series");
            }

            var validationError = ValidateImportRow(importValues, TextRules.CleanTitle(title));
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                preflightErrors.Add($"Row {preflightRowNumber}: {validationError}");
            }
        }

        if (preflightErrors.Count > 0)
        {
            preflightErrors.Insert(0, "No rows were imported because the CSV has errors. Fix the listed rows and import again.");
            return new ShelfImportResponse(0, 0, 0, preflightErrors.Count - 1, preflightErrors.Take(20).ToList());
        }

        var messages = new List<string>();
        var imported = 0;
        var createdCatalog = 0;
        var updatedShelf = 0;
        var skipped = 0;
        var rowNumber = 1;
        await using var transaction = shelf.SupportsTransactions
            ? await shelf.BeginTransactionAsync(cancellationToken)
            : null;

        foreach (var row in rows.Skip(1))
        {
            rowNumber++;
            try
            {
                var createdThisRow = false;
                var values = TextRules.RowToDictionary(headers, row);
                if (!TextRules.TryApplyColumnMappings(values, mappedFields, availableHeaders, out var mappingError))
                {
                    return new ShelfImportResponse(imported, createdCatalog, updatedShelf, skipped, [mappingError]);
                }
                var importValues = ReadImportValues(values);
                var title = importValues.Title;
                var link = importValues.Link;
                if (Uri.TryCreate(title, UriKind.Absolute, out _))
                {
                    link = title;
                    title = TextRules.FirstValue(values, "title", "name", "manga", "series");
                }

                title = TextRules.CleanTitle(title);
                var validationError = ValidateImportRow(importValues, title);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    skipped++;
                    messages.Add($"Row {rowNumber}: {validationError}");
                    continue;
                }

                var mangaDexId = TextRules.ExtractMangaDexId(TextRules.FirstNonEmpty(importValues.MangaDexId, link));
                MangaEntry? manga = null;
                if (!string.IsNullOrWhiteSpace(mangaDexId))
                {
                    manga = await catalog.FindByMangaDexIdAsync(mangaDexId, cancellationToken);
                }

                if (manga is null && !string.IsNullOrWhiteSpace(importValues.MyAnimeListId))
                {
                    manga = await catalog.FindByMyAnimeListIdAsync(importValues.MyAnimeListId, cancellationToken);
                }

                if (manga is null && !string.IsNullOrWhiteSpace(importValues.MangaUpdatesId))
                {
                    manga = await catalog.FindByMangaUpdatesIdAsync(importValues.MangaUpdatesId, cancellationToken);
                }

                if (manga is null && !string.IsNullOrWhiteSpace(importValues.OpenLibraryKey))
                {
                    manga = await catalog.FindByOpenLibraryKeyAsync(importValues.OpenLibraryKey, cancellationToken);
                }

                if (manga is null && !string.IsNullOrWhiteSpace(link))
                {
                    manga = await catalog.FindByReaderUrlAsync(link, cancellationToken);
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
                        MangaDexId = mangaDexId
                    };
                    ApplyCatalogImportValues(manga, importValues, mangaDexId, link);
                    await catalog.AddAsync(manga, cancellationToken);
                    createdThisRow = true;
                }
                else
                {
                    ApplyCatalogImportValues(manga, importValues, mangaDexId, link);
                }

                var shelfEntry = await shelf.GetAsync(userId, manga.Id, cancellationToken);
                var isNewShelfEntry = shelfEntry is null;
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

                if (importValues.HasReadingStatus)
                {
                    shelfEntry.ReadingStatus = TextRules.NormalizeShelfStatus(importValues.ReadingStatus);
                }
                else if (isNewShelfEntry)
                {
                    shelfEntry.ReadingStatus = string.IsNullOrWhiteSpace(importValues.CurrentChapter) ? "planned" : "reading";
                }

                if (importValues.HasCurrentChapter)
                {
                    var currentChapterChanged = !string.Equals(shelfEntry.CurrentChapter, importValues.CurrentChapter, StringComparison.Ordinal);
                    shelfEntry.CurrentChapter = importValues.CurrentChapter;
                    if (shelfEntry.ReadingStatus != "done" && currentChapterChanged)
                    {
                        shelfEntry.IsRead = false;
                    }
                }
                if (shelfEntry.ReadingStatus == "done") shelfEntry.IsRead = true;
                else if (importValues.IsRead is not null) shelfEntry.IsRead = importValues.IsRead.Value;
                if (importValues.HasScore) shelfEntry.Score = TextRules.ParseScore(importValues.Score);
                if (importValues.HasPersonalCategory) shelfEntry.Category = importValues.PersonalCategory;
                if (importValues.HasPersonalSummary) shelfEntry.Summary = importValues.PersonalSummary;
                if (importValues.HasNotes) shelfEntry.Notes = importValues.Notes;
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

        if (skipped > 0)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            messages.Insert(0, "No rows were imported because the CSV has errors. Fix the listed rows and import again.");
            return new ShelfImportResponse(0, 0, 0, skipped, messages.Take(20).ToList());
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new ShelfImportResponse(imported, createdCatalog, updatedShelf, skipped, messages.Take(20).ToList());
    }

    private static string ValidateImportRow(CsvImportValues values, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Title is required.";
        if (values.HasScore && !string.IsNullOrWhiteSpace(values.Score) && TextRules.ParseScore(values.Score) is null)
        {
            return $"Rating '{values.Score}' must be a number from 1 to 5.";
        }
        if (values.HasIsRead && !string.IsNullOrWhiteSpace(values.IsReadValue) && values.IsRead is null)
        {
            return $"Current chapter read value '{values.IsReadValue}' must be true/false, yes/no, 1/0, read, or unread.";
        }

        return "";
    }

    private static CsvImportValues ReadImportValues(Dictionary<string, string> values)
    {
        var hasPersonalCategory = TextRules.TryFirstValue(values, out var personalCategory, "personal category", "shelf category", "category");
        var hasCatalogCategory = TextRules.TryFirstValue(values, out var catalogCategory, "catalog category", "catalog categories", "categories", "genres", "tags");
        if (!hasPersonalCategory && !hasCatalogCategory)
        {
            hasPersonalCategory = TextRules.TryFirstValue(values, out personalCategory, "tipo", "type", "genre");
        }
        if (!hasCatalogCategory)
        {
            catalogCategory = personalCategory;
        }

        var hasPersonalSummary = TextRules.TryFirstValue(values, out var personalSummary, "personal summary", "shelf summary", "summary");
        var hasCatalogDescription = TextRules.TryFirstValue(values, out var catalogDescription, "catalog description", "description");
        if (!hasPersonalSummary && hasCatalogDescription)
        {
            hasPersonalSummary = true;
            personalSummary = catalogDescription;
        }
        if (!hasCatalogDescription)
        {
            catalogDescription = personalSummary;
        }

        var hasReadingStatus = TextRules.TryFirstValue(values, out var readingStatus, "status", "reading status");
        var hasCurrentChapter = TextRules.TryFirstValue(values, out var currentChapter, "chapter", "current chapter", "chapters read");
        var hasScore = TextRules.TryFirstValue(values, out var score, "rating", "score");
        var hasNotes = TextRules.TryFirstValue(values, out var notes, "notes", "note", "comments");
        var hasIsRead = TextRules.TryFirstValue(values, out var isRead, "current chapter read", "is read", "chapter read");

        var mediaType = TextRules.FirstValue(values, "format", "media type", "mediatype").Trim();
        if (string.IsNullOrWhiteSpace(mediaType) && hasCatalogCategory)
        {
            mediaType = TextRules.FirstValue(values, "type").Trim();
        }

        return new CsvImportValues(
            TextRules.FirstValue(values, "name+link", "name", "title", "manga", "series").Trim(),
            TextRules.FirstValue(values, "link", "url", "mangadex url", "mangadex", "source url").Trim(),
            TextRules.FirstValue(values, "mangadex id", "mangadexid").Trim(),
            TextRules.FirstValue(values, "fallback reader url", "fallbackreaderurl", "external url", "externalurl", "reader url", "readerurl").Trim(),
            TextRules.FirstValue(values, "cover", "cover url", "image", "image url", "thumbnail", "poster").Trim(),
            TextRules.FirstValue(values, "authors", "author", "artist", "mangaka", "creator").Trim(),
            catalogCategory.Trim(),
            catalogDescription.Trim(),
            TextRules.FirstValue(values, "metadata source", "metadatasource").Trim(),
            TextRules.FirstValue(values, "mal id", "malid", "myanimelist id", "myanimelistid", "myanimelist", "mal").Trim(),
            TextRules.FirstValue(values, "openlibrary key", "openlibrarykey", "openlibrary id", "openlibraryid", "openlibrary").Trim(),
            TextRules.ParseNullableInt(TextRules.FirstValue(values, "first published", "firstpublishyear", "published", "year")),
            mediaType,
            TextRules.FirstValue(values, "publishing status", "publishingstatus", "publication status", "publicationstatus").Trim(),
            TextRules.ParseNullableInt(TextRules.FirstValue(values, "chapter count", "chaptercount", "total chapters", "totalchapters")),
            TextRules.ParseNullableInt(TextRules.FirstValue(values, "volume count", "volumecount", "volumes")),
            TextRules.FirstValue(values, "mangaupdates id", "mangaupdatesid", "mangaupdates").Trim(),
            TextRules.FirstValue(values, "reader preference", "readerpreference", "reader type", "readertype").Trim(),
            hasReadingStatus,
            readingStatus.Trim(),
            hasCurrentChapter,
            currentChapter.Trim(),
            hasScore,
            score.Trim(),
            hasPersonalCategory,
            personalCategory.Trim(),
            hasPersonalSummary,
            personalSummary.Trim(),
            hasNotes,
            notes.Trim(),
            hasIsRead,
            isRead.Trim(),
            TextRules.ParseBoolean(isRead));
    }

    private static void ApplyCatalogImportValues(MangaEntry manga, CsvImportValues values, string mangaDexId, string link)
    {
        var changed = false;
        var metadataSource = TextRules.FirstNonEmpty(values.MetadataSource,
            !string.IsNullOrWhiteSpace(values.MyAnimeListId) ? "myanimelist" : "",
            !string.IsNullOrWhiteSpace(values.OpenLibraryKey) ? "openlibrary" : "",
            !string.IsNullOrWhiteSpace(mangaDexId) ? "mangadex" : "");
        var fallbackReaderUrl = TextRules.FirstNonEmpty(values.FallbackReaderUrl, string.IsNullOrWhiteSpace(mangaDexId) ? link : "");

        if (string.IsNullOrWhiteSpace(manga.Authors) && !string.IsNullOrWhiteSpace(values.Authors)) { manga.Authors = values.Authors; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.Category) && !string.IsNullOrWhiteSpace(values.CatalogCategory)) { manga.Category = values.CatalogCategory; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.Description) && !string.IsNullOrWhiteSpace(values.CatalogDescription)) { manga.Description = values.CatalogDescription; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.CoverUrl) && !string.IsNullOrWhiteSpace(values.CoverUrl)) { manga.CoverUrl = values.CoverUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.MetadataSource) && !string.IsNullOrWhiteSpace(metadataSource)) { manga.MetadataSource = metadataSource; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.MyAnimeListId) && !string.IsNullOrWhiteSpace(values.MyAnimeListId)) { manga.MyAnimeListId = values.MyAnimeListId; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.OpenLibraryKey) && !string.IsNullOrWhiteSpace(values.OpenLibraryKey)) { manga.OpenLibraryKey = values.OpenLibraryKey; changed = true; }
        if (manga.FirstPublishYear is null && values.FirstPublishYear is not null) { manga.FirstPublishYear = values.FirstPublishYear; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.MediaType) && !string.IsNullOrWhiteSpace(values.MediaType)) { manga.MediaType = values.MediaType; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.PublishingStatus) && !string.IsNullOrWhiteSpace(values.PublishingStatus)) { manga.PublishingStatus = values.PublishingStatus; changed = true; }
        if (manga.ChapterCount is null && values.ChapterCount is not null) { manga.ChapterCount = values.ChapterCount; changed = true; }
        if (manga.VolumeCount is null && values.VolumeCount is not null) { manga.VolumeCount = values.VolumeCount; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.MangaDexId) && !string.IsNullOrWhiteSpace(mangaDexId)) { manga.MangaDexId = mangaDexId; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.MangaUpdatesId) && !string.IsNullOrWhiteSpace(values.MangaUpdatesId)) { manga.MangaUpdatesId = values.MangaUpdatesId; changed = true; }
        if (string.IsNullOrWhiteSpace(manga.FallbackReaderUrl) && !string.IsNullOrWhiteSpace(fallbackReaderUrl)) { manga.FallbackReaderUrl = fallbackReaderUrl; changed = true; }
        if (!string.IsNullOrWhiteSpace(values.ReaderPreference) && manga.ReaderPreference == ReaderPreference.MangaHub)
        {
            manga.ReaderPreference = ReaderPreference.Normalize(values.ReaderPreference);
            changed = true;
        }

        if (changed)
        {
            manga.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private sealed record CsvImportValues(
        string Title,
        string Link,
        string MangaDexId,
        string FallbackReaderUrl,
        string CoverUrl,
        string Authors,
        string CatalogCategory,
        string CatalogDescription,
        string MetadataSource,
        string MyAnimeListId,
        string OpenLibraryKey,
        int? FirstPublishYear,
        string MediaType,
        string PublishingStatus,
        int? ChapterCount,
        int? VolumeCount,
        string MangaUpdatesId,
        string ReaderPreference,
        bool HasReadingStatus,
        string ReadingStatus,
        bool HasCurrentChapter,
        string CurrentChapter,
        bool HasScore,
        string Score,
        bool HasPersonalCategory,
        string PersonalCategory,
        bool HasPersonalSummary,
        string PersonalSummary,
        bool HasNotes,
        string Notes,
        bool HasIsRead,
        string IsReadValue,
        bool? IsRead);
}
