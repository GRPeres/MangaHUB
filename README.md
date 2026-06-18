# MangaHub

MangaHub is a self-hosted manga reader for local CBZ archives and expandable remote sources. This first development pass follows the `.NET` OpenSpec with a Blazor WebAssembly frontend, ASP.NET Core API, EF Core/PostgreSQL persistence, and background workers.

## Projects

- `src/MangaHub.Api`: HTTP API for auth, library, search, reader pages, and progress.
- `src/MangaHub.Web`: Blazor WebAssembly frontend.
- `src/MangaHub.Core`: domain models, DTOs, source contracts, and service abstractions.
- `src/MangaHub.Infrastructure`: EF Core, local scanner, CBZ reader, JWT, Argon2id hashing, MangaDex client.
- `src/MangaHub.Workers`: scheduled local scans and remote sync placeholder.

## Local Run

```bash
dotnet restore
dotnet run --project src/MangaHub.Api
dotnet run --project src/MangaHub.Web
```

Put development CBZ files under:

```text
library/
  Series Name/
    Chapter 0001.cbz
```

## Docker

```bash
copy .env.example .env
docker compose up --build
```

Open:

- Web: `http://localhost:3000`
- API: `http://localhost:8000`

## Cloudflared Deployment

`deploy.cloudflare.yml` builds from a GitHub URL placeholder and exposes the Blazor web container through Cloudflared.

Before deploying, replace `https://github.com/PLACEHOLDER_OWNER/MangaHUB.git#main` with the real repository URL and copy `deploy.env.example` to `.env`.

```bash
docker compose -f deploy.cloudflare.yml --env-file .env up --build -d
```

## Current MVP Slice

- Local account register/login/logout
- HttpOnly JWT cookie sessions
- Local CBZ scan endpoint and hourly worker scan
- Library, chapter, and page APIs
- Reading progress API
- MangaDex search connector scaffold
- Blazor screens for auth, library, search, series, and reader
