# Background wrapper (RG-134): runs one command, writes stdout+stderr to <OutDir>/<Name>.log and ALWAYS writes <OutDir>/<Name>.exit with the exit code
# (in a finally, also when the command is missing or this wrapper is stopped). The marker is the fix for the RG-119 loops that waited for a "done" line
# that went to stdout instead of the file (four loops ran 12-14 h). Wait for it with wait-run.ps1 -ExitFile <OutDir>/<Name>.exit.
# Usage: run-bg.ps1 -Name <n> -OutDir <dir> -- <command> [args...]       (a .ps1 command runs in powershell -NoProfile -ExecutionPolicy Bypass -File)
#   run it with run_in_background: powershell -NoProfile -ExecutionPolicy Bypass -File tools\review\run-bg.ps1 -Name build -OutDir artifacts\tmp -- tools\kn5-unity\build-player.ps1
# Files: <n>.log (output), <n>.pid (this wrapper's PID, for wait-run.ps1 -Pid), <n>.exit (exit code as text; written last, atomically).
# No param() block: PowerShell 5.1 binds the "--" separator as a parameter name, so the arguments are parsed by hand from $args.
$Name = $null; $OutDir = $null; $Command = @()
for ($i = 0; $i -lt $args.Count; $i++) {
    if ($args[$i] -eq '--') { $Command = @($args | Select-Object -Skip ($i + 1)); break }
    elseif ($args[$i] -eq '-Name' -and $i + 1 -lt $args.Count) { $Name = $args[++$i] }
    elseif ($args[$i] -eq '-OutDir' -and $i + 1 -lt $args.Count) { $OutDir = $args[++$i] }
    else { [Console]::Error.WriteLine("run-bg: unexpected argument '$($args[$i])' (usage: -Name n -OutDir d -- command args)"); exit 64 }
}
$ErrorActionPreference = 'Stop'
if (-not $Name -or -not $OutDir -or -not $Command) { [Console]::Error.WriteLine('run-bg: usage: -Name n -OutDir d -- command args'); exit 64 }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$log = Join-Path $OutDir "$Name.log"; $exitFile = Join-Path $OutDir "$Name.exit"
Remove-Item $exitFile -ErrorAction SilentlyContinue
Set-Content (Join-Path $OutDir "$Name.pid") "$PID" -Encoding ascii
$code = 1
try {
    function Quote([string]$a) { if ($a -match '[\s&|<>^()"]' -or $a -eq '') { '"' + ($a -replace '"', '\"') + '"' } else { $a } }
    $parts = @($Command)
    if ($parts[0] -like '*.ps1') { $parts = @('powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File') + $parts }
    $line = ($parts | ForEach-Object { Quote $_ }) -join ' '
    # cmd merges stdout and stderr into the log itself (PowerShell 5.1 would wrap native stderr lines in ErrorRecords)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $env:ComSpec
    $psi.Arguments = '/s /c "' + $line + ' > "' + $log + '" 2>&1"'
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.WaitForExit()
    $code = $p.ExitCode
}
catch { $code = 1; Add-Content $log "run-bg: $($_.Exception.Message)" }
finally {
    $tmp = "$exitFile.tmp"
    Set-Content $tmp "$code" -Encoding ascii
    Move-Item $tmp $exitFile -Force
}
exit $code
