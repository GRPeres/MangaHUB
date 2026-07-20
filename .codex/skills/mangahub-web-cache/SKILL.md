---
name: mangahub-web-cache
description: Maintain MangaHub's Blazor PWA asset cache after a shipped UI change. Use when editing Razor components, CSS, JavaScript, static web assets, index.html, or the service worker in this repository, especially before a TrueNAS deployment.
---

# MangaHub Web Cache

Run this workflow for every UI or static-web change that will be deployed.

1. From the repository root, run `scripts/bump-web-cache.ps1`.
2. Confirm the script updated both stylesheet query strings in `src/MangaHub.Web/wwwroot/index.html`, the matching URLs in `src/MangaHub.Web/wwwroot/service-worker.js`, and the service-worker `CACHE_NAME`.
3. Build `src/MangaHub.Web/MangaHub.Web.csproj` and inspect the diff.
4. Do not bump versions for API-only, worker-only, database-only, or test-only changes.

The asset query version and the service-worker cache version are independent counters. Increase each one by exactly one per shipped UI update. Do not reuse an older version or change only one of the two files.

The workflow changes files locally. Follow `$mangahub-debug-release` for commit and push behavior.
