namespace MangaHub.Web.API.DTOs;

public sealed record ShelfImportResponse(int Imported, int CreatedCatalogEntries, int UpdatedShelfEntries, int Skipped, List<string> Messages);
