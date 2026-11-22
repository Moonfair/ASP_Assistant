param(
    [Parameter(Mandatory = $true)][string]$R8Jar,
    [switch]$Update
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$work = Join-Path $repo ('artifacts/mumu-build-' + [guid]::NewGuid().ToString('N'))
$r8 = (Resolve-Path -LiteralPath $R8Jar).Path
if ((Get-FileHash -LiteralPath $r8).Hash -ne '59753E70A74F918389CC87F1B7D66B5C0862932559167425708DED159E3DE439') {
    throw 'Expected com.android.tools:r8:8.3.37 from Google Maven.'
}
$classes = New-Item -ItemType Directory -Force (Join-Path $work 'classes')
$dex = New-Item -ItemType Directory -Force (Join-Path $work 'dex')
& javac -encoding UTF-8 --release 8 -g:none -d $classes.FullName "$PSScriptRoot/BattleProbe.java" "$PSScriptRoot/BossBoardReader.java" "$PSScriptRoot/BattleProbeTest.java"
if ($LASTEXITCODE) { throw 'javac failed' }
& java -cp $classes.FullName aspa.mumu.BattleProbeTest
if ($LASTEXITCODE) { throw 'BattleProbe fixture failed' }
& java -cp $r8 com.android.tools.r8.D8 --release --min-api 26 --output $dex.FullName "$($classes.FullName)/aspa/mumu/BattleProbe.class" "$($classes.FullName)/aspa/mumu/BossBoardReader.class"
if ($LASTEXITCODE) { throw 'D8 failed' }
$product = Join-Path $repo 'src/ASPAssistant.Core/Resources/MuMuBattle.dex'
$candidate = Join-Path $dex.FullName 'classes.dex'
if ($Update) { Copy-Item -LiteralPath $candidate -Destination $product }
if ((Get-FileHash $candidate).Hash -ne (Get-FileHash $product).Hash) { throw 'Packaged DEX differs; review changes before rebuilding with -Update.' }
Get-FileHash $product
