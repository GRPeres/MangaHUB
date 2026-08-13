using System.Text;
using MangaHub.Core.Dto;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MangaHub.Api.Services;

public sealed class ShelfExportService
{
    public byte[] CreateCsv(IReadOnlyList<MangaEntryResponse> entries)
    {
        var csv = new StringBuilder();
        AppendRow(csv,
            "Name", "Link", "Status", "Chapter", "Rating", "Type", "Summary", "Notes",
            "Authors", "Categories", "Current Chapter Read", "Publishing Status", "Latest Preferred Language Chapter",
            "Metadata Source", "MAL ID", "MangaDex ID", "MangaUpdates ID", "Fallback Reader URL", "Reader Preference", "OpenLibrary Key",
            "Personal Category", "Catalog Description", "First Published", "Chapter Count", "Volume Count");

        foreach (var entry in entries)
        {
            var mangaDexUrl = string.IsNullOrWhiteSpace(entry.MangaDexId) ? "" : $"https://mangadex.org/title/{entry.MangaDexId}";
            AppendRow(csv,
                entry.Title,
                string.IsNullOrWhiteSpace(mangaDexUrl) ? entry.FallbackReaderUrl : mangaDexUrl,
                entry.ReadingStatus,
                entry.CurrentChapter,
                entry.Score?.ToString() ?? "",
                entry.MediaType,
                entry.Summary,
                entry.Notes,
                entry.Authors,
                FirstNonEmpty(entry.Category, entry.CatalogCategory),
                entry.IsRead ? "true" : "false",
                entry.PublishingStatus,
                entry.MangaDexPreferredLanguageLatestChapter?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                entry.MetadataSource,
                entry.MyAnimeListId,
                entry.MangaDexId,
                entry.MangaUpdatesId,
                entry.FallbackReaderUrl,
                entry.ReaderPreference,
                entry.OpenLibraryKey,
                entry.Category,
                entry.Description,
                entry.FirstPublishYear?.ToString() ?? "",
                entry.ChapterCount?.ToString() ?? "",
                entry.VolumeCount?.ToString() ?? "");
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
    }

    public byte[] CreatePdf(string username, IReadOnlyList<MangaEntryResponse> entries)
    {
        var counts = entries
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.ReadingStatus) ? "planned" : entry.ReadingStatus, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var generatedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken3));
                page.Header().Column(column =>
                {
                    column.Item().Text("MangaHub").FontSize(24).Bold().FontColor(Colors.Purple.Darken2);
                    column.Item().Text($"{username}'s shelf").FontSize(16).SemiBold();
                    column.Item().Text($"{entries.Count} manga tracked - exported {generatedAt}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(12).Row(row =>
                    {
                        AddCountCard(row, "Reading", Count(counts, "reading"), Colors.Blue.Darken2);
                        AddCountCard(row, "On hiatus", Count(counts, "paused"), Colors.Orange.Darken2);
                        AddCountCard(row, "Planned", Count(counts, "planned"), Colors.Purple.Darken2);
                        AddCountCard(row, "Done", Count(counts, "done"), Colors.Green.Darken2);
                    });
                    column.Item().PaddingVertical(12).LineHorizontal(1).LineColor(Colors.Purple.Lighten3);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("Manga");
                        header.Cell().Element(HeaderCell).Text("Status");
                        header.Cell().Element(HeaderCell).Text("Progress");
                        header.Cell().Element(HeaderCell).Text("Latest");
                        header.Cell().Element(HeaderCell).Text("Score");
                    });

                    foreach (var entry in entries.OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase))
                    {
                        table.Cell().Element(Cell).Column(column =>
                        {
                            column.Item().Text(entry.Title).SemiBold().FontColor(Colors.Purple.Darken3);
                            var metadata = string.Join(" - ", new[] { entry.Authors, entry.MediaType, FirstNonEmpty(entry.Category, entry.CatalogCategory) }.Where(value => !string.IsNullOrWhiteSpace(value)));
                            if (!string.IsNullOrWhiteSpace(metadata)) column.Item().Text(metadata).FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                        table.Cell().Element(Cell).Text(StatusLabel(entry.ReadingStatus));
                        table.Cell().Element(Cell).Text(string.IsNullOrWhiteSpace(entry.CurrentChapter) ? "Not started" : $"Ch. {entry.CurrentChapter}");
                        table.Cell().Element(Cell).Text(entry.MangaDexPreferredLanguageLatestChapter is { } latest ? $"Ch. {latest:0.###}" : "-");
                        table.Cell().Element(Cell).Text(entry.Score is { } score ? $"{score}/5" : "-");
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("MangaHub - personal manga shelf").FontSize(8).FontColor(Colors.Grey.Darken1);
                    text.Span("  |  ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void AddCountCard(RowDescriptor row, string label, int count, string color) =>
        row.RelativeItem().PaddingRight(6).Background(color).Padding(8).Column(column =>
        {
            column.Item().Text(label).FontColor(Colors.White).FontSize(8);
            column.Item().Text(count.ToString()).FontColor(Colors.White).FontSize(16).Bold();
        });

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Purple.Darken2).Padding(6);

    private static IContainer Cell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6).PaddingRight(6);

    private static int Count(IReadOnlyDictionary<string, int> counts, string status) => counts.TryGetValue(status, out var count) ? count : 0;

    private static string StatusLabel(string status) => status.ToLowerInvariant() switch
    {
        "paused" => "On hiatus",
        "done" => "Done",
        "reading" => "Reading",
        "planned" => "Planned",
        "dropped" => "Dropped",
        _ => "Planned"
    };

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static void AppendRow(StringBuilder csv, params string[] values) =>
        csv.AppendJoin(',', values.Select(Escape)).AppendLine();

    private static string Escape(string value)
    {
        var safeValue = value ?? "";
        if (safeValue.Length > 0 && safeValue[0] is '=' or '+' or '-' or '@')
        {
            safeValue = $"\t{safeValue}";
        }

        var escaped = safeValue.Replace("\"", "\"\"");
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
    }
}
