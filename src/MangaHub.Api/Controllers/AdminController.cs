using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using Microsoft.AspNetCore.Mvc;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(CurrentUserService currentUsers, AdminService admin, OneTimeTestAccountService testAccounts, AdminOperationsService operations, IssueReportingService issues, MangaDexTranslationCoverageService coverage, CatalogService catalog, MangaHubDbContext db, IHttpClientFactory httpClients) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }
        if (!CurrentUserService.IsAdmin(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        if (user.TemporaryAccountExpiresAt is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(await admin.ListUsersAsync(cancellationToken));
    }

    [HttpPost("test-accounts")]
    public async Task<IActionResult> CreateOneTimeTestAccount(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        if (user.TemporaryAccountExpiresAt is not null) return StatusCode(StatusCodes.Status403Forbidden);

        var result = await testAccounts.CreateAsync(user.Id, cancellationToken);
        return result.Error switch
        {
            null => Ok(result.Credentials),
            "active_account_exists" => Conflict("You already have an unclaimed one-time test account. Use it or wait for it to expire before creating another."),
            _ => BadRequest()
        };
    }

    [HttpGet("diagnostics/database")]
    public async Task<IActionResult> TestDatabase(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var connected = await db.Database.CanConnectAsync(cancellationToken);
        return Ok(new DiagnosticResult(connected, connected ? "PostgreSQL is reachable." : "PostgreSQL could not be reached."));
    }

    [HttpGet("diagnostics/mangadex")]
    public async Task<IActionResult> TestMangaDex(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            using var response = await httpClients.CreateClient("mangadex-sync").GetAsync("/ping", cancellationToken);
            return Ok(new DiagnosticResult(response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "MangaDex API is reachable." : $"MangaDex returned HTTP {(int)response.StatusCode}."));
        }
        catch (HttpRequestException ex)
        {
            return Ok(new DiagnosticResult(false, $"MangaDex connection failed: {ex.Message}"));
        }
    }

    [HttpGet("operations")]
    public async Task<IActionResult> Operations(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await operations.GetOverviewAsync(cancellationToken));
    }

    [HttpGet("archive")]
    public async Task<IActionResult> Archive(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await operations.GetArchiveOverviewAsync(cancellationToken));
    }

    [HttpPost("archive/test-recovery")]
    public async Task<IActionResult> TestArchiveRecovery(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await operations.TestArchiveRecoveryAsync(cancellationToken));
    }

    [HttpGet("catalog/count")]
    public async Task<IActionResult> CatalogCount(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await db.MangaEntries.CountAsync(cancellationToken));
    }

    [HttpGet("operations/jobs")]
    public async Task<IActionResult> OperationHistory([FromQuery] int offset = 0, [FromQuery] int limit = 25, [FromQuery] string? type = null, [FromQuery] string? status = null, [FromQuery] string? trigger = null, CancellationToken cancellationToken = default)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await operations.ListHistoryAsync(offset, limit, type, status, trigger, cancellationToken));
    }

    [HttpGet("issues")]
    public async Task<IActionResult> Issues([FromQuery] string? status, [FromQuery] int offset = 0, [FromQuery] int limit = 40, CancellationToken cancellationToken = default)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await issues.ListAsync(status, offset, limit, cancellationToken));
    }

    [HttpPost("issues/{issueId:guid}/suggest-repair")]
    public async Task<IActionResult> SuggestIssueRepair(Guid issueId, [FromServices] IConfiguration configuration,
        [FromServices] ChatGptProposalProvider chatGpt, [FromQuery] bool? webResearch, CancellationToken cancellationToken) =>
        await SuggestIssueRepairs([issueId], configuration, chatGpt, webResearch, cancellationToken);

    public sealed record ProposalScheduleSettings(bool Enabled, int BatchIntervalMinutes, int RequestIntervalSeconds, int GroupSize, bool? WebResearchEnabled = null);

    [HttpPost("issues/chatgpt-status")]
    public async Task<IActionResult> GetChatGptStatus([FromServices] ChatGptProposalProvider provider,
        [FromServices] IConfiguration configuration, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(403);
        Response.Headers.CacheControl = "no-store";
        await provider.VerifyIfDueAsync(configuration["MangaHub:IssueProposals:Model"], cancellationToken);
        return Ok(new { provider.Enabled, credentialPresent = provider.IsConnected, provider.LastVerifiedAt });
    }

    [HttpPost("issues/chatgpt-test")]
    public async Task<IActionResult> TestChatGptConnection([FromServices] ChatGptProposalProvider provider,
        [FromServices] IConfiguration configuration, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(403);
        var model = configuration["MangaHub:IssueProposals:Model"];
        if (!provider.IsConnected || string.IsNullOrWhiteSpace(model))
            return BadRequest("The ChatGPT credential file or model is missing.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await provider.DraftAsync(model, "Reply with only OK.", "Connection test.", timeout.Token);
            return Ok(new { provider.Enabled, credentialPresent = provider.IsConnected, provider.LastVerifiedAt });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, ex.StatusCode is { } status
                ? $"OpenAI returned HTTP {(int)status}. Check the model and account access."
                : "Could not reach OpenAI.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Text.Json.JsonException or KeyNotFoundException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return StatusCode(502, "ChatGPT verification failed. Check credential permissions, account access, and model configuration.");
        }
    }

    [HttpGet("issues/proposal-schedule")]
    public async Task<IActionResult> GetProposalSchedule([FromServices] ChatGptProposalProvider chatGpt, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return Forbid();
        var schedule = await db.IssueProposalSchedules.AsNoTracking().SingleAsync(cancellationToken);
        var drafts = await db.ScheduledIssueProposals.AsNoTracking().Where(row => row.ReviewedAt == null && row.Text != "")
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.Text, row.Action, row.SnoozeDays, row.ReplacementId, row.SourceUrl,
                row.ActionValue, row.ActionLanguage,
                title = db.AdminIssues.Where(issue => issue.Id == row.IssueId).Select(issue => issue.TitleSnapshot).FirstOrDefault(),
                manual = row.EvidenceKey.StartsWith("manual:") }).ToListAsync(cancellationToken);
        return Ok(new { schedule.Enabled, schedule.BatchIntervalMinutes, schedule.RequestIntervalSeconds,
            schedule.GroupSize, schedule.WebResearchEnabled, schedule.NextRunAt, running = schedule.RunId != null || schedule.PendingManualBatch != null, schedule.LastError, drafts,
            manualRunning = schedule.PendingManualBatch != null || (schedule.RunId != null && schedule.ManualBatchRunning),
            schedule.ManualStopRequested, chatGptConfigured = chatGpt.Enabled, chatGptCredentialPresent = chatGpt.IsConnected });
    }

    [HttpPost("issues/proposal-schedule/run")]
    public async Task<IActionResult> StartManualProposalBatch([FromBody] ManualIssueProposalBatch batch,
        [FromServices] IConfiguration configuration, [FromServices] ChatGptProposalProvider chatGpt, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return Forbid();
        if (batch.IssueIds is not { Length: <= 1000 } || batch.GroupSize is < 1 or > 10
            || batch.RequestIntervalSeconds is < 1 or > 120)
            return BadRequest("Invalid proposal batch settings.");
        if (string.IsNullOrWhiteSpace(configuration["MangaHub:IssueProposals:Model"]) ||
            (chatGpt.Enabled ? !chatGpt.IsConnected : string.IsNullOrWhiteSpace(configuration["MangaHub:IssueProposals:OpenAiApiKey"])))
            return BadRequest("Issue proposals are not configured.");

        var payload = System.Text.Json.JsonSerializer.Serialize(batch);
        var queued = await db.IssueProposalSchedules.Where(row => row.Id == 1 && row.RunId == null && row.PendingManualBatch == null
                && !db.ScheduledIssueProposals.Any(draft => draft.ReviewedAt == null))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.PendingManualBatch, payload)
                .SetProperty(row => row.LastError, ""), cancellationToken);
        if (queued != 1) return Conflict("A proposal batch is already pending or running, or drafts still need review.");
        return Accepted(new { accepted = true });
    }

    [HttpPost("issues/proposal-schedule/stop")]
    public async Task<IActionResult> StopManualProposalBatch(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return Forbid();
        await db.IssueProposalSchedules.Where(row => row.Id == 1 &&
                (row.PendingManualBatch != null || (row.RunId != null && row.ManualBatchRunning)))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.PendingManualBatch, (string?)null)
                .SetProperty(row => row.ManualStopRequested, row => row.RunId != null), cancellationToken);
        return NoContent();
    }

    [HttpPost("issues/proposal-schedule")]
    public async Task<IActionResult> UpdateProposalSchedule([FromBody] ProposalScheduleSettings settings, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return Forbid();
        if (settings.BatchIntervalMinutes is < 5 or > 43200 || settings.RequestIntervalSeconds is < 1 or > 3600
            || settings.GroupSize is < 1 or > 10) return BadRequest("Schedule settings are outside the allowed range.");
        var row = await db.IssueProposalSchedules.SingleAsync(cancellationToken);
        if (!row.Enabled && settings.Enabled) row.NextRunAt = DateTimeOffset.UtcNow;
        row.Enabled = settings.Enabled;
        row.BatchIntervalMinutes = settings.BatchIntervalMinutes;
        row.RequestIntervalSeconds = settings.RequestIntervalSeconds;
        row.GroupSize = settings.GroupSize;
        if (settings.WebResearchEnabled is { } webResearch) row.WebResearchEnabled = webResearch;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("issues/proposal-schedule/{id:guid}/review")]
    public async Task<IActionResult> ReviewProposal(Guid id, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return Forbid();
        var draft = await db.ScheduledIssueProposals.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id && row.Text != "", cancellationToken);
        if (draft is null) return NotFound();
        // All evidence markers for the displayed group share its group identifier.
        var now = DateTimeOffset.UtcNow;
        await db.ScheduledIssueProposals.Where(row => row.GroupId == draft.GroupId && row.ReviewedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ReviewedAt, now), cancellationToken);
        return NoContent();
    }

    [HttpPost("issues/proposal-schedule/{id:guid}/approve")]
    public async Task<IActionResult> ApproveProposal(Guid id, [FromServices] RemoteMaintenanceService maintenance,
        [FromServices] MangaHub.Core.Services.IMangaDexCatalogLookup lookup,
        [FromServices] MangaDexReleaseStallService stalls,
        [FromServices] MangaHub.Core.Services.IMangaUpdatesClient mangaUpdates,
        [FromServices] ILogger<AdminController> logger, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(403);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var draft = await db.ScheduledIssueProposals.FirstOrDefaultAsync(row => row.Id == id && row.ReviewedAt == null, cancellationToken);
        if (draft?.IssueId is not { } issueId || !IssueProposalActions.IsActionable(draft.Action))
            return BadRequest("This draft has no pending action to approve.");
        var issue = await issues.GetDetailsAsync(issueId, cancellationToken);
        if (issue is null || issue.Status != "open" || IssueProposalActions.Fingerprint(issue.MetadataJson) != draft.EvidenceSnapshot)
            return Conflict("The issue changed after this draft was created. Leave it unchanged or generate a new draft.");

        var recheck = false;
        if (draft.Action == "refresh_chapters")
        {
            if (!await maintenance.RefreshMangaDexTitleAsync(issue.SubjectId, cancellationToken))
                return Conflict("MangaDex did not return a successful chapter refresh. The issue remains open.");
            recheck = true;
        }
        else if (draft.Action == "snooze")
        {
            if (draft.SnoozeDays is not (7 or 30 or 90)) return BadRequest("Invalid snooze duration.");
            if (await issues.SnoozeAsync([issueId], draft.SnoozeDays.Value, cancellationToken) != 1)
                return Conflict("The issue could not be snoozed.");
        }
        else if (draft.Action == "mark_hiatus")
        {
            if (issue.Kind != "mangadex-release-stalled" || string.IsNullOrWhiteSpace(draft.SourceUrl))
                return BadRequest("This draft has no cited hiatus evidence.");
            var manga = await db.MangaEntries.SingleOrDefaultAsync(row => row.Id == issue.SubjectId, cancellationToken);
            if (manga is null || !string.Equals(manga.PublishingStatus, "ongoing", StringComparison.OrdinalIgnoreCase))
                return Conflict("The catalog status changed after this proposal was drafted.");
            manga.PublishingStatus = "hiatus";
            manga.PublishingHiatusOverride = true;
            manga.PublishingHiatusSource = draft.SourceUrl;
            manga.MangaDexNonHiatusSince = null;
            manga.UpdatedAt = DateTimeOffset.UtcNow;
            var issueRow = await db.AdminIssues.SingleAsync(row => row.Id == issueId, cancellationToken);
            issueRow.Status = "resolved";
            issueRow.ResolvedAt = manga.UpdatedAt;
            issueRow.UpdatedAt = manga.UpdatedAt;
            issueRow.ResolvedByUserId = user.Id;
            issueRow.ResolutionNote = "Admin approved a cited publication hiatus: " + draft.SourceUrl;
        }
        else if (draft.Action is "clear_hiatus" or "mark_complete" or "record_external" or "repair_mangaupdates" or "repair_mal")
        {
            var manga = await db.MangaEntries.SingleOrDefaultAsync(row => row.Id == issue.SubjectId, cancellationToken);
            if (manga is null) return Conflict("The catalog entry no longer exists.");
            if (draft.Action is "repair_mal" or "repair_mangaupdates")
            {
                if (string.IsNullOrWhiteSpace(draft.ActionValue) || !Guid.TryParse(manga.MangaDexId, out var currentId))
                    return Conflict("A linked MangaDex series and a proposed ID are required.");
                var identity = await lookup.GetIdentityAsync(currentId.ToString(), cancellationToken);
                if (identity is null || !Guid.TryParse(identity.Id, out var verifiedId) || verifiedId != currentId)
                    return Conflict("MangaDex did not confirm the current series.");
                if (draft.Action == "repair_mal")
                {
                    if (issue.Kind != "mangadex-identity-mismatch" || draft.ActionValue != identity.MyAnimeListId)
                        return Conflict("MangaDex did not confirm that MAL ID for this identity issue.");
                    using var evidence = System.Text.Json.JsonDocument.Parse(issue.MetadataJson);
                    if (!evidence.RootElement.TryGetProperty("expectedMyAnimeListId", out var expected)
                        || expected.GetString() != manga.MyAnimeListId
                        || !evidence.RootElement.TryGetProperty("linkedMangaDexId", out var linked)
                        || !Guid.TryParse(linked.GetString(), out var oldId) || oldId != currentId)
                        return Conflict("The catalog identity changed after this draft was created.");
                    var (match, error) = await issues.RepairMalIdentityAsync(user.Id, issueId, manga.MyAnimeListId, draft.ActionValue, cancellationToken);
                    if (match is null) return Conflict(error);
                }
                else
                {
                    if (draft.ActionValue != identity.MangaUpdatesId
                        || string.IsNullOrWhiteSpace(manga.MyAnimeListId) || identity.MyAnimeListId != manga.MyAnimeListId)
                        return Conflict("MangaDex did not confirm both the current MAL ID and the proposed MangaUpdates ID.");
                    if (draft.ActionValue == manga.MangaUpdatesId || await db.MangaEntries.AnyAsync(row => row.Id != manga.Id
                            && row.MangaUpdatesId == draft.ActionValue, cancellationToken))
                        return Conflict("The proposed MangaUpdates ID is already in use.");
                    var details = await mangaUpdates.GetSeriesAsync(draft.ActionValue, cancellationToken);
                    if (details is null || details.Id != draft.ActionValue)
                        return Conflict("MangaUpdates did not return the proposed series.");
                    manga.MangaUpdatesId = draft.ActionValue;
                    manga.MangaUpdatesLatestChapter = details.LatestChapter;
                    manga.MangaUpdatesLastSyncedAt = DateTimeOffset.UtcNow;
                    manga.MangaUpdatesStatus = details.Status;
                    manga.MangaUpdatesCompleted = details.Completed;
                    recheck = true;
                }
            }
            else if (draft.Action == "record_external")
            {
                if (issue.Kind != "mangadex-language-coverage" || !Uri.TryCreate(manga.FallbackReaderUrl, UriKind.Absolute, out var configured)
                    || !Uri.TryCreate(draft.SourceUrl, UriKind.Absolute, out var cited)
                    || cited.Scheme is not ("http" or "https") || !string.Equals(configured.IdnHost, cited.IdnHost, StringComparison.OrdinalIgnoreCase)
                    || !decimal.TryParse(draft.ActionValue, System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out var chapter)
                    || chapter is < 0 or > 9999999.999m || string.IsNullOrWhiteSpace(draft.ActionLanguage))
                    return Conflict("The citation must be a chapter listing on the configured external reader.");
                if (manga.CitedExternalLanguage == draft.ActionLanguage && manga.CitedExternalChapter > chapter)
                    return Conflict("The recorded chapter is already newer than this proposal.");
                manga.CitedExternalChapter = chapter;
                manga.CitedExternalLanguage = draft.ActionLanguage;
                manga.CitedExternalSource = cited.AbsoluteUri;
                manga.CitedExternalCheckedAt = DateTimeOffset.UtcNow;
                recheck = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(draft.SourceUrl) || (draft.Action == "clear_hiatus" && !manga.PublishingHiatusOverride)
                    || (draft.Action == "mark_complete" && manga.PublishingCompletionSource.Length > 0))
                    return Conflict("The current publication status no longer supports this proposal.");
                manga.PublishingHiatusOverride = false;
                manga.PublishingHiatusSource = "";
                manga.PublishingStatus = draft.Action == "mark_complete" ? "completed" : "ongoing";
                manga.PublishingCompletionSource = draft.Action == "mark_complete" ? draft.SourceUrl : "";
                manga.MangaDexNonHiatusSince = draft.Action == "clear_hiatus" ? DateTimeOffset.UtcNow : null;
                if (issue.Kind == "mangadex-release-stalled")
                {
                    var issueRow = await db.AdminIssues.SingleAsync(row => row.Id == issueId, cancellationToken);
                    issueRow.Status = "resolved";
                    issueRow.ResolvedAt = issueRow.UpdatedAt = DateTimeOffset.UtcNow;
                    issueRow.ResolvedByUserId = user.Id;
                    issueRow.ResolutionNote = $"Admin approved publication status {manga.PublishingStatus}: {draft.SourceUrl}";
                }
            }
            manga.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            if (!Guid.TryParse(draft.ReplacementId, out _) || string.IsNullOrWhiteSpace(draft.ExpectedMalId))
                return BadRequest("This draft has no verified replacement.");
            using var evidence = System.Text.Json.JsonDocument.Parse(issue.MetadataJson);
            if (!evidence.RootElement.TryGetProperty("linkedMangaDexId", out var linked)
                || linked.ValueKind != System.Text.Json.JsonValueKind.String
                || !string.Equals(linked.GetString(), issue.MangaDexId, StringComparison.OrdinalIgnoreCase))
                return Conflict("The current MangaDex link no longer matches the proposal evidence.");
            var (match, error) = await issues.RepairMangaDexIdentityAsync(user.Id, issueId, cancellationToken,
                confirmedMatchId: draft.ReplacementId, confirmedMalId: draft.ExpectedMalId);
            if (match is null) return Conflict(error);
        }

        draft.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        if (recheck)
        {
            try
            {
                await coverage.RunAsync(issue.SubjectId, cancellationToken);
                await stalls.RunAsync(issue.SubjectId, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Follow-up check failed for approved issue proposal {ProposalId}.", id);
                return Ok(new { applied = true, warning = "Proposal applied, but the follow-up check failed. Recheck the title from Issues." });
            }
        }
        return Ok(new { applied = true });
    }

    [HttpPost("issues/suggest-repairs")]
    public async Task<IActionResult> SuggestIssueRepairs([FromBody] Guid[] issueIds, [FromServices] IConfiguration configuration,
        [FromServices] ChatGptProposalProvider chatGpt, [FromQuery] bool? webResearch, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        if (issueIds is not { Length: > 0 and <= 10 }) return BadRequest("Send 1 to 10 issues per request.");
        var key = configuration["MangaHub:IssueProposals:OpenAiApiKey"];
        var model = configuration["MangaHub:IssueProposals:Model"];
        if (string.IsNullOrWhiteSpace(model) || (chatGpt.Enabled ? !chatGpt.IsConnected : string.IsNullOrWhiteSpace(key)))
            return BadRequest("Issue proposals are not configured.");

        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.IssueProposalSchedules.Where(row => row.Id == 1
                && row.PendingManualBatch == null && (row.RunId == null || row.LeaseUntil <= now)
                && !db.ScheduledIssueProposals.Any(draft => draft.ReviewedAt == null))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RunId, runId)
                .SetProperty(row => row.LeaseUntil, now.AddSeconds(90)), cancellationToken);
        if (claimed != 1) return Conflict("Another proposal request or scheduled batch is already running.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var stage = "collecting issue evidence";
        try
        {
            var packets = new List<object>();
            var sources = new List<(Guid Id, string Snapshot, string? Candidate, string? ExpectedMal, string Kind)>();
            var schedule = await db.IssueProposalSchedules.AsNoTracking().SingleAsync(timeout.Token);
            var research = false;
            foreach (var issueId in issueIds.Distinct())
            {
                var issue = await issues.GetDetailsAsync(issueId, timeout.Token);
                if (issue is null || issue.Status != "open"
                    || issue.Kind is not ("mangadex-release-stalled" or "mangadex-identity-mismatch" or "mangadex-language-coverage")) continue;
                var data = IssueProposalEvidence.ReadIssueFacts(issue.MetadataJson);
                var catalogEvidence = await IssueProposalEvidence.ReadCatalogAsync(db, issue.SubjectId, timeout.Token);
                research |= (webResearch ?? schedule.WebResearchEnabled) && issue.Kind is "mangadex-release-stalled" or "mangadex-language-coverage";
                string? candidate = null;
                var mal = data.GetValueOrDefault("expectedMyAnimeListId");
                if (issue.Kind == "mangadex-identity-mismatch" && !string.IsNullOrWhiteSpace(mal))
                {
                    var (match, _) = await issues.RepairMangaDexIdentityAsync(user.Id, issueId, timeout.Token, preview: true, confirmedMalId: mal);
                    candidate = match?.Id;
                }
                sources.Add((issueId, IssueProposalActions.Fingerprint(issue.MetadataJson), candidate, mal, issue.Kind));
                packets.Add(new { number = packets.Count + 1, issue.Kind, issue.Title, evidence = data,
                    catalog = catalogEvidence, verifiedReplacementId = candidate });
            }
            if (packets.Count == 0) return Ok(new { text = "No eligible open issues remain in this group." });
            var instructions = IssueProposalActions.Instructions + (research ? "\n" + IssueProposalResearch.Instructions : "");
            var input = System.Text.Json.JsonSerializer.Serialize(packets);
            stage = "waiting for the proposal provider";

            if (chatGpt.Enabled)
            {
                var chatGptDraft = await chatGpt.DraftAsync(model, instructions, input, timeout.Token, research);
                return await SaveManualProposalAsync(chatGptDraft, sources, cancellationToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new("Bearer", key);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { model, store = false,
                max_output_tokens = 400 + 160 * packets.Count, instructions, input,
                tools = research ? new[] { new { type = "web_search" } } : [] });
            using var client = httpClients.CreateClient();
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return StatusCode(502, $"The proposal provider returned HTTP {(int)response.StatusCode}.");
            using var result = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var text = string.Join("\n", result.RootElement.GetProperty("output").EnumerateArray()
                .Where(item => item.TryGetProperty("content", out var body) && body.ValueKind == System.Text.Json.JsonValueKind.Array)
                .SelectMany(item => item.GetProperty("content").EnumerateArray())
                .Where(part => part.TryGetProperty("text", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString()));
            if (string.IsNullOrWhiteSpace(text)) return StatusCode(502, "No proposal was returned.");
            return await SaveManualProposalAsync(research ? IssueProposalResearch.AttachSources(result.RootElement, text) : text, sources, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(504, $"The draft timed out while {stage}. Try fewer issues per request or turn off web research for this test.");
        }
        catch (ChatGptRequestException ex)
        {
            return StatusCode(502, ex.Message);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status)
        {
            return StatusCode(502, $"An upstream service returned HTTP {(int)status} while {stage}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return StatusCode(502, $"The draft failed while {stage}.");
        }
        finally
        {
            await db.IssueProposalSchedules.Where(row => row.Id == 1 && row.RunId == runId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RunId, (Guid?)null)
                    .SetProperty(row => row.LeaseUntil, (DateTimeOffset?)null), CancellationToken.None);
        }
    }

    private async Task<IActionResult> SaveManualProposalAsync(string text,
        List<(Guid Id, string Snapshot, string? Candidate, string? ExpectedMal, string Kind)> sources, CancellationToken cancellationToken)
    {
        var groupId = Guid.NewGuid();
        var drafts = sources.Select((source, index) => IssueProposalActions.Create(groupId, $"manual:{groupId:N}:{index}",
            source.Id, source.Snapshot, index + 1, text, source.Candidate, source.ExpectedMal, source.Kind)).ToList();
        db.ScheduledIssueProposals.AddRange(drafts);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { text = string.Join("\n\n", drafts.Select(draft => draft.Text)) });
    }

    [HttpGet("issues/health")]
    public async Task<IActionResult> IssueHealth(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await issues.GetHealthAsync(cancellationToken));
    }

    [HttpGet("issues/open-count")]
    public async Task<IActionResult> OpenIssueCount(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await issues.CountOpenAsync(cancellationToken));
    }

    [HttpGet("issues/{issueId:guid}")]
    public async Task<IActionResult> Issue(Guid issueId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.CanViewAdminPanels(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await issues.GetDetailsAsync(issueId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("issues/{issueId:guid}/resolve")]
    public async Task<IActionResult> ResolveIssue(Guid issueId, [FromBody] ResolveAdminIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.ResolveExternalReaderLinkAsync(user.Id, issueId, request, cancellationToken) ? NoContent() : BadRequest("Enter a valid replacement URL.");
    }

    [HttpPost("issues/{issueId:guid}/record-external-coverage")]
    public async Task<IActionResult> RecordExternalReaderCoverage(Guid issueId, [FromBody] RecordExternalReaderCoverageRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var mangaEntryId = await issues.RecordExternalReaderCoverageAsync(issueId, request, cancellationToken);
        if (mangaEntryId is null)
        {
            return BadRequest("Enter a valid non-negative chapter number for an issue with an external reader.");
        }

        await coverage.RunAsync(mangaEntryId, cancellationToken);
        return NoContent();
    }

    [HttpPost("issues/{issueId:guid}/resolve-cover")]
    public async Task<IActionResult> ResolveCoverIssue(Guid issueId, [FromBody] ResolveCoverImageIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.ResolveCoverImageAsync(user.Id, issueId, request, cancellationToken) ? NoContent() : BadRequest("Enter a valid image URL, or leave it blank to remove the cover.");
    }

    [HttpPost("issues/{issueId:guid}/reintegrate-mangadex")]
    public async Task<IActionResult> ReintegrateWithMangaDex(Guid issueId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var match = await issues.ReintegrateWithMangaDexAsync(user.Id, issueId, cancellationToken);
        return match is null
            ? NotFound("No automatic MangaDex match was found. Keep or replace the external link instead.")
            : Ok(match);
    }

    [HttpPost("issues/{issueId:guid}/repair-mal-identity")]
    public async Task<IActionResult> RepairMalIdentity(Guid issueId, [FromQuery] string expectedMalId, [FromQuery] string linkedMalId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await issues.RepairMalIdentityAsync(user.Id, issueId, expectedMalId, linkedMalId, cancellationToken);
        return result.Match is null ? BadRequest(result.Error) : Ok(result.Match);
    }

    [HttpPost("issues/{issueId:guid}/repair-mangadex-identity")]
    public async Task<IActionResult> RepairMangaDexIdentity(Guid issueId, CancellationToken cancellationToken, [FromQuery] bool preview = false, [FromQuery] string? confirmedMatchId = null, [FromQuery] string? confirmedMalId = null)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await issues.RepairMangaDexIdentityAsync(user.Id, issueId, cancellationToken, preview, confirmedMatchId, confirmedMalId);
        return result.Match is null ? BadRequest(result.Error) : Ok(result.Match);
    }

    [HttpPost("issues/{issueId:guid}/reintegrate-metadata")]
    public async Task<IActionResult> ReintegrateWithSelectedMetadata(Guid issueId, [FromBody] ReintegrateIssueWithMetadataRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await issues.ReintegrateWithSelectedMetadataAsync(user.Id, issueId, request, cancellationToken);
        return result is null ? BadRequest("Select a valid MyAnimeList result for this open external reader issue.") : Ok(result);
    }

    [HttpPost("issues/{issueId:guid}/resolve-catalog-registration")]
    public async Task<IActionResult> ResolveCatalogRegistration(Guid issueId, [FromBody] MangaEntryRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        if (!await issues.IsOpenCatalogRegistrationFailureAsync(issueId, cancellationToken))
        {
            return BadRequest("This catalog-registration issue is no longer open.");
        }

        try
        {
            var created = await catalog.CreateAsync(user.Id, request, cancellationToken);
            if (!await issues.ResolveCatalogRegistrationFailureAsync(user.Id, issueId, created.Title, cancellationToken))
            {
                return BadRequest("This catalog-registration issue is no longer open.");
            }

            return Ok(created);
        }
        catch (CatalogDuplicateIdentityException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (CatalogIdentityResolutionException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("issues/{issueId:guid}/merge-duplicates")]
    public async Task<IActionResult> MergeDuplicateCatalogIssue(Guid issueId, [FromBody] MergeDuplicateCatalogIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.MergeDuplicateCatalogIssueAsync(user.Id, issueId, request, cancellationToken)
            ? NoContent()
            : BadRequest("Choose a valid duplicate catalog entry to keep.");
    }

    [HttpPost("issues/batch")]
    public async Task<IActionResult> ReviewIssues([FromBody] BatchIssueReviewRequest request,
        [FromServices] MangaDexIdentityAuditService identity, [FromServices] MangaDexReleaseStallService stalls,
        CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        if (request.IssueIds is not { Length: > 0 and <= 100 }) return BadRequest();
        if (request.Action == "recheck_titles")
        {
            if (request.IssueIds.Length > 10) return BadRequest("Select at most ten titles.");
            var rows = await db.AdminIssues.AsNoTracking().Where(row => request.IssueIds.Contains(row.Id)
                && row.Status == "open" && row.SubjectType == "catalog-manga")
                .Select(row => new { row.Kind, row.SubjectId }).Distinct().ToListAsync(cancellationToken);
            var checkedCount = 0;
            foreach (var row in rows)
            {
                switch (row.Kind)
                {
                    case "mangadex-identity-mismatch": await identity.RunAsync(row.SubjectId, cancellationToken); break;
                    case "mangadex-release-stalled": await stalls.RunAsync(row.SubjectId, cancellationToken); break;
                    case "mangadex-language-coverage":
                    case "mangadex-translation-abandoned": await coverage.RunAsync(row.SubjectId, cancellationToken); break;
                    default: continue;
                }
                checkedCount++;
            }
            return Ok(checkedCount);
        }
        if (request.Action == "snooze" && request.Days is 7 or 30 or 90)
            return Ok(await issues.SnoozeAsync(request.IssueIds, request.Days, cancellationToken));
        if (request.Action is not ("dismiss" or "dismiss_current" or "use_external")) return BadRequest();
        var changed = 0;
        foreach (var id in request.IssueIds.Distinct())
        {
            var updated = request.Action == "use_external"
                ? await issues.MarkIntentionallyExternalAsync(user.Id, id, cancellationToken)
                : await issues.DismissAsync(user.Id, id, new("Dismissed during batch review."), cancellationToken,
                    confirmedCurrent: request.Action == "dismiss_current");
            if (updated) changed++;
        }
        return Ok(changed);
    }

    [HttpPost("issues/{issueId:guid}/dismiss")]
    public async Task<IActionResult> DismissIssue(Guid issueId, [FromBody] DismissAdminIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.DismissAsync(user.Id, issueId, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("issues/{issueId:guid}/reopen")]
    public async Task<IActionResult> ReopenIssue(Guid issueId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.ReopenAsync(user.Id, issueId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("operations/jobs")]
    public async Task<IActionResult> QueueJob([FromBody] QueueMaintenanceJobRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var job = await operations.QueueAsync(user.Id, request.Type, cancellationToken);
        return job is null ? BadRequest("Unsupported maintenance job.") : Accepted(job);
    }

    [HttpPut("users/{userId:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid userId, [FromBody] UpdateUserRoleRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }
        if (!CurrentUserService.IsAdmin(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }
        if (user.TemporaryAccountExpiresAt is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await admin.UpdateRoleAsync(user.Id, userId, request, cancellationToken);
        return result.Error switch
        {
            null => Ok(result.User),
            "not_found" => NotFound(),
            "bad_role" => BadRequest("Role must be admin or user."),
            "self_role_change" => BadRequest("You cannot change your own admin role."),
            "last_admin" => BadRequest("At least one admin must remain."),
            _ => BadRequest()
        };
    }

    [HttpDelete("users/{userId:guid}")]
    public async Task<IActionResult> DeleteTestAccount(Guid userId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user) || user.TemporaryAccountExpiresAt is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return await admin.DeleteTestAccountAsync(userId, cancellationToken) ? NoContent() : NotFound();
    }
}
