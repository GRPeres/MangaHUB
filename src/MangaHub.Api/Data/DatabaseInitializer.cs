using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Data;

public sealed class DatabaseInitializer(MangaHubDbContext db)
{
    public async Task InitializeAsync()
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureMangaEntryTableAsync();
    }

    private async Task EnsureMangaEntryTableAsync()
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
                "MetadataSource" character varying(40) NOT NULL DEFAULT '',
                "MyAnimeListId" character varying(80) NOT NULL DEFAULT '',
                "OpenLibraryKey" text NOT NULL,
                "FirstPublishYear" integer NULL,
                "MediaType" character varying(80) NOT NULL DEFAULT '',
                "PublishingStatus" character varying(80) NOT NULL DEFAULT '',
                "ChapterCount" integer NULL,
                "VolumeCount" integer NULL,
                "MangaDexUrl" text NOT NULL,
                "MangaDexId" character varying(80) NOT NULL,
                "MangaDexLastSyncedAt" timestamp with time zone NULL,
                "MangaDexLastPrefetchedChapter" numeric(10,3) NULL,
                "MangaDexLastPrefetchedAt" timestamp with time zone NULL,
                "MangaDexLastBackfilledAt" timestamp with time zone NULL,
                "LocalSeriesId" uuid NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "Category" character varying(120) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "CoverUrl" text NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MetadataSource" character varying(40) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MyAnimeListId" character varying(80) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MediaType" character varying(80) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "PublishingStatus" character varying(80) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "ChapterCount" integer NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "VolumeCount" integer NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastSyncedAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastPrefetchedChapter" numeric(10,3) NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastPrefetchedAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastBackfilledAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "UserId" uuid NULL;
            ALTER TABLE manga_entries ALTER COLUMN "UserId" DROP NOT NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "ReadingStatus" character varying(40) NULL;
            ALTER TABLE manga_entries ALTER COLUMN "ReadingStatus" DROP NOT NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "Notes" text NULL;
            ALTER TABLE manga_entries ALTER COLUMN "Notes" DROP NOT NULL;

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

            CREATE TABLE IF NOT EXISTS site_activity (
                "Id" integer PRIMARY KEY,
                "LastActivityAt" timestamp with time zone NOT NULL
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
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MyAnimeListId" ON manga_entries ("MyAnimeListId");
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MangaDexLastSyncedAt" ON manga_entries ("MangaDexLastSyncedAt");
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MangaDexLastPrefetchedAt" ON manga_entries ("MangaDexLastPrefetchedAt");
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MangaDexLastBackfilledAt" ON manga_entries ("MangaDexLastBackfilledAt");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_manga_entries_UserId_MangaEntryId" ON user_manga_entries ("UserId", "MangaEntryId");
            """);
    }
}
