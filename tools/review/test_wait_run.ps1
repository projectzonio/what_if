# Self-test of wait-run.ps1 / run-bg.ps1 (RG-134): exit 0 (found), 1 (process gone without marker), 2 (timeout). Exit code 0 only if all cases pass.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\review\test_wait_run.ps1 [-Tmp <dir>]
param([string]$Tmp = '')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $Tmp) { $Tmp = Join-Path $root 'artifacts/tmp/test-wait-run' }
Remove-Item $Tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $Tmp | Out-Null
$wait = Join-Path $PSScriptRoot 'wait-run.ps1'; $bg = Join-Path $PSScriptRoot 'run-bg.ps1'
$fail = 0
function Check([string]$what, $got, $want) {
    if ("$got" -eq "$want") { Write-Host "ok   $what = $got" } else { Write-Host "FAIL $what = $got (want $want)"; $script:fail++ }
}
function Wait([string[]]$a) { $o = & powershell -NoProfile -ExecutionPolicy Bypass -File $wait @a; return @{ code = $LASTEXITCODE; out = ($o -join "`n") } }

# run-bg: exit code marker + log, also for a failing command
& powershell -NoProfile -ExecutionPolicy Bypass -File $bg -Name ok -OutDir $Tmp -- cmd /c "echo hello & exit 0" | Out-Null
Check 'run-bg exit file (0)' ((Get-Content (Join-Path $Tmp 'ok.exit') -Raw).Trim()) '0'
Check 'run-bg log has output' ((Get-Content (Join-Path $Tmp 'ok.log') -Raw) -match 'hello') 'True'
& powershell -NoProfile -ExecutionPolicy Bypass -File $bg -Name bad -OutDir $Tmp -- cmd /c "echo oops 1>&2 & exit 3" | Out-Null
Check 'run-bg exit file (3)' ((Get-Content (Join-Path $Tmp 'bad.exit') -Raw).Trim()) '3'
Check 'run-bg log has stderr' ((Get-Content (Join-Path $Tmp 'bad.log') -Raw) -match 'oops') 'True'

# case 0: exit marker found, wrapped code passed through as text
$r = Wait @('-ExitFile', (Join-Path $Tmp 'bad.exit'), '-TimeoutS', '10')
Check 'wait ExitFile found -> exit' $r.code 0
Check 'wait ExitFile found -> wrapped code text' $r.out '3'
# case 0: pattern found in a log
$r = Wait @('-File', (Join-Path $Tmp 'ok.log'), '-Pattern', '^hel+o', '-TimeoutS', '10')
Check 'wait Pattern found -> exit' $r.code 0
# case 1: the process is gone and wrote no marker
$gone = Start-Process cmd -ArgumentList '/c', 'exit 0' -PassThru -WindowStyle Hidden; $gone.WaitForExit()
$r = Wait @('-ExitFile', (Join-Path $Tmp 'never.exit'), '-Pid', "$($gone.Id)", '-TimeoutS', '10')
Check 'wait process gone -> exit' $r.code 1
$r = Wait @('-File', (Join-Path $Tmp 'ok.log'), '-Pattern', 'no-such-marker', '-Pid', "$($gone.Id)", '-TimeoutS', '10')
Check 'wait process gone (pattern) -> exit' $r.code 1
# case 2: timeout while the process is still alive
$live = Start-Process cmd -ArgumentList '/c', 'ping -n 30 127.0.0.1 > nul' -PassThru -WindowStyle Hidden
$r = Wait @('-ExitFile', (Join-Path $Tmp 'never.exit'), '-Pid', "$($live.Id)", '-TimeoutS', '3', '-PollS', '1')
Check 'wait timeout -> exit' $r.code 2
Stop-Process -Id $live.Id -Force -ErrorAction SilentlyContinue
# usage error
$r = Wait @('-TimeoutS', '1')
Check 'wait without arguments -> exit' $r.code 64

if ($fail -gt 0) { Write-Host "test_wait_run: $fail case(s) FAILED"; exit 1 }
Write-Host 'test_wait_run: all cases passed'
exit 0
