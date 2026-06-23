# Development Notes for Future AI Work

Read this before making larger changes.

## User Preferences

The owner prefers:

- practical implementation over abstract plans
- maintainable structure
- componentized UI
- MudBlazor
- tri-file Blazor components
- modals for add/edit/import flows
- compact robust UI over marketing/landing-page style
- local/TrueNAS deployment support
- clear push-ready changes

## Important Product Decisions

- OpenLibrary was not good enough for manga as the primary source.
- MyAnimeList is the primary metadata source.
- OpenLibrary should appear only as fallback/load-more or explicit admin action.
- Duplicate metadata results should prefer MAL over OpenLibrary.
- Users should not freely create catalog manga. Normal users choose from existing catalog and add to shelf.
- Admins manage the catalog.
- Shelf and catalog are separate pages, not one unified add page.
- Add/edit/import flows should be modals.
- The app replaces a spreadsheet, so chapter tracking, status, scoring, notes, import, and export/backup matter.

## Current UX State

Main layout:

- compact sidebar with expand button
- account menu beside theme toggle
- login modal
- theme persisted in localStorage

Shelf:

- card list
- filter popup
- quick status chip filters
- add/import menu
- edit modal

Catalog:

- admin-only
- card list
- filter popup
- quick source chip filters
- add/import menu
- add/edit modals

## Common Pitfalls

- Do not reintroduce large inline page card markup when component files exist.
- Do not put catalog metadata matching only inside shelf edit; catalog metadata belongs to catalog/admin flows.
- Do not make users create new catalog entries from their shelf flow.
- Do not assume local port forwarding works; the user has CGNAT.
- Do not commit local TrueNAS secrets.
- Do not rely on DuckDNS for current production; Cloudflare Tunnel with `mangahub.app` is the current direction.
- Do not treat `POSTGRES_PASSWORD` as changing an existing database volume password.

## Useful Commands

Build web:

```bash
dotnet build src/MangaHub.Web/MangaHub.Web.csproj
```

Build all:

```bash
dotnet build
```

Run tests:

```bash
dotnet test
```

Validate deploy examples:

```bash
docker compose -f deploy.duckdns.yml --env-file deploy.env.example config
docker compose -f deploy.truenas.local.yml config
```

