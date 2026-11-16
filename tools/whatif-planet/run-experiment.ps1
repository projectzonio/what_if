# RG-162 (was RG-161): runs the WI-ADR-001 experiment (checks a-d, r1-r5; RG-165: group w, the WorldGen adapter, w5 timed in the same cost processes; RG-167: group e, adapter v2, e10 in the same cost processes) on this notebook. Refuses while a NightRacer player runs; takes the measurement lock for the
# whole run (waits for a held notebook slot, ADR-028); Release build; check (d) = 3 separate processes, rerun once as 3 more when the spread of the medians
# exceeds 20 %. Raw output: artifacts/whatif-planet/<stamp>/ (git-ignored); result docs: .planning/race-game/docs/what-if-planet-experiment.{md,csv}.
param([switch]$NoDocs)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repo/tools/game-unity/measure-lock.ps1"
$player = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'NightRacer*' }
if ($player) { throw "a NightRacer process is running ($($player[0].ProcessName)); not measuring while a player is open" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $repo "artifacts/whatif-planet/$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
# RG-169 (RG-170 decision 3): wait for a measurement held by another worker instead of throwing (max 120 min)
$waitUntil = (Get-Date).AddMinutes(120); $told = $false
while ((Get-MeasureLock) -and "$((Get-MeasureLock).pid)" -ne $env:NR_MEASURE_LOCK_PID -and (Get-Date) -lt $waitUntil) { if (-not $told) { Write-Host "measurement '$((Get-MeasureLock).name)' running; waiting for it to finish (max 120 min)"; $told = $true }; Start-Sleep -Seconds 10 }
$own = Enter-MeasureLock 'RG-169 planet experiment'
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    Push-Location $repo
    & dotnet build src/WhatIf.Planet.Experiment -c Release --nologo -v q 2>&1 | Tee-Object (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    $dll = Join-Path $repo 'artifacts/bin/WhatIf.Planet.Experiment/release/WhatIf.Planet.Experiment.dll'
    $sha = (git rev-parse HEAD).Trim()
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $plan = (powercfg /getactivescheme) -replace '^.*:\s*', ''
    $hw = "$cpu; $([Environment]::ProcessorCount) logical CPUs; power plan: $plan; $([Environment]::OSVersion.VersionString); dotnet $(& dotnet --version)"

    & dotnet $dll checks $out 2>&1 | Tee-Object (Join-Path $out 'checks.log')
    if ($LASTEXITCODE -ne 0) { throw "checks failed ($LASTEXITCODE)" }
    $costRuns = 0; $rerun = $false
    foreach ($pass in 1, 2) {
        foreach ($i in 1..3) {
            $costRuns++
            & dotnet $dll cost $out "$costRuns" 2>&1 | Tee-Object (Join-Path $out "cost$costRuns.log") -Append
            if ($LASTEXITCODE -ne 0) { throw "cost run $costRuns failed ($LASTEXITCODE)" }
        }
        # spread of the medians over the runs of this pass
        $med = @{}
        foreach ($n in (($costRuns - 2)..$costRuns)) {
            foreach ($l in (Get-Content (Join-Path $out "d_run$n.csv") | Select-Object -Skip 1)) { $p = $l.Split(','); $k = "$($p[0])/$($p[1])"; if (-not $med[$k]) { $med[$k] = @() }; $med[$k] += [double]$p[2] }
        }
        $spread = ($med.Values | ForEach-Object { $m = ($_ | Measure-Object -Minimum -Maximum -Average); ($m.Maximum - $m.Minimum) / $m.Average } | Measure-Object -Maximum).Maximum
        Write-Host ("pass {0}: max spread of the medians {1:P1}" -f $pass, $spread)
        if ($spread -le 0.20) { break }
        if ($pass -eq 1) { $rerun = $true; Write-Host 'spread > 20 %: rerunning check (d) once (3 more runs, d and w5); the report uses only the rerun'; Get-ChildItem $out -File | Where-Object { $_.Name -match '^[dw]_run\d+\.csv$' } | ForEach-Object { Rename-Item $_.FullName ($_.Name + '.pass1') } }
    }
    # RG-167: e10 (adapter v2 sector build) in 3 own processes after check (d): each builds 4 region worlds in parallel first (untimed), which must not
    # heat the CPU right before a (d) run
    foreach ($i in 1..3) {
        & dotnet $dll ecost $out "$i" 2>&1 | Tee-Object (Join-Path $out "ecost$i.log")
        if ($LASTEXITCODE -ne 0) { throw "ecost run $i failed ($LASTEXITCODE)" }
    }
    # RG-169: blocks A / B on the surface base (p1-p5, p7: builds, checks, images, sweep of all sectors), then the worst sectors re-timed in 2 more processes
    & dotnet $dll block $out 2>&1 | Tee-Object (Join-Path $out 'block.log')
    if ($LASTEXITCODE -ne 0) { throw "block run failed ($LASTEXITCODE)" }
    foreach ($i in 2, 3) {
        & dotnet $dll pworst $out "$i" 2>&1 | Tee-Object (Join-Path $out "pworst$i.log")
        if ($LASTEXITCODE -ne 0) { throw "pworst run $i failed ($LASTEXITCODE)" }
    }
    $sw.Stop()
    $md = if ($NoDocs) { Join-Path $out 'result.md' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-experiment.md' }
    $csv = if ($NoDocs) { Join-Path $out 'result.csv' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-experiment.csv' }
    & dotnet $dll report $out $md $csv --sha $sha --hw $hw --wall ([int]$sw.Elapsed.TotalSeconds) 2>&1 | Tee-Object (Join-Path $out 'report.log')
    if ($rerun) { Add-Content $md "`nNote: the first 3 cost runs had a spread above 20 %; check (d) was rerun once (3 more runs) and only the rerun is in the table. First-pass files: d_run*.csv.pass1 and w_run*.csv.pass1 in $out." }
    Get-ChildItem $out -File | Where-Object { $_.Extension -in '.csv', '.log', '.txt' -and $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
    Write-Host "raw output: $out"
}
finally {
    Pop-Location
    if ($own) { Exit-MeasureLock }
}
