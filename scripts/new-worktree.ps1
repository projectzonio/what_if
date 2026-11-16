# Creates an isolated worktree + branch for one agent/ticket.  Usage: scripts/new-worktree.ps1 -Agent claude -Ticket WI-011
# Layout: <repo>/../what_if-worktrees/<agent>-<ticket>, branch agent/<agent>/<ticket>.
param([Parameter(Mandatory)][ValidateSet('claude', 'codex', 'gemini')][string]$Agent,
      [Parameter(Mandatory)][ValidatePattern('^WI-\d{3}$')][string]$Ticket,
      [string]$Base = 'main')
. "$PSScriptRoot/_common.ps1"
Exit-OnError {
    $root = Split-Path $script:RepoRoot -Parent
    $dir = Join-Path $root "$(Split-Path $script:RepoRoot -Leaf)-worktrees/$Agent-$Ticket"
    $branch = "agent/$Agent/$Ticket"
    if (Test-Path $dir) { throw "Worktree path already exists: $dir" }
    Invoke-Checked git @('-C', $script:RepoRoot, 'worktree', 'add', '-b', $branch, $dir, $Base)
    Write-Host "Worktree: $dir`nBranch:   $branch"
}
