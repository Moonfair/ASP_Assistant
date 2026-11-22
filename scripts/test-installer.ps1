#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    [string]$PreviousInstaller = $Installer
)

$ErrorActionPreference = 'Stop'
$setup = (Get-Item -LiteralPath $Installer).FullName
$previous = (Get-Item -LiteralPath $PreviousInstaller).FullName
# Do not touch shortcuts, registry or the user's profile when a running assistant vetoes setup.
$appMutex = $null
if ([Threading.Mutex]::TryOpenExisting('ASPAssistant.App.InUse', [ref]$appMutex)) {
    $appMutex.Dispose()
    throw 'ASP Assistant is running. Use a clean test session for the installer smoke test.'
}
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Moonfair.ASPAssistant_is1'
$protocol = 'HKCU:\Software\Classes\aspassistant'
$links = @('Programs', 'DesktopDirectory') | ForEach-Object {
    Join-Path ([Environment]::GetFolderPath($_)) 'ASP Assistant.lnk'
}
if ((Test-Path -LiteralPath $uninstallKey) -or ($links | Where-Object { Test-Path -LiteralPath $_ })) {
    throw 'Run the installer smoke test in a user profile without an installed ASP Assistant or existing shortcuts.'
}

function Invoke-Setup([string]$Executable, [string[]]$Options) {
    $p = Start-Process -FilePath $Executable -ArgumentList (@('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') + $Options) -WindowStyle Hidden -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "$Executable exited with code $($p.ExitCode)." }
}

$id = [guid]::NewGuid().ToString('N')
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = [IO.Directory]::CreateDirectory((Join-Path $tempRoot "aspa-setup-test-$id")).FullName
$app = Join-Path $fixture '安装目录 [x64]'
$uninstaller = Join-Path $app 'unins000.exe'
$exe = Join-Path $app 'ASPAssistant.App.exe'
$runtimeInfo = Join-Path $app 'runtime-dependencies.txt'
$foreignFile = Join-Path $app 'user-file.txt'
$profileDir = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'ASPAssistant'
$profileExisted = Test-Path -LiteralPath $profileDir
$profileMarker = Join-Path $profileDir "installer-test-$id.txt"
$backup = Join-Path $fixture 'portable-protocol.reg'
if (Test-Path -LiteralPath $protocol) {
    reg.exe export 'HKCU\Software\Classes\aspassistant' $backup | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not back up the existing portable protocol registration.' }
}

try {
    [IO.Directory]::CreateDirectory($profileDir) | Out-Null
    [IO.File]::WriteAllText($profileMarker, $id)
    Invoke-Setup $previous @('/TASKS=desktopicon', ('/DIR="' + $app + '"'))
    foreach ($path in @($exe, $runtimeInfo, $uninstaller) + $links) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "First install missing $path" }
    }
    [IO.File]::WriteAllText($foreignFile, $id)
    [IO.File]::WriteAllText($runtimeInfo, 'overwrite this file')

    # Omit /DIR on the second run to exercise automatic detection of a custom install location.
    Invoke-Setup $setup
    $record = Get-ItemProperty -LiteralPath $uninstallKey
    if ($record.InstallLocation.TrimEnd('\') -ne $app -or $record.UninstallString.Trim('"') -ne $uninstaller -or
        [IO.Path]::GetFileName($setup) -ne "ASPAssistant-v$($record.DisplayVersion)-win-x64-setup.exe" -or
        [IO.File]::ReadAllText($runtimeInfo) -notlike "ASPAssistant $($record.DisplayVersion) /*") {
        throw 'Overwrite installation did not update the program and its uninstall registration in place.'
    }
    $command = (Get-Item -LiteralPath "$protocol\shell\open\command").GetValue('')
    if ($command -ne ('"' + $exe + '" --lineup-uri "%1"')) { throw 'Wrong lineup protocol target.' }
    if ([IO.File]::ReadAllText($profileMarker) -ne $id) { throw 'Upgrade changed user data.' }

    Invoke-Setup ($record.UninstallString.Trim('"'))
    foreach ($path in @($uninstallKey, $protocol) + $links) {
        if (Test-Path -LiteralPath $path) { throw "Uninstall left $path" }
    }
    if (@(Get-ChildItem -LiteralPath $app -Force).Count -ne 1 -or
        [IO.File]::ReadAllText($foreignFile) -ne $id -or [IO.File]::ReadAllText($profileMarker) -ne $id) {
        throw 'Uninstall failed to remove its payload or preserve user files.'
    }
    Write-Output 'First install, in-place overwrite, user data retention and uninstall passed.'
} finally {
    try {
        if (Test-Path -LiteralPath $uninstaller) { Invoke-Setup $uninstaller }
    } finally {
        if (Test-Path -LiteralPath $backup) {
            reg.exe import $backup | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not restore the protocol registration from $backup" }
        }
        [IO.File]::Delete($profileMarker)
        if (-not $profileExisted -and (Test-Path -LiteralPath $profileDir) -and
            -not (Get-ChildItem -LiteralPath $profileDir -Force)) { [IO.Directory]::Delete($profileDir) }
        if ($fixture -eq [IO.Path]::GetFullPath((Join-Path $tempRoot "aspa-setup-test-$id"))) {
            Remove-Item -LiteralPath $fixture -Recurse -Force
        }
    }
}
