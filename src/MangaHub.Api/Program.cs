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
        .Select(x => new OpenLibraryResult(x.Key, x.Title, x.Authors, x.CoverUrl, x.FirstPublishYear))
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
            x.MangaEntry.Description,
            x.MangaEntry.CoverUrl,
            x.MangaEntry.OpenLibraryKey,
            x.MangaEntry.FirstPublishYear,
            x.ReadingStatus,
            x.MangaEntry.MangaDexUrl,
            x.MangaEntry.MangaDexId,
            x.MangaEntry.LocalSeriesId,
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

    var manga = new MangaEntry
    {
        CreatedByUserId = user.Id,
        Title = entry.Title.Trim(),
        Authors = entry.Authors.Trim(),
        Description = entry.Description.Trim(),
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
            MangaEntryId = manga.Id,
            ReadingStatus = NormalizeShelfStatus(shelfRequest.ReadingStatus),
            Notes = shelfRequest.Notes.Trim()
        };
        db.UserMangaEntries.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/manga/{manga.Id}", ToMangaEntryResponse(manga, shelf));
    }

    shelf.ReadingStatus = NormalizeShelfStatus(shelfRequest.ReadingStatus);
    shelf.Notes = shelfRequest.Notes.Trim();
    shelf.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return TypedResults.Ok(ToMangaEntryResponse(manga, shelf));
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
        entry.Description,
        entry.CoverUrl,
        entry.OpenLibraryKey,
        entry.FirstPublishYear,
        shelf.ReadingStatus,
        entry.MangaDexUrl,
        entry.MangaDexId,
        entry.LocalSeriesId,
        shelf.Notes);

static CatalogMangaResponse ToCatalogMangaResponse(MangaEntry entry, bool isInMyShelf) =>
    new(
        entry.Id,
        entry.Title,
        entry.Authors,
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
    return normalized is "reading" or "done" or "planned" or "dropped" ? normalized : "planned";
}

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
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "UserId" uuid NULL;
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "ReadingStatus" character varying(40) NULL;
        ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "Notes" text NULL;

        CREATE TABLE IF NOT EXISTS user_manga_entries (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "MangaEntryId" uuid NOT NULL,
            "ReadingStatus" character varying(40) NOT NULL,
            "Notes" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );

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
