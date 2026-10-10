namespace MangaHub.Core.Services;

public sealed record AdminIssueTypeDefinition(string Kind, string SubjectType, string DefaultPriority);

public static class AdminIssueTypes
{
    public const string CatalogManga = "catalog-manga";
    public const string ExternalReaderLink = "external-reader-link";
    public const string MangaDexLanguageCoverage = "mangadex-language-coverage";
    public const string MangaDexTranslationAbandoned = "mangadex-translation-abandoned";
    public const string CoverImage = "cover-image";
    public const string CatalogMetadata = "catalog-metadata";
    public const string DuplicateCatalogId = "duplicate-catalog-id";
    public const string MaintenanceTask = "maintenance-task";
    public const string MaintenanceOverdue = "maintenance-overdue";
    public const string ArchiveIntegrity = "archive-integrity";
    public const string CatalogAddition = "catalog-addition";
    public const string CatalogRegistrationFailure = "catalog-registration-failure";

    private static readonly IReadOnlyDictionary<string, AdminIssueTypeDefinition> Definitions =
        new Dictionary<string, AdminIssueTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [ExternalReaderLink] = new(ExternalReaderLink, CatalogManga, "normal"),
            [MangaDexLanguageCoverage] = new(MangaDexLanguageCoverage, CatalogManga, "normal"),
            [MangaDexTranslationAbandoned] = new(MangaDexTranslationAbandoned, CatalogManga, "high"),
            [CoverImage] = new(CoverImage, CatalogManga, "normal"),
            [CatalogMetadata] = new(CatalogMetadata, CatalogManga, "normal"),
            [DuplicateCatalogId] = new(DuplicateCatalogId, CatalogManga, "high"),
            [MaintenanceOverdue] = new(MaintenanceOverdue, MaintenanceTask, "high"),
            [ArchiveIntegrity] = new(ArchiveIntegrity, MaintenanceTask, "high"),
            [CatalogRegistrationFailure] = new(CatalogRegistrationFailure, CatalogAddition, "normal")
        };

    public static bool TryGet(string kind, out AdminIssueTypeDefinition definition) =>
        Definitions.TryGetValue(kind.Trim(), out definition!);

    public static bool Supports(string kind, string subjectType) =>
        TryGet(kind, out var definition) && string.Equals(definition.SubjectType, subjectType.Trim(), StringComparison.OrdinalIgnoreCase);
}
