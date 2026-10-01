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

$appCssMatch = [regex]::Match($index, 'css/app\.css\?v=(\d+)')
$componentCssMatch = [regex]::Match($index, 'MangaHub\.Web\.styles\.css\?v=(\d+)')
$cacheMatch = [regex]::Match($worker, 'const CACHE_NAME = "mangahub-app-v(\d+)";')
if (-not $appCssMatch.Success -or -not $componentCssMatch.Success -or -not $cacheMatch.Success) {
    throw "Could not find the expected MangaHub asset or service-worker cache version."
}

$nextAppCss = ([int]$appCssMatch.Groups[1].Value) + 1
$nextComponentCss = ([int]$componentCssMatch.Groups[1].Value) + 1
$nextCache = ([int]$cacheMatch.Groups[1].Value) + 1
$index = [regex]::Replace($index, '(app\.css\?v=)\d+', "`${1}$nextAppCss")
$index = [regex]::Replace($index, '(MangaHub\.Web\.styles\.css\?v=)\d+', "`${1}$nextComponentCss")
$worker = [regex]::Replace($worker, 'mangahub-app-v\d+', "mangahub-app-v$nextCache")
$worker = [regex]::Replace($worker, '(app\.css\?v=)\d+', "`${1}$nextAppCss")
$worker = [regex]::Replace($worker, '(MangaHub\.Web\.styles\.css\?v=)\d+', "`${1}$nextComponentCss")

$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($indexPath, $index, $utf8WithoutBom)
[System.IO.File]::WriteAllText($workerPath, $worker, $utf8WithoutBom)
Write-Output "Updated app CSS to v=$nextAppCss, component CSS to v=$nextComponentCss, and service worker cache to mangahub-app-v$nextCache."
