namespace MangaHub.Core.Services;

public interface IChapterTranslationEngine
{
    bool IsEnabled { get; }

    Task<ChapterTranslationResult> TranslateAsync(
        ChapterTranslationRequest request,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null);
}

public sealed record ChapterTranslationRequest(
    string SourceArchivePath,
    string OutputArchivePath,
    string SourceLanguage,
    string TargetLanguage);

public sealed record ChapterTranslationResult(
    string RelativePath,
    int PageCount,
    string FileHash);

public sealed class ChapterTranslationUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
