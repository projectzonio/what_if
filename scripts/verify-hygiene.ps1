# Repository hygiene gate (exit 0 = clean). Checks tracked files and files that would be added:
#  - no binaries / build output (dll exe pdb so dylib, bin/ obj/ target/ artifacts/ .vs/), no secrets files (.env, *.pfx, *.key)
#  - no file over 1 MB (raw run data stays local + a SHA-256 list)
#  - no ChatGPT UI junk in research/ (favicons, utm_source=chatgpt.com, images.openai.com)
#  - task_pool.json valid (pool.py validate)
# Simplified from race_game scripts/verify-hygiene.ps1 (Unity/KN5 rules removed).
. "$PSScriptRoot/_common.ps1"
Exit-OnError {
    Push-Location $script:RepoRoot
    try {
        $files = @(git ls-files --cached --others --exclude-standard) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
        $bad = @()
        foreach ($f in $files) {
            $n = $f.Replace([string][char]92, '/')
            if ($n -match '\.(dll|exe|pdb|so|dylib|pfx|key|pem)$') { $bad += "$n : binary or key file" }
            elseif ($n -match '(^|/)(bin|obj|target|artifacts|\.vs)/') { $bad += "$n : build output" }
            elseif ($n -match '(^|/)\.env(\.|$)' -and $n -notmatch 'example') { $bad += "$n : secrets file" }
            elseif ((Get-Item -LiteralPath $f).Length -gt 1MB) { $bad += "$n : larger than 1 MB" }
        }
        foreach ($f in ($files | Where-Object { $_.Replace([string][char]92, '/') -like 'research/*.md' })) {
            if (Select-String -LiteralPath $f -Pattern 'utm_source=chatgpt\.com|images\.openai\.com|google\.com/s2/favicons' -Quiet) { $bad += "$f : ChatGPT UI junk" }
        }
        if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "HYGIENE: $_" }; throw "$($bad.Count) hygiene violation(s)" }
        Invoke-Checked python @('-X', 'utf8', 'agent_pool/scripts/pool.py', 'validate')
        Write-Host "verify-hygiene: $($files.Count) files clean."
    }
    finally { Pop-Location }
}
