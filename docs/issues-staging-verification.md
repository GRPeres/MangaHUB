# Issues database upgrade verification

Use a trusted custom-format PostgreSQL dump from before the Issues upgrade,
preferably from staging and containing at least one historical issue and report.
Use a local Docker engine and an API image built from the candidate revision.

```powershell
docker build -t mangahub-api:issue-upgrade -f src/MangaHub.Api/Dockerfile .
./scripts/Test-IssueUpgrade.ps1 -DumpPath ./staging-before-upgrade.dump -ApiImage mangahub-api:issue-upgrade
```

The verifier restores into a fresh PostgreSQL 16 container, starts the candidate
API on an isolated network, compares the original issue and report data, and
checks the new columns and writes. It publishes no ports and removes its
containers, database volume, and network after the run. It never connects to
the existing staging or production database.

After that check passes, use a disposable staging deployment to exercise the
browser workflow: verify history and reports are visible, snooze a test issue,
recheck a selected title, and confirm a read-only test account cannot mutate
issues. The script does not replace those UI checks.
