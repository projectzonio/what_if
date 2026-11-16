# What If (Planetary Engine) — agent instructions

Read `README.md`, `.planning/what-if/README.md`, `.planning/what-if/docs/decisions.md`, `agent_pool/README.md`, your current ticket and its brief in `agent_pool/shared/briefs/`. Workflow origin: race_game (2026-11-16); the NightRacer game itself is not part of this repo.

## Scope and ownership

- What If = one persistent physical universe (surface of a planet to interplanetary space), architecture "Planetary Engine" W01–W10. Working stack from `research/gpt/final_for_orchestrator.md`: Rust (authoritative runtime), Node.js (control plane), Unity (client only, never authority), Python (scientific/validation tools). The C# planetary engine and world generator in `src/WhatIf.*` were built in race_game as the What If test bed and are the starting code here; their future language is open (WI-012, ADR-016 proposal), do not rewrite them by default.
- **Source precedence — GPT work is read from the back (user, 2026-11-16):** 1. `research/gpt/final_for_orchestrator.md`, 2. `research/gpt/gpt01..08.md`, 3. `research/worker-0N/w0N-supplement-01.md`, 4. `research/worker-0N/w0N-original.md`, 5. `audit/` (W10, CONFLICTS, OPEN_QUESTIONS). A higher layer overrides a lower one only where it says so. Contradictions (C-xx, NC-xx, INT-xx) are reported with both citations; they come from parallel blind work, not from model error. GPT output is a proposal: ADRs are `Accepted` only after the user decided.
- Documents in `research/**` originals and supplements are not rewritten. Integration goes into new files (`architecture/`, `integration/`, `decisions/`, `w0N-integration-note.md`).
- Claude Opus orchestrates (skill `wi-orchestrator`): plan, briefs, pool tooling, docs, verification, integration. Sonnet owns focused work and every subagent (user, 2026-11-16: nine Opus subagents exceeded the budget). Opus workers only where the ticket says so (WI-006, WI-010). Fable only with explicit per-ticket permission.
- Worker launch preference (race_game, user 2026-10-10, unchanged): the user starts Claude manually (or the pool-supervisor does, see skill `watch-the-watcher`). The orchestrator prepares briefs, reservations and worktrees but does not launch workers unless the user asks. Queueing a ticket alone does not authorize launch.
- Do not start the next ticket implicitly. Only claim a queued ready task; no dependency bypass.
- Autonomy policy (PD-001, from race_game ADR-028): do not ask the user to confirm routine steps. Review runs end-to-end; merge + approve after green check/hygiene/branch-scope. Ask only for architecture decisions/ADR acceptance (USER-001..010, OQ-xx), acceptance-criteria changes, data/licence sources, installs/downloads, C:, push and worker launches, collected at the end of the session.
- Before any implementation report `git status --short`, branch/worktree and existing dirty state. One branch/worktree per worker (`D:\workspace\what_if-worktrees\claude-WI-xxx`, branch `agent/claude/WI-xxx`), disjoint write paths from the brief. Capacity: 2 worker slots; 1 slot = 1 implementation worker or 3 `Research` tickets. External workers (ChatGPT etc., run by the user) take no slot and never write to the repo or pool; their output enters through the orchestrator into `research/gpt/` as its own commit.
- Never invent numbers. Every value in the research documents (time quantum, 2^15 m cell, frequencies, budgets) is "návrh, nezměřeno" until a benchmark gate measured it.
- Keep downloads, caches and TEMP on D: (C: is nearly full). Scratch scripts and throwaway outputs go to the git-ignored `<worktree>/artifacts/tmp/`, not the C: session scratchpad and not Git Bash `/tmp`; do not override `$env:TEMP`.
- New dependencies and toolchain versions need actual tool/version evidence (dotnet SDK pinned by `global.json`, cargo available, Python 3.13, Node 22).

## Knowledge layer (first step before reading the tree)

- `node tools/knowledge/rag-query.js --query "<topic, ID (PE-/INT-/C-/OQ-/WI-), contract or file>"` finds docs, briefs, reviews, wiki, research and code; it rebuilds a stale index itself. A hit is navigation: open the cited `path:lines`. Ticket status comes live from the main checkout's `task_pool.json`.
- Project skills (tested procedure + traps): `.claude/skills/` — `wi-orchestrator`, `wi-worker`, `wi-knowledge`; registry `wiki/entity/agent-skills.md`. `rag-query.js` prints *Skill cards* for the query: load the carded skill before acting. Pool supervision: skill `watch-the-watcher` (in `D:/workspace/.claude/skills`).
- After changing `wiki/`: `node tools/knowledge/lint-wiki.js` (0 fatal); retrieval regression: `python -X utf8 tools/knowledge/evaluate_rag.py`.
- Harness traps measured over all race_game sessions: `wiki/concept/agent-harness-traps.md`. Above all: write files over ~6 kB with the Write tool, never a Bash heredoc (commands over ~7.5k characters are cut).
- Occasional Zonio tools are used in place, never copied: `wiki/concept/external-tools-zonio.md`. Zonio pool/handoff conventions do not apply here.

## Pool protocol

- `.planning/what-if/task_pool.json` is canonical for task and dispatch state. Task Markdown, `TASKS.md`, inbox/job headers, state JSON and `_pool.md` are derived projections (`pool.py render`).
- Mutate via `python -X utf8 agent_pool/scripts/pool.py`; its exclusive lock protects writes. Queue reserves a task, claim starts work, review requires actual evidence files, approve requires an explicit reviewer and acceptance review file.
- Evidence belongs in `agent_pool/agents/<agent>/{outbox,logs}` or project docs. Report exact commands/exit codes, changed files, failures, and next action. Never copy tokens, credentials or raw environment secrets.
- Keep durable memory in `agent_pool/agents/<agent>/memory/MEMORY.md`, especially before long waits: job ID, objective, verified state, next command.
- Completion of a job is only a handoff: the task stays `review` until the orchestrator verifies all acceptance. Failures are reported as failures; missing measurements stay "unmeasured".
- When editing task scope/dependencies directly in JSON, validate and render immediately (`pool.py validate`, `pool.py render`). Update the shared brief if ownership or scope changes. Never maintain ticket status in Markdown.
