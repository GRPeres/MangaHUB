using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using MangaHub.Infrastructure.Sources;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddMangaHubInfrastructure(builder.Configuration);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration["FrontendOrigin"] ?? "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 8;
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureMangaEntryTableAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseRateLimiter();
app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/auth/register", async Task<Results<Created<UserResponse>, Conflict<string>>> (
    AuthRequest request,
    MangaHubDbContext db,
    IPasswordHasher passwordHasher,
    ISessionTokenService tokens,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    var username = request.Username.Trim();
    if (await db.Users.AnyAsync(x => x.Username == username, cancellationToken))
    {
        return TypedResults.Conflict("Username already exists.");
    }

    var isFirstUser = !await db.Users.AnyAsync(cancellationToken);
    var user = new MangaUser
    {
        Username = username,
        PasswordHash = passwordHasher.Hash(request.Password),
        Role = isFirstUser ? "admin" : "user"
    };
    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);
    SetSessionCookie(response, tokens.CreateToken(user.Id, user.Username));
    return TypedResults.Created("/auth/me", new UserResponse(user.Id, user.Username, user.Role));
});

app.MapPost("/auth/login", async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> (
    AuthRequest request,
    MangaHubDbContext db,
    IPasswordHasher passwordHasher,
    ISessionTokenService tokens,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    var username = request.Username.Trim();
    var user = await db.Users.FirstOrDefaultAsync(x => x.Username == username, cancellationToken);
    if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
    {
        return TypedResults.Unauthorized();
    }

    SetSessionCookie(response, tokens.CreateToken(user.Id, user.Username));
    return TypedResults.Ok(new UserResponse(user.Id, user.Username, user.Role));
}).RequireRateLimiting("login");

app.MapPost("/auth/logout", (HttpResponse response) =>
{
    response.Cookies.Delete("mangahub_session");
    return Results.Ok(new { status = "ok" });
});

app.MapGet("/auth/me", async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(new UserResponse(user.Id, user.Username, user.Role));
});

app.MapGet("/api/openlibrary/search", async Task<Ok<List<OpenLibraryResult>>> (
    IOpenLibraryClient openLibrary,
    string q,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return TypedResults.Ok(new List<OpenLibraryResult>());
    }

    var results = await openLibrary.SearchAsync(q, cancellationToken);
    return TypedResults.Ok(results
        .Select(x => new OpenLibraryResult(x.Key, x.Title, x.Authors, x.CoverUrl, x.FirstPublishYear, x.Category, x.Description))
        .ToList());
});

app.MapGet("/api/manga", async Task<Results<Ok<List<MangaEntryResponse>>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    string? status,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    var query = db.UserMangaEntries.AsNoTracking()
        .Include(x => x.MangaEntry)
        .Where(x => x.UserId == user.Id);
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.ReadingStatus == status);
    }

    var entries = await query
        .OrderBy(x => x.ReadingStatus)
        .ThenBy(x => x.MangaEntry!.Title)
        .Select(x => new MangaEntryResponse(
            x.MangaEntry!.Id,
            x.MangaEntry.Title,
            x.MangaEntry.Authors,
            x.MangaEntry.Category,
            x.MangaEntry.Description,
            x.MangaEntry.CoverUrl,
            x.MangaEntry.OpenLibraryKey,
            x.MangaEntry.FirstPublishYear,
            x.ReadingStatus,
            x.MangaEntry.MangaDexUrl,
            x.MangaEntry.MangaDexId,
            x.MangaEntry.LocalSeriesId,
            x.CurrentChapter,
            x.Score,
            x.Category,
            x.Summary,
            x.Notes))
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(entries);
});

app.MapGet("/api/catalog", async Task<Results<Ok<List<CatalogMangaResponse>>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    string? q,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    var shelfIds = db.UserMangaEntries
        .Where(x => x.UserId == user.Id)
        .Select(x => x.MangaEntryId);

    var query = db.MangaEntries.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q))
    {
        query = query.Where(x => EF.Functions.ILike(x.Title, $"%{q}%") || EF.Functions.ILike(x.Authors, $"%{q}%"));
    }

    var entries = await query
        .OrderBy(x => x.Title)
        .Select(x => new CatalogMangaResponse(
            x.Id,
            x.Title,
            x.Authors,
            x.Category,
            x.Description,
            x.CoverUrl,
            x.OpenLibraryKey,
            x.FirstPublishYear,
            x.MangaDexUrl,
            x.MangaDexId,
            x.LocalSeriesId,
            shelfIds.Contains(x.Id)))
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(entries);
});

