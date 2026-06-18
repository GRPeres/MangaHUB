using System.IO.Compression;
using MangaHub.Core.Services;

namespace MangaHub.Infrastructure.Local;

public sealed class CbzArchiveReader : IArchiveReader
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    public int CountPages(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        return archive.Entries.Count(IsImage);
    }

    public async Task<ArchivePage?> ReadPageAsync(string archivePath, int pageIndex, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.Where(IsImage).OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Skip(pageIndex).FirstOrDefault();
        if (entry is null)
        {
            return null;
        }

        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return new ArchivePage(entry.Name, GetContentType(entry.Name), buffer.ToArray());
    }

    private static bool IsImage(ZipArchiveEntry entry) => ImageExtensions.Contains(Path.GetExtension(entry.FullName));

    private static string GetContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };
}

