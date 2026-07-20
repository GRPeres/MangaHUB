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
```

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
MangaHub__MangaDexPrefetchDelayMilliseconds=5000
```

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

## Historical DuckDNS Deploy

`deploy.duckdns.yml` exists for a direct-DNS/port-forward setup, but CGNAT made that unsuitable.

Keep it as reference. Current production should prefer Cloudflare Tunnel with `mangahub.app`.

## Future Auto-Updates

Current compose builds directly from GitHub:

```yaml
build:
  context: https://github.com/GRPeres/MangaHUB.git#main
```

This does not work well with Watchtower because Watchtower updates pulled images, not local rebuilds.

Future recommended release flow:

1. GitHub Actions builds Docker images.
2. Push images to GHCR.
3. Compose uses `image: ghcr.io/grperes/...`.
4. Watchtower or TrueNAS update flow pulls new images.
