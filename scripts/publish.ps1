#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory = 'publish/win-x64',
    [string]$VcRedistDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
if (Test-Path -LiteralPath $outputPath) {
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container) -or
        @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0) {
        throw "Publish output must be an empty directory to prevent stale files: $outputPath"
    }
}

# Use redistributable files, never machine-specific copies from System32 or compiler bin/.
if (-not $VcRedistDirectory) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio C++ redistributables not found. Install C++ Build Tools or pass -VcRedistDirectory.'
    }
    $install = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $install) { throw 'Visual Studio C++ Build Tools not found.' }
    $redistRoot = Join-Path $install.Trim() 'VC/Redist/MSVC'
    $versions = Get-ChildItem -LiteralPath $redistRoot -Directory |
        Where-Object { $_.Name -match '^\d+(\.\d+)+$' } | Sort-Object { [version]$_.Name } -Descending
    foreach ($redistVersion in $versions) {
        $x64 = Join-Path $redistVersion.FullName 'x64'
        if (Test-Path -LiteralPath $x64) {
            $crt = Get-ChildItem -LiteralPath $x64 -Directory -Filter 'Microsoft.VC*.CRT' | Select-Object -First 1
            if ($crt) { $VcRedistDirectory = $crt.FullName; break }
        }
    }
}
if (-not $VcRedistDirectory) { throw 'No x64 Visual C++ CRT redistributable directory found.' }
$VcRedistDirectory = [IO.Path]::GetFullPath($VcRedistDirectory)
$requiredCrt = @('msvcp140.dll', 'msvcp140_1.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'concrt140.dll')
foreach ($name in $requiredCrt) {
    $dll = Join-Path $VcRedistDirectory $name
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Missing redistributable: $dll" }
    # Prevent accidentally supplying x86/ARM64 CRT files to a win-x64 release.
    $bytes = [IO.File]::ReadAllBytes($dll)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) { throw "Not an x64 DLL: $dll" }
}

& dotnet publish (Join-Path $repoRoot 'src/ASPAssistant.App/ASPAssistant.App.csproj') `
    -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o $outputPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

$nativeDirectory = Join-Path $outputPath 'runtimes/win-x64/native'
Get-ChildItem -LiteralPath $VcRedistDirectory -Filter '*.dll' -File | ForEach-Object {
    # Maa loads by absolute DLL path, so dependencies must sit beside those DLLs.
    Copy-Item -LiteralPath $_.FullName -Destination $nativeDirectory
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $outputPath 'LICENSE-ASPAssistant.txt')
$crtVersion = (Get-Item -LiteralPath (Join-Path $VcRedistDirectory 'vcruntime140.dll')).VersionInfo.FileVersion
@"
ASPAssistant $Version / Windows x64
.NET Desktop Runtime is included (self-contained publish).
Visual C++ CRT $crtVersion is deployed beside Maa DLLs in runtimes/win-x64/native.
CRT source: Visual Studio x64 redistributable directory.
Deployment reference: https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files
"@ | Set-Content -LiteralPath (Join-Path $outputPath 'runtime-dependencies.txt') -Encoding utf8

$requiredPayload = @('ASPAssistant.App.exe', 'ASPAssistant.App.dll', 'ASPAssistant.Core.dll',
    'ASPAssistant.App.runtimeconfig.json', 'coreclr.dll',
    'data/operators.json', 'data/equipment.json', 'data/covenants.json', 'data/strategies.json',
    'data/model/ocr/det.onnx', 'data/model/ocr/rec.onnx', 'data/model/ocr/keys.txt',
    'runtimes/win-x64/native/MaaFramework.dll', 'runtimes/win-x64/native/onnxruntime_maa.dll') +
    @($requiredCrt | ForEach-Object { "runtimes/win-x64/native/$_" })
foreach ($name in $requiredPayload) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputPath $name) -PathType Leaf)) {
        throw "Incomplete publish output: $name"
    }
}
$files = @(Get-ChildItem -LiteralPath $outputPath -Recurse -File)
$unexpected = @($files | Where-Object { $_.Extension -in @('.pdb', '.pyc', '.log') -or $_.Name -eq 'tracking.json' })
if ($unexpected.Count -gt 0) { throw "Unexpected runtime/debug files in publish output: $($unexpected.Name -join ', ')" }
$otherPlatforms = @(Get-ChildItem -LiteralPath (Join-Path $outputPath 'runtimes') -Directory | Where-Object Name -ne 'win-x64')
if ($otherPlatforms.Count -gt 0) { throw "Unexpected native platforms: $($otherPlatforms.Name -join ', ')" }
foreach ($catalog in @('operators', 'equipment', 'strategies')) {
    $data = Get-Content -LiteralPath (Join-Path $outputPath "data/$catalog.json") -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($entry in $data.$catalog) {
        if ($entry.iconPath -and -not (Test-Path -LiteralPath (Join-Path $outputPath "data/$($entry.iconPath)") -PathType Leaf)) {
            throw "Missing icon for $($entry.name): $($entry.iconPath)"
        }
    }
}
$size = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
Write-Output "Published $($files.Count) files / $size MiB to $outputPath (CRT $crtVersion)."
