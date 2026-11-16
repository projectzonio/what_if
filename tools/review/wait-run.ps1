# Wait helper (RG-134): blocks until a background run is finished or a marker shows up, then exits with a code the caller can branch on.
#   wait-run.ps1 -ExitFile <path> [-Pid <pid>] -TimeoutS <s>            waits for the .exit file written by run-bg.ps1
#   wait-run.ps1 -File <path> -Pattern <regex> [-Pid <pid>] -TimeoutS <s>   waits for a line matching the regex in a log
# Exit codes: 0 found (stdout: the wrapped exit code as text for -ExitFile, or the matching line for -Pattern),
#             1 the process (-Pid) is gone without the marker, 2 timeout.
# -Pid (parameter WaitPid; $PID is reserved in PowerShell) is optional: without it only 0 and 2 are possible. Prints one line to stdout and nothing while waiting (one wake per batch).
param([string]$ExitFile = '', [string]$File = '', [string]$Pattern = '', [Alias("Pid")][int]$WaitPid = 0, [int]$TimeoutS = 3600, [int]$PollS = 2)
$ErrorActionPreference = 'Stop'
if ($ExitFile -and ($File -or $Pattern)) { [Console]::Error.WriteLine('wait-run: use -ExitFile OR -File with -Pattern'); exit 64 }
if (-not $ExitFile -and -not ($File -and $Pattern)) { [Console]::Error.WriteLine('wait-run: give -ExitFile <path> or -File <path> -Pattern <regex>'); exit 64 }

function Test-Marker {
    # returns the text to print when the marker is present, otherwise $null
    if ($ExitFile) {
        if (Test-Path $ExitFile) { return ((Get-Content $ExitFile -Raw) -replace '\s+$', '') }
        return $null
    }
    if (Test-Path $File) {
        $m = Select-String -Path $File -Pattern $Pattern -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($m) { return $m.Line }
    }
    return $null
}

$deadline = (Get-Date).AddSeconds($TimeoutS)
while ($true) {
    $hit = Test-Marker
    if ($null -ne $hit) { Write-Output $hit; exit 0 }
    if ($WaitPid -gt 0 -and -not (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue)) {
        $hit = Test-Marker   # the process may have written the marker right before it ended
        if ($null -ne $hit) { Write-Output $hit; exit 0 }
        Write-Output "process $WaitPid ended without the marker"
        exit 1
    }
    if ((Get-Date) -ge $deadline) { Write-Output "timeout after ${TimeoutS}s"; exit 2 }
    Start-Sleep -Seconds $PollS
}
