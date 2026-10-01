---
name: mangahub-debug-release
description: Control MangaHub debugging, hotfix commits, and GitHub releases. Use while investigating or fixing bugs in this repository, and whenever a request could lead to a commit, push, branch sync, or production deployment.
---

# MangaHub Debug Release

Use `workbench` as the default development branch. Begin ordinary MangaHub work from `workbench`, creating it from the current `main` tip when it does not exist. Keep `dev` unchanged because it is reserved for independent staging or experimental work.

Keep individual debugging attempts local. Once a requested bug fix is complete and verified, create one clean commit and push `workbench` automatically, unless the user explicitly says not to commit or push.

Do not merge, fast-forward, or push changes into `main` during ordinary work. Merge `workbench` into `main` only when the user explicitly says the workday is done or otherwise directly asks to release the accumulated work to `main`.

Do not create a commit for each experiment, failed attempt, or intermediate styling adjustment. Finish the investigation, validate the complete fix, then make one descriptive commit. An explicit request to keep work local overrides the automatic hotfix push rule.

Before the automatic workbench update:

1. Inspect `git status`, current branch, and the staged diff. Preserve unrelated user changes.
2. Run the focused build and tests appropriate to the change; use the full solution test suite for shared API, infrastructure, or reader changes.
3. Run `git diff --check`.
4. Commit only the intentional files with a concise message, then push `workbench`.
5. For UI/static changes, apply `$mangahub-web-cache` before committing.

When the user authorizes the end-of-day release, update `workbench` from `main` if needed, merge `workbench` into `main`, validate the merged result, and push `main`. Report the merge commit or fast-forward result clearly.
