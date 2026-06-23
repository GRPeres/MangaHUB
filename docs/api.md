# API Guide

The API should follow controller -> service -> repository.

## Pattern

Controllers:

- define route and HTTP verb
- bind request DTOs
- perform access checks
- call one service method
- return typed results

Services:

- own business logic
- combine repository calls
- apply rules
- call remote metadata/local scanner clients

Repositories:

- own EF Core queries and updates
- expose reusable database operations
- avoid UI/business phrasing where possible

## Current Controller Areas

Auth:

- register
- login
- logout
- current user

Admin:

- user listing
- role changes

Catalog:

- list/search catalog
- create catalog manga
- update catalog manga

Shelf:

- add existing catalog manga to shelf
- update shelf entry
- delete shelf entry
- import shelf CSV

Metadata:

- search metadata, preferring MyAnimeList
- optionally include OpenLibrary fallback/load-more

Series/Library/Reader/Progress:

- local library scan
- series/chapter browsing
- reader page serving
- reading progress APIs

## DTO Guidance

DTOs should be one type per file.

Do not rebuild monolithic DTO/service files as the app grows.

Frontend DTOs live under `src/MangaHub.Web/API/DTOs`.

Frontend API call wrappers live under `src/MangaHub.Web/API/Services`, ideally one service per backend controller.

## Access Control Rules

- anonymous users can only access public/basic app surfaces
- shelf operations require login
- catalog mutations require admin
- CSV import that creates catalog entries belongs in admin/catalog flows
- admin role assignment requires admin

## Error Handling

Prefer clear failure messages at service/controller boundaries.

For DB errors during import, return row-level skipped messages where possible. Avoid dumping sensitive connection details.

