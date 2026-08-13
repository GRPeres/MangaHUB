namespace MangaHub.Web.API.DTOs;

public sealed record ShelfImportRequest(string CsvText, bool CreateMissingCatalogEntries, Dictionary<string, string>? ColumnMappings = null);
