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
& $iscc "/DRepoRoot=$root" "/DOutputDir=$installerOut" (Join-Path $root 'installer\T16Bridge.iss')

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler failed with exit code $LASTEXITCODE."
}

Write-Host "Release artifacts: $installerOut"
