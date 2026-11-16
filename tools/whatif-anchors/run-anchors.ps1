# RG-177: anchor spike 1 - variant B (WhatIf.Planet.Anchors; the region-first variant A was removed in RG-185) on 5 x 5 regions around the RG-169
# blocks A and B (seed 1). Waits for a held measurement lock (max 120 min, ADR-028), then takes it for the whole run; Release build; n5 = 3 separate hash
# processes (forward, reverse request order, 4 threads); then one measure process (rows n1-n4, c1-c2 with -Repeats repeats, top views).
# RG-181: -Corridors = 3 hash processes (forward, reverse, 4 threads) + rows r1-r7 before (Legacy builder) / after as the section "Corridors B2 (RG-181)" (-NoDocs: corridors-section.md + corridors.csv in the raw folder).
# RG-178: -Sectors = rows s1-s5 (one process, CPU, single thread timings) and s6 (runtime drive, B only since RG-185, -DriveSeconds) instead of the RG-177 rows;
# the section is appended to the doc (-NoDocs: sectors-section.md in the raw folder).
# RG-182: -Settlements = rows e1-e5 (settlement worlds before / after: town and city streets, pinned corridor entrances) + the plan PNGs, section "Settlements B3 (RG-182)" (-NoDocs: settlements-section.md + settlements.csv in the raw folder).
# RG-184: -Closeout = 3 hash processes (r7) + sectors --closeout (stall summary, s1-s5, s3 warm x 3) -> rows k1-k4, section "Close-out B4 (RG-184)" (-NoDocs: closeout-section.md in the raw folder).
# Raw output: artifacts/whatif-anchors/<stamp>/ (git-ignored); result docs: .planning/race-game/docs/what-if-planet-anchor-spike.{md,csv} (-NoDocs: into the raw folder).
# A Unity editor or NightRacer player running at the same time is recorded in the doc (the timing rows are then not from a quiet notebook).
param([switch]$NoDocs, [int]$Repeats = 3, [switch]$Sectors, [switch]$Corridors, [switch]$Settlements, [switch]$Closeout, [int]$DriveSeconds = 300)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
. "$repo/tools/game-unity/measure-lock.ps1"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $repo "artifacts/whatif-anchors/$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
$waitUntil = (Get-Date).AddMinutes(120); $told = $false
while ((Get-MeasureLock) -and "$((Get-MeasureLock).pid)" -ne $env:NR_MEASURE_LOCK_PID -and (Get-Date) -lt $waitUntil) { if (-not $told) { Write-Host "measurement '$((Get-MeasureLock).name)' running; waiting for it to finish (max 120 min)"; $told = $true }; Start-Sleep -Seconds 10 }
$own = Enter-MeasureLock $(if ($Sectors) { 'RG-178 anchor spike 2 (sectors)' } elseif ($Corridors) { 'RG-181 corridors B2' } elseif ($Settlements) { 'RG-182 settlements B3' } elseif ($Closeout) { 'RG-184 close-out B4' } else { 'RG-177 anchor spike' })
# RG-178: only a Unity EDITOR (...\Editor\Unity.exe) or a NightRacer player counts; the idle Unity Hub helper (Unity Hub\resources\unity.exe) and the licensing client do not.
function Get-Overlap { (@(Get-Process -ErrorAction SilentlyContinue | Where-Object { ($_.ProcessName -eq 'Unity' -and $_.Path -like '*\Editor\Unity.exe') -or $_.ProcessName -like 'NightRacer*' } | ForEach-Object { $_.ProcessName } | Sort-Object -Unique)) -join ', ' }
$sw = [Diagnostics.Stopwatch]::StartNew()
try {
    Push-Location $repo
    $overStart = Get-Overlap
    & dotnet build src/WhatIf.Planet.Anchors.Experiment -c Release --nologo -v q 2>&1 | Tee-Object (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    $dll = Join-Path $repo 'artifacts/bin/WhatIf.Planet.Anchors.Experiment/release/WhatIf.Planet.Anchors.Experiment.dll'
    $sha = (git rev-parse --short HEAD).Trim()
    $dirty = if ((git status --porcelain -- src tools tests) -ne $null) { ' (+ uncommitted changes)' } else { '' }
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $plan = (powercfg /getactivescheme) -replace '^.*:\s*', ''
    $hw = "$cpu; $([Environment]::ProcessorCount) logical CPUs; power plan: $plan; dotnet $(& dotnet --version)"

    if ($Corridors) {
        foreach ($mode in 'forward', 'reverse', 'threads4') {
            & dotnet $dll hash $out $mode 2>&1 | Tee-Object (Join-Path $out "hash-$mode.log")
            if ($LASTEXITCODE -ne 0) { throw "hash $mode failed ($LASTEXITCODE)" }
        }
        $overMid = Get-Overlap
        $ovAll = (@($overStart, $overMid) | Where-Object { $_ } | Sort-Object -Unique) -join ', '
        $unity = if ($ovAll) { "Overlap: [$ovAll] running during the run: NOT a quiet notebook, the timing rows are an upper bound." } else { 'No Unity editor or NightRacer player was running at the start or before the corridors process.' }
        $cmd = @($dll, 'corridors', $out, '--sha', "$sha$dirty", '--hw', $hw, '--unity', $unity)
        if (-not $NoDocs) { $cmd += @('--md', (Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.md'), '--csv', (Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.csv')) }
        & dotnet @cmd 2>&1 | Tee-Object (Join-Path $out 'corridors.log')
        if ($LASTEXITCODE -ne 0) { throw "corridors failed ($LASTEXITCODE)" }
        Get-ChildItem $out -File | Where-Object { $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + ''), '')" } | Set-Content (Join-Path $out 'sha256.txt')
        Write-Host "raw output: $out"
        return
    }

    if ($Settlements) {
        $overMid = Get-Overlap
        $ovAll = (@($overStart, $overMid) | Where-Object { $_ } | Sort-Object -Unique) -join ', '
        $unity = if ($ovAll) { "Overlap: [$ovAll] running during the run: NOT a quiet notebook, the timing rows are an upper bound." } else { 'No Unity editor or NightRacer player was running at the start of the run.' }
        $cmd = @($dll, 'settlements', $out, '--sha', "$sha$dirty", '--hw', $hw, '--unity', $unity)
        if (-not $NoDocs) { $cmd += @('--md', (Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.md'), '--csv', (Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.csv')) }
        & dotnet @cmd 2>&1 | Tee-Object (Join-Path $out 'settlements.log')
        if ($LASTEXITCODE -ne 0) { throw "settlements failed ($LASTEXITCODE)" }
        Get-ChildItem $out -File | Where-Object { $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + ''), '')" } | Set-Content (Join-Path $out 'sha256.txt')
        Write-Host "raw output: $out"
        return
    }

    if ($Closeout) {
        # RG-184: r7 (3 hash processes) + one sectors process with --closeout (stall summary, s1-s5, s3 warm x3) -> rows k1-k4; section appended to the spike doc,
        # k rows appended to the spike csv, the full sectors csv replaces -sectors.csv (as the RG-178/181/182 reruns did)
        foreach ($mode in 'forward', 'reverse', 'threads4') {
            & dotnet $dll hash $out $mode 2>&1 | Tee-Object (Join-Path $out "hash-$mode.log")
            if ($LASTEXITCODE -ne 0) { throw "hash $mode failed ($LASTEXITCODE)" }
        }
        $smd = Join-Path $out 'sectors.md'; $scsv = Join-Path $out 'sectors.csv'
        & dotnet $dll sectors $out --md $smd --csv $scsv --sha "$sha$dirty" --hw $hw --closeout 2>&1 | Tee-Object (Join-Path $out 'sectors.log')
        if ($LASTEXITCODE -ne 0) { throw "sectors failed ($LASTEXITCODE)" }
        $overEnd = Get-Overlap; $sw.Stop()
        $sb = New-Object Text.StringBuilder
        [void]$sb.Append("`n## Close-out B4 (RG-184): stall exits, anchor cache, street carve, warm sector time (rows k1-k4; s1-s5 rerun)`n`n")
        $ov = (@($overStart, $overEnd) | Where-Object { $_ } | Sort-Object -Unique) -join ', '
        [void]$sb.Append($(if ($ov) { "Overlap: [$ov] running at the start or end of the run: NOT a quiet notebook, the timing rows are an upper bound.`n`n" } else { "No Unity editor or NightRacer player was running at the start or end of the run.`n`n" }))
        [void]$sb.Append([IO.File]::ReadAllText($smd).Replace("`r`n", "`n"))
        [void]$sb.Append("`nRunner wall time: $([int]$sw.Elapsed.TotalSeconds) s; raw folder artifacts/whatif-anchors/$stamp (git-ignored; stalls.txt = every stall-exit segment with its stations).`n")
        if ($NoDocs) { [IO.File]::WriteAllText((Join-Path $out 'closeout-section.md'), $sb.ToString()) } else {
            [IO.File]::AppendAllText((Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.md'), $sb.ToString())
            $k = (Get-Content $scsv | Where-Object { $_ -like '"k*' }) -join "`n"
            [IO.File]::AppendAllText((Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.csv'), "$k`n")
            Copy-Item $scsv (Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike-sectors.csv') -Force
        }
        Get-ChildItem $out -File | Where-Object { $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
        Write-Host "raw output: $out"
        return
    }

    if ($Sectors) {
        & dotnet build src/WhatIf.Planet.Runtime.Experiment -c Release --nologo -v q 2>&1 | Tee-Object (Join-Path $out 'build-runtime.log')
        if ($LASTEXITCODE -ne 0) { throw "runtime build failed ($LASTEXITCODE)" }
        $rdll = Join-Path $repo 'artifacts/bin/WhatIf.Planet.Runtime.Experiment/release/WhatIf.Planet.Runtime.Experiment.dll'
        $smd = Join-Path $out 'sectors.md'; $scsv = Join-Path $out 'sectors.csv'
        & dotnet $dll sectors $out --md $smd --csv $scsv --sha "$sha$dirty" --hw $hw 2>&1 | Tee-Object (Join-Path $out 'sectors.log')
        if ($LASTEXITCODE -ne 0) { throw "sectors failed ($LASTEXITCODE)" }
        & dotnet $rdll adrive $out --seconds $DriveSeconds 2>&1 | Tee-Object (Join-Path $out 'adrive-B.log')
        if ($LASTEXITCODE -ne 0) { throw "adrive failed ($LASTEXITCODE)" }
        $overEnd = Get-Overlap
        $sb = New-Object Text.StringBuilder
        [void]$sb.Append("`n## Sectors from anchors (RG-178 rows s1-s6; rerun $(if ($env:NR_RERUN_TAG) { $env:NR_RERUN_TAG } else { "RG-178" }))`n`n")
        $ov = (@($overStart, $overEnd) | Where-Object { $_ } | Sort-Object -Unique) -join ', '
        [void]$sb.Append($(if ($ov) { "Overlap: [$ov] running at the start or end of the run: NOT a quiet notebook, the timing rows are an upper bound.`n`n" } else { "No Unity editor or NightRacer player was running at the start or end of the run.`n`n" }))
        [void]$sb.Append([IO.File]::ReadAllText($smd).Replace("`r`n", "`n"))
        [void]$sb.Append("`n### s6 runtime drive (B = anchor path; the region path A was removed in RG-185)`n`n| row | A | B | note |`n|---|---|---|---|`n")
        $rb = Get-Content (Join-Path $out 'rows-adrive-B.txt')
        foreach ($line in $rb) { $y = $line.Split('|'); [void]$sb.Append("| $($y[0] -replace '-B$', '') | - | $($y[2]) [$($y[4])] (threshold $($y[3])) | B: $($y[5]) |`n") }
        $sw.Stop()
        [void]$sb.Append("`nRunner wall time: $([int]$sw.Elapsed.TotalSeconds) s; raw folder artifacts/whatif-anchors/$stamp (git-ignored).`n")
        if ($NoDocs) { [IO.File]::WriteAllText((Join-Path $out 'sectors-section.md'), $sb.ToString()) } else {
            [IO.File]::AppendAllText((Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.md'), $sb.ToString())
            Copy-Item $scsv (Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike-sectors.csv') -Force
        }
        Get-ChildItem $out -File | Where-Object { $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
        Write-Host "raw output: $out"
        return
    }

    foreach ($mode in 'forward', 'reverse', 'threads4') {
        & dotnet $dll hash $out $mode 2>&1 | Tee-Object (Join-Path $out "hash-$mode.log")
        if ($LASTEXITCODE -ne 0) { throw "hash $mode failed ($LASTEXITCODE)" }
    }
    $overMid = Get-Overlap
    $md = if ($NoDocs) { Join-Path $out 'result.md' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.md' }
    $csv = if ($NoDocs) { Join-Path $out 'result.csv' } else { Join-Path $repo '.planning/race-game/docs/what-if-planet-anchor-spike.csv' }
    $ovAll = (@($overStart, $overMid) | Where-Object { $_ } | Sort-Object -Unique) -join ', '
    $unity = if ($ovAll) { "Overlap: [$ovAll] running during the run: NOT a quiet notebook, the timing rows are an upper bound." } else { 'No Unity editor or NightRacer player was running at the start or before the measure process.' }
    & dotnet $dll measure $out --repeats $Repeats --md $md --csv $csv --sha "$sha$dirty" --hw $hw --unity $unity 2>&1 | Tee-Object (Join-Path $out 'measure.log')
    if ($LASTEXITCODE -ne 0) { throw "measure failed ($LASTEXITCODE)" }
    $overEnd = Get-Overlap
    if ($overEnd) { [IO.File]::AppendAllText($md, "`nOverlap at the end of the run: $overEnd.`n") }   # LF only (the repo is LF; Add-Content writes CRLF)
    $sw.Stop()
    [IO.File]::AppendAllText($md, "`nRunner wall time: $([int]$sw.Elapsed.TotalSeconds) s; raw folder artifacts/whatif-anchors/$stamp (git-ignored, sha256.txt lists its files).`n")
    Get-ChildItem $out -File | Where-Object { $_.Name -ne 'sha256.txt' } | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($_.Path -replace [regex]::Escape($out + '\'), '')" } | Set-Content (Join-Path $out 'sha256.txt')
    Write-Host "raw output: $out"
}
finally {
    Pop-Location
    if ($own) { Exit-MeasureLock }
}
