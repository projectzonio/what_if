# RG-179: What If planet viewer runs v1..v6 on the notebook (wraps tools/kn5-unity/run-kn5.ps1 -Mode stream; the player takes --stream-map planet).
# Usage: run-viewer.ps1 -Row fly|drive|seams|shots|manual|orbit|oshots|roads (RG-183: orbit = descent run, oshots = orbit shots, roads = road shots; -Bake default thread; -Spawn orbit for the descent) [-Preset max|low] [-Spawn p1] [-Emit own|shared] [-Bake main|thread] [-Empty] [-Duration 300] [-Stamp <label>] [-Extra '--planet-...']
# Reports: artifacts/whatif-viewer/<stamp>/ (player report kn5-stream-<series>.json, planet-<series>.json, frame csv, run-*.os.json with GPU memory, shots-<series>/).
param([ValidateSet('fly', 'drive', 'seams', 'shots', 'manual', 'orbit', 'oshots', 'roads')][string]$Row = 'fly', [ValidateSet('max', 'low')][string]$Preset = 'max', [string]$Spawn = 'p1',
    [ValidateSet('own', 'shared')][string]$Emit = 'own', [ValidateSet('main', 'thread')][string]$Bake = 'thread', [switch]$Empty, [double]$Duration = 300, [double]$Warmup = 10,
    [string]$Stamp = '', [string]$Extra = '', [int]$TimeoutS = 1500)
$root = (Resolve-Path "$PSScriptRoot/../..").Path
if (-not $Stamp) { $Stamp = Get-Date -Format 'yyyyMMdd-HHmmss' }
$dir = Join-Path $root "artifacts/whatif-viewer/$Stamp"; New-Item -ItemType Directory -Force $dir | Out-Null
$series = "$Row-$Preset-$Spawn-$Emit-$Bake" + $(if ($Empty) { '-empty' } else { '' })
$run = switch ($Row) { 'seams' { 'shots' } 'shots' { 'shots' } 'oshots' { 'shots' } 'roads' { 'shots' } default { $Row } }
$extra = "--stream-map planet --stream-run $run --planet-spawn $Spawn --planet-emit $Emit --planet-bake $Bake --graphics-lab $(Join-Path $PSScriptRoot "presets/planet-$Preset.json") --duration $Duration --warmup $Warmup"
if ($Row -eq 'seams') { $extra += ' --planet-do seams' } elseif ($Row -eq 'shots') { $extra += ' --planet-do shots' } elseif ($Row -eq 'oshots') { $extra += ' --planet-do orbit' } elseif ($Row -eq 'roads') { $extra += ' --planet-do roads' }
if ($Empty) { $extra += ' --planet-empty 1' }
if ($Extra) { $extra += " $Extra" }
$win = if ($Row -in 'seams', 'shots', 'manual', 'oshots', 'roads') { '-Windowed' } else { '' }
$a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools/kn5-unity/run-kn5.ps1'), '-Mode', 'stream', '-Series', $series, '-ReportDir', $dir, '-SkipFixtureHash', '-NoSummary', '-TimeoutS', "$TimeoutS", '-PlayerExtra', $extra)
if ($win) { $a += $win }
& powershell @a
exit $LASTEXITCODE
