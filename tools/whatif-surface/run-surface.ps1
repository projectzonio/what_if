# RG-168: runs the What If planet surface experiment (rows s1-s7 + images) on this notebook. Refuses while a NightRacer player runs; takes the measurement lock for the
# whole run (waits while RG-167's experiment or a series holds it, ADR-028); Release build; checks (incl. images) once, determinism in 2 separate processes, cost row s7 in
# 3 separate processes. Raw output: artifacts/whatif-surface/<stamp>/ (git-ignored, sha256.txt); result docs: .planning/race-game/docs/what-if-planet-surface.{md,csv}.
# -NoDocs writes result.md/result.csv into the raw folder instead (debug runs).
param([switch]$NoDocs)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repo/tools/game-unity/measure-lock.ps1"
$player = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'NightRacer*' }
if ($player) { throw "a NightRacer process is running ($($player[0].ProcessName)); not measuring while a player is open" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $repo "artifacts/whatif-surface/$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
# RG-169 (RG-170 decision 3): wait for a measurement held by another worker instead of throwing (max 120 min)
$waitUntil = (Get-Date).AddMinutes(120); $told = $false
while ((Get-MeasureLock) -and "$((Get-MeasureLock).pid)" -ne $env:NR_MEASURE_LOCK_PID -and (Get-Date) -lt $waitUntil) { if (-not $told) { Write-Host "measurement '$((Get-MeasureLock).name)' running; waiting for it to finish (max 120 min)"; $told = $true }; Start-Sleep -Seconds 10 }
$own = Enter-MeasureLock 'RG-168 planet surface experiment'
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    Push-Location $repo
    & dotnet build src/WhatIf.Planet.Surface.Experiment -c Release --nologo -v q 2>&1 | Tee-Object (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    $dll = Join-Path $repo 'artifacts/bin/WhatIf.Planet.Surface.Experiment/release/WhatIf.Planet.Surface.Experiment.dll'
    $sha = (git rev-parse HEAD).Trim()
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $plan = (powercfg /getactivescheme) -replace '^.*:\s*', ''
    $hw = "$cpu; $([Environment]::ProcessorCount) logical CPUs; power plan: $plan; $([Environment]::OSVersion.VersionString); dotnet $(& dotnet --version)"

    foreach ($i in 1, 2) {
        & dotnet $dll det $out "$i" 2>&1 | Tee-Object (Join-Path $out "det$i.log")
        if ($LASTEXITCODE -ne 0) { throw "det run $i failed ($LASTEXITCODE)" }
    }
    # cost rows first (single thread, nothing else of ours running), then the heavy checks + images
    foreach ($i in 1..3) {
        & dotnet $dll cost $out "$i" 2>&1 | Tee-Object (Join-Path $out "cost$i.log")
        if ($LASTEXITCODE -ne 0) { throw "cost run $i failed ($LASTEXITCODE)" }
    }
    & dotnet $dll checks $out 2>&1 | Tee-Object (Join-Path $out 'checks.log')
    if ($LASTEXITCODE -ne 0) { throw "checks failed ($LASTEXITCODE)" }
    $sw.Stop()
    $md = if ($NoDocs) { Join-Path $out 'result.md' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-surface.md' }
    $csv = if ($NoDocs) { Join-Path $out 'result.csv' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-surface.csv' }
    & dotnet $dll report $out $md $csv --sha $sha --hw $hw --wall ([int]$sw.Elapsed.TotalSeconds) 2>&1 | Tee-Object (Join-Path $out 'report.log')
    Get-ChildItem $out -File | Where-Object { $_.Extension -in '.csv', '.log', '.png', '.sha', '.txt' -and $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
    Write-Host "raw output: $out"
}
finally {
    Pop-Location
    if ($own) { Exit-MeasureLock }
}
