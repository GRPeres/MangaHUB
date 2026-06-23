# Frontend Guide

MangaHub Web is Blazor WebAssembly with MudBlazor.

## UI Principles

- Build the usable app directly; avoid landing-page-style filler.
- Prefer dense, calm operational UI.
- Use MudBlazor components for controls and layout.
- Keep pages light and move meaningful UI chunks into components.
- Use `.razor`, `.razor.cs`, and `.razor.css` tri-file structure for maintainable components.
- Avoid inline styles except temporary debugging.

## Layout

Main layout owns:

- app bar
- sidebar navigation
- dark/light theme toggle
- account menu
- login modal
- navigation guard for pages requiring login

Theme preference persists in browser `localStorage`:

```text
mangahub_theme=dark|light
```

## Auth UX

Login is a modal component available from anywhere.

If a logged-out user clicks a protected route, the layout opens the modal and shows a "please log in" message.

The account menu should show the current user and role, allow account management, and logout.

## Shelf Page

The shelf page is a personal tracker.

Current direction:

- compact header actions
- filter popup instead of bulky inline filter bar
- vertical list of horizontal cards on desktop
- normal stacked cards on mobile
- cover left, metadata middle, progress/actions right
- status chips are color-coded and clickable quick filters

Shelf card component:

```text
Components/Shelf/ShelfEntryCard.*
```

## Catalog Page

The catalog page is an admin metadata surface.

Current direction:

- compact header actions
- filter popup
- list cards with source chips
- source chips are clickable quick filters
- add/edit/import are modal flows

Catalog card component:

```text
Components/Catalog/CatalogEntryCard.*
```

## Modals

Existing modal style is shared via:

```text
.mh-modal-backdrop
.mh-modal
.mh-small-modal
.mh-filter-modal
```

Add/edit/import flows should generally be modals over the current page rather than separate pages.

## API Client

The web app uses:

- `ApiHttpClient`
- one service per backend controller area
- DTO files under `API/DTOs`

Avoid having auth/session state perform duplicate raw API calls when an API service already exists.

