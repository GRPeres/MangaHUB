using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class ChapterTranslationRepository(MangaHubDbContext db)
{
    public async Task<MangaChapterTranslation> EnsurePendingAsync(
        Guid mangaChapterId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage.Trim().ToLowerInvariant();
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
}
