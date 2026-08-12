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

## Charts

MangaHub's default chart library is the charting built into MudBlazor (`MudChart` and the types in `MudBlazor.Charts`). Do not add a separate chart package for normal dashboard work.

This covers the expected dashboard visuals:

- line and area-style reading activity trends
- bar and stacked-bar comparisons
- pie, donut, and rose breakdowns
- heat maps and radar charts when they genuinely aid comparison

Use the existing MangaHub palette through `ChartOptions.ChartPalette`; chart colors must remain readable in both light and dark themes. Keep charts in a bounded-height component or bento tile, and provide an adjacent textual summary so a chart is not the only way to understand a value.

`LiveChartsCore.SkiaSharpView.Blazor` is the approved escalation path only when a future requirement needs capabilities MudBlazor does not provide well, such as zoomable/time-axis-heavy analytics, advanced gauges, or custom drawing. Record the reason in the relevant change/spec before adding it. Do not introduce Blazor-ApexCharts as a second default.
