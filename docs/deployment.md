# Deployment

## Current Production Direction

Current target:

- TrueNAS custom app / Docker Compose
- Cloudflare named Tunnel
- fixed hostname: `https://mangahub.app`
- PostgreSQL persistent volume
- NAS backup share for database dumps

The local secret-filled file is:

```text
deploy.truenas.local.yml
```

It is intentionally ignored by Git.

## Public Routing

Cloudflare Tunnel should route:

```text
Hostname: mangahub.app
Service: http://mangahub-web:80
```

Cloudflare DNS should contain a proxied CNAME for `mangahub.app` pointing at the tunnel target:

```text
<tunnel-id>.cfargotunnel.com
```

The `cloudflared` container should run named tunnel token mode:

```yaml
cloudflared:
  command:
    - tunnel
    - '--no-autoupdate'
    - run
    - '--token'
    - <token>
```

## App Services

Production compose should include:

- `postgres`
- `postgres-backup`
- `mangahub-api`
- `mangahub-workers`
- `mangahub-web`
- `cloudflared`

`mangahub-web` serves Blazor static files and proxies API calls internally to `mangahub-api`.

## Required Environment

API:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
ConnectionStrings__MangaHub=Host=postgres;Database=mangahub;Username=mangahub;Password=<password>
FrontendOrigin=https://mangahub.app
MangaHub__JwtSecret=<long secret>
MangaHub__LibraryPath=/library
MangaHub__MangaDexCachePath=/mangadex-cache
MangaHub__MangaDexEnabled=true
MangaHub__MyAnimeListClientId=<client id>
MangaHub__SessionCookieSameSite=Lax
MangaHub__SessionCookieSecure=true
MangaHub__Email__SmtpHost=smtp.gmail.com
MangaHub__Email__SmtpPort=587
MangaHub__Email__UseSsl=true
MangaHub__Email__SmtpUsername=<sender address>
MangaHub__Email__SmtpPassword=<app password>
MangaHub__Email__FromAddress=<sender address>
MangaHub__Email__FromName=MangaHub
MangaHub__GoogleAuth__ClientId=<Google OAuth client id>
MangaHub__GoogleAuth__ClientSecret=<Google OAuth client secret>
```

## Account Recovery And Google Sign-In

Only the mangahub-api service needs account-recovery and Google OAuth configuration. Do not put the SMTP app password or Google client secret in the worker, repository, or GitHub Actions variables used for public builds.

For Gmail SMTP, create a Google App Password after enabling two-step verification, then use smtp.gmail.com, port 587, and TLS. The Google OAuth client must be a Web application with this exact authorized redirect URI:

    https://mangahub.app/auth/google/callback

Existing MangaHub accounts remain valid after deployment. A user can add their recovery email, change password, and link Google from the Account page. New password registration requires at least 10 characters, uppercase, lowercase, and a digit.

Workers:

```text
DOTNET_ENVIRONMENT=Production
ConnectionStrings__MangaHub=Host=postgres;Database=mangahub;Username=mangahub;Password=<password>
MangaHub__JwtSecret=<long secret>
MangaHub__LibraryPath=/library
MangaHub__MangaDexEnabled=true
MangaHub__MyAnimeListClientId=<client id>
MangaHub__MangaDexCachePath=/mangadex-cache
MangaHub__MangaDexMaintenanceHour=4
MangaHub__MangaDexMaintenanceTimeZone=America/Sao_Paulo
MangaHub__MangaDexPrefetchBatchSize=6
MangaHub__MangaDexPrefetchMaxChaptersPerManga=3
MangaHub__MangaDexIdleBackfillEnabled=true
MangaHub__MangaDexIdleMinutes=30
MangaHub__MangaDexIdleBackfillCheckMinutes=60
MangaHub__MangaDexIdleBackfillBatchSize=1
MangaHub__MangaDexIdleBackfillMaxChaptersPerManga=2
MangaHub__MangaUpdatesMatchPollMinutes=15
MangaHub__MangaUpdatesMatchRetryHours=24
```

Provider request pacing is configured separately for both API and worker processes under
`MangaHub__RemoteRequests__<Provider>__RequestsPerSecond` and
`MangaHub__RemoteRequests__<Provider>__MaxConcurrency`. See `docs/operations.md` for defaults.

## Library Mount

Current placeholder mount:

```yaml
/mnt/storage/manga:/library:ro
```

Change `/mnt/storage/manga` to the actual TrueNAS manga dataset path.

## MangaDex Reader Cache

The admin-only MangaDex reader downloads chapters as CBZ cache files. The reader serves later page views from this local cache and does not request those pages from MangaDex again.

Add the same separate writable mount to both `mangahub-api` and `mangahub-workers`; do not use the read-only `/library` mount. The API reads cached chapters, while the worker pre-downloads newly released MangaDex chapters.

```yaml
mangahub-api:
  environment:
    MangaHub__MangaDexCachePath: /mangadex-cache
  volumes:
    - mangadex-cache:/mangadex-cache

