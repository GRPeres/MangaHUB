namespace MangaHub.Core.Services;

public sealed record AdminIssueTypeDefinition(string Kind, string SubjectType, string DefaultPriority);

public static class AdminIssueTypes
{
    public const string CatalogManga = "catalog-manga";
    public const string ExternalReaderLink = "external-reader-link";

    private static readonly IReadOnlyDictionary<string, AdminIssueTypeDefinition> Definitions =
        new Dictionary<string, AdminIssueTypeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [ExternalReaderLink] = new(ExternalReaderLink, CatalogManga, "normal")
        };

    public static bool TryGet(string kind, out AdminIssueTypeDefinition definition) =>
        Definitions.TryGetValue(kind.Trim(), out definition!);

    public static bool Supports(string kind, string subjectType) =>
        TryGet(kind, out var definition) && string.Equals(definition.SubjectType, subjectType.Trim(), StringComparison.OrdinalIgnoreCase);
}
