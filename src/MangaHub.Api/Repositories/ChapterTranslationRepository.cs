using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class ChapterTranslationRepository(MangaHubDbContext db)
{
    public Task<MangaChapterTranslation?> GetAsync(
        Guid mangaChapterId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguage(targetLanguage);
        return db.ChapterTranslations
            .FirstOrDefaultAsync(item => item.MangaChapterId == mangaChapterId && item.TargetLanguage == normalizedLanguage, cancellationToken);
    }

    public async Task<MangaChapterTranslation> EnsurePendingAsync(
        Guid mangaChapterId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguage(targetLanguage);
        var translation = await db.ChapterTranslations
            .FirstOrDefaultAsync(item => item.MangaChapterId == mangaChapterId && item.TargetLanguage == normalizedLanguage, cancellationToken);
        if (translation is not null)
        {
            return translation;
        }

        translation = new MangaChapterTranslation
        {
            MangaChapterId = mangaChapterId,
            TargetLanguage = normalizedLanguage,
            Status = ChapterTranslationStatus.Pending
        };
        db.ChapterTranslations.Add(translation);
        await db.SaveChangesAsync(cancellationToken);
        return translation;
    }

    public async Task MarkProcessingAsync(MangaChapterTranslation translation, CancellationToken cancellationToken)
    {
        translation.Status = ChapterTranslationStatus.Processing;
        translation.Error = "";
        translation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkReadyAsync(
        MangaChapterTranslation translation,
        string relativePath,
        int pageCount,
        string fileHash,
        CancellationToken cancellationToken)
    {
        translation.Status = ChapterTranslationStatus.Ready;
        translation.RelativePath = relativePath;
        translation.PageCount = pageCount;
        translation.FileHash = fileHash;
        translation.Error = "";
        translation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        MangaChapterTranslation translation,
        string status,
        string error,
        CancellationToken cancellationToken)
    {
        translation.Status = status;
        translation.Error = error;
        translation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeLanguage(string targetLanguage) =>
        string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage.Trim().ToLowerInvariant();
}
