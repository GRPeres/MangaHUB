using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using MangaHub.Core.Services;
using Microsoft.Extensions.Options;
using SkiaSharp;

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
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "mangahub-translation", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputArchivePath)!);
            progress?.Report(new ReaderPreparationProgress("Waiting for the local translation engine", 54));
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
                            $"Reading text on page {pageNumber} of {sourcePages.Count}",
                            ScaleProgress(index, sourcePages.Count, 55, 82),
                            index,
                            sourcePages.Count));

                        var translatedPage = await TranslatePageAsync(
                            sourcePages[index],
                            request.SourceLanguage,
                            request.TargetLanguage,
                            temporaryRoot,
                            cancellationToken);

                        progress?.Report(new ReaderPreparationProgress(
                            $"Rendering translated page {pageNumber} of {sourcePages.Count}",
                            ScaleProgress(pageNumber, sourcePages.Count, 82, 96),
                            pageNumber,
                            sourcePages.Count));
                        var outputEntry = outputArchive.CreateEntry($"{pageNumber:0000}.png", CompressionLevel.Optimal);
                        await using var entryStream = outputEntry.Open();
                        await entryStream.WriteAsync(translatedPage, cancellationToken);
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
                "The local OCR and translation pipeline could not create this chapter.",
                ex);
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
            translationGate.Release();
        }
    }

    private async Task<byte[]> TranslatePageAsync(
        ZipArchiveEntry sourcePage,
        string sourceLanguage,
        string targetLanguage,
        string temporaryRoot,
        CancellationToken cancellationToken)
    {
        await using var sourceStream = sourcePage.Open();
        using var image = SKBitmap.Decode(sourceStream)
            ?? throw new ChapterTranslationUnavailableException($"Page '{sourcePage.Name}' is not a supported image.");
        var inputPath = Path.Combine(temporaryRoot, $"{Guid.NewGuid():N}.png");
        try
        {
            await SavePngAsync(image, inputPath, cancellationToken);
            var regions = await RecognizeAsync(inputPath, sourceLanguage, cancellationToken);
            if (regions.Count == 0)
            {
                return EncodePng(image);
            }

            var translated = await TranslateTextsAsync(
                regions.Select(region => region.Text).ToList(),
                sourceLanguage,
                targetLanguage,
                cancellationToken);
            RenderTranslations(image, regions, translated);
            return EncodePng(image);
        }
        finally
        {
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private async Task<List<OcrRegion>> RecognizeAsync(
        string imagePath,
        string sourceLanguage,
        CancellationToken cancellationToken)
    {
        var tesseractLanguage = ToTesseractLanguage(sourceLanguage);
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.TesseractCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(tesseractLanguage);
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add(tesseractLanguage.Contains("_vert", StringComparison.Ordinal) ? "5" : "11");
        startInfo.ArgumentList.Add("tsv");

        using var process = Process.Start(startInfo)
            ?? throw new ChapterTranslationUnavailableException("Tesseract could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new ChapterTranslationUnavailableException(
                $"Tesseract OCR failed for language '{sourceLanguage}': {error.Trim()}");
        }

        return ParseTsv(output);
    }

    private List<OcrRegion> ParseTsv(string tsv)
    {
        var words = new List<OcrWord>();
        foreach (var line in tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var columns = line.TrimEnd('\r').Split('\t');
            if (columns.Length < 12
                || !int.TryParse(columns[2], out var block)
                || !int.TryParse(columns[3], out var paragraph)
                || !int.TryParse(columns[4], out var lineNumber)
                || !int.TryParse(columns[6], out var left)
                || !int.TryParse(columns[7], out var top)
                || !int.TryParse(columns[8], out var width)
                || !int.TryParse(columns[9], out var height)
                || !float.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                || confidence < settings.MinimumOcrConfidence
                || string.IsNullOrWhiteSpace(columns[11]))
            {
                continue;
            }

            words.Add(new OcrWord(block, paragraph, lineNumber, left, top, width, height, columns[11].Trim()));
        }

        return words
            .GroupBy(word => new { word.Block, word.Paragraph, word.Line })
            .Select(group =>
            {
                var left = group.Min(word => word.Left);
                var top = group.Min(word => word.Top);
                var right = group.Max(word => word.Left + word.Width);
                var bottom = group.Max(word => word.Top + word.Height);
                return new OcrRegion(
                    left,
                    top,
                    right - left,
                    bottom - top,
                    string.Join(' ', group.OrderBy(word => word.Left).Select(word => word.Text)));
            })
            .Where(region => region.Width >= 8 && region.Height >= 8 && region.Text.Length > 1)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> TranslateTextsAsync(
        IReadOnlyList<string> texts,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var source = ToLibreTranslateLanguage(sourceLanguage);
        var target = ToLibreTranslateLanguage(targetLanguage);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return texts;
        }

        var client = httpClientFactory.CreateClient("chapter-translator");
        using var response = await client.PostAsJsonAsync("translate", new
        {
            q = texts,
            source,
            target,
            format = "text",
            api_key = string.IsNullOrWhiteSpace(settings.LibreTranslateApiKey) ? null : settings.LibreTranslateApiKey
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ChapterTranslationUnavailableException(
                $"LibreTranslate rejected {source} to {target} translation ({(int)response.StatusCode}): {detail}");
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("translatedText", out var translatedText))
        {
            throw new ChapterTranslationUnavailableException("LibreTranslate returned an invalid response.");
        }

        if (translatedText.ValueKind == JsonValueKind.Array)
        {
            return translatedText.EnumerateArray().Select(item => item.GetString() ?? "").ToList();
        }

        return texts.Count == 1 && translatedText.ValueKind == JsonValueKind.String
            ? [translatedText.GetString() ?? ""]
            : throw new ChapterTranslationUnavailableException("LibreTranslate returned an unexpected translation count.");
    }

    private async Task WaitForTranslatorAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("chapter-translator");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, settings.RequestTimeoutSeconds));
        Exception? lastError = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var response = await client.GetAsync("languages", attempt.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
                lastError = new HttpRequestException($"LibreTranslate readiness returned {(int)response.StatusCode}.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        throw new ChapterTranslationUnavailableException(
            "The local translation service did not become ready in time.",
            lastError);
    }

    private void RenderTranslations(
        SKBitmap image,
        IReadOnlyList<OcrRegion> regions,
        IReadOnlyList<string> translated)
    {
        using var canvas = new SKCanvas(image);
        using var typeface = SKTypeface.FromFamilyName(settings.FontFamily) ?? SKTypeface.Default;
        using var background = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };

        for (var index = 0; index < Math.Min(regions.Count, translated.Count); index++)
        {
            var region = regions[index];
            var padding = Math.Max(4, Math.Min(region.Width, region.Height) / 10);
            var left = Math.Max(0, region.Left - padding);
            var top = Math.Max(0, region.Top - padding);
            var width = Math.Min(image.Width - left, region.Width + (padding * 2));
            var height = Math.Min(image.Height - top, Math.Max(region.Height + (padding * 2), 24));
            var rectangle = new SKRect(left, top, left + width, top + height);
            canvas.DrawRect(rectangle, background);

            using var font = new SKFont(typeface, Math.Clamp(height * 0.48f, 10f, 42f));
            var lines = WrapText(translated[index], font, Math.Max(8, width - (padding * 2)));
            var lineHeight = font.Size * 1.12f;
            while (lines.Count * lineHeight > height - (padding * 2) && font.Size > 8)
            {
                font.Size -= 1;
                lineHeight = font.Size * 1.12f;
                lines = WrapText(translated[index], font, Math.Max(8, width - (padding * 2)));
            }

            var firstBaseline = top + ((height - (lines.Count * lineHeight)) / 2) + font.Size;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                canvas.DrawText(
                    lines[lineIndex],
                    left + (width / 2f),
                    firstBaseline + (lineIndex * lineHeight),
                    SKTextAlign.Center,
                    font,
                    textPaint);
            }
        }
        canvas.Flush();
    }

    private static List<string> WrapText(string text, SKFont font, float maximumWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [""];
        }

        var lines = new List<string>();
        var current = words[0];
        foreach (var word in words.Skip(1))
        {
            var candidate = $"{current} {word}";
            if (font.MeasureText(candidate) <= maximumWidth)
            {
                current = candidate;
                continue;
            }
            lines.Add(current);
            current = word;
        }
        lines.Add(current);
        return lines;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task SavePngAsync(SKBitmap bitmap, string path, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(path, EncodePng(bitmap), cancellationToken);
    }

    private static int ScaleProgress(int current, int total, int minimum, int maximum) =>
        total <= 0 ? minimum : minimum + (int)Math.Round((maximum - minimum) * (current / (double)total));

    private static string ToTesseractLanguage(string language) => NormalizeLanguage(language) switch
    {
        "ja" => "jpn_vert+jpn",
        "ko" => "kor",
        "pt" or "pt-br" => "por",
        "es" => "spa",
        "fr" => "fra",
        "de" => "deu",
        "it" => "ita",
        "zh" or "zh-cn" => "chi_sim",
        "zh-hk" or "zh-tw" => "chi_tra",
        _ => "eng"
    };

    private static string ToLibreTranslateLanguage(string language) => NormalizeLanguage(language) switch
    {
        "pt-br" => "pb",
        "zh-hk" or "zh-tw" => "zt",
        "zh-cn" => "zh",
        var normalized => normalized
    };

    private static string NormalizeLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record OcrWord(
        int Block,
        int Paragraph,
        int Line,
        int Left,
        int Top,
        int Width,
        int Height,
        string Text);

    private sealed record OcrRegion(int Left, int Top, int Width, int Height, string Text);
}
