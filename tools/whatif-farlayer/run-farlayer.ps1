# RG-171: runs the What If planet far-layer experiment (rows f1-f8 + images) on this notebook. Refuses while a NightRacer player runs; takes the measurement lock for the
# whole run (waits while RG-170's experiment or a series holds it, ADR-028); Release build; cost rows f3-f6 in 3 separate processes (nothing else of ours running),
# then the checks (f1, f2, f7, f8 + images). Raw output: artifacts/whatif-farlayer/<stamp>/ (git-ignored, sha256.txt); result docs: .planning/race-game/docs/what-if-planet-far-layer.{md,csv}.
# -NoDocs writes result.md/result.csv into the raw folder instead (debug runs). Keep one full run <= 5 min.
param([switch]$NoDocs)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repo/tools/game-unity/measure-lock.ps1"
$player = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'NightRacer*' -and $_.ProcessName -notlike '*Tests*' }
if ($player) { throw "a NightRacer process is running ($($player[0].ProcessName)); not measuring while a player is open" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $repo "artifacts/whatif-farlayer/$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
$own = Enter-MeasureLock 'RG-171 planet far layer experiment'
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    Push-Location $repo
    & dotnet build src/WhatIf.Planet.FarLayer.Experiment -c Release --nologo -v q 2>&1 | Tee-Object (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    $dll = Join-Path $repo 'artifacts/bin/WhatIf.Planet.FarLayer.Experiment/release/WhatIf.Planet.FarLayer.Experiment.dll'
    $sha = (git rev-parse HEAD).Trim()
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $plan = (powercfg /getactivescheme) -replace '^.*:\s*', ''
    $hw = "$cpu; $([Environment]::ProcessorCount) logical CPUs; power plan: $plan; $([Environment]::OSVersion.VersionString); dotnet $(& dotnet --version)"

    foreach ($i in 1..3) {
        & dotnet $dll cost $out "$i" 2>&1 | Tee-Object (Join-Path $out "cost$i.log")
        if ($LASTEXITCODE -ne 0) { throw "cost run $i failed ($LASTEXITCODE)" }
    }
    & dotnet $dll checks $out 2>&1 | Tee-Object (Join-Path $out 'checks.log')
    if ($LASTEXITCODE -ne 0) { throw "checks failed ($LASTEXITCODE)" }
    $sw.Stop()
    $md = if ($NoDocs) { Join-Path $out 'result.md' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-far-layer.md' }
    $csv = if ($NoDocs) { Join-Path $out 'result.csv' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-far-layer.csv' }
    & dotnet $dll report $out $md $csv --sha $sha --hw $hw --wall ([int]$sw.Elapsed.TotalSeconds) 2>&1 | Tee-Object (Join-Path $out 'report.log')
    Get-ChildItem $out -File | Where-Object { $_.Extension -in '.csv', '.log', '.png', '.txt' -and $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
    Write-Host "raw output: $out"
}
finally {
    Pop-Location
    if ($own) { Exit-MeasureLock }
}
