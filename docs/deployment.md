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
```

## Library Mount

Current placeholder mount:

```yaml
/mnt/storage/manga:/library:ro
```

Change `/mnt/storage/manga` to the actual TrueNAS manga dataset path.

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

