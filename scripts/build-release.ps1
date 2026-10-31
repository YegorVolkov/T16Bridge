param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$projectPath = Join-Path $root 'src\T16Bridge\T16Bridge.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$appVersion = [string]$project.Project.PropertyGroup.Version
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid application version: $appVersion"
}
$releaseNotesRelative = "docs/releases/$appVersion.md"
if (-not (Test-Path -LiteralPath (Join-Path $root $releaseNotesRelative))) {
    throw "Release notes are missing: $releaseNotesRelative"
}
if ((Get-Content -LiteralPath (Join-Path $root $releaseNotesRelative) -TotalCount 1) -ne "# T16Bridge $appVersion") {
    throw "Release notes heading does not match version $appVersion."
}
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$appVersion") {
    throw "Release tag $($env:GITHUB_REF_NAME) does not match application version v$appVersion."
}
Write-Host "Building T16Bridge $appVersion"

& (Join-Path $PSScriptRoot 'prepare-deps.ps1') -Root $root

$publishDir = Join-Path $root 'artifacts\publish'
$installerOut = Join-Path $root 'artifacts\installer'
foreach ($outputPath in @($publishDir, $installerOut)) {
    $resolvedOutput = [IO.Path]::GetFullPath($outputPath)
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedOutput.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove output outside artifacts: $resolvedOutput"
    }
    if (Test-Path -LiteralPath $resolvedOutput) {
        Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
    }
}
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

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Resolve Inno Setup compiler robustly.
# Chocolatey on GitHub Actions usually exposes ISCC.exe through its shim
# (C:\ProgramData\chocolatey\bin), while a normal local install lives under
# Program Files (x86)\Inno Setup 6.
$iscc = $null

$command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($command) {
    $iscc = $command.Source
}

if (-not $iscc) {
    $candidates = @(
        (Join-Path $root 'artifacts\tools\InnoSetup6\ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:ChocolateyInstall\bin\ISCC.exe",
        "C:\ProgramData\chocolatey\bin\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    $iscc = $candidates | Select-Object -First 1
}

if (-not $iscc) {
    throw 'Inno Setup 6 (ISCC.exe) was not found. Install Inno Setup 6 or ensure ISCC.exe is available on PATH.'
}

Write-Host "Using Inno Setup compiler: $iscc"

Write-Host 'Building T16BridgeSetup-x64.exe...'
& $iscc "/DRepoRoot=$root" "/DOutputDir=$installerOut" "/DAppVersion=$appVersion" (Join-Path $root 'installer\T16Bridge.iss')

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}

Write-Host "Release artifacts: $installerOut"
if ($env:GITHUB_OUTPUT) {
    "version=$appVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "release_notes=$releaseNotesRelative" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}
