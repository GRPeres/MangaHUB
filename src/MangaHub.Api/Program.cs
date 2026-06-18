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

    var user = new MangaUser { Username = username, PasswordHash = passwordHasher.Hash(request.Password) };
    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);
    SetSessionCookie(response, tokens.CreateToken(user.Id, user.Username));
    return TypedResults.Created("/auth/me", new UserResponse(user.Id, user.Username));
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
    return TypedResults.Ok(new UserResponse(user.Id, user.Username));
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
    return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(new UserResponse(user.Id, user.Username));
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

    var query = db.MangaEntries.AsNoTracking().Where(x => x.UserId == user.Id);
    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.ReadingStatus == status);
    }

    var entries = await query
        .OrderBy(x => x.ReadingStatus)
        .ThenBy(x => x.Title)
        .Select(x => new MangaEntryResponse(
            x.Id,
            x.Title,
            x.Authors,
            x.Description,
            x.CoverUrl,
            x.OpenLibraryKey,
            x.FirstPublishYear,
            x.ReadingStatus,
            x.MangaDexUrl,
            x.MangaDexId,
            x.LocalSeriesId,
            x.Notes))
        .ToListAsync(cancellationToken);

    return TypedResults.Ok(entries);
});

app.MapPost("/api/manga", async Task<Results<Created<MangaEntryResponse>, UnauthorizedHttpResult>> (
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

    var manga = new MangaEntry
    {
        UserId = user.Id,
        Title = entry.Title.Trim(),
        Authors = entry.Authors.Trim(),
        Description = entry.Description.Trim(),
        CoverUrl = entry.CoverUrl.Trim(),
        OpenLibraryKey = entry.OpenLibraryKey.Trim(),
        FirstPublishYear = entry.FirstPublishYear,
        ReadingStatus = NormalizeShelfStatus(entry.ReadingStatus),
        MangaDexUrl = entry.MangaDexUrl.Trim(),
        MangaDexId = ExtractMangaDexId(entry.MangaDexUrl),
        LocalSeriesId = entry.LocalSeriesId,
        Notes = entry.Notes.Trim()
    };

    db.MangaEntries.Add(manga);
    await db.SaveChangesAsync(cancellationToken);
    return TypedResults.Created($"/api/manga/{manga.Id}", ToMangaEntryResponse(manga));
});

app.MapPut("/api/manga/{entryId:guid}", async Task<Results<Ok<MangaEntryResponse>, NotFound, UnauthorizedHttpResult>> (
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

    var manga = await db.MangaEntries.FirstOrDefaultAsync(x => x.Id == entryId && x.UserId == user.Id, cancellationToken);
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
    manga.ReadingStatus = NormalizeShelfStatus(entry.ReadingStatus);
    manga.MangaDexUrl = entry.MangaDexUrl.Trim();
    manga.MangaDexId = ExtractMangaDexId(entry.MangaDexUrl);
    manga.LocalSeriesId = entry.LocalSeriesId;
    manga.Notes = entry.Notes.Trim();
    manga.UpdatedAt = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync(cancellationToken);
    return TypedResults.Ok(ToMangaEntryResponse(manga));
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

    var entry = await db.MangaEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entryId && x.UserId == user.Id, cancellationToken);
    if (entry is null)
    {
        return TypedResults.NotFound();
    }

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

static MangaEntryResponse ToMangaEntryResponse(MangaEntry entry) =>
    new(
        entry.Id,
        entry.Title,
        entry.Authors,
        entry.Description,
        entry.CoverUrl,
        entry.OpenLibraryKey,
        entry.FirstPublishYear,
        entry.ReadingStatus,
        entry.MangaDexUrl,
        entry.MangaDexId,
        entry.LocalSeriesId,
        entry.Notes);

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
        CREATE TABLE IF NOT EXISTS manga_entries (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "Title" character varying(255) NOT NULL,
            "Authors" text NOT NULL,
            "Description" text NOT NULL,
            "CoverUrl" text NOT NULL,
            "OpenLibraryKey" text NOT NULL,
            "FirstPublishYear" integer NULL,
            "ReadingStatus" character varying(40) NOT NULL,
            "MangaDexUrl" text NOT NULL,
            "MangaDexId" character varying(80) NOT NULL,
            "LocalSeriesId" uuid NULL,
            "Notes" text NOT NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_manga_entries_UserId_OpenLibraryKey" ON manga_entries ("UserId", "OpenLibraryKey");
        """);
}
