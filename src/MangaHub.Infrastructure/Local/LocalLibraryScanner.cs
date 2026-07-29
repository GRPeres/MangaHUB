using System.Security.Cryptography;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Local;

public sealed class LocalLibraryScanner(
    MangaHubDbContext db,
    IArchiveReader archiveReader,
    IOptions<MangaHubOptions> options,
    ILogger<LocalLibraryScanner> logger) : ILibraryScanner
{
    public async Task<LibraryScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.Value.LibraryPath);
        if (!Directory.Exists(root))
        {
            logger.LogInformation("Library path {LibraryPath} does not exist yet.", root);
            return new LibraryScanResult(0, 0);
        }

        var seenSeries = 0;
        var seenChapters = 0;

        foreach (var seriesDirectory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = Path.GetFileName(seriesDirectory);
            var externalId = Path.GetRelativePath(root, seriesDirectory);
            var series = await db.Series.Include(x => x.Chapters)
                .FirstOrDefaultAsync(x => x.Source == "local" && x.ExternalId == externalId, cancellationToken);

            if (series is null)
            {
                series = new MangaSeries { Title = title, Source = "local", ExternalId = externalId };
                db.Series.Add(series);
            }

            seenSeries++;

            foreach (var archivePath in Directory.EnumerateFiles(seriesDirectory, "*.cbz"))
            {
                var sourceId = Path.GetRelativePath(root, archivePath);
                var chapter = series.Chapters.FirstOrDefault(x => x.SourceId == sourceId);
                if (chapter is null)
                {
                    chapter = new MangaChapter
                    {
                        Series = series,
                        ChapterNumber = ExtractChapterNumber(Path.GetFileNameWithoutExtension(archivePath)),
                        SourceLanguage = "local",
                        Title = Path.GetFileNameWithoutExtension(archivePath),
                        SourceId = sourceId
                    };
                    series.Chapters.Add(chapter);
                }

                chapter.FileHash = await HashFileAsync(archivePath, cancellationToken);
                chapter.PageCount = archiveReader.CountPages(archivePath);
                seenChapters++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new LibraryScanResult(seenSeries, seenChapters);
    }

    private static string ExtractChapterNumber(string name)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? name : digits.TrimStart('0').DefaultIfEmpty('0').Aggregate("", (a, c) => a + c);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
