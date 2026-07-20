---
name: mangahub-debug-release
description: Control MangaHub debugging, hotfix commits, and GitHub releases. Use while investigating or fixing bugs in this repository, and whenever a request could lead to a commit, push, branch sync, or production deployment.
---

# MangaHub Debug Release

Keep individual debugging attempts local. Once a requested bug fix is complete and verified, create one clean hotfix commit and push the current branch automatically, unless the user explicitly says not to commit or push.

Do not create a commit for each experiment, failed attempt, or intermediate styling adjustment. Finish the investigation, validate the complete fix, then make one descriptive commit. An explicit request to keep work local overrides the automatic hotfix push rule.

Before the automatic hotfix release:

1. Inspect `git status`, current branch, and the staged diff. Preserve unrelated user changes.
2. Run the focused build and tests appropriate to the change; use the full solution test suite for shared API, infrastructure, or reader changes.
3. Run `git diff --check`.
4. Commit only the intentional files with a concise message, then push the current branch.
5. For UI/static changes, apply `$mangahub-web-cache` before committing.

If the user requests both `main` and `dev`, keep their relationship explicit: push the requested branch first, then fast-forward or merge only with the user's stated intent.
