using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

public sealed class IssueProposalScheduleWorker(IServiceScopeFactory scopes, IConfiguration config,
    IHttpClientFactory clients, ChatGptProposalProvider chatGpt, ILogger<IssueProposalScheduleWorker> logger) : BackgroundService
{
    private const int DraftTimeoutSeconds = 60;
    private const int ResearchTimeoutSeconds = 240;
    private const int LeaseBufferSeconds = 120;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunIfDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "Scheduled issue proposal run failed."); }
        }
    }

    private async Task RunIfDueAsync(CancellationToken token)
    {
        var key = config["MangaHub:IssueProposals:OpenAiApiKey"];
        var model = config["MangaHub:IssueProposals:Model"];
        if (string.IsNullOrWhiteSpace(model) || (chatGpt.Enabled ? !chatGpt.IsConnected : string.IsNullOrWhiteSpace(key))) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var issueService = scope.ServiceProvider.GetRequiredService<IssueReportingService>();
        var now = DateTimeOffset.UtcNow;
        var settings = await db.IssueProposalSchedules.AsNoTracking().SingleAsync(token);
        if (settings.RunId is not null && settings.LeaseUntil <= now)
        {
            await db.IssueProposalSchedules.Where(row => row.Id == settings.Id && row.RunId == settings.RunId && row.LeaseUntil <= now)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RunId, (Guid?)null)
                    .SetProperty(row => row.ManualBatchRunning, false)
                    .SetProperty(row => row.ManualStopRequested, false)
                    .SetProperty(row => row.LastError, "The previous run was interrupted."), token);
            return;
        }
        var payload = settings.PendingManualBatch;
        var manual = payload is null ? null : JsonSerializer.Deserialize<ManualIssueProposalBatch>(payload);
        if (settings.RunId is not null || (manual is null && !settings.CanStart(now, hasUnreadDrafts: false))) return;
        var delaySeconds = manual?.RequestIntervalSeconds ?? settings.RequestIntervalSeconds;

        var runId = Guid.NewGuid();
        var claimed = await db.IssueProposalSchedules.Where(row => row.Id == settings.Id
                && row.RunId == null && row.PendingManualBatch == payload
                && (payload != null || (row.Enabled && (row.NextRunAt == null || row.NextRunAt <= now)))
                && !db.ScheduledIssueProposals.Any(item => item.ReviewedAt == null))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RunId, runId)
                .SetProperty(row => row.PendingManualBatch, (string?)null)
                .SetProperty(row => row.ManualBatchRunning, payload != null)
                .SetProperty(row => row.ManualStopRequested, false)
                .SetProperty(row => row.LeaseUntil, now.AddSeconds(delaySeconds + ResearchTimeoutSeconds + LeaseBufferSeconds))
                .SetProperty(row => row.LastError, ""), token);
        if (claimed != 1) return;

        var error = "";
        var index = 0;
        try
        {
            var known = manual is null ? await db.ScheduledIssueProposals.Select(item => item.EvidenceKey).ToListAsync(token) : [];
            var query = db.AdminIssues.AsNoTracking().Where(issue => issue.Status == "open"
                && issue.ReviewState == "action_required" && (issue.SnoozedUntil == null || issue.SnoozedUntil <= now)
                && (issue.Kind == "mangadex-release-stalled" || issue.Kind == "mangadex-identity-mismatch" || issue.Kind == "mangadex-language-coverage"));
            if (manual is { IssueIds.Length: > 0 }) query = query.Where(issue => manual.IssueIds.Contains(issue.Id));
            var entries = await query.OrderBy(issue => issue.CreatedAt).ThenBy(issue => issue.Id).ToListAsync(token);
            var seen = known.ToHashSet();
            var groups = entries.Select(issue => CreatePacket(issue, (manual?.WebResearch ?? settings.WebResearchEnabled)
                    && issue.Kind is "mangadex-release-stalled" or "mangadex-language-coverage"))
                .Where(item => seen.Add(item.Key)).GroupBy(item => item.Research)
                .SelectMany(group => group.Chunk(manual?.GroupSize ?? settings.GroupSize));
            foreach (var group in groups)
            {
                if (index++ > 0) await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                var lease = DateTimeOffset.UtcNow.AddSeconds(delaySeconds + ResearchTimeoutSeconds + LeaseBufferSeconds);
                if (await db.IssueProposalSchedules.Where(row => row.Id == settings.Id && (payload != null || row.Enabled)
                        && row.RunId == runId && !row.ManualStopRequested)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.LeaseUntil, lease), token) != 1) break;
                for (var i = 0; i < group.Length; i++)
                {
                    var packet = group[i];
                    packet = packet with { Catalog = await IssueProposalEvidence.ReadCatalogAsync(db, packet.SubjectId, token) };
                    group[i] = packet;
                    if (packet.Kind != "mangadex-identity-mismatch" || !packet.Evidence.TryGetValue("expectedMyAnimeListId", out var mal)) continue;
                    var (match, _) = await issueService.RepairMangaDexIdentityAsync(Guid.Empty, packet.Id, token, preview: true, confirmedMalId: mal);
                    group[i] = packet with { Candidate = match?.Id, ExpectedMal = mal };
                }
                var seconds = group[0].Research ? ResearchTimeoutSeconds : DraftTimeoutSeconds;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
                string text;
                try
                {
                    text = await DraftAsync(group, key ?? "", model, deadline.Token);
                }
                catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    var mode = group[0].Research ? " with web research" : "";
                    throw new TimeoutException($"Proposal request {index}{mode} exceeded its {seconds}-second time limit. Try fewer issues per request.", ex);
                }
                db.ScheduledIssueProposals.AddRange(group.Select((packet, i) => IssueProposalActions.Create(
                    runId, manual is null ? packet.Key : $"manual:{runId:N}:{packet.Id:N}", packet.Id, packet.Snapshot,
                    i + 1, text, packet.Candidate, packet.ExpectedMal, packet.Kind)));
                await db.SaveChangesAsync(token);
            }
            if (manual is not null && index == 0) error = "No eligible open issues were found.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            error = ex is ChatGptRequestException or TimeoutException ? ex.Message
                : "The run stopped before all requests completed. Check the provider and API logs.";
            logger.LogWarning(ex, "Issue proposal run {RunId} stopped.", runId);
        }
        finally
        {
            await db.IssueProposalSchedules.Where(row => row.Id == settings.Id && row.RunId == runId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RunId, (Guid?)null)
                    .SetProperty(row => row.ManualBatchRunning, false)
                    .SetProperty(row => row.ManualStopRequested, false)
                    .SetProperty(row => row.LastError, error)
                    .SetProperty(row => row.NextRunAt, row => payload != null ? row.NextRunAt : DateTimeOffset.UtcNow.AddMinutes(settings.BatchIntervalMinutes)),
                    CancellationToken.None);
        }
    }

    private sealed record Packet(string Key, Guid Id, Guid SubjectId, string Snapshot, string Kind, string Title,
        SortedDictionary<string, string> Evidence, bool Research, string? Candidate = null, string? ExpectedMal = null, object? Catalog = null);

    private static Packet CreatePacket(AdminIssue issue, bool research)
    {
        var data = IssueProposalEvidence.ReadIssueFacts(issue.MetadataJson);
        var fingerprint = research ? JsonSerializer.Serialize(new { issue.Id, issue.Kind, data, research })
            : JsonSerializer.Serialize(new { issue.Id, issue.Kind, data });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return new("action:" + Convert.ToHexString(hash), issue.Id, issue.SubjectId, IssueProposalActions.Fingerprint(issue.MetadataJson),
            issue.Kind, issue.TitleSnapshot, data, research);
    }

    private async Task<string> DraftAsync(Packet[] group, string key, string model, CancellationToken token)
    {
        var research = group[0].Research;
        var instructions = IssueProposalActions.Instructions;
        if (research) instructions += "\n" + IssueProposalResearch.Instructions;
        var input = JsonSerializer.Serialize(group.Select((item, index) => new
        {
            number = index + 1, item.Kind, item.Title, item.Evidence, item.Catalog, verifiedReplacementId = item.Candidate
        }));
        if (chatGpt.Enabled) return await chatGpt.DraftAsync(model, instructions, input, token, research);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new("Bearer", key);
        request.Content = JsonContent.Create(new
        {
            model, store = false, max_output_tokens = 400 + 160 * group.Length,
            instructions, input, tools = research ? new[] { new { type = "web_search" } } : []
        });
        using var client = clients.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (doc.RootElement.GetProperty("status").GetString() != "completed")
            throw new InvalidOperationException("Proposal response was incomplete.");
        var text = string.Join("\n", doc.RootElement.GetProperty("output").EnumerateArray()
            .Where(item => item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(part => part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
            .Select(part => part.GetProperty("text").GetString()));
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("No proposal text returned.");
        return research ? IssueProposalResearch.AttachSources(doc.RootElement, text) : text;
    }
}
