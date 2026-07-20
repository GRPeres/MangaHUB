# MangaHub

**MangaHub** is a self-hosted manga catalog, reading tracker, and local library companion built for a personal TrueNAS setup.

Live instance:

[https://mangahub.app](https://mangahub.app)

## English

MangaHub replaces a spreadsheet-based manga reading list with a full web app. It keeps a shared admin-managed catalog of manga and lets each user maintain their own shelf with reading status, current chapter, score, notes, and read sources.

The app prefers MyAnimeList metadata for manga, keeps OpenLibrary as a fallback, and can connect catalog entries to MangaDex links and/or a local manga library mounted from NAS storage.

## Português

O **MangaHub** substitui uma planilha de acompanhamento de mangás por uma aplicação web auto-hospedada. Ele mantém um catálogo compartilhado administrado por admins e permite que cada usuário gerencie sua própria lista com status de leitura, capítulo atual, nota, observações e fontes de leitura.

A aplicação usa MyAnimeList como fonte principal de metadados, mantém OpenLibrary como alternativa, e permite ligar entradas do catálogo a links do MangaDex e/ou a uma biblioteca local de mangás montada no NAS.

## What It Does

- Tracks manga by user shelf: reading, done, paused, planned, dropped.
- Stores current chapter, score, notes, personal category, and summaries.
- Lets admins curate the shared catalog.
- Imports CSV data from the old spreadsheet workflow.
- Pulls metadata from MyAnimeList first, with OpenLibrary fallback.
- Supports cover images and external metadata ids.
- Connects manga to MangaDex and local library entries.
- Scans local CBZ-style manga libraries.
- Lets admins cache explicitly opened MangaDex chapters for the internal CBZ reader.
- Runs behind Cloudflare Tunnel for CGNAT-friendly hosting.
- Backs up PostgreSQL dumps to a NAS share.

## Tech Stack

- .NET 9
- ASP.NET Core API
- Blazor WebAssembly
- MudBlazor
- Entity Framework Core
- PostgreSQL
- Docker Compose
- nginx static web/proxy container
- Cloudflare Tunnel
- TrueNAS target deployment

## Repository Layout

```text
src/
  MangaHub.Api/             ASP.NET Core API, controllers, services, repositories
  MangaHub.Core/            Domain contracts and shared core models
  MangaHub.Infrastructure/  EF Core, local scanner, archive reader, remote clients
  MangaHub.Web/             Blazor WebAssembly frontend
  MangaHub.Workers/         Background scan/sync workers
tests/
  MangaHub.Api.Tests/
  MangaHub.Core.Tests/
docs/
  Architecture, API, frontend, deployment, operations, and AI handoff docs
```

## Documentation

Start here for future development:

- [OPENSPEC.md](OPENSPEC.md)
- [docs/README.md](docs/README.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/api.md](docs/api.md)
- [docs/frontend.md](docs/frontend.md)
- [docs/deployment.md](docs/deployment.md)
- [docs/operations.md](docs/operations.md)
- [docs/development-notes.md](docs/development-notes.md)

## Local Development

Run the stack with Docker:

```bash
copy .env.example .env
docker compose up --build
```

Open:

- Web: `http://localhost:3000`
- API: `http://localhost:8000`

Or run projects directly:

```bash
dotnet restore
dotnet run --project src/MangaHub.Api
dotnet run --project src/MangaHub.Web
```

Development library layout:

```text
library/
  Series Name/
    Chapter 0001.cbz
    Chapter 0002.cbz
```

## Production Deployment

Current production direction is TrueNAS plus a named Cloudflare Tunnel:

```text
https://mangahub.app
  -> Cloudflare Tunnel
  -> mangahub-web:80
  -> nginx /api and /auth proxy
  -> mangahub-api:8080
```

The secret-filled TrueNAS compose file is intentionally local-only:

```text
deploy.truenas.local.yml
```

It is ignored by Git and should not be committed.

The public example deploy file `deploy.duckdns.yml` is kept as historical/reference compose for direct DNS/port-forward setups, but Cloudflare Tunnel is the current production approach because the deployment network is behind CGNAT.

## Backups

The TrueNAS deployment includes a `postgres-backup` sidecar that writes `pg_dump -Fc` backups to:

```text
/mnt/Shared/NAS/MangaHUBBackups
```

Backups are database-only. Manga files live separately in the NAS library mount.

## Verification

Web build:

```bash
dotnet build src/MangaHub.Web/MangaHub.Web.csproj
```

Full build/tests:

```bash
dotnet build
dotnet test
```

Compose validation:

```bash
docker compose -f deploy.duckdns.yml --env-file deploy.env.example config
```

## Status

MangaHub is actively evolving. The current focus is keeping the app maintainable while expanding the catalog/shelf workflow, metadata matching, local reading, admin tools, and deployment reliability.
