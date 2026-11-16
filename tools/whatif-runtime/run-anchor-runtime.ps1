# RG-184 B5: the runtime t-rows (what-if-planet-runtime.md, budgets unchanged) on ANCHOR content along a city-to-city road path, + row t9 (road continuity at the
# crossed old region borders). WAITS (max 120 min) for a held measurement lock or an open NightRacer player, then takes the lock for the whole run; Release build;
# each mode in its own process: cpath (pick + save the path) -> cdrive 120 m/s -> cverify (t8 + t9) -> cdrive 30 m/s -> cverify (t8) -> ccold (t5) -> cflight (t1/t7) -> creport.
# Raw output: artifacts/whatif-runtime/<stamp>/ (git-ignored, sha256.txt). The section is APPENDED to .planning/race-game/docs/what-if-planet-runtime.md and its rows to
# what-if-planet-runtime.csv (-NoDocs: anchor-section.md / anchor-rows.csv in the raw folder).
param([switch]$NoDocs, [double[]]$Speeds = @(120, 30))
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repo/tools/game-unity/measure-lock.ps1"
function Get-Player { Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'NightRacer*' -and $_.ProcessName -notlike '*Tests*' } }
function Get-Overlap { (@(Get-Process -ErrorAction SilentlyContinue | Where-Object { ($_.ProcessName -eq 'Unity' -and $_.Path -like '*\Editor\Unity.exe') -or $_.ProcessName -like 'NightRacer*' } | ForEach-Object { $_.ProcessName } | Sort-Object -Unique)) -join ', ' }

$waitUntil = (Get-Date).AddMinutes(120); $told = $false
while (((Get-MeasureLock) -and "$((Get-MeasureLock).pid)" -ne $env:NR_MEASURE_LOCK_PID) -or (Get-Player)) {
    if ((Get-Date) -gt $waitUntil) { throw 'measurement lock / NightRacer player still busy after 120 min' }
    if (-not $told) { Write-Host 'measurement lock held or a NightRacer player open; waiting (max 120 min)'; $told = $true }
    Start-Sleep -Seconds 10
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $repo "artifacts/whatif-runtime/$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
$own = Enter-MeasureLock 'RG-184 anchor runtime city-to-city'
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    Push-Location $repo
    $overStart = Get-Overlap
    & dotnet build src/WhatIf.Planet.Runtime.Experiment -c Release --nologo -v q 2>&1 | Tee-Object (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    $dll = Join-Path $repo 'artifacts/bin/WhatIf.Planet.Runtime.Experiment/release/WhatIf.Planet.Runtime.Experiment.dll'
    $sha = (git rev-parse --short HEAD).Trim()
    $dirty = if ((git status --porcelain -- src tools tests) -ne $null) { ' (+ uncommitted changes)' } else { '' }
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $plan = (powercfg /getactivescheme) -replace '^.*:\s*', ''
    $hw = "$cpu; $([Environment]::ProcessorCount) logical CPUs; power plan: $plan; dotnet $(& dotnet --version)"

    $steps = @(, @('cpath'))
    $first = $true
    foreach ($v in $Speeds) {
        $steps += , @('cdrive', '--speed', "$v")
        $steps += , @('cverify', '--speed', "$v", '--t9', $(if ($first) { 'yes' } else { 'no' }))
        $first = $false
    }
    $steps += , @('ccold'); $steps += , @('cflight')
    foreach ($s in $steps) {
        $name = ($s -join '-') -replace '--', ''
        & dotnet $dll $s[0] $out @($s | Select-Object -Skip 1) 2>&1 | Tee-Object (Join-Path $out "$name.log")
        if ($LASTEXITCODE -ne 0) { throw "$name failed ($LASTEXITCODE)" }
    }
    $overEnd = Get-Overlap; $sw.Stop()
    $md = Join-Path $out 'anchor-section.md'; $csv = Join-Path $out 'anchor-rows.csv'
    & dotnet $dll creport $out $md $csv --sha "$sha$dirty" --hw $hw --wall ([int]$sw.Elapsed.TotalSeconds) 2>&1 | Tee-Object (Join-Path $out 'report.log')
    if ($LASTEXITCODE -ne 0) { throw "report failed ($LASTEXITCODE)" }
    $ov = (@($overStart, $overEnd) | Where-Object { $_ } | Sort-Object -Unique) -join ', '
    $text = "`n" + [IO.File]::ReadAllText($md).Replace("`r`n", "`n") + "`n" + $(if ($ov) { "Overlap: [$ov] running at the start or end of the run: NOT a quiet notebook, the timing rows are an upper bound.`n" } else { "No Unity editor or NightRacer player was running at the start or end of the run.`n" }) + "Raw folder artifacts/whatif-runtime/$stamp (git-ignored, sha256.txt).`n"
    if (-not $NoDocs) {
        [IO.File]::AppendAllText((Join-Path $repo '.planning/race-game/docs/what-if-planet-runtime.md'), $text)
        [IO.File]::AppendAllText((Join-Path $repo '.planning/race-game/docs/what-if-planet-runtime.csv'), ([IO.File]::ReadAllText($csv).Replace("`r`n", "`n")))
    }
    Get-ChildItem $out -File | Where-Object { $_.Extension -in '.csv', '.log', '.txt', '.md' -and $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
    Write-Host "raw output: $out"
}
finally {
    Pop-Location
    if ($own) { Exit-MeasureLock }
}
