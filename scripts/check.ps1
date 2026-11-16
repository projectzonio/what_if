# Full local gate: lint, build, test. Stops at the first failure with a non-zero exit code.
# RG-134: the test step runs the runners in parallel after the build above (test.ps1 -NoBuild); -Serial = one runner after another as before.
param([switch]$Serial)
$ErrorActionPreference = 'Stop'
foreach ($step in 'lint', 'build', 'test') {
    $file = Join-Path $PSScriptRoot "$step.ps1"
    if ($step -ne 'test') { & powershell -NoProfile -ExecutionPolicy Bypass -File $file }
    elseif ($Serial) { & powershell -NoProfile -ExecutionPolicy Bypass -File $file -Serial }
    else { & powershell -NoProfile -ExecutionPolicy Bypass -File $file -NoBuild }
    if ($LASTEXITCODE -ne 0) { [Console]::Error.WriteLine("check: step '$step' failed ($LASTEXITCODE)"); exit $LASTEXITCODE }
}
Write-Host 'check: all steps passed.'
