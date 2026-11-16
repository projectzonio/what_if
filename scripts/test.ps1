# Runs every test runner: each tests/* project (dotnet run), the Python tests (worldgen, knowledge, pool), the wait-run self-test and, when rust/ exists, cargo test.
# Default = build once (scripts/build.ps1, skip with -NoBuild when the caller just built), then run all runners with `dotnet run --no-build` IN PARALLEL (-Throttle 4),
#   each into its own log (<repo>/artifacts/tmp/test-logs/<runner>.log). A failing runner's output is printed and the exit code is 1. -Serial = one `dotnet run` after another.
# Ported from race_game scripts/test.ps1 (RG-134); Unity, KN5 and notebook measurement-lock parts removed.
param([string]$Configuration = 'Release', [switch]$Serial, [switch]$NoBuild, [int]$Throttle = 4)
. "$PSScriptRoot/_common.ps1"

# One entry per runner. Dotnet = project (run with dotnet run); Outside = not part of WhatIf.sln (built once before the parallel run); Cmd/Args = any other command.
$runners = @()
foreach ($p in (Get-ChildItem (Join-Path $script:RepoRoot 'tests') -Directory | Where-Object { Test-Path (Join-Path $_.FullName "$($_.Name).csproj") } | Sort-Object Name)) {
    $runners += @{ Name = $p.Name; Dotnet = "tests/$($p.Name)"; Outside = $false }
}
foreach ($dir in 'tools/worldgen', 'tools/knowledge', 'agent_pool/scripts') {
    foreach ($t in (Get-ChildItem (Join-Path $script:RepoRoot $dir) -Filter 'test_*.py' | Sort-Object Name)) {
        $isolate = if ($dir -eq 'agent_pool/scripts') { @() } else { @('-I') }   # test_pool.py imports pool.py from its own folder
        $runners += @{ Name = "py-$(Split-Path $dir -Leaf)-$($t.BaseName)"; Cmd = 'python'; Args = (@('-X', 'utf8') + $isolate + @("$dir/$($t.Name)")) }
    }
}
$runners += @{ Name = 'wait-run-selftest'; Cmd = 'powershell'; Args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/review/test_wait_run.ps1') }
if (Test-Path (Join-Path $script:RepoRoot 'rust/Cargo.toml')) { $runners += @{ Name = 'cargo-test'; Cmd = 'cargo'; Args = @('test', '--workspace', '--manifest-path', 'rust/Cargo.toml', '--quiet') } }

Exit-OnError {
    Push-Location $script:RepoRoot
    try {
        if ($Serial) {
            foreach ($r in $runners) {
                if ($r.ContainsKey('Dotnet')) { Invoke-Checked dotnet @('run', '--project', $r.Dotnet, '-c', $Configuration, '--nologo') } else { Invoke-Checked $r.Cmd $r.Args }
            }
            return
        }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        if (-not $NoBuild) { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1'); if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed ($LASTEXITCODE)" } }
        foreach ($r in ($runners | Where-Object { $_.ContainsKey('Outside') -and $_.Outside })) { Invoke-Checked dotnet @('build', $r.Dotnet, '-c', $Configuration, '--nologo', '-v', 'q') }   # once, serial: they share project references
        Write-Host "build phase: $([int]$sw.Elapsed.TotalSeconds) s; running $($runners.Count) runners, $Throttle at a time"

        $logDir = Join-Path $script:RepoRoot 'artifacts/tmp/test-logs'
        New-Item -ItemType Directory -Force $logDir | Out-Null
        Get-ChildItem $logDir -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force
        $queue = New-Object System.Collections.Queue
        foreach ($r in $runners) { $queue.Enqueue($r) }
        $running = @(); $done = @()
        while ($queue.Count -gt 0 -or $running.Count -gt 0) {
            while ($queue.Count -gt 0 -and $running.Count -lt $Throttle) {
                $r = $queue.Dequeue()
                $parts = if ($r.ContainsKey('Dotnet')) { @('dotnet', 'run', '--no-build', '--project', $r.Dotnet, '-c', $Configuration, '--nologo') } else { @($r.Cmd) + $r.Args }
                $line = ($parts | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
                $log = Join-Path $logDir "$($r.Name).log"
                $psi = New-Object System.Diagnostics.ProcessStartInfo
                $psi.FileName = $env:ComSpec; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
                $psi.Arguments = '/s /c "' + $line + ' > "' + $log + '" 2>&1"'   # cmd merges stdout and stderr into the runner's own log
                $running += @{ R = $r; Log = $log; P = [Diagnostics.Process]::Start($psi); T = [Diagnostics.Stopwatch]::StartNew() }
            }
            Start-Sleep -Milliseconds 200
            $still = @()
            foreach ($x in $running) { if ($x.P.HasExited) { $x.T.Stop(); $done += $x } else { $still += $x } }
            $running = $still
        }
        $failed = @($done | Where-Object { $_.P.ExitCode -ne 0 })
        foreach ($x in ($done | Sort-Object { $_.R.Name })) { Write-Host ("{0,-4} {1,-52} {2,6:N1} s" -f $(if ($x.P.ExitCode -eq 0) { 'ok' } else { 'FAIL' }), $x.R.Name, $x.T.Elapsed.TotalSeconds) }
        foreach ($x in $failed) { Write-Host "`n===== FAILED runner $($x.R.Name) (exit $($x.P.ExitCode)); log $($x.Log) ====="; Get-Content $x.Log | Write-Host }
        Write-Host "tests: $($done.Count - $failed.Count)/$($done.Count) runners passed in $([int]$sw.Elapsed.TotalSeconds) s (build + run)"
        if ($failed.Count -gt 0) { throw "$($failed.Count) runner(s) failed: $(($failed | ForEach-Object { $_.R.Name }) -join ', ')" }
    }
    finally { Pop-Location }
}
