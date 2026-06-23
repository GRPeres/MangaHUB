# Architecture

MangaHub is a .NET 9 solution using Blazor WebAssembly, ASP.NET Core, EF Core, PostgreSQL, and background workers.

## Runtime Components

```text
Browser
  |
  v
mangahub-web
  nginx serves Blazor static files
  nginx proxies /api, /auth, /health to mangahub-api:8080
  |
  v
mangahub-api
  ASP.NET Core controllers
  service layer
  repository layer
  EF Core DbContext
  |
  v
postgres

mangahub-workers
  scheduled background work
  same PostgreSQL database
  same mounted library path

cloudflared
  named Cloudflare Tunnel to mangahub-web:80

postgres-backup
  pg_dump sidecar to NAS share
```

## Project Responsibilities

### MangaHub.Api

Owns HTTP behavior:

- controllers
- auth/session endpoints
- access control
- API services
- repositories
- current-user/session cookie helpers
- database initialization

Preferred structure:

```text
Controllers/
Services/
Repositories/
DTOs/
Common/
Data/
```

Controllers should be thin. They define route, verb, binding, auth/access checks, and call services.

Services contain business logic and orchestration.

Repositories contain reusable database calls and EF Core query/update details.

### MangaHub.Core

Owns domain-level models/contracts that can be shared without depending on ASP.NET or EF-specific concerns.

### MangaHub.Infrastructure

Owns implementations for:

- EF Core DbContext
- local library scanning
- archive/CBZ reading
- password/session/security infrastructure
- remote clients such as MyAnimeList, OpenLibrary, and MangaDex

### MangaHub.Web

Owns Blazor WebAssembly UI:

- pages
- reusable components
- MudBlazor layout
- API client services
- browser-local session/theme state

Frontend API services are grouped to mirror backend controllers where possible.

### MangaHub.Workers

Owns background jobs such as local library scanning and future sync/update jobs.

## Data Flow

Catalog data is shared app-wide and admin controlled.

Shelf data is per-user and points to a catalog entry.

Local library series/chapters are scanned from the mounted library path and can be bound to catalog entries.

MangaDex links are stored on catalog entries and are used only when the user chooses to read.

MyAnimeList metadata is preferred for catalog search/enrichment. OpenLibrary remains available as fallback or explicit "load more" behavior.

## Auth Flow

The API issues a JWT session token and stores it in an HttpOnly cookie. The web client also maintains browser-side session awareness so Blazor navigation and account menus stay responsive.

Important cookie settings for HTTPS tunnel production:

```text
MangaHub__SessionCookieSameSite=Lax
MangaHub__SessionCookieSecure=true
FrontendOrigin=https://mangahub.app
```

## UI Layout

The app uses MudBlazor with a custom purple-based palette. Main layout has:

- app bar
- compact/expandable sidebar
- account menu
- persisted dark/light theme preference
- login modal available from anywhere

Shelf and catalog list pages use component cards, not large inline page blocks.