app.MapPost("/api/catalog", async Task<Results<Created<CatalogMangaResponse>, UnauthorizedHttpResult, ForbidHttpResult>> (
    MangaEntryRequest entry,
    HttpRequest request,
    MangaHubDbContext db,
    IOpenLibraryClient openLibrary,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }
    if (!IsAdmin(user))
    {
        return TypedResults.Forbid();
    }

    var details = string.IsNullOrWhiteSpace(entry.OpenLibraryKey)
        ? null
        : await openLibrary.GetWorkAsync(entry.OpenLibraryKey.Trim(), cancellationToken);

    var manga = new MangaEntry
    {
        CreatedByUserId = user.Id,
        Title = entry.Title.Trim(),
        Authors = entry.Authors.Trim(),
        Category = FirstNonEmpty(entry.Category, details?.Category),
        Description = FirstNonEmpty(entry.Description, details?.Description),
        CoverUrl = entry.CoverUrl.Trim(),
        OpenLibraryKey = entry.OpenLibraryKey.Trim(),
        FirstPublishYear = entry.FirstPublishYear,
        MangaDexUrl = entry.MangaDexUrl.Trim(),
        MangaDexId = ExtractMangaDexId(entry.MangaDexUrl),
        LocalSeriesId = entry.LocalSeriesId
    };

    db.MangaEntries.Add(manga);
    await db.SaveChangesAsync(cancellationToken);
    return TypedResults.Created($"/api/catalog/{manga.Id}", ToCatalogMangaResponse(manga, false));
});

app.MapPut("/api/catalog/{entryId:guid}", async Task<Results<Ok<CatalogMangaResponse>, NotFound, UnauthorizedHttpResult, ForbidHttpResult>> (
    Guid entryId,
    MangaEntryRequest entry,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }
    if (!IsAdmin(user))
    {
        return TypedResults.Forbid();
    }

    var manga = await db.MangaEntries.FirstOrDefaultAsync(x => x.Id == entryId, cancellationToken);
    if (manga is null)
    {
        return TypedResults.NotFound();
    }

    manga.Title = entry.Title.Trim();
    manga.Authors = entry.Authors.Trim();
    manga.Category = entry.Category.Trim();
    manga.Description = entry.Description.Trim();
    manga.CoverUrl = entry.CoverUrl.Trim();
    manga.OpenLibraryKey = entry.OpenLibraryKey.Trim();
    manga.FirstPublishYear = entry.FirstPublishYear;
    manga.MangaDexUrl = entry.MangaDexUrl.Trim();
    manga.MangaDexId = ExtractMangaDexId(entry.MangaDexUrl);
    manga.LocalSeriesId = entry.LocalSeriesId;
    manga.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync(cancellationToken);
    var isInShelf = await db.UserMangaEntries.AnyAsync(x => x.UserId == user.Id && x.MangaEntryId == manga.Id, cancellationToken);
    return TypedResults.Ok(ToCatalogMangaResponse(manga, isInShelf));
});

app.MapPost("/api/shelf", async Task<Results<Created<MangaEntryResponse>, Ok<MangaEntryResponse>, NotFound, UnauthorizedHttpResult>> (
    AddToShelfRequest shelfRequest,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    var manga = await db.MangaEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == shelfRequest.MangaEntryId, cancellationToken);
    if (manga is null)
    {
        return TypedResults.NotFound();
    }

    var shelf = await db.UserMangaEntries.FirstOrDefaultAsync(x => x.UserId == user.Id && x.MangaEntryId == manga.Id, cancellationToken);
    if (shelf is null)
    {
        shelf = new UserMangaEntry
        {
            UserId = user.Id,
            MangaEntryId = manga.Id
        };
        ApplyShelfRequest(shelf, shelfRequest, manga);
        db.UserMangaEntries.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/manga/{manga.Id}", ToMangaEntryResponse(manga, shelf));
    }

    ApplyShelfRequest(shelf, shelfRequest, manga);
    shelf.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return TypedResults.Ok(ToMangaEntryResponse(manga, shelf));
});

