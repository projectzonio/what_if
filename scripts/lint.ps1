# Lint: dotnet format verification of the C# solution. Repository hygiene is its own gate: scripts/verify-hygiene.ps1.
. "$PSScriptRoot/_common.ps1"
Exit-OnError {
    Push-Location $script:RepoRoot
    try {
        Invoke-Checked dotnet @('format', 'WhatIf.sln', '--verify-no-changes', '--no-restore')
    }
    finally { Pop-Location }
}
