# Git workflow, layout a build skeleton — RG-005

## Layout

```text
NightRacer.sln            .NET smoke solution (engine-neutral; not the game)
global.json               pinned SDK 8.0.403 (rollForward latestFeature) – evidence: env-audit.md
Directory.Build.props     ArtifactsPath = <worktree>/artifacts (git-ignored, per worktree)
src/NightRacer.Smoke      FrameStats.Percentile (nearest-rank, matches benchmark-protocol.md)
tests/NightRacer.Smoke.Tests   dependency-free console checks, exit 0/1
tests/negative/BrokenBuild     deliberately non-compiling; only for verify-failure-codes.ps1, NOT in the .sln
scripts/                  build, test, lint, check, verify-failure-codes, new-worktree (PowerShell)
.planning/ agent_pool/    plan and pool (versioned; pool CLI is the only writer of task state)
```

Engine projects (RG-010/011/012) get their own top-level folders (e.g. `engine-unity/`, `engine-godot/`) with separate build/log folders; the engine-specific batchmode commands are added only after a real installation (RG-011/012), the production wiring in RG-037.

## Branches and worktrees

- `main` – only reviewed/approved work. The first commit (`c29b2f3`) is the **Git bootstrap**: it founded the repository (`git init`) and the baseline directly on `main`; no branch or worktree existed before it. All later implementation, including RG-005 corrections, lives on its own ticket branch/worktree and merges only after approve.
- One branch **and** one worktree per worker and ticket: `agent/<agent>/<RG-id>`, directory `<repo>-worktrees/<agent>-<RG-id>` next to the repo (outside it).
  `powershell -File scripts/new-worktree.ps1 -Agent claude -Ticket RG-011`
- Worker capacity (ADR-035): 2 slots, 1 slot = 1 implementation worker or 3 research workers (role `Research`); at most one GPU-bound ticket; disjoint write paths declared in the brief before `claim`. Global engine settings and main scenes have a single owner.
- Merge into `main` only after review/approve in the pool (orchestrator). No force-push, no reset of other people's changes.
- Each worktree builds into its own `artifacts/`; no shared mutable build folder. Never point two workers at the same engine project folder.
- `.task_pool.lock` and pool JSON are edited only through `pool.py` in the main worktree.

## Never committed (see `.gitignore`, `project-policy.json`)

Third-party mods/KN5/AC content (`Mods/`, `*.kn5`, …), derived caches, engine `Library/`/`Temp/`/`Logs/`/`.godot/` (at any depth, e.g. `engine-unity/Library/`; `.gitignore` entries are deliberately unanchored, only `agent_pool/agents/*/logs/` is re-included), builds and `artifacts/`, local benchmark `results/`, logs of tools. `scripts/lint.ps1` (rules in `scripts/_hygiene.ps1`, matching is case-insensitive) fails when such files would be committed. Binaries/KN5-like formats are rejected everywhere except in `agent_pool/archive/`. Model files `.glb/.gltf/.fbx/.blend` are allowed **only** in `assets/original/<asset-id>/` with a valid `PROVENANCE.json` – see [owned-assets.md](owned-assets.md). Project purpose (`project-policy.json`) does not grant rights to third-party assets.

## Commands

```powershell
powershell -File scripts/build.ps1                # dotnet build NightRacer.sln (or -Target <project>)
powershell -File scripts/test.ps1                 # build + run smoke checks
powershell -File scripts/lint.ps1                 # dotnet format --verify-no-changes + hygiene
powershell -File scripts/check.ps1                # lint -> build -> test
powershell -File scripts/verify-failure-codes.ps1 # negative test: failures give non-zero exit codes
powershell -File scripts/verify-hygiene.ps1       # hygiene filter: allowed owned asset + rejected cases (temp fixtures)
```

All scripts derive paths from their own location (no personal paths) and return exit code 1 on any error.