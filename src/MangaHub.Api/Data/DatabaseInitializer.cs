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
            ALTER TABLE users ADD COLUMN IF NOT EXISTS "PreferredLanguage" character varying(16) NOT NULL DEFAULT 'en';

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
                "FallbackReaderUrl" text NOT NULL DEFAULT '',
                "ReaderPreference" character varying(20) NOT NULL DEFAULT 'mangahub',
                "MangaDexId" character varying(80) NOT NULL,
                "MangaDexLatestChapter" numeric(10,3) NULL,
                "MangaDexLastSyncedAt" timestamp with time zone NULL,
                "MangaDexLastPrefetchedChapter" numeric(10,3) NULL,
                "MangaDexLastPrefetchedAt" timestamp with time zone NULL,
                "MangaDexLastBackfilledAt" timestamp with time zone NULL,
                "MangaUpdatesId" character varying(32) NOT NULL DEFAULT '',
                "MangaUpdatesLatestChapter" numeric(10,3) NULL,
                "MangaUpdatesStatus" text NOT NULL DEFAULT '',
                "MangaUpdatesCompleted" boolean NULL,
                "MangaUpdatesLastSyncedAt" timestamp with time zone NULL,
                "MangaUpdatesLastMatchAttemptAt" timestamp with time zone NULL,
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
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLatestChapter" numeric(10,3) NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "FallbackReaderUrl" text NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "ReaderPreference" character varying(20) NOT NULL DEFAULT 'mangahub';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexUrl" text NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexId" character varying(80) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastSyncedAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastPrefetchedChapter" numeric(10,3) NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastPrefetchedAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaDexLastBackfilledAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaUpdatesId" character varying(32) NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaUpdatesLatestChapter" numeric(10,3) NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaUpdatesStatus" text NOT NULL DEFAULT '';
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaUpdatesCompleted" boolean NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaUpdatesLastSyncedAt" timestamp with time zone NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "MangaUpdatesLastMatchAttemptAt" timestamp with time zone NULL;

            CREATE TABLE IF NOT EXISTS app_migrations (
                "Name" text PRIMARY KEY,
                "AppliedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            WITH first_apply AS (
                INSERT INTO app_migrations ("Name")
                VALUES ('mangaupdates-numeric-id-repair-v1')
                ON CONFLICT ("Name") DO NOTHING
                RETURNING "Name"
            )
            UPDATE manga_entries
            SET "MangaUpdatesLastMatchAttemptAt" = NULL
            WHERE "MangaUpdatesId" = ''
              AND EXISTS (SELECT 1 FROM first_apply);

            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "UserId" uuid NULL;
            ALTER TABLE manga_entries ALTER COLUMN "UserId" DROP NOT NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "ReadingStatus" character varying(40) NULL;
            ALTER TABLE manga_entries ALTER COLUMN "ReadingStatus" DROP NOT NULL;
            ALTER TABLE manga_entries ADD COLUMN IF NOT EXISTS "Notes" text NULL;
            ALTER TABLE manga_entries ALTER COLUMN "Notes" DROP NOT NULL;

            UPDATE manga_entries
            SET "MangaDexId" = lower(regexp_replace(
                "MangaDexUrl",
                '^.*mangadex[.]org/title/([0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}).*$',
                '\1',
                'i'))
            WHERE "MangaDexId" = ''
              AND "MangaDexUrl" ~* 'mangadex[.]org/title/[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}';

            UPDATE manga_entries
            SET "FallbackReaderUrl" = "MangaDexUrl"
            WHERE "FallbackReaderUrl" = ''
              AND "MangaDexId" = ''
              AND "MangaDexUrl" <> ''
              AND "MangaDexUrl" !~* 'mangadex[.]org/title/[0-9a-f]{{8}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{4}}-[0-9a-f]{{12}}';

            UPDATE manga_entries
            SET "ReaderPreference" = 'mangahub'
            WHERE "ReaderPreference" NOT IN ('mangahub', 'external', 'hybrid');

            CREATE TABLE IF NOT EXISTS user_manga_entries (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "MangaEntryId" uuid NOT NULL,
                "ReadingStatus" character varying(40) NOT NULL,
                "CurrentChapter" character varying(40) NOT NULL DEFAULT '',
                "IsRead" boolean NOT NULL DEFAULT false,
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

            CREATE TABLE IF NOT EXISTS mangadex_language_latest_chapters (
                "Id" uuid PRIMARY KEY,
                "MangaEntryId" uuid NOT NULL,
                "Language" character varying(16) NOT NULL,
                "LatestChapter" numeric(10,3) NOT NULL,
                "SyncedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS notifications (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "MangaEntryId" uuid NOT NULL,
                "Type" character varying(40) NOT NULL,
                "ChapterNumber" numeric(10,3) NOT NULL,
                "Language" character varying(16) NOT NULL,
                "Title" character varying(255) NOT NULL,
                "Body" text NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ReadAt" timestamp with time zone NULL
            );

            ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "CurrentChapter" character varying(40) NOT NULL DEFAULT '';
            ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "IsRead" boolean NOT NULL DEFAULT false;
            ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "Score" integer NULL;
            ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "Category" character varying(120) NOT NULL DEFAULT '';
            ALTER TABLE user_manga_entries ADD COLUMN IF NOT EXISTS "Summary" text NOT NULL DEFAULT '';
            ALTER TABLE chapters ADD COLUMN IF NOT EXISTS "Language" character varying(16) NOT NULL DEFAULT 'en';

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
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MangaUpdatesId" ON manga_entries ("MangaUpdatesId");
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MangaUpdatesLastSyncedAt" ON manga_entries ("MangaUpdatesLastSyncedAt");
            CREATE INDEX IF NOT EXISTS "IX_manga_entries_MangaUpdatesLastMatchAttemptAt" ON manga_entries ("MangaUpdatesLastMatchAttemptAt");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_manga_entries_UserId_MangaEntryId" ON user_manga_entries ("UserId", "MangaEntryId");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_mangadex_language_latest_chapters_MangaEntryId_Language" ON mangadex_language_latest_chapters ("MangaEntryId", "Language");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_notifications_UserId_MangaEntryId_Type_ChapterNumber_Language" ON notifications ("UserId", "MangaEntryId", "Type", "ChapterNumber", "Language");
            CREATE INDEX IF NOT EXISTS "IX_notifications_UserId_ReadAt_CreatedAt" ON notifications ("UserId", "ReadAt", "CreatedAt");
            """);
    }
}