app.MapPost("/api/shelf/import", async Task<Results<Ok<ShelfImportResponse>, UnauthorizedHttpResult, BadRequest<string>>> (
    ShelfImportRequest import,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(import.CsvText))
    {
        return TypedResults.BadRequest("CSV text is required.");
    }

    var rows = ParseCsv(import.CsvText);
    if (rows.Count == 0)
    {
        return TypedResults.BadRequest("CSV has no data rows.");
    }

    var headers = rows[0].Select(NormalizeHeader).ToList();
    var messages = new List<string>();
    var imported = 0;
    var createdCatalog = 0;
    var updatedShelf = 0;
    var skipped = 0;
    var canCreateCatalog = IsAdmin(user) && import.CreateMissingCatalogEntries;

    foreach (var row in rows.Skip(1))
    {
        var values = RowToDictionary(headers, row);
        var title = FirstValue(values, "name+link", "name", "title", "manga", "series");
        var link = FirstValue(values, "link", "url", "mangadexurl", "mangadex", "sourceurl");
        if (Uri.TryCreate(title, UriKind.Absolute, out _))
        {
            link = title;
            title = FirstValue(values, "title", "name", "manga", "series");
        }

        title = CleanTitle(title);
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(link))
        {
            skipped++;
            continue;
        }

        var mangaDexId = ExtractMangaDexId(link);
        MangaEntry? manga = null;
        if (!string.IsNullOrWhiteSpace(mangaDexId))
        {
            manga = await db.MangaEntries.FirstOrDefaultAsync(x => x.MangaDexId == mangaDexId, cancellationToken);
        }

        if (manga is null && !string.IsNullOrWhiteSpace(link))
        {
            manga = await db.MangaEntries.FirstOrDefaultAsync(x => x.MangaDexUrl == link, cancellationToken);
        }

        if (manga is null && !string.IsNullOrWhiteSpace(title))
        {
            manga = await db.MangaEntries.FirstOrDefaultAsync(x => EF.Functions.ILike(x.Title, title), cancellationToken);
        }

        if (manga is null)
        {
            if (!canCreateCatalog)
            {
                skipped++;
                messages.Add($"Skipped '{title}': not found in catalog.");
                continue;
            }

            manga = new MangaEntry
            {
                CreatedByUserId = user.Id,
                Title = string.IsNullOrWhiteSpace(title) ? link : title,
                Category = FirstValue(values, "tipo", "type", "category", "genre").Trim(),
                Description = FirstValue(values, "summary", "description").Trim(),
                MangaDexUrl = link.Trim(),
                MangaDexId = mangaDexId
            };
            db.MangaEntries.Add(manga);
            createdCatalog++;
        }
        else if (!string.IsNullOrWhiteSpace(link) && string.IsNullOrWhiteSpace(manga.MangaDexUrl))
        {
            manga.MangaDexUrl = link.Trim();
            manga.MangaDexId = mangaDexId;
            manga.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var shelf = await db.UserMangaEntries.FirstOrDefaultAsync(x => x.UserId == user.Id && x.MangaEntryId == manga.Id, cancellationToken);
        if (shelf is null)
        {
            shelf = new UserMangaEntry
            {
                UserId = user.Id,
                MangaEntryId = manga.Id
            };
            db.UserMangaEntries.Add(shelf);
        }
        else
        {
            updatedShelf++;
        }

        shelf.ReadingStatus = NormalizeShelfStatus(FirstValue(values, "status", "readingstatus"));
        shelf.CurrentChapter = FirstValue(values, "chapter", "currentchapter", "chapters").Trim();
        shelf.Score = ParseScore(FirstValue(values, "rating", "score"));
        shelf.Category = FirstValue(values, "tipo", "type", "category", "genre").Trim();
        shelf.Summary = FirstValue(values, "summary", "description").Trim();
        shelf.Notes = FirstValue(values, "notes", "note").Trim();
        shelf.UpdatedAt = DateTimeOffset.UtcNow;
        imported++;
    }

    await db.SaveChangesAsync(cancellationToken);
    if (!canCreateCatalog && import.CreateMissingCatalogEntries)
    {
        messages.Add("Missing catalog entries were not created because only admins can create catalog manga.");
    }

    return TypedResults.Ok(new ShelfImportResponse(imported, createdCatalog, updatedShelf, skipped, messages.Take(20).ToList()));
});

