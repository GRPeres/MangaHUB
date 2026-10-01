using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using System.Text.Json;

namespace MangaHub.Api.Services;

public sealed class CatalogService(
    CatalogRepository catalog,
    IOpenLibraryClient openLibrary,
    MangaDexCatalogMatchService mangaDexMatches,
    MangaDexTitleMatchService mangaDexTitleMatches,
    CatalogIdentityEnrichmentService identityEnrichment,
    UsageTrackingService? usage = null)
{
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    public Task<List<CatalogMangaResponse>> SearchAsync(Guid userId, string? query, string preferredLanguage, int offset, int limit, CancellationToken cancellationToken) =>
        catalog.SearchAsync(userId, query, preferredLanguage, offset, limit, cancellationToken);

    public async Task<CatalogMangaResponse> CreateAsync(Guid currentUserId, MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        await CreateLock.WaitAsync(cancellationToken);
        try
        {
            var details = await TryGetOpenLibraryDetailsAsync(entry.OpenLibraryKey, cancellationToken);
            var mangaUpdatesId = entry.MangaUpdatesId.Trim();
            var suppliedReaderLinks = ResolveReaderLinks(entry);
            await EnsureUniqueIdentitiesAsync(suppliedReaderLinks.MangaDexId, mangaUpdatesId, entry.MyAnimeListId.Trim(), null, cancellationToken);

            var readerLinks = await ResolveReaderLinksForCreateAsync(entry, cancellationToken);
            await EnsureUniqueIdentitiesAsync(readerLinks.MangaDexId, mangaUpdatesId, entry.MyAnimeListId.Trim(), null, cancellationToken);

            var manga = new MangaEntry
            {
                CreatedByUserId = currentUserId,
                Title = entry.Title.Trim(),
                Authors = entry.Authors.Trim(),
                Category = TextRules.FirstNonEmpty(entry.Category, details?.Category),
                Description = TextRules.FirstNonEmpty(entry.Description, details?.Description),
                CoverUrl = entry.CoverUrl.Trim(),
                MetadataSource = entry.MetadataSource.Trim(),
                MyAnimeListId = entry.MyAnimeListId.Trim(),
                OpenLibraryKey = entry.OpenLibraryKey.Trim(),
                FirstPublishYear = entry.FirstPublishYear,
                MediaType = entry.MediaType.Trim(),
                PublishingStatus = entry.PublishingStatus.Trim(),
                ChapterCount = entry.ChapterCount,
                VolumeCount = entry.VolumeCount,
                MangaDexId = readerLinks.MangaDexId,
                FallbackReaderUrl = readerLinks.FallbackReaderUrl,
                ReaderPreference = NormalizeReaderPreference(entry.ReaderPreference),
                MangaUpdatesId = mangaUpdatesId,
                LocalSeriesId = entry.LocalSeriesId
            };

            await catalog.AddAsync(manga, cancellationToken);
            await identityEnrichment.QueueAsync(currentUserId, cancellationToken);
            if (usage is not null) await usage.TrackAsync(currentUserId, UsageEventTypes.CatalogCreated, manga.Id, cancellationToken);
            return ApiMapping.ToCatalogMangaResponse(manga, false);
        }
        finally
        {
            CreateLock.Release();
        }
    }

    public async Task<CatalogMangaResponse?> UpdateAsync(Guid currentUserId, Guid entryId, MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        var manga = await catalog.GetByIdAsync(entryId, cancellationToken);
        if (manga is null)
        {
            return null;
        }

        manga.Title = entry.Title.Trim();
        manga.Authors = entry.Authors.Trim();
        manga.Category = entry.Category.Trim();
        manga.Description = entry.Description.Trim();
        manga.CoverUrl = entry.CoverUrl.Trim();
        manga.MetadataSource = entry.MetadataSource.Trim();
        manga.MyAnimeListId = entry.MyAnimeListId.Trim();
        manga.OpenLibraryKey = entry.OpenLibraryKey.Trim();
        manga.FirstPublishYear = entry.FirstPublishYear;
        manga.MediaType = entry.MediaType.Trim();
        manga.PublishingStatus = entry.PublishingStatus.Trim();
        manga.ChapterCount = entry.ChapterCount;
        manga.VolumeCount = entry.VolumeCount;
        var readerLinks = ResolveReaderLinks(entry);
        var mangaUpdatesId = entry.MangaUpdatesId.Trim();
        await EnsureUniqueIdentitiesAsync(readerLinks.MangaDexId, mangaUpdatesId, entry.MyAnimeListId.Trim(), manga.Id, cancellationToken);

        manga.MangaDexId = readerLinks.MangaDexId;
        manga.FallbackReaderUrl = readerLinks.FallbackReaderUrl;
        manga.ReaderPreference = NormalizeReaderPreference(entry.ReaderPreference);
        manga.MangaUpdatesId = mangaUpdatesId;
        manga.MangaDexLastMatchAttemptAt = string.IsNullOrWhiteSpace(manga.MangaDexId) ? null : manga.MangaDexLastMatchAttemptAt;
        manga.MangaUpdatesLastMatchAttemptAt = string.IsNullOrWhiteSpace(manga.MangaUpdatesId) ? null : manga.MangaUpdatesLastMatchAttemptAt;
        manga.LocalSeriesId = entry.LocalSeriesId;
        manga.UpdatedAt = DateTimeOffset.UtcNow;

        await catalog.SaveChangesAsync(cancellationToken);
        await identityEnrichment.QueueAsync(currentUserId, cancellationToken);
        if (usage is not null) await usage.TrackAsync(currentUserId, UsageEventTypes.CatalogUpdated, manga.Id, cancellationToken);
        var isInShelf = await catalog.IsInUserShelfAsync(currentUserId, manga.Id, cancellationToken);
        return ApiMapping.ToCatalogMangaResponse(manga, isInShelf);
    }

    private static ReaderLinks ResolveReaderLinks(MangaEntryRequest entry)
    {
        var mangaDexId = NormalizeMangaDexId(entry.MangaDexId);
        var fallbackReaderUrl = entry.FallbackReaderUrl.Trim();
        return new ReaderLinks(mangaDexId, fallbackReaderUrl);
    }

    private async Task<ReaderLinks> ResolveReaderLinksForCreateAsync(MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        var readerLinks = ResolveReaderLinks(entry);
        if (!string.IsNullOrWhiteSpace(readerLinks.MangaDexId) || IsManualUntrackedEntry(entry))
        {
            return readerLinks;
        }

        // Metadata-selected additions must settle their MangaDex identity before they
        // can be persisted. The provider request runs through the interactive remote
        // queue, so the create request waits without bypassing API limits.
        var match = await mangaDexMatches.FindAsync(entry.MyAnimeListId, entry.Title, cancellationToken)
            ?? await mangaDexTitleMatches.FindAsync(entry.Title, cancellationToken);
        if (match is null || string.IsNullOrWhiteSpace(match.Id))
        {
            throw new CatalogIdentityResolutionException("MangaDex could not confirm this metadata selection. No catalog entry was created; try again, select a different match, or create it as an external-reader entry.");
        }

        return readerLinks with { MangaDexId = NormalizeMangaDexId(match.Id) };
    }

    private static bool IsManualUntrackedEntry(MangaEntryRequest entry) =>
        string.IsNullOrWhiteSpace(entry.MyAnimeListId)
        && (string.IsNullOrWhiteSpace(entry.MetadataSource)
            || string.Equals(entry.MetadataSource, "manual", StringComparison.OrdinalIgnoreCase));

    private async Task<OpenLibraryWorkDetails?> TryGetOpenLibraryDetailsAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        try
        {
            return await openLibrary.GetWorkAsync(key.Trim(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            // OpenLibrary enriches a save but must not be allowed to block a manual catalog entry.
            return null;
        }
    }

    private async Task EnsureUniqueIdentitiesAsync(
        string mangaDexId,
        string mangaUpdatesId,
        string myAnimeListId,
        Guid? currentEntryId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(mangaDexId))
        {
            var existing = await catalog.FindByMangaDexIdAsync(mangaDexId, cancellationToken);
            if (existing is not null && existing.Id != currentEntryId)
            {
                throw new CatalogDuplicateIdentityException("MangaDex", mangaDexId, existing.Title);
            }
        }

        if (!string.IsNullOrWhiteSpace(mangaUpdatesId))
        {
            var existing = await catalog.FindByMangaUpdatesIdAsync(mangaUpdatesId, cancellationToken);
            if (existing is not null && existing.Id != currentEntryId)
            {
                throw new CatalogDuplicateIdentityException("MangaUpdates", mangaUpdatesId, existing.Title);
            }
        }

        if (!string.IsNullOrWhiteSpace(myAnimeListId))
        {
            var existing = await catalog.FindByMyAnimeListIdAsync(myAnimeListId, cancellationToken);
            if (existing is not null && existing.Id != currentEntryId)
            {
                throw new CatalogDuplicateIdentityException("MyAnimeList", myAnimeListId, existing.Title);
            }
        }
    }

    private static string NormalizeMangaDexId(string value)
    {
        var trimmed = value.Trim();
        if (Guid.TryParse(trimmed, out var id))
        {
            return id.ToString();
        }

        return TextRules.ExtractMangaDexId(trimmed);
    }

    private static string NormalizeReaderPreference(string value) =>
        ReaderPreference.Normalize(value);

    private sealed record ReaderLinks(string MangaDexId, string FallbackReaderUrl);
}

public sealed class CatalogDuplicateIdentityException(string provider, string id, string existingTitle)
    : InvalidOperationException($"A catalog manga using this {provider} ID already exists: '{existingTitle}' ({id}). Review the existing entry or resolve it from Admin Issues.");

public sealed class CatalogIdentityResolutionException(string message) : InvalidOperationException(message);