mangahub-workers:
  environment:
    MangaHub__MangaDexCachePath: /mangadex-cache
    MangaHub__MangaDexMaintenanceHour: 4
    MangaHub__MangaDexMaintenanceTimeZone: America/Sao_Paulo
  volumes:
    - mangadex-cache:/mangadex-cache

volumes:
  mangadex-cache:
```

For a cache visible in a TrueNAS dataset instead, use a bind mount such as `/mnt/Shared/NAS/MangaHubMangaDexCache:/mangadex-cache`. Keep this cache separate from the original manga library: it is derived reader data, not your owned-library mount.

## MangaDex Daily Maintenance

The workers run MangaDex maintenance immediately when the container starts, then every day at the configured hour. This means a server that was off at 04:00 catches up as soon as it returns.

Each run first refreshes stale MangaDex catalog metadata, then checks manga with at least one shelf entry in `reading` status. The first pre-download run records the currently known chapter as a watermark, without downloading historical chapters. Later runs cache only chapters above that watermark.

The defaults are intentionally conservative: 50 metadata checks per run, up to 6 reading manga, at most 3 chapters per manga, and a 5-second pause between chapter downloads. Increase the batch values only after observing MangaDex usage and container performance.

While the authenticated site has been idle for 30 minutes, the worker also performs a tiny historical backfill every hour. It uses the highest recorded current chapter for each shelf manga and works backwards through missing cached chapters. Its defaults are one manga, two chapters, and a 10-second pause, and it pauses again immediately when site activity resumes. This backfill includes any shelf entry with a recorded current chapter, so paused or completed manga can be warmed too.

## Historical DuckDNS Deploy

`deploy.duckdns.yml` exists for a direct-DNS/port-forward setup, but CGNAT made that unsuitable.

Keep it as reference. Current production should prefer Cloudflare Tunnel with `mangahub.app`.

## Automated App Updates

MangaHub can update itself without rebuilding from GitHub on TrueNAS. The flow is:

1. A push to `main` builds API, Web, and Workers images through GitHub Actions.
2. GitHub Actions publishes them to GitHub Container Registry (GHCR).
3. Watchtower in the existing MangaHub Custom App checks every six hours.
4. When a changed image exists, Watchtower pulls it, restarts that one MangaHub container, and removes its previous image.

The public template is [deploy.truenas.autoupdate.example.yml](../deploy.truenas.autoupdate.example.yml). Copy its image, labels, and `watchtower` service into the existing MangaHub TrueNAS app; do not create a second application, because that would create a different PostgreSQL volume.

Only `mangahub-api`, `mangahub-web`, and `mangahub-workers` have the Watchtower opt-in label. PostgreSQL, Cloudflare Tunnel, and backups are deliberately untouched. `--cleanup` removes replaced images only; it does not remove named volumes, database data, the local library, or the MangaDex cache. Watchtower needs Docker's socket to restart containers, so treat the custom Compose configuration as administrator-level access.

After the first workflow finishes, open GitHub Packages and set these packages to **Public**:

- `mangahub-api`
- `mangahub-web`
- `mangahub-workers`

Alternatively, authenticate the TrueNAS Docker host to `ghcr.io` with a GitHub token that has `read:packages`. Public packages are simpler for this self-hosted, public repository.
