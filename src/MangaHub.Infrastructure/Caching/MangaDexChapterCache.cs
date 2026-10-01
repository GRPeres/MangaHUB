using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using ImageMagick;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Caching;

public sealed class MangaDexChapterCache : IMangaDexChapterCache
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<MangaHubOptions> options;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(StringComparer.Ordinal);

    public MangaDexChapterCache(IHttpClientFactory httpClientFactory, IOptions<MangaHubOptions> options)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        EnsureSyncthingActiveCacheIgnoreRule();
    }

    public async Task<MangaDexCachedChapter> EnsureCachedAsync(
        string mangaDexId,
        string chapterId,
        IReadOnlyList<MangaPage> pages,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null,
        string imageQuality = "original")
    {
        EnsureSyncthingActiveCacheIgnoreRule();
        var quality = NormalizeQuality(imageQuality);
        var (relativePath, activePath) = GetArchivePath(mangaDexId, chapterId, quality);
        var archivedPath = GetArchivedPath(mangaDexId, chapterId, quality);

        Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
        var downloadLock = DownloadLocks.GetOrAdd(activePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(activePath))
            {
                RemoveArchivedDuplicate(archivedPath);
                progress?.Report(new ReaderPreparationProgress("Using the cached local chapter", 100));
                return await ReadCachedArchiveAsync(activePath, relativePath, cancellationToken);
            }
            if (File.Exists(archivedPath))
            {
                progress?.Report(new ReaderPreparationProgress("Restoring the archived local chapter", 12));
                File.Move(archivedPath, activePath);
                progress?.Report(new ReaderPreparationProgress("Using the restored local chapter", 100));
                return await ReadCachedArchiveAsync(activePath, relativePath, cancellationToken);
            }
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("MangaDex did not provide any readable pages for this chapter.");
            }

            var temporaryPath = $"{activePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
                {
                    var orderedPages = pages.OrderBy(page => page.Index).ToList();
                    for (var index = 0; index < orderedPages.Count; index++)
                    {
                        var page = orderedPages[index];
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(new ReaderPreparationProgress(
                            $"Downloading page {index + 1} of {orderedPages.Count}",
                            25 + (int)Math.Round(index * 65d / orderedPages.Count),
                            index,
                            orderedPages.Count));
                        if (!IsMangaDexImageUrl(page.Url))
                        {
                            throw new InvalidOperationException("MangaDex returned an unsafe page URL.");
                        }

                        using var response = await httpClientFactory.CreateClient("mangadex-pages").GetAsync(page.Url, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        var contentType = response.Content.Headers.ContentType?.MediaType;
                        if (!IsImageContentType(contentType))
                        {
                            throw new InvalidOperationException("MangaDex returned a non-image chapter page.");
                        }

                        var entry = archive.CreateEntry($"{page.Index + 1:D4}{ExtensionFor(contentType!)}", CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await response.Content.CopyToAsync(entryStream, cancellationToken);
                        progress?.Report(new ReaderPreparationProgress(
                            $"Downloaded page {index + 1} of {orderedPages.Count}",
                            25 + (int)Math.Round((index + 1) * 65d / orderedPages.Count),
                            index + 1,
                            orderedPages.Count));
                    }
                }

                progress?.Report(new ReaderPreparationProgress("Building the local reader file", 95, pages.Count, pages.Count));
                File.Move(temporaryPath, activePath);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
                throw;
            }

            var cached = await ReadCachedArchiveAsync(activePath, relativePath, cancellationToken);
            progress?.Report(new ReaderPreparationProgress("Local chapter is ready", 100, cached.PageCount, cached.PageCount));
            return cached with { WasCached = false };
        }
        finally
        {
            downloadLock.Release();
        }
    }

    public async Task DeleteAsync(string mangaDexId, string chapterId, CancellationToken cancellationToken, string imageQuality = "original")
    {
        var (_, activePath) = GetArchivePath(mangaDexId, chapterId, imageQuality);
        var archivePath = GetArchivedPath(mangaDexId, chapterId, imageQuality);
        var downloadLock = DownloadLocks.GetOrAdd(activePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(activePath))
            {
                File.Delete(activePath);
            }
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        finally
        {
            downloadLock.Release();
        }
    }

    public async Task<bool> ArchiveAsync(string mangaDexId, string chapterId, CancellationToken cancellationToken, string imageQuality = "original")
    {
        var (_, activePath) = GetArchivePath(mangaDexId, chapterId, imageQuality);
        var archivePath = GetArchivedPath(mangaDexId, chapterId, imageQuality);
        var downloadLock = DownloadLocks.GetOrAdd(activePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(activePath))
            {
                return false;
            }

            EnsureSyncthingActiveCacheIgnoreRule();
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
            File.Move(activePath, archivePath, overwrite: true);
            return true;
        }
        finally
        {
            downloadLock.Release();
        }
    }

    public async Task<MangaDexCachedChapter> CreateDataSaverFromOriginalAsync(string mangaDexId, string chapterId, CancellationToken cancellationToken)
    {
        var (_, originalPath) = GetArchivePath(mangaDexId, chapterId, "original");
        var (relativePath, dataSaverPath) = GetArchivePath(mangaDexId, chapterId, "data-saver");
        var archivedOriginalPath = GetArchivedPath(mangaDexId, chapterId, "original");
        var sourcePath = File.Exists(originalPath) ? originalPath : archivedOriginalPath;
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The original chapter is not available for local Data Saver conversion.", originalPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dataSaverPath)!);
        var conversionLock = DownloadLocks.GetOrAdd(dataSaverPath, _ => new SemaphoreSlim(1, 1));
        await conversionLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(dataSaverPath))
            {
                return await ReadCachedArchiveAsync(dataSaverPath, relativePath, cancellationToken);
            }

            var temporaryPath = $"{dataSaverPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var source = ZipFile.OpenRead(sourcePath))
                await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
                {
                    var pages = source.Entries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                        .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                        .ToList();
                    if (pages.Count == 0)
                    {
                        throw new InvalidOperationException("The original CBZ archive does not contain any readable pages.");
                    }

                    for (var index = 0; index < pages.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await using var pageStream = pages[index].Open();
                        using var page = new MagickImage(pageStream);
                        page.AutoOrient();
                        page.Strip();
                        if (page.Width > (uint)Math.Clamp(options.Value.MangaDexArchiveFallbackMaxWidth, 480, 2400))
                        {
                            page.Resize((uint)Math.Clamp(options.Value.MangaDexArchiveFallbackMaxWidth, 480, 2400), 0);
                        }
                        page.Format = MagickFormat.Jpeg;
                        page.Quality = (uint)Math.Clamp(options.Value.MangaDexArchiveFallbackJpegQuality, 40, 90);

                        var entry = target.CreateEntry($"{index + 1:D4}.jpg", CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        page.Write(entryStream);
                    }
                }

                File.Move(temporaryPath, dataSaverPath);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
                throw;
            }

            var cached = await ReadCachedArchiveAsync(dataSaverPath, relativePath, cancellationToken);
            return cached with { WasCached = false };
        }
        finally
        {
            conversionLock.Release();
        }
    }

    public async Task<bool> RestoreArchivedAsync(string mangaDexId, string chapterId, CancellationToken cancellationToken, string imageQuality = "original")
    {
        var (_, activePath) = GetArchivePath(mangaDexId, chapterId, imageQuality);
        var archivePath = GetArchivedPath(mangaDexId, chapterId, imageQuality);
        var downloadLock = DownloadLocks.GetOrAdd(activePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(activePath))
            {
                RemoveArchivedDuplicate(archivePath);
                return true;
            }
            if (!File.Exists(archivePath))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(activePath)!);
            File.Move(archivePath, activePath);
            return true;
        }
        finally
        {
            downloadLock.Release();
        }
    }

    private (string RelativePath, string ArchivePath) GetArchivePath(string mangaDexId, string chapterId, string imageQuality = "original")
    {
        var qualityFolder = NormalizeQuality(imageQuality) == "original" ? "" : "data-saver";
        var relativePath = Path.Combine("mangadex", qualityFolder, SafePathSegment(mangaDexId), $"{SafePathSegment(chapterId)}.cbz");
        var cacheRoot = Path.GetFullPath(options.Value.MangaDexCachePath);
        var archivePath = Path.GetFullPath(Path.Combine(cacheRoot, relativePath));
        if (!archivePath.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The MangaDex cache path is invalid.");
        }

        return (relativePath, archivePath);
    }

    private string GetArchivedPath(string mangaDexId, string chapterId, string imageQuality = "original")
    {
        var cacheRoot = Path.GetFullPath(options.Value.MangaDexCachePath);
        var archivePath = Path.GetFullPath(Path.Combine(
            cacheRoot,
            "archive",
            "mangadex",
            NormalizeQuality(imageQuality) == "original" ? "" : "data-saver",
            SafePathSegment(mangaDexId),
            $"{SafePathSegment(chapterId)}.cbz"));
        if (!archivePath.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The MangaDex archive path is invalid.");
        }

        return archivePath;
    }

    private void EnsureSyncthingActiveCacheIgnoreRule()
    {
        var ignoreFile = Path.Combine(Path.GetFullPath(options.Value.MangaDexCachePath), ".stignore");
        const string activeCacheRule = "/mangadex";
        const string legacyArchiveRule = "/archive";
        const string legacyComment = "// MangaHub archived CBZ files stay on the server.";
        const string comment = "// MangaHub active cache is disposable; archive CBZ files are backed up by Syncthing.";
        try
        {
            var lines = File.Exists(ignoreFile) ? File.ReadAllLines(ignoreFile) : [];
            var retainedLines = lines
                .Where(line => !string.Equals(line.Trim(), legacyArchiveRule, StringComparison.Ordinal)
                    && !string.Equals(line.Trim(), legacyComment, StringComparison.Ordinal)
                    && !string.Equals(line.Trim(), comment, StringComparison.Ordinal)
                    && !string.Equals(line.Trim(), activeCacheRule, StringComparison.Ordinal))
                .ToList();
            retainedLines.Add(comment);
            retainedLines.Add(activeCacheRule);

            if (lines.SequenceEqual(retainedLines))
            {
                return;
            }

            File.WriteAllLines(ignoreFile, retainedLines);
        }
        catch (IOException)
        {
            // The archive remains usable even when the cache root is not a Syncthing folder.
        }
        catch (UnauthorizedAccessException)
        {
            // A read-only cache cannot archive chapters, but preserve normal reader behavior.
        }
    }

    private static async Task<MangaDexCachedChapter> ReadCachedArchiveAsync(string archivePath, string relativePath, CancellationToken cancellationToken)
    {
        var pageCount = 0;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            pageCount = archive.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Name));
        }
        if (pageCount == 0)
        {
            throw new InvalidOperationException("The CBZ archive does not contain any readable pages.");
        }

        await using var stream = File.OpenRead(archivePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new MangaDexCachedChapter(relativePath, pageCount, Convert.ToHexString(hash).ToLowerInvariant(), true);
    }

    private static string SafePathSegment(string value)
    {
        var segment = value.Trim();
        if (string.IsNullOrWhiteSpace(segment) || segment.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw new InvalidOperationException("MangaDex returned an invalid identifier.");
        }

        return segment;
    }

    private static void RemoveArchivedDuplicate(string archivedPath)
    {
        if (File.Exists(archivedPath))
        {
            File.Delete(archivedPath);
        }
    }

    private static string NormalizeQuality(string? imageQuality) =>
        string.Equals(imageQuality, "data-saver", StringComparison.OrdinalIgnoreCase) ? "data-saver" : "original";

    private static bool IsMangaDexImageUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && (uri.Host.EndsWith(".mangadex.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".mangadex.network", StringComparison.OrdinalIgnoreCase));

    private static bool IsImageContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/avif" => ".avif",
        _ => ".jpg"
    };
}
