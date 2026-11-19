using System.Text.Json;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

internal static class IssueEvidence
{
    public static decimal? ReadNumber(string metadata, string key)
    {
        using var document = JsonDocument.Parse(metadata);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number) ? number : null;
    }

    public static async Task<bool> WasDismissedAsync(MangaHubDbContext db, string kind, Guid subjectId,
        string metadata, string[] keys, CancellationToken cancellationToken)
    {
        var previous = await db.AdminIssues.AsNoTracking()
            .Where(issue => issue.Kind == kind && issue.SubjectType == AdminIssueTypes.CatalogManga && issue.SubjectId == subjectId)
            .OrderByDescending(issue => issue.UpdatedAt).ThenByDescending(issue => issue.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (previous?.Status != "dismissed") return false;
        using var before = JsonDocument.Parse(previous.MetadataJson);
        using var after = JsonDocument.Parse(metadata);
        return before.RootElement.ValueKind == JsonValueKind.Object && keys.All(key =>
            before.RootElement.TryGetProperty(key, out var oldValue)
            && after.RootElement.TryGetProperty(key, out var newValue)
            && JsonElement.DeepEquals(oldValue, newValue));
    }
}
