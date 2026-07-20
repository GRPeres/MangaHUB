param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
)

$indexPath = Join-Path $RepositoryRoot "src\MangaHub.Web\wwwroot\index.html"
$workerPath = Join-Path $RepositoryRoot "src\MangaHub.Web\wwwroot\service-worker.js"

foreach ($path in @($indexPath, $workerPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Expected MangaHub web asset file was not found: $path"
    }
}

$index = Get-Content -LiteralPath $indexPath -Raw
$worker = Get-Content -LiteralPath $workerPath -Raw

$assetMatch = [regex]::Match($index, 'css/app\.css\?v=(\d+)')
$cacheMatch = [regex]::Match($worker, 'const CACHE_NAME = "mangahub-app-v(\d+)";')
if (-not $assetMatch.Success -or -not $cacheMatch.Success) {
    throw "Could not find the expected MangaHub asset or service-worker cache version."
}

$nextAsset = ([int]$assetMatch.Groups[1].Value) + 1
$nextCache = ([int]$cacheMatch.Groups[1].Value) + 1
$replaceAssetVersion = {
    param($match)
    "$($match.Groups[1].Value)$nextAsset"
}
$index = [regex]::Replace($index, '(app\.css\?v=|MangaHub\.Web\.styles\.css\?v=)\d+', $replaceAssetVersion)
$worker = [regex]::Replace($worker, 'mangahub-app-v\d+', "mangahub-app-v$nextCache")
$worker = [regex]::Replace($worker, '(app\.css\?v=|MangaHub\.Web\.styles\.css\?v=)\d+', $replaceAssetVersion)

Set-Content -LiteralPath $indexPath -Value $index -NoNewline -Encoding utf8
Set-Content -LiteralPath $workerPath -Value $worker -NoNewline -Encoding utf8
Write-Output "Updated CSS asset cache to v=$nextAsset and service worker cache to mangahub-app-v$nextCache."
