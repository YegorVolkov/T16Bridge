param(
    [Parameter(Mandatory=$true)]
    [string]$PayloadDirectory
)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $PayloadDirectory 'HidHideSetup.exe'
$msi = Join-Path $PayloadDirectory 'HidHideSetup.msi'

if (Test-Path $exe) {
    # HidHide v1.5.x bootstrapper accepts /quiet /norestart; /install is not a valid switch.
    $p = Start-Process -FilePath $exe -ArgumentList '/quiet /norestart' -Wait -PassThru
    if ($p.ExitCode -notin @(0, 3010, 1641)) {
        throw "HidHide installer failed with exit code $($p.ExitCode)."
    }
    exit $p.ExitCode
}

if (Test-Path $msi) {
    $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', "`"$msi`"", '/qn', '/norestart') -Wait -PassThru
    if ($p.ExitCode -notin @(0, 3010, 1641)) {
        throw "HidHide MSI failed with exit code $($p.ExitCode)."
    }
    exit $p.ExitCode
}

throw 'HidHide installer payload not found.'
