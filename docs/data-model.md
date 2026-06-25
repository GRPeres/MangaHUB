# Data Model

This document describes the product-level model, not an exhaustive EF schema dump.

## Users

Users can be normal users or admins.

Normal users:

- sign in
- manage their own shelf
- add existing catalog manga to shelf
- edit their own reading status, chapter, score, category, summary, and notes

Admins:

- all normal user abilities
- manage catalog entries
- import CSV/catalog data
- assign admin roles
- edit other user shelves where UI supports owner selection

## Catalog Manga

Catalog entries are shared metadata records.

Important fields:

- title
- authors
- category/genre
- description
- cover URL
- metadata source
- OpenLibrary key
- MyAnimeList id
- media type
- publishing status
- chapter count
- volume count
- MangaDex URL/id
- local series id

Catalog entry creation should support:

- simple manual add
- add from MyAnimeList metadata
- fallback/load-more from OpenLibrary
- edit after metadata load
- CSV import for admin workflows

## Shelf Entry

Shelf entries are per-user records linked to catalog manga.

Important fields:

- user id
- catalog manga id
- reading status
- current chapter
- score
- personal category
- personal summary
- notes

Status values currently used by UI:

```text
reading
done
paused
planned
dropped
```

Score is primarily relevant when status is `done`, but existing data may contain score independently.

## Local Library

Local library scan discovers series and chapters from the mounted library path.

Expected structure:

```text
library/
  Series Name/
    Chapter 0001.cbz
    Chapter 0002.cbz
```

Local scan data can be bound to catalog entries so the reader can serve local pages.

## Metadata Sources

Preferred order:

1. MyAnimeList
2. OpenLibrary fallback
3. Manual entry

Duplicates between MAL and OpenLibrary should prefer MAL and ignore the OpenLibrary duplicate.

MyAnimeList searches intentionally send `nsfw=true` so MAL does not hide adult/explicit manga from catalog metadata search. The app stores metadata; user-facing access control/content filtering should be handled separately if it becomes a product requirement.

## Imports

CSV import was added to replace the original spreadsheet workflow.

Expected behavior:

- links are more reliable than titles
- imported titles may be incorrect
- import can create catalog entries when admin-controlled
- import can add/update shelf entries for a user
- imported catalog records should later be matchable to correct MAL/OpenLibrary metadata
