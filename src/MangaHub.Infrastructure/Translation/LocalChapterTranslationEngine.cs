using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MangaHub.Core.Services;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.Translation;

public sealed class LocalChapterTranslationEngine(
    IHttpClientFactory httpClientFactory,
    IOptions<MangaHubOptions> options) : IChapterTranslationEngine
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif"
    };

    private readonly ChapterTranslationOptions settings = options.Value.Translation;
    private readonly SemaphoreSlim translationGate = new(1, 1);

    public bool IsEnabled => settings.Enabled;

    public async Task<ChapterTranslationResult> TranslateAsync(
        ChapterTranslationRequest request,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null)
    {
        if (!IsEnabled)
        {
            throw new ChapterTranslationUnavailableException("Local chapter translation is disabled.");
        }

        await translationGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputArchivePath)!);
            progress?.Report(new ReaderPreparationProgress("Waiting for the manga translation engine", 54));
            await WaitForTranslatorAsync(cancellationToken);

            using var sourceArchive = ZipFile.OpenRead(request.SourceArchivePath);
            var sourcePages = sourceArchive.Entries
                .Where(entry => ImageExtensions.Contains(Path.GetExtension(entry.FullName)))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sourcePages.Count == 0)
            {
                throw new ChapterTranslationUnavailableException("The source chapter does not contain readable images.");
            }

            var temporaryArchive = request.OutputArchivePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var outputStream = File.Create(temporaryArchive))
                using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    for (var index = 0; index < sourcePages.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var pageNumber = index + 1;
                        progress?.Report(new ReaderPreparationProgress(
                            $"Translating page {pageNumber} of {sourcePages.Count}",
                            ScaleProgress(index, sourcePages.Count, 55, 94),
                            index,
                            sourcePages.Count));

                        var translatedPage = await TranslatePageAsync(
                            sourcePages[index],
                            request.TargetLanguage,
                            cancellationToken);

                        var outputEntry = outputArchive.CreateEntry(
                            $"{pageNumber:0000}.png",
                            CompressionLevel.Optimal);
                        await using var entryStream = outputEntry.Open();
                        await entryStream.WriteAsync(translatedPage, cancellationToken);

                        progress?.Report(new ReaderPreparationProgress(
                            $"Translated page {pageNumber} of {sourcePages.Count}",
                            ScaleProgress(pageNumber, sourcePages.Count, 55, 96),
                            pageNumber,
                            sourcePages.Count));
                    }
                }

                File.Move(temporaryArchive, request.OutputArchivePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryArchive))
                {
                    File.Delete(temporaryArchive);
                }
            }

            progress?.Report(new ReaderPreparationProgress(
                "Finalizing the translated chapter archive",
                97,
                sourcePages.Count,
                sourcePages.Count));
            return new ChapterTranslationResult(
                Path.GetFileName(request.OutputArchivePath),
                sourcePages.Count,
                await ComputeHashAsync(request.OutputArchivePath, cancellationToken));
        }
        catch (ChapterTranslationUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ChapterTranslationUnavailableException(
                "The manga translation pipeline could not create this chapter.",
                ex);
        }
        finally
        {
            translationGate.Release();
        }
    }

    private async Task<byte[]> TranslatePageAsync(
        ZipArchiveEntry sourcePage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await using var sourceStream = sourcePage.Open();
        using var sourceBuffer = new MemoryStream();
        await sourceStream.CopyToAsync(sourceBuffer, cancellationToken);
        var sourceBytes = sourceBuffer.ToArray();
        if (sourceBytes.Length == 0)
        {
            throw new ChapterTranslationUnavailableException($"Page '{sourcePage.Name}' is empty.");
        }

        using var form = new MultipartFormDataContent();
        using var image = new ByteArrayContent(sourceBytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(ToMediaType(sourcePage.Name));
        form.Add(image, "image", sourcePage.Name);
        form.Add(
            new StringContent(CreateTranslatorConfig(targetLanguage), Encoding.UTF8),
            "config");

        var client = httpClientFactory.CreateClient("chapter-translator");
        using var response = await client.PostAsync(
            "translate/with-form/image",
            form,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ChapterTranslationUnavailableException(
                $"The manga translator rejected page '{sourcePage.Name}' "
                + $"({(int)response.StatusCode}): {Truncate(detail, 500)}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null
            && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ChapterTranslationUnavailableException(
                $"The manga translator returned '{mediaType}' instead of an image: {Truncate(detail, 500)}");
        }

        var translated = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (translated.Length < 128)
        {
            throw new ChapterTranslationUnavailableException(
                $"The manga translator returned an invalid image for page '{sourcePage.Name}'.");
        }

        return translated;
    }

    private string CreateTranslatorConfig(string targetLanguage) =>
        JsonSerializer.Serialize(new
        {
            translator = new
            {
                translator = settings.Translator,
                target_lang = ToMangaTranslatorLanguage(targetLanguage)
            },
            ocr = new
            {
                ocr = "48px",
                min_text_length = Math.Max(0, settings.MinimumTextLength),
                ignore_bubble = Math.Clamp(settings.IgnoreNonBubbleText, 0, 50)
            }
        });

    private async Task WaitForTranslatorAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("chapter-translator");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, settings.ReadinessTimeoutSeconds));
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var response = await client.PostAsync("queue-size", content: null, attempt.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
                lastError = new HttpRequestException(
                    $"Manga translator readiness returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        throw new ChapterTranslationUnavailableException(
            "The manga translation service did not become ready in time.",
            lastError);
    }

    private static string ToMangaTranslatorLanguage(string language) =>
        NormalizeLanguage(language) switch
        {
            "zh" or "zh-cn" => "CHS",
            "zh-hk" or "zh-tw" => "CHT",
            "cs" => "CSY",
            "nl" => "NLD",
            "en" => "ENG",
            "fr" => "FRA",
            "de" => "DEU",
            "hu" => "HUN",
            "it" => "ITA",
            "ja" => "JPN",
            "ko" => "KOR",
            "pl" => "POL",
            "pt" or "pt-br" => "PTB",
            "ro" => "ROM",
            "ru" => "RUS",
            "es" => "ESP",
            "tr" => "TRK",
            "uk" => "UKR",
            "vi" => "VIN",
            "ar" => "ARA",
            "sr" => "SRP",
            "hr" => "HRV",
            "th" => "THA",
            "id" => "IND",
            "fil" or "tl" => "FIL",
            var unsupported => throw new ChapterTranslationUnavailableException(
                $"Translation language '{unsupported}' is not supported by the manga translator.")
        };

    private static string NormalizeLanguage(string language) =>
        language.Trim().ToLowerInvariant().Replace('_', '-');

    private static string ToMediaType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".avif" => "image/avif",
            _ => "application/octet-stream"
        };

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static int ScaleProgress(int current, int total, int minimum, int maximum) =>
        total <= 0
            ? minimum
            : minimum + (int)Math.Round((maximum - minimum) * (current / (double)total));

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