app.MapGet("/api/manga/{entryId:guid}/read-options", async Task<Results<Ok<object>, NotFound, UnauthorizedHttpResult>> (
    Guid entryId,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    var shelf = await db.UserMangaEntries.AsNoTracking()
        .Include(x => x.MangaEntry)
        .FirstOrDefaultAsync(x => x.MangaEntryId == entryId && x.UserId == user.Id, cancellationToken);
    if (shelf?.MangaEntry is null)
    {
        return TypedResults.NotFound();
    }
    var entry = shelf.MangaEntry;

    var localFirstChapter = entry.LocalSeriesId is null
        ? null
        : await db.Chapters.AsNoTracking()
            .Where(x => x.SeriesId == entry.LocalSeriesId)
            .OrderBy(x => x.ChapterNumber)
            .Select(x => new { x.Id, x.PageCount })
            .FirstOrDefaultAsync(cancellationToken);

    return TypedResults.Ok<object>(new
    {
        entry.Id,
        entry.Title,
        HasMangaDex = !string.IsNullOrWhiteSpace(entry.MangaDexUrl),
        entry.MangaDexUrl,
        HasLocal = localFirstChapter is not null,
        LocalReaderUrl = localFirstChapter is null ? "" : $"/reader/{localFirstChapter.Id}/{localFirstChapter.PageCount}"
    });
});

app.MapPost("/api/library/scan", async Task<Results<Ok<LibraryScanResult>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    ILibraryScanner scanner,
    CancellationToken cancellationToken) =>
{
    if (await GetCurrentUserAsync(request, db, tokens, cancellationToken) is null)
    {
        return TypedResults.Unauthorized();
    }

    return TypedResults.Ok(await scanner.ScanAsync(cancellationToken));
});

app.MapGet("/api/series", async Task<Results<Ok<List<SeriesResponse>>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    string? title,
    string? source,
    string? status,
    CancellationToken cancellationToken) =>
{
    if (await GetCurrentUserAsync(request, db, tokens, cancellationToken) is null)
    {
        return TypedResults.Unauthorized();
    }

    var query = db.Series.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(title))
    {
        query = query.Where(x => EF.Functions.ILike(x.Title, $"%{title}%"));
    }
    if (!string.IsNullOrWhiteSpace(source))
    {
        query = query.Where(x => x.Source == source);
    }
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.Status == status);
    }

    var series = await query.OrderBy(x => x.Title)
        .Select(x => new SeriesResponse(x.Id, x.Title, x.Description, x.CoverUrl, x.Status, x.Source, x.ExternalId))
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(series);
});

app.MapGet("/api/series/search", async Task<Results<Ok<List<object>>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    MangaSourceRegistry sources,
    string q,
    CancellationToken cancellationToken) =>
{
    if (await GetCurrentUserAsync(request, db, tokens, cancellationToken) is null)
    {
        return TypedResults.Unauthorized();
    }

    var local = await db.Series.AsNoTracking()
        .Where(x => EF.Functions.ILike(x.Title, $"%{q}%"))
        .Take(25)
        .Select(x => new { x.Id, x.Title, x.Description, x.CoverUrl, x.Status, x.Source })
        .ToListAsync(cancellationToken);

    var results = local.Cast<object>().ToList();
    foreach (var source in sources.All.Where(x => x.Name != "local"))
    {
        results.AddRange(await source.SearchAsync(q, cancellationToken));
    }

    return TypedResults.Ok(results);
});

