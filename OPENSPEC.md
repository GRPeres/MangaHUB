# MangaHub OpenSpec

This is the current development guide for MangaHub. Treat it as the first file to read before changing the app.

`OPENSPEC.txt` is the original draft. This Markdown OpenSpec reflects the project as it exists now.

## Purpose

MangaHub is a self-hosted manga tracking and reading app. The primary use case is replacing a spreadsheet-based reading list with a database-backed web app that tracks:

- catalog manga metadata
- personal shelf status
- current chapter
- score
- notes
- local read sources
- MangaDex read links
- metadata from MyAnimeList, with OpenLibrary as fallback

The app is designed for a personal TrueNAS deployment behind Cloudflare Tunnel.

## Current Product Model

MangaHub separates shared catalog data from user shelf data.

- Catalog: admin-managed manga entries shared across the app.
- Shelf: per-user reading list entries that point to catalog manga.
- Local library: optional local CBZ/archives scanned from the mounted library path.
- Remote metadata: MyAnimeList is the preferred source; OpenLibrary is secondary/fallback.
- Reading source: only chosen when the user reads. A catalog entry can point to MangaDex and/or local series.

Normal users should manage only their own shelf. Admins can manage catalog metadata, imports, users, and admin roles.

## Architecture

See [docs/architecture.md](docs/architecture.md).

High-level flow:

```text
Blazor WebAssembly
  -> nginx static web container
  -> nginx /api and /auth reverse proxy
  -> ASP.NET Core API
  -> services
  -> repositories
  -> EF Core/PostgreSQL
```

Background workers run scheduled scans/sync-style work against the same database and mounted library.

## Repository Layout

```text
src/
  MangaHub.Api/
  MangaHub.Core/
  MangaHub.Infrastructure/
  MangaHub.Web/
  MangaHub.Workers/
tests/
  MangaHub.Api.Tests/
  MangaHub.Core.Tests/
docs/
  architecture.md
  api.md
  data-model.md
  deployment.md
  frontend.md
  operations.md
  development-notes.md
```

## Development Rules

- Prefer small, maintainable components over large page files.
- Keep API structure as controller -> service -> repository.
- Keep API DTOs as one type per file under the web/API DTO structure and API DTO folders.
- Put frontend API calls in one service per backend controller.
- Keep business rules in API services unless a frontend-only UI behavior requires local state.
- Use MudBlazor for UI controls and layout.
- Keep visual styles in `.razor.css` or shared `wwwroot/css/app.css`; avoid inline styling.
- Preserve local secret files as ignored files.
- Do not commit `deploy.truenas.local.yml`, `.env`, appsettings production secrets, database dumps, or manga archives.

## Deployment Target

Current production target:

- TrueNAS custom app / Docker Compose
- PostgreSQL container
- API container
- worker container
- Blazor/nginx web container
- cloudflared named tunnel container
- Postgres backup sidecar dumping to `/mnt/Shared/NAS/MangaHUBBackups`

Current fixed public URL:

```text
https://mangahub.app
```

See [docs/deployment.md](docs/deployment.md) and [docs/operations.md](docs/operations.md).

## Critical Secrets

Secrets exist only in local/TrueNAS config:

- PostgreSQL password
- JWT secret
- Cloudflare Tunnel token
- MyAnimeList client id
- DuckDNS token, historical/test only

If a secret is pasted into chat or logs, treat it as exposed and rotate it before real production use.

## Verification

Before committing web changes:

```bash
dotnet build src/MangaHub.Web/MangaHub.Web.csproj
```

Before committing API/core/infrastructure changes:

```bash
dotnet build
dotnet test
```

For compose changes:

```bash
docker compose -f deploy.truenas.local.yml config
docker compose -f deploy.duckdns.yml --env-file deploy.env.example config
```

