# Builds the solution (default) or a single project. Exit code 0 only on success.
param([string]$Target = 'WhatIf.sln', [string]$Configuration = 'Release')
. "$PSScriptRoot/_common.ps1"
Exit-OnError {
    Push-Location $script:RepoRoot
    try {
        Invoke-Checked dotnet @('build', $Target, '-c', $Configuration, '--nologo')
    }
    finally { Pop-Location }
}
