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

## Secret Rotation

Rotate these if they were pasted into chat, logs, screenshots, or Git:

- Cloudflare Tunnel token
- MyAnimeList client secret
- DuckDNS token
- JWT secret
- PostgreSQL password, if feasible

The app currently needs the MyAnimeList client id. It does not need the MyAnimeList client secret for metadata search.

