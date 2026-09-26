param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$hidMaestroVersion = 'v1.9.0'
$hidHideVersion = 'v1.5.230.0'

$depsDir = Join-Path $Root 'artifacts\deps'
$licenseDir = Join-Path $Root 'artifacts\licenses'
$libDir = Join-Path $Root 'src\T16Bridge\lib'
$tempDir = Join-Path $Root 'artifacts\tmp'

New-Item -ItemType Directory -Force -Path $depsDir, $licenseDir, $libDir, $tempDir | Out-Null

function Invoke-GitHubRequest {
    param([string]$Uri)

    $headers = @{ 'User-Agent' = 'T16Bridge-build' }
    if ($env:GITHUB_TOKEN) {
        $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)"
        $headers['X-GitHub-Api-Version'] = '2022-11-28'
    }

    return Invoke-RestMethod -Uri $Uri -Headers $headers
}

Write-Host "Downloading HIDMaestro $hidMaestroVersion..."
$hmZip = Join-Path $tempDir 'HIDMaestro.zip'
$hmUrl = "https://github.com/hifihedgehog/HIDMaestro/releases/download/$hidMaestroVersion/HIDMaestro-$hidMaestroVersion.zip"
Invoke-WebRequest -Uri $hmUrl -OutFile $hmZip

$hmExtract = Join-Path $tempDir 'hidmaestro'
Remove-Item -Recurse -Force $hmExtract -ErrorAction SilentlyContinue
Expand-Archive -Path $hmZip -DestinationPath $hmExtract -Force

$hmDll = Get-ChildItem -Path $hmExtract -Filter 'HIDMaestro.Core.dll' -Recurse | Select-Object -First 1
if (-not $hmDll) { throw 'HIDMaestro.Core.dll was not found in the release archive.' }
Copy-Item $hmDll.FullName (Join-Path $libDir 'HIDMaestro.Core.dll') -Force

Invoke-WebRequest `
    -Uri "https://raw.githubusercontent.com/hifihedgehog/HIDMaestro/$hidMaestroVersion/LICENSE" `
    -OutFile (Join-Path $licenseDir 'HIDMaestro-LICENSE.txt')

Write-Host "Downloading HidHide $hidHideVersion..."
$release = Invoke-GitHubRequest "https://api.github.com/repos/nefarius/HidHide/releases/tags/$hidHideVersion"
$asset = $release.assets | Where-Object {
    $_.name -match '(?i)\.(exe|msi)$' -and $_.name -notmatch '(?i)(source|symbols|pdb)'
} | Select-Object -First 1

if (-not $asset) { throw "No HidHide installer asset found for $hidHideVersion." }

$ext = [System.IO.Path]::GetExtension($asset.name).ToLowerInvariant()
$destName = if ($ext -eq '.msi') { 'HidHideSetup.msi' } else { 'HidHideSetup.exe' }
$dest = Join-Path $depsDir $destName
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $dest

Invoke-WebRequest `
    -Uri "https://raw.githubusercontent.com/nefarius/HidHide/$hidHideVersion/LICENSE" `
    -OutFile (Join-Path $licenseDir 'HidHide-LICENSE.txt')

$hashes = @()
$hashes += Get-FileHash (Join-Path $libDir 'HIDMaestro.Core.dll') -Algorithm SHA256
$hashes += Get-FileHash $dest -Algorithm SHA256
$hashes | ForEach-Object { "{0}  {1}" -f $_.Hash, $_.Path } |
    Set-Content -Path (Join-Path $depsDir 'SHA256SUMS.txt') -Encoding UTF8

Write-Host 'Dependencies prepared.'
Write-Host "  HIDMaestro: $hidMaestroVersion"
Write-Host "  HidHide:    $hidHideVersion ($($asset.name))"
