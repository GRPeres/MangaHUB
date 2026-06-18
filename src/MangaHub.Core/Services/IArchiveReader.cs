namespace MangaHub.Core.Services;

public interface IArchiveReader
{
    int CountPages(string archivePath);
    Task<ArchivePage?> ReadPageAsync(string archivePath, int pageIndex, CancellationToken cancellationToken);
}

public sealed record ArchivePage(string FileName, string ContentType, byte[] Bytes);

