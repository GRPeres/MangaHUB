using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

/// <summary>
/// Validates the stored MangaDex link against MangaDex's own MAL relation.
/// Only an explicit MAL disagreement is treated as a mismatch; titles alone are not reliable enough.
/// </summary>
public sealed class MangaDexIdentityAuditService(
    MangaHubDbContext db,
    IMangaDexCatalogLookup mangaDexCatalog,
    ILogger<MangaDexIdentityAuditService> logger)
{
    public async Task<MangaDexIdentityAuditResult> RunAsync(CancellationToken cancellationToken)
    {
        var entries = await db.MangaEntries
            .Where(entry => entry.MangaDexId != "" && entry.MyAnimeListId != "")
            .ToListAsync(cancellationToken);
        var openIssues = await db.AdminIssues
            .Where(issue => issue.Kind == AdminIssueTypes.MangaDexIdentityMismatch
                && issue.SubjectType == AdminIssueTypes.CatalogManga
                && issue.Status == "open")
            .ToDictionaryAsync(issue => issue.SubjectId, cancellationToken);

        var audited = 0;
        var openedOrUpdated = 0;
        var resolved = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MangaDexCatalogIdentity? identity;
            try
            {
                identity = await mangaDexCatalog.GetIdentityAsync(entry.MangaDexId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                logger.LogWarning(ex, "Could not audit MangaDex identity for catalog entry {EntryId}.", entry.Id);
                continue;
            }

            if (identity is null || string.IsNullOrWhiteSpace(identity.MyAnimeListId))
            {
                continue;
            }

            audited++;
            if (string.Equals(entry.MyAnimeListId.Trim(), identity.MyAnimeListId.Trim(), StringComparison.Ordinal))
            {
                if (openIssues.Remove(entry.Id, out var resolvedIssue))
                {
                    resolvedIssue.Status = "resolved";
                    resolvedIssue.ResolvedAt = now;
                    resolvedIssue.UpdatedAt = now;
                    resolvedIssue.ResolutionNote = "MangaDex now reports the same MyAnimeList ID as this catalog entry.";
                    resolved++;
                }
                continue;
            }

            var metadata = JsonSerializer.Serialize(new
            {
                expectedMyAnimeListId = entry.MyAnimeListId,
                linkedMangaDexId = entry.MangaDexId,
                linkedMyAnimeListId = identity.MyAnimeListId,
                linkedTitle = identity.Title,
                linkedAlternateTitles = identity.AlternateTitles,
                checkedAt = now
            });
            if (openIssues.TryGetValue(entry.Id, out var existing))
            {
                existing.MetadataJson = metadata;
                existing.Priority = "high";
                existing.UpdatedAt = now;
            }
            else
            {
                db.AdminIssues.Add(new AdminIssue
                {
                    Kind = AdminIssueTypes.MangaDexIdentityMismatch,
                    SubjectType = AdminIssueTypes.CatalogManga,
                    SubjectId = entry.Id,
                    Priority = "high",
                    TitleSnapshot = $"MangaDex identity mismatch: {entry.Title}",
                    MetadataJson = metadata,
                    ResolutionNote = "The stored MangaDex link belongs to a different MyAnimeList record. Replace it from Catalog after verifying the title."
                });
            }
            openedOrUpdated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("MangaDex identity audit checked {AuditedCount} entries, opened or updated {IssueCount} issues, and resolved {ResolvedCount} issues.", audited, openedOrUpdated, resolved);
        return new MangaDexIdentityAuditResult(audited, openedOrUpdated, resolved);
    }
}

public sealed record MangaDexIdentityAuditResult(int AuditedEntries, int OpenedOrUpdatedIssues, int ResolvedIssues);
