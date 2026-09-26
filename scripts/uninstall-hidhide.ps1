$ErrorActionPreference = 'Stop'

$roots = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)

$entries = @(
    foreach ($root in $roots) {
        Get-ItemProperty $root -ErrorAction SilentlyContinue |
            Where-Object {
                $_.DisplayName -and
                ($_.DisplayName -eq 'HidHide' -or $_.DisplayName -like 'HidHide *')
            }
    }
) | Sort-Object PSPath -Unique

if ($entries.Count -eq 0) {
    Write-Host 'HidHide is not installed.'
    exit 0
}

$restartRequired = $false

foreach ($entry in $entries) {
    Write-Host "Removing $($entry.DisplayName) $($entry.DisplayVersion)..."

    $productCode = $null

    if ($entry.WindowsInstaller -eq 1 -and
        $entry.PSChildName -match '^\{[0-9A-Fa-f-]+\}$') {
        $productCode = $entry.PSChildName
    }
    elseif ($entry.UninstallString -match '\{[0-9A-Fa-f-]+\}') {
        $productCode = $Matches[0]
    }

    if ($productCode) {
        $p = Start-Process `
            -FilePath 'msiexec.exe' `
            -ArgumentList @('/x', $productCode, '/qn', '/norestart') `
            -Wait `
            -PassThru

        if ($p.ExitCode -in @(3010, 1641)) {
            $restartRequired = $true
        }
        elseif ($p.ExitCode -notin @(0, 1605, 1614)) {
            throw "HidHide MSI uninstall failed with exit code $($p.ExitCode)."
        }

        continue
    }

    if ($entry.QuietUninstallString) {
        $p = Start-Process `
            -FilePath 'cmd.exe' `
            -ArgumentList @('/c', $entry.QuietUninstallString) `
            -Wait `
            -PassThru

        if ($p.ExitCode -in @(3010, 1641)) {
            $restartRequired = $true
        }
        elseif ($p.ExitCode -ne 0) {
            throw "HidHide quiet uninstall failed with exit code $($p.ExitCode)."
        }

        continue
    }

    throw "Could not determine a silent uninstall command for HidHide."
}

if ($restartRequired) {
    exit 3010
}

exit 0