app.MapGet("/api/series/{seriesId:guid}", async Task<Results<Ok<SeriesResponse>, NotFound, UnauthorizedHttpResult>> (
    Guid seriesId,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    if (await GetCurrentUserAsync(request, db, tokens, cancellationToken) is null)
    {
        return TypedResults.Unauthorized();
    }

    var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(x => x.Id == seriesId, cancellationToken);
    return series is null
        ? TypedResults.NotFound()
        : TypedResults.Ok(new SeriesResponse(series.Id, series.Title, series.Description, series.CoverUrl, series.Status, series.Source, series.ExternalId));
});

app.MapGet("/api/series/{seriesId:guid}/chapters", async Task<Results<Ok<List<ChapterResponse>>, UnauthorizedHttpResult>> (
    Guid seriesId,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    if (await GetCurrentUserAsync(request, db, tokens, cancellationToken) is null)
    {
        return TypedResults.Unauthorized();
    }

    var chapters = await db.Chapters.AsNoTracking()
        .Where(x => x.SeriesId == seriesId)
        .OrderBy(x => x.ChapterNumber)
        .Select(x => new ChapterResponse(x.Id, x.SeriesId, x.ChapterNumber, x.Title, x.PageCount))
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(chapters);
});

app.MapGet("/api/read/{chapterId:guid}/pages/{pageIndex:int}", async Task<Results<FileContentHttpResult, NotFound, UnauthorizedHttpResult>> (
    Guid chapterId,
    int pageIndex,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    IArchiveReader archives,
    IOptions<MangaHubOptions> options,
    CancellationToken cancellationToken) =>
{
    if (await GetCurrentUserAsync(request, db, tokens, cancellationToken) is null)
    {
        return TypedResults.Unauthorized();
    }

    var chapter = await db.Chapters.Include(x => x.Series).FirstOrDefaultAsync(x => x.Id == chapterId, cancellationToken);
    if (chapter?.Series?.Source != "local")
    {
        return TypedResults.NotFound();
    }

    var root = Path.GetFullPath(options.Value.LibraryPath);
    var archivePath = Path.GetFullPath(Path.Combine(root, chapter.SourceId));
    if (!archivePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return TypedResults.NotFound();
    }

    var page = await archives.ReadPageAsync(archivePath, pageIndex, cancellationToken);
    return page is null ? TypedResults.NotFound() : TypedResults.File(page.Bytes, page.ContentType);
});

app.MapPost("/api/progress", async Task<Results<Ok<ProgressResponse>, UnauthorizedHttpResult>> (
    ProgressRequest progress,
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    var existing = await db.ReadingProgress.FirstOrDefaultAsync(x => x.UserId == user.Id && x.SeriesId == progress.SeriesId, cancellationToken);
    if (existing is null)
    {
        db.ReadingProgress.Add(new ReadingProgress
        {
            UserId = user.Id,
            SeriesId = progress.SeriesId,
            ChapterId = progress.ChapterId,
            Page = progress.Page
        });
    }
    else
    {
        existing.ChapterId = progress.ChapterId;
        existing.Page = progress.Page;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }

    await db.SaveChangesAsync(cancellationToken);
    return TypedResults.Ok(new ProgressResponse(progress.SeriesId, progress.ChapterId, progress.Page));
});

app.MapGet("/api/progress", async Task<Results<Ok<List<ProgressResponse>>, UnauthorizedHttpResult>> (
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await GetCurrentUserAsync(request, db, tokens, cancellationToken);
    if (user is null)
    {
        return TypedResults.Unauthorized();
    }

    var progress = await db.ReadingProgress.AsNoTracking()
        .Where(x => x.UserId == user.Id)
        .OrderByDescending(x => x.UpdatedAt)
        .Select(x => new ProgressResponse(x.SeriesId, x.ChapterId, x.Page))
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(progress);
});

app.Run();

static void SetSessionCookie(HttpResponse response, string token)
{
    response.Cookies.Append("mangahub_session", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = false,
        MaxAge = TimeSpan.FromDays(7)
    });
}

static async Task<MangaUser?> GetCurrentUserAsync(
    HttpRequest request,
    MangaHubDbContext db,
    ISessionTokenService tokens,
    CancellationToken cancellationToken)
{
    if (!request.Cookies.TryGetValue("mangahub_session", out var token))
    {
        return null;
    }

    var userId = tokens.ReadUserId(token);
    return userId is null ? null : await db.Users.FindAsync([userId.Value], cancellationToken);
}

