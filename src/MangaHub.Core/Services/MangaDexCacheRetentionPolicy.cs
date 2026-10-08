namespace MangaHub.Core.Services;

public static class MangaDexCacheRetentionPolicy
{
    public sealed record ReaderProgress(string CurrentChapter, bool IncludeCurrentChapter);

    public static bool ShouldQueueContinuation(bool batchLimitReached, int archivedCount) =>
        batchLimitReached && archivedCount > 0;

    public static bool ShouldRetain(string sourceId, string chapterNumber, decimal? earliestActiveChapter)
        => ShouldRetain(
            sourceId,
            chapterNumber,
            earliestActiveChapter is null ? [] : [new ReaderProgress(earliestActiveChapter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), true)],
            null,
            DateTimeOffset.UtcNow,
            0);

    public static bool ShouldRetain(
        string sourceId,
        string chapterNumber,
        decimal? earliestActiveChapter,
        DateTimeOffset? lastAccessedAt,
        DateTimeOffset now,
        int graceDays)
    {
        if (sourceId.StartsWith("manual-", StringComparison.Ordinal))
        {
            return true;
        }

        if (lastAccessedAt is not null && lastAccessedAt.Value >= now.AddDays(-Math.Clamp(graceDays, 0, 90)))
        {
            return true;
        }

        return ShouldRetain(
            sourceId,
            chapterNumber,
            earliestActiveChapter is null ? [] : [new ReaderProgress(earliestActiveChapter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), true)],
            lastAccessedAt,
            now,
            graceDays);
    }

    public static bool ShouldRetain(
        string sourceId,
        string chapterNumber,
        IReadOnlyCollection<ReaderProgress> readerProgress,
        DateTimeOffset? lastAccessedAt,
        DateTimeOffset now,
        int graceDays)
    {
        if (sourceId.StartsWith("manual-", StringComparison.Ordinal))
        {
            return true;
        }

        if (lastAccessedAt is not null && lastAccessedAt.Value >= now.AddDays(-Math.Clamp(graceDays, 0, 90)))
        {
            return true;
        }

        return IsProtectedForReader(chapterNumber, readerProgress);
    }

    public static bool IsProtectedForReader(string chapterNumber, IReadOnlyCollection<ReaderProgress> readerProgress)
    {
        var hasUsableProgress = false;
        foreach (var progress in readerProgress)
        {
            if (string.IsNullOrWhiteSpace(progress.CurrentChapter))
            {
                continue;
            }

            if (!ChapterNumberComparer.TryCompareNumericParts(chapterNumber, progress.CurrentChapter, out var comparison))
            {
                continue;
            }

            hasUsableProgress = true;
            if (comparison > 0 || (comparison == 0 && progress.IncludeCurrentChapter))
            {
                return true;
            }
        }

        // Do not delete a chapter with an unparseable label while a reader has progress for this manga.
        return !hasUsableProgress && readerProgress.Any(progress => !string.IsNullOrWhiteSpace(progress.CurrentChapter));
    }

    public static ReaderProgress? FindMostProtectiveProgress(IEnumerable<ReaderProgress> progress)
    {
        ReaderProgress? result = null;
        foreach (var candidate in progress)
        {
            if (string.IsNullOrWhiteSpace(candidate.CurrentChapter))
            {
                continue;
            }

            if (result is null)
            {
                result = candidate;
                continue;
            }

            if (!ChapterNumberComparer.TryCompareNumericParts(candidate.CurrentChapter, result.CurrentChapter, out var comparison))
            {
                continue;
            }

            if (comparison < 0 || (comparison == 0 && candidate.IncludeCurrentChapter && !result.IncludeCurrentChapter))
            {
                result = candidate;
            }
        }

        return result;
    }
}
