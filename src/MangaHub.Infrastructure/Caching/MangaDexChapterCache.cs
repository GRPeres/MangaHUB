using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Caching;

public sealed class MangaDexChapterCache(
    IHttpClientFactory httpClientFactory,
    IOptions<MangaHubOptions> options) : IMangaDexChapterCache
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(StringComparer.Ordinal);

    public async Task<MangaDexCachedChapter> EnsureCachedAsync(
        string mangaDexId,
        string chapterId,
        IReadOnlyList<MangaPage> pages,
        CancellationToken cancellationToken)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("MangaDex did not provide any readable pages for this chapter.");
        }

        var relativePath = Path.Combine("mangadex", SafePathSegment(mangaDexId), $"{SafePathSegment(chapterId)}.cbz");
        var cacheRoot = Path.GetFullPath(options.Value.MangaDexCachePath);
        var archivePath = Path.GetFullPath(Path.Combine(cacheRoot, relativePath));
        if (!archivePath.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The MangaDex cache path is invalid.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        var downloadLock = DownloadLocks.GetOrAdd(archivePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(archivePath))
            {
                return await ReadCachedArchiveAsync(archivePath, relativePath, cancellationToken);
            }

            var temporaryPath = $"{archivePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var page in pages.OrderBy(page => page.Index))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                    }
                }

                File.Move(temporaryPath, archivePath);
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
                throw;
            }

            var cached = await ReadCachedArchiveAsync(archivePath, relativePath, cancellationToken);
            return cached with { WasCached = false };
        }
        finally
        {
            downloadLock.Release();
        }
    }

    private static async Task<MangaDexCachedChapter> ReadCachedArchiveAsync(string archivePath, string relativePath, CancellationToken cancellationToken)
    {
        var pageCount = 0;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            pageCount = archive.Entries.Count(entry => !string.IsNullOrWhiteSpace(entry.Name));
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