static bool IsAdmin(MangaUser user) => string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase);

static MangaEntryResponse ToMangaEntryResponse(MangaEntry entry, UserMangaEntry shelf) =>
    new(
        entry.Id,
        entry.Title,
        entry.Authors,
        entry.Category,
        entry.Description,
        entry.CoverUrl,
        entry.OpenLibraryKey,
        entry.FirstPublishYear,
        shelf.ReadingStatus,
        entry.MangaDexUrl,
        entry.MangaDexId,
        entry.LocalSeriesId,
        shelf.CurrentChapter,
        shelf.Score,
        shelf.Category,
        shelf.Summary,
        shelf.Notes);

static CatalogMangaResponse ToCatalogMangaResponse(MangaEntry entry, bool isInMyShelf) =>
    new(
        entry.Id,
        entry.Title,
        entry.Authors,
        entry.Category,
        entry.Description,
        entry.CoverUrl,
        entry.OpenLibraryKey,
        entry.FirstPublishYear,
        entry.MangaDexUrl,
        entry.MangaDexId,
        entry.LocalSeriesId,
        isInMyShelf);

static string NormalizeShelfStatus(string status)
{
    var normalized = status.Trim().ToLowerInvariant();
    return normalized switch
    {
        "finished" or "complete" or "completed" => "done",
        "ongoing" or "current" or "reading" => "reading",
        "hiatus" or "paused" => "paused",
        "to read" or "plan to read" or "planned" => "planned",
        "dropped" => "dropped",
        "done" => "done",
        _ => "planned"
    };
}

static void ApplyShelfRequest(UserMangaEntry shelf, AddToShelfRequest request, MangaEntry? catalogEntry = null)
{
    shelf.ReadingStatus = NormalizeShelfStatus(request.ReadingStatus);
    shelf.CurrentChapter = request.CurrentChapter.Trim();
    shelf.Score = NormalizeScore(request.Score);
    shelf.Category = FirstNonEmpty(request.Category, catalogEntry?.Category);
    shelf.Summary = FirstNonEmpty(request.Summary, catalogEntry?.Description);
    shelf.Notes = request.Notes.Trim();
}

static int? NormalizeScore(int? score) => score is null or <= 0 ? null : Math.Clamp(score.Value, 1, 5);

static int? ParseScore(string score)
{
    if (string.IsNullOrWhiteSpace(score))
    {
        return null;
    }

    var normalized = score.Trim().Replace(',', '.');
    if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
    {
        return null;
    }

    return Math.Clamp((int)Math.Round(parsed, MidpointRounding.AwayFromZero), 1, 5);
}

static string FirstNonEmpty(params string?[] values) =>
    values.Select(x => x?.Trim() ?? "").FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

static string ExtractMangaDexId(string urlOrId)
{
    var value = urlOrId.Trim();
    if (Guid.TryParse(value, out var id))
    {
        return id.ToString();
    }

    var marker = "/title/";
    var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
        return "";
    }

    var afterTitle = value[(index + marker.Length)..];
    var segment = afterTitle.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    return Guid.TryParse(segment, out var parsed) ? parsed.ToString() : "";
}

static string NormalizeHeader(string header)
{
    var builder = new StringBuilder();
    foreach (var ch in header.Trim().ToLowerInvariant())
    {
        if (char.IsLetterOrDigit(ch) || ch == '+')
        {
            builder.Append(ch);
        }
    }

    return builder.ToString();
}

static Dictionary<string, string> RowToDictionary(List<string> headers, List<string> row)
{
    var values = new Dictionary<string, string>();
    for (var i = 0; i < headers.Count; i++)
    {
        values[headers[i]] = i < row.Count ? row[i] : "";
    }

    return values;
}

