using System.Text;
using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace MangaHub.Web.Components.Shelf;

public partial class CsvImportModal
{
    private static readonly CsvField[] Fields =
    [
        new("title", "Title", true, ["name", "title", "manga", "series"]),
        new("readingstatus", "Reading status", false, ["status", "readingstatus"]),
        new("currentchapter", "Current chapter", false, ["chapter", "currentchapter", "chaptersread"]),
        new("score", "Rating / score", false, ["rating", "score"]),
        new("isread", "Current chapter read", false, ["currentchapterread", "isread", "chapterread"]),
        new("personalcategory", "Personal category", false, ["personalcategory", "shelfcategory", "category"]),
        new("personalsummary", "Personal summary", false, ["personalsummary", "shelfsummary", "summary"]),
        new("notes", "Notes", false, ["notes", "note", "comments"]),
        new("link", "Link / source URL", false, ["link", "url", "mangadexurl", "sourceurl"]),
        new("mangadexid", "MangaDex ID", false, ["mangadexid"]),
        new("fallbackreaderurl", "Fallback reader URL", false, ["fallbackreaderurl", "externalurl", "readerurl"]),
        new("readerpreference", "Reader preference", false, ["readerpreference", "readertype"]),
        new("myanimelistid", "MAL ID", false, ["malid", "myanimelistid"]),
        new("mangaupdatesid", "MangaUpdates ID", false, ["mangaupdatesid"]),
        new("openlibrarykey", "OpenLibrary key", false, ["openlibrarykey", "openlibraryid"]),
        new("authors", "Authors", false, ["authors", "author", "artist", "mangaka"]),
        new("catalogcategory", "Catalog categories", false, ["catalogcategory", "catalogcategories", "categories", "genres", "tags"]),
        new("catalogdescription", "Catalog description", false, ["catalogdescription", "description"]),
        new("coverurl", "Cover URL", false, ["coverurl", "imageurl", "thumbnail", "poster"]),
        new("mediatype", "Format", false, ["format", "mediatype"]),
        new("firstpublishyear", "First published", false, ["firstpublished", "firstpublishyear", "published", "year"]),
        new("chaptercount", "Chapter count", false, ["chaptercount", "totalchapters"]),
        new("volumecount", "Volume count", false, ["volumecount", "volumes"]),
        new("publishingstatus", "Publishing status", false, ["publishingstatus", "publicationstatus"]),
        new("metadatasource", "Metadata source", false, ["metadatasource"])
    ];

    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public EventCallback OnImported { get; set; }
    [Parameter] public bool CreateMissingCatalogEntries { get; set; }
    [Parameter] public string ContextLabel { get; set; } = "Shelf";

    private string csvText = "";
    private List<string> headers = [];
    private readonly Dictionary<string, string> mappings = new(StringComparer.OrdinalIgnoreCase);
    private int rowCount;
    private string importMessage = "";
    private Severity importSeverity = Severity.Info;
    private bool isImporting;

    private bool IsMappingValid => GetMapping("title").Length > 0 && DuplicateMappings.Count == 0;
    private List<string> DuplicateMappings => mappings
        .Where(mapping => !string.IsNullOrWhiteSpace(mapping.Value))
        .GroupBy(mapping => mapping.Value, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .ToList();
    private Severity preflightSeverity => IsMappingValid ? Severity.Success : Severity.Warning;
    private string PreflightMessage => GetMapping("title").Length == 0
        ? "Choose the CSV column that contains each manga title. Everything else is optional."
        : DuplicateMappings.Count > 0
            ? $"Assign each CSV column once. Duplicate mapping: {string.Join(", ", DuplicateMappings)}."
            : $"{rowCount} data rows are ready. Only selected columns will update existing data.";

    private async Task ReadCsv(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null) return;

        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024);
            using var reader = new StreamReader(stream);
            csvText = await reader.ReadToEndAsync();
            var rows = ParseCsv(csvText);
            headers = rows.Count == 0 ? [] : rows[0].Select(header => header.Trim()).Where(header => !string.IsNullOrWhiteSpace(header)).ToList();
            rowCount = Math.Max(0, rows.Count - 1);
            importMessage = headers.Count == 0 ? "The file does not contain a CSV header row." : "";
            importSeverity = Severity.Error;
            ResetMapping();
        }
        catch
        {
            csvText = "";
            headers = [];
            rowCount = 0;
            importSeverity = Severity.Error;
            importMessage = "Could not read this CSV file. Try a UTF-8 CSV exported from Excel or another spreadsheet app.";
        }
    }

    private void ResetMapping()
    {
        mappings.Clear();
        foreach (var field in Fields)
        {
            var match = headers.FirstOrDefault(header => field.Aliases.Contains(NormalizeHeader(header), StringComparer.OrdinalIgnoreCase));
            mappings[field.Key] = match ?? "";
        }
    }

    private string GetMapping(string field) => mappings.GetValueOrDefault(field, "");

    private void SetMapping(string field, string value) => mappings[field] = value;

    private async Task Import()
    {
        if (!IsMappingValid || string.IsNullOrWhiteSpace(csvText)) return;

        try
        {
            isImporting = true;
            importSeverity = Severity.Info;
            importMessage = "Checking and importing your CSV...";
            var selectedMappings = mappings.Where(mapping => !string.IsNullOrWhiteSpace(mapping.Value))
                .ToDictionary(mapping => mapping.Key, mapping => mapping.Value, StringComparer.OrdinalIgnoreCase);
            var result = await ShelfApi.ImportShelfAsync(new ShelfImportRequest(csvText, CreateMissingCatalogEntries, selectedMappings));
            importSeverity = result is null ? Severity.Error : result.Skipped > 0 ? Severity.Warning : Severity.Success;
            importMessage = result is null
                ? "Import failed. Check the selected columns and try again."
                : $"Imported {result.Imported} rows, created {result.CreatedCatalogEntries} catalog entries, updated {result.UpdatedShelfEntries}, skipped {result.Skipped}.";
            if (result?.Messages.Count > 0) importMessage += " " + string.Join(" ", result.Messages);
            if (result is not null && result.Imported > 0) await OnImported.InvokeAsync();
        }
        catch
        {
            importSeverity = Severity.Error;
            importMessage = "Import could not complete. Check the selected columns and CSV values, then try again.";
        }
        finally
        {
            isImporting = false;
        }
    }

    private async Task Close()
    {
        if (isImporting) return;
        csvText = "";
        headers = [];
        mappings.Clear();
        rowCount = 0;
        importMessage = "";
        await OpenChanged.InvokeAsync(false);
    }

    private static string NormalizeHeader(string value) => new string(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < csv.Length && csv[index + 1] == '"') { value.Append(character); index++; }
                else inQuotes = !inQuotes;
            }
            else if (character == ',' && !inQuotes) { row.Add(value.ToString()); value.Clear(); }
            else if ((character == '\n' || character == '\r') && !inQuotes)
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                row.Add(value.ToString()); value.Clear();
                if (row.Any(cell => !string.IsNullOrWhiteSpace(cell))) rows.Add(row);
                row = [];
            }
            else value.Append(character);
        }
        row.Add(value.ToString());
        if (row.Any(cell => !string.IsNullOrWhiteSpace(cell))) rows.Add(row);
        return rows;
    }

    private sealed record CsvField(string Key, string Label, bool Required, IReadOnlyList<string> Aliases);
}
