# Operations

## Backup

The TrueNAS local deploy includes a `postgres-backup` sidecar.

It runs:

- immediately on startup
- once every 24 hours

Backups are written to:

```text
/mnt/Shared/NAS/MangaHUBBackups
```

Files are named:

```text
mangahub-YYYYMMDD-HHMMSS.dump
```

Format:

```text
pg_dump -Fc
```

Retention:

```text
30 days
```

This backs up database data only. Manga files are expected to already live on the NAS storage.

## Restore

Restore conceptually uses `pg_restore` into the `mangahub` database.

Before restoring:

- stop API/workers
- keep Postgres running
- verify the dump file exists in the backup share
- understand that restore may overwrite current data

Exact restore commands should be tested before relying on them for production recovery.

## Password Mismatch Pitfall

Postgres only applies `POSTGRES_PASSWORD` when the database volume is first initialized.

If the password in compose changes while the old volume remains, the API will crash with:

```text
28P01: password authentication failed for user "mangahub"
```

For test/empty data, delete only the app Postgres volume:

```text
ix-mangahub_postgres-data
```

Do not unset the TrueNAS Apps pool.

For real data, do not delete the volume unless a backup exists and restore has been tested.

## Cloudflare Tunnel Checks

If `https://mangahub.app` does not work:

1. Check Cloudflare Tunnel connector health.
2. Check public hostname route:
   `mangahub.app -> http://mangahub-web:80`
3. Check DNS CNAME points to `<tunnel-id>.cfargotunnel.com`.
4. Check `cloudflared` logs.
5. Check `mangahub-web` logs.
6. Check `mangahub-api` logs.

If UI loads but API calls fail:

- check API logs first
- verify `FrontendOrigin=https://mangahub.app`
- verify secure cookie setting is true
- verify nginx proxy config still routes `/api`, `/auth`, and `/health`

## MangaDex Maintenance

Each MangaDex release sync also refreshes the live publishing status for the catalog entries it checks. MangaDex `hiatus` moves active shelf entries to `paused`, while `ongoing` restores `paused` and incorrectly completed entries to `reading`, retaining chapter progress, `IsRead` state, notes, and rating. Paused entries remain eligible for unread-release visibility, notifications, pre-downloads, and cache retention so readers can catch up to the hiatus point. The admin Operations page can queue **Refresh MangaDex statuses and repair shelves** to run this correction across the configured full maintenance batch immediately.

At startup and after the daily MangaDex pre-download maintenance, cache retention removes automatically downloaded MangaDex CBZ chapters that are below the earliest current chapter of any user actively reading that manga. A reader at chapter 24 retains chapter 24 onward; another reader at chapter 1 retains the whole numbered run. Manga with no active readers have their automatically downloaded MangaDex chapters removed. Manually imported cache chapters (`manual-*`) are never removed by this job. Queue **Trim read MangaDex cache** from Admin Operations to run it immediately. Set `MangaHub__MangaDexCacheRetentionEnabled=false` to disable automatic retention.

`mangahub-workers` performs MangaDex maintenance immediately at startup and then at 04:00 in `America/Sao_Paulo` by default. It first refreshes stale chapter metadata, then pre-downloads recent chapters for manga that are actively being read.

Check the worker logs for `MangaDex catalog sync` and `MangaDex pre-download`. The first run only records each reading manga's current chapter watermark. Subsequent runs cache newer releases, up to the configured batch limits.

Both `mangahub-api` and `mangahub-workers` must mount the exact same writable `/mangadex-cache` volume. If one does not, the worker may download a chapter that the reader cannot find.

When the authenticated site has been idle for 30 minutes, the worker performs a small historical cache backfill every hour. It only downloads chapters at or below a user's recorded current chapter, prioritizing closest missing chapters first. Default workload limits are one manga and two chapters. External request pacing is owned by the shared priority scheduler.

MangaUpdates identity repair checks for newly eligible unbound catalog entries every 15 minutes. A specific entry that could not be matched is deferred for 24 hours using its own `MangaUpdatesLastMatchAttemptAt` timestamp, so one failed title never delays a different newly added manga.

## Remote Request Scheduler

External requests are queued by provider and priority. Rates are configured under `MangaHub:RemoteRequests` in both API and worker settings:

```json
{
  "MangaDexApi": { "RequestsPerSecond": 2, "MaxConcurrency": 2 },
  "MangaDexPages": { "RequestsPerSecond": 1, "MaxConcurrency": 2 },
  "MangaUpdates": { "RequestsPerSecond": 0.5, "MaxConcurrency": 1 },
  "MyAnimeList": { "RequestsPerSecond": 1, "MaxConcurrency": 1 },
  "OpenLibrary": { "RequestsPerSecond": 0.5, "MaxConcurrency": 1 }
}
```

These are conservative per-process defaults because API and workers are separate containers behind the same public IP. Environment variable overrides use the normal .NET form, for example:

```text
MangaHub__RemoteRequests__MangaDexApi__RequestsPerSecond=2
MangaHub__RemoteRequests__MangaDexPages__MaxConcurrency=2
```

Priority order is reader open, next-chapter prefetch, interactive metadata, release sync, maintenance, then idle historical backfill. HTTP `429` responses temporarily pause the affected provider.

Open Library officially allows 1 request per second for unidentified clients and 3 requests per second for clients whose User-Agent includes contact information: https://openlibrary.org/developers/api. MangaHub stays below the unidentified allowance by default. MangaDex, MangaUpdates, and MyAnimeList do not publish a stable general-purpose ceiling that MangaHub can safely depend on, so their defaults are deliberately conservative and should be raised only after observing provider responses.

## Secret Rotation

Rotate these if they were pasted into chat, logs, screenshots, or Git:

- Cloudflare Tunnel token
- MyAnimeList client secret
- DuckDNS token
- JWT secret
- PostgreSQL password, if feasible

The app currently needs the MyAnimeList client id. It does not need the MyAnimeList client secret for metadata search.
