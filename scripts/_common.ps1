# Shared helpers. Dot-source from other scripts. No hard-coded personal paths: everything derives from the script location.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Invoke-Checked {
    # Runs a native command and throws if its exit code is non-zero.
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @())
    Write-Host "> $Exe $($Arguments -join ' ')"
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Exe $($Arguments -join ' ')"
    }
}

function Exit-OnError {
    # Wraps a script body: any exception -> message on stderr and non-zero exit code.
    param([Parameter(Mandatory)][scriptblock]$Body)
    try {
        & $Body
        exit 0
    }
    catch {
        [Console]::Error.WriteLine("FAILED: $($_.Exception.Message)")
        exit 1
    }
}