static string FirstValue(Dictionary<string, string> values, params string[] keys)
{
    foreach (var key in keys)
    {
        if (values.TryGetValue(NormalizeHeader(key), out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return "";
}

static string CleanTitle(string title)
{
    var cleaned = title.Trim();
    if (cleaned.Length > 3 && cleaned.Length % 2 == 0)
    {
        var half = cleaned.Length / 2;
        if (string.Equals(cleaned[..half], cleaned[half..], StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..half].Trim();
        }
    }

    return cleaned;
}

static List<List<string>> ParseCsv(string csv)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var field = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < csv.Length; i++)
    {
        var ch = csv[i];
        if (ch == '"')
        {
            if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
            {
                field.Append('"');
                i++;
            }
            else
            {
                inQuotes = !inQuotes;
            }
            continue;
        }

        if (ch == ',' && !inQuotes)
        {
            row.Add(field.ToString());
            field.Clear();
            continue;
        }

        if ((ch == '\n' || ch == '\r') && !inQuotes)
        {
            if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
            {
                i++;
            }

            row.Add(field.ToString());
            field.Clear();
            if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
            {
                rows.Add(row);
            }
            row = [];
            continue;
        }

        field.Append(ch);
    }

    row.Add(field.ToString());
    if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
    {
        rows.Add(row);
    }

    return rows;
}

static async Task EnsureMangaEntryTableAsync(MangaHubDbContext db)
{
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE users ADD COLUMN IF NOT EXISTS "Role" character varying(40) NOT NULL DEFAULT 'user';

        UPDATE users
        SET "Role" = 'admin'
        WHERE "Id" = (
            SELECT "Id"
            FROM users
            ORDER BY "CreatedAt" ASC
            LIMIT 1
        )
        AND NOT EXISTS (SELECT 1 FROM users WHERE "Role" = 'admin');

        CREATE TABLE IF NOT EXISTS manga_entries (
            "Id" uuid PRIMARY KEY,
            "Title" character varying(255) NOT NULL,
            "Authors" text NOT NULL,
            "Category" character varying(120) NOT NULL DEFAULT '',
            "Description" text NOT NULL,
            "CoverUrl" text NOT NULL,
            "OpenLibraryKey" text NOT NULL,
            "FirstPublishYear" integer NULL,
            "MangaDexUrl" text NOT NULL,
            "MangaDexId" character varying(80) NOT NULL,
            "LocalSeriesId" uuid NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );

        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "Category" character varying(120) NOT NULL DEFAULT '';
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "UserId" uuid NULL;
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "ReadingStatus" character varying(40) NULL;
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "Notes" text NULL;

        CREATE TABLE IF NOT EXISTS user_manga_entries (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "MangaEntryId" uuid NOT NULL,
            "ReadingStatus" character varying(40) NOT NULL,
            "CurrentChapter" character varying(40) NOT NULL DEFAULT '',
            "Score" integer NULL,
            "Category" character varying(120) NOT NULL DEFAULT '',
            "Summary" text NOT NULL DEFAULT '',
            "Notes" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );

        ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "CurrentChapter" character varying(40) NOT NULL DEFAULT '';
        ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "Score" integer NULL;
        ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "Category" character varying(120) NOT NULL DEFAULT '';
        ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "Summary" text NOT NULL DEFAULT '';

        INSERT INTO user_manga_entries ("Id", "UserId", "MangaEntryId", "ReadingStatus", "Notes", "CreatedAt", "UpdatedAt")
        SELECT gen_random_uuid(),
               "UserId",
               "Id",
               COALESCE("ReadingStatus", 'planned'),
               COALESCE("Notes", ''),
               "CreatedAt",
               "UpdatedAt"
        FROM manga_entries
        WHERE "UserId" IS NOT NULL
          AND NOT EXISTS (
              SELECT 1
              FROM user_manga_entries
              WHERE user_manga_entries."UserId" = manga_entries."UserId"
                AND user_manga_entries."MangaEntryId" = manga_entries."Id"
          );

        UPDATE manga_entries
        SET "CreatedByUserId" = "UserId"
        WHERE "CreatedByUserId" IS NULL AND "UserId" IS NOT NULL;

        CREATE INDEX IF NOT EXISTS "IX_manga_entries_OpenLibraryKey" ON manga_entries ("OpenLibraryKey");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_manga_entries_UserId_MangaEntryId" ON user_manga_entries ("UserId", "MangaEntryId");
        """);
}
