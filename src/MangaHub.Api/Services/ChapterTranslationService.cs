using System.Collections.Concurrent;
using MangaHub.Api.Repositories;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class ChapterTranslationService(
    ChapterTranslationRepository translations,
    IChapterTranslationEngine engine,
    IOptions<MangaHubOptions> options)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TranslationLocks = new(StringComparer.Ordinal);

    public async Task<MangaChapterTranslation> EnsureReadyAsync(
        MangaChapter chapter,
        string mangaDexId,
        string targetLanguage,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null)
    {
        var normalizedTarget = NormalizeLanguage(targetLanguage);
        var normalizedSource = NormalizeLanguage(
            string.IsNullOrWhiteSpace(chapter.SourceLanguage) ? chapter.Language : chapter.SourceLanguage);
        var lockKey = $"{chapter.Id:N}:{normalizedTarget}";
        var translationLock = TranslationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await translationLock.WaitAsync(cancellationToken);
        try
        {
            var translation = await translations.EnsurePendingAsync(chapter.Id, normalizedTarget, cancellationToken);
            if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new ReaderPreparationProgress(
                    "The source chapter already matches your translation language",
                    95,
                    chapter.PageCount,
                    chapter.PageCount));
                if (translation.Status != ChapterTranslationStatus.Ready || translation.RelativePath.Length > 0)
                {
                    await translations.MarkReadyAsync(
                        translation,
                        "",
                        chapter.PageCount,
                        chapter.FileHash,
                        cancellationToken);
                }
                return translation;
            }

            var cacheRoot = Path.GetFullPath(options.Value.MangaDexCachePath);
            var sourcePath = ResolveInside(
                cacheRoot,
                Path.Combine("mangadex", mangaDexId, $"{chapter.SourceId}.cbz"));
            var relativeOutputPath = Path.Combine(
                "translations",
                chapter.Id.ToString("N"),
                $"{SafeLanguage(normalizedTarget)}.cbz");
            var outputPath = ResolveInside(cacheRoot, relativeOutputPath);

            if (translation.Status == ChapterTranslationStatus.Ready
                && string.Equals(translation.RelativePath, relativeOutputPath, StringComparison.Ordinal)
                && File.Exists(outputPath))
            {
                return translation;
            }
            if (!engine.IsEnabled)
            {
                const string message = "Local chapter translation is not enabled on this server.";
                await translations.MarkFailedAsync(
                    translation,
                    ChapterTranslationStatus.Unsupported,
                    message,
                    cancellationToken);
                throw new ChapterTranslationUnavailableException(message);
            }

            progress?.Report(new ReaderPreparationProgress("Starting local OCR and translation", 52, 0, chapter.PageCount));
            await translations.MarkProcessingAsync(translation, cancellationToken);
            try
            {
                var result = await engine.TranslateAsync(
                    new ChapterTranslationRequest(
                        sourcePath,
                        outputPath,
                        normalizedSource,
                        normalizedTarget),
                    cancellationToken,
                    progress);
                await translations.MarkReadyAsync(
                    translation,
                    relativeOutputPath,
                    result.PageCount,
                    result.FileHash,
                    cancellationToken);
                return translation;
            }
            catch (ChapterTranslationUnavailableException ex)
            {
                await translations.MarkFailedAsync(
                    translation,
                    ChapterTranslationStatus.Failed,
                    ex.Message,
                    cancellationToken);
                throw;
            }
        }
        finally
        {
            translationLock.Release();
        }
    }

    public async Task<string?> GetReadableArchivePathAsync(
        MangaChapter chapter,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var translation = await translations.GetAsync(chapter.Id, targetLanguage, cancellationToken);
        if (translation?.Status != ChapterTranslationStatus.Ready
            || string.IsNullOrWhiteSpace(translation.RelativePath))
        {
            return null;
        }

        var cacheRoot = Path.GetFullPath(options.Value.MangaDexCachePath);
        var path = ResolveInside(cacheRoot, translation.RelativePath);
        return File.Exists(path) ? path : null;
    }

    private static string ResolveInside(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ChapterTranslationUnavailableException("The translated archive path is invalid.");
        }
        return fullPath;
    }

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private static string SafeLanguage(string language) =>
        new(language.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
}
