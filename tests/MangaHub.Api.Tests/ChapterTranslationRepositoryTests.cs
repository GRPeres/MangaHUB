using MangaHub.Api.Repositories;
using MangaHub.Core.Models;

namespace MangaHub.Api.Tests;

public sealed class ChapterTranslationRepositoryTests
{
    [Fact]
    public async Task EnsurePendingAsync_CreatesOneArtifactPerTargetLanguage()
    {
        await using var db = TestDb.Create();
        var series = new MangaSeries { Title = "Berserk", Source = "mangadex-cache", ExternalId = "series-id" };
        var chapter = new MangaChapter { Series = series, ChapterNumber = "1", SourceId = "chapter-id", PageCount = 20 };
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        var repository = new ChapterTranslationRepository(db);

        var first = await repository.EnsurePendingAsync(chapter.Id, "PT-BR", CancellationToken.None);
        var second = await repository.EnsurePendingAsync(chapter.Id, "pt-br", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("pt-br", first.TargetLanguage);
        Assert.Equal(ChapterTranslationStatus.Pending, first.Status);
        Assert.Single(db.ChapterTranslations);
    }
}
