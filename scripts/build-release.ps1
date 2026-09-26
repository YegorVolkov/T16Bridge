param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

& (Join-Path $PSScriptRoot 'prepare-deps.ps1') -Root $root

$publishDir = Join-Path $root 'artifacts\publish'
$installerOut = Join-Path $root 'artifacts\installer'
Remove-Item -Recurse -Force $publishDir, $installerOut -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir, $installerOut | Out-Null

Write-Host 'Publishing T16Bridge self-contained...'
dotnet publish (Join-Path $root 'src\T16Bridge\T16Bridge.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

$iscc = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it or run this through GitHub Actions.'
}

Write-Host 'Building T16BridgeSetup-x64.exe...'
& $iscc "/DRepoRoot=$root" "/DOutputDir=$installerOut" (Join-Path $root 'installer\T16Bridge.iss')

Write-Host "Release artifacts: $installerOut"
