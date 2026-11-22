#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory = 'publish',
    [string]$VcRedistDirectory,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
if (-not $IsccPath) {
    $IsccPath = @(
        (Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
        (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6/ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Install Inno Setup 6.7.3 or pass -IsccPath with the path to ISCC.exe.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$jobName = 'ASPAssistant-Publish-' + [guid]::NewGuid().ToString('N')
$staging = [IO.Directory]::CreateDirectory((Join-Path $tempRoot $jobName)).FullName
try {
    $payload = Join-Path $staging 'win-x64'
    & "$PSScriptRoot/publish-payload.ps1" -Version $Version -OutputDirectory $payload -VcRedistDirectory $VcRedistDirectory
    $baseName = "ASPAssistant-v$Version-win-x64-setup"
    & $IsccPath /Q "/DAppVersion=$Version" "/DPayloadDir=$payload" "/F$baseName" "/O$staging" "$PSScriptRoot/installer/ASPAssistant.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE)." }
    [IO.Directory]::CreateDirectory($outputPath) | Out-Null
    $installer = Join-Path $outputPath "$baseName.exe"
    [IO.File]::Move((Join-Path $staging "$baseName.exe"), $installer, $true)
    Write-Output "Installer: $installer"
} finally {
    if ($staging -eq [IO.Path]::GetFullPath((Join-Path $tempRoot $jobName))) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
