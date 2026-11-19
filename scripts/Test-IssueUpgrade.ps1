param(
    [Parameter(Mandatory)][string]$DumpPath,
    [Parameter(Mandatory)][string]$ApiImage
)

$ErrorActionPreference = 'Stop'
$dump = Get-Item -LiteralPath $DumpPath
if ($dump.PSIsContainer -or $dump.Length -eq 0) { throw 'Provide a nonempty pg_dump custom-format file.' }

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed: $($args[0])." }
}

$suffix = [guid]::NewGuid().ToString('N')
$network = "mh-upgrade-$suffix"
$pg = "$network-db"
$api = "$network-api"
$volume = "$network-data"
$password = [guid]::NewGuid().ToString('N')
$jwt = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')

function Invoke-Sql([string]$Sql) {
    $Sql | & docker exec -i $pg psql -X -qAt -v ON_ERROR_STOP=1 -U verifier -d verification
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL verification query failed.' }
}

$snapshot = @'
SELECT count(*) || ':' || coalesce(md5(string_agg(row_to_json(saved)::text, E'\n' ORDER BY saved."Id")), '')
FROM (SELECT "Id", "Kind", "SubjectType", "SubjectId", "Status", "Priority", "TitleSnapshot",
             "MetadataJson", "CreatedAt", "UpdatedAt", "ResolvedAt", "ResolvedByUserId", "ResolutionNote"
      FROM admin_issues) saved;
SELECT count(*) || ':' || coalesce(md5(string_agg(row_to_json(saved)::text, E'\n' ORDER BY saved."Id")), '')
FROM admin_issue_reports saved;
'@

Invoke-Docker info --format '{{.ServerVersion}}' | Out-Null
Invoke-Docker image inspect $ApiImage --format '{{.Id}}' | Out-Null
try {
    Invoke-Docker network create --internal $network | Out-Null
    Invoke-Docker volume create $volume | Out-Null
    Invoke-Docker run -d --name $pg --network $network --network-alias postgres -e "POSTGRES_PASSWORD=$password" -e POSTGRES_USER=verifier -e POSTGRES_DB=verification -v "${volume}:/var/lib/postgresql/data" postgres:16-alpine | Out-Null
    for ($i = 0; $i -lt 60; $i++) {
        & docker exec $pg pg_isready -h 127.0.0.1 -U verifier -d verification | Out-Null
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Seconds 1
    }
    if ($i -eq 60) { throw 'Disposable PostgreSQL did not become ready.' }

    Invoke-Docker cp $dump.FullName "${pg}:/tmp/input.dump"
    Invoke-Docker exec $pg pg_restore --exit-on-error --single-transaction --no-owner --no-privileges -U verifier -d verification /tmp/input.dump
    $before = (Invoke-Sql $snapshot) -join '|'
    Invoke-Docker run -d --name $api --network $network -e ASPNETCORE_URLS=http://+:8080 -e ASPNETCORE_ENVIRONMENT=Production -e "ConnectionStrings__MangaHub=Host=postgres;Database=verification;Username=verifier;Password=$password" -e "MangaHub__JwtSecret=$jwt" -e MangaHub__MangaDexEnabled=false $ApiImage | Out-Null

    for ($i = 0; $i -lt 90; $i++) {
        $state = Invoke-Docker inspect --format '{{.State.Status}}' $api
        if ($state -ne 'running') { throw "API stopped during startup ($state). Inspect startup logs on a sanitized copy for details." }
        $logs = Invoke-Docker logs $api
        if ($logs -match 'Now listening on:') { break }
        Start-Sleep -Seconds 1
    }
    if ($i -eq 90) { throw 'API did not finish startup within 90 seconds.' }
    if (((Invoke-Sql $snapshot) -join '|') -ne $before) { throw 'The upgrade changed existing issue or report data.' }

    $columns = Invoke-Sql @'
SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND
 ((table_name = 'admin_issues' AND column_name IN ('ReviewState', 'SnoozedUntil', 'LastCheckedAt')) OR
  (table_name = 'manga_entries' AND column_name IN ('MangaDexReleaseIntervalDays', 'MangaDexReleaseIntervalSamples')));
'@
    if ($columns -ne '5') { throw 'Expected review or cadence columns are missing.' }
    Invoke-Sql @'
BEGIN;
INSERT INTO admin_issues ("Id", "Kind", "SubjectType", "SubjectId", "CreatedAt", "UpdatedAt",
    "Status", "ReviewState", "Priority", "TitleSnapshot", "MetadataJson", "ResolutionNote")
VALUES (gen_random_uuid(), 'upgrade-smoke-test', 'test',
        gen_random_uuid(), now(), now(),
        'open', 'action_required', 'normal', 'Upgrade smoke test', '{}', '');
UPDATE admin_issues SET "ReviewState" = 'monitoring', "SnoozedUntil" = now() + interval '7 days',
    "LastCheckedAt" = now() WHERE "Kind" = 'upgrade-smoke-test';
ROLLBACK;
'@
    Write-Output 'PASS: restored database, completed API startup, preserved issue history, and verified all five new columns.'
}
finally {
    foreach ($name in @($api, $pg)) { & docker rm -f $name 2>$null | Out-Null }
    & docker volume rm $volume 2>$null | Out-Null
    & docker network rm $network 2>$null | Out-Null
}
