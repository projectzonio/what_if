---
name: wi-orchestrator
description: What If orchestrator loop (převzato z race_game rg-orchestrator, ADR-028) - take over orchestration, review a ticket in review, merge, approve, release the next tickets with start prompts, rewrite the handover, process GPT/external research. Use for "převezmi orchestraci", "WI-0xx je ready k review", "uvolni tickety", "připrav startovací prompty", "rework WI-0xx", "zpracuj GPT výstup". Distilled from race_game orchestrator sessions 2026-10-18..11-16; What If specifics added 2026-11-16.
---

# What If orchestrator — tested loop

Rules: `AGENTS.md`, `.planning/what-if/docs/decisions.md` (PD-001..), `agent_pool/README.md`. Origin and history of the loop: `wiki/concept/orchestration-workflow.md` (written in race_game; ticket IDs there are RG-*, here WI-*). What If is an architecture + code project: acceptance is evidence in documents and tests, there is no Unity ride and no manual-test queue.

## Role (user, race_game 2026-10-28, valid here)
"Váš úkol je workerům pomoct, připravit úkol a potom zkontrolovat." The orchestrator is not a relay that turns a FAIL into a `## Rework N`.
- **Prepare:** pick the simplest mechanism that meets the acceptance before the worker starts; size the brief to the estimate; write the brief as current state + accepted verdicts + task.
- **Help:** treat "Limits", "Decisions for you" and "not measured" in every outbox as review findings and answer them. On a FAIL diagnose the root cause (document, code, log) and hand the worker a concrete design.
- **Check:** verify independently — open at least 5 cited `path:lines` per research deliverable and compare with the source; a research claim without a source is a finding. Never invent numbers: every value is "návrh, nezměřeno" until measured.
- **Step back at the second rework** of the same ticket: change the mechanism, do not patch the same approach again.

## What If specifics
- **Sources are read from the back (user, 2026-11-16):** `research/gpt/final_for_orchestrator.md` first, then `research/gpt/gpt0N.md`, then `research/worker-0N/w0N-supplement-01.md`, then `w0N-original.md`, then `audit/`. A higher layer overrides a lower only where it says so; contradictions are reported (they are "blind work" findings, not model errors), never silently resolved.
- GPT output is a proposal, never an accepted decision. ADR status `Accepted` only after the user decided (`decisions/USER_DECISIONS.md`).
- Originals in `research/**` are not overwritten; integration goes into new files (`w0N-integration-note.md`, `architecture/**`).
- External GPT output enters through intake only: orchestrator copies it into `research/gpt/`, cleans UI junk (`python` one-offs, see `wiki/concept/agent-harness-traps.md`), commits as its own commit. External workers take no slot and never write to the pool.
- Model budget (user, 2026-11-16): subagents and workers on Sonnet; Opus only where the ticket says so (WI-006, WI-010). Fable only with explicit per-ticket permission.
- The C# planetary engine and world generator (`src/WhatIf.*`) were developed in race_game as What If's test bed; their wiki is `wiki/entity/what-if-planet-*.md`, history `.planning/what-if/docs/race_game-lane-history/`. race_game still carries its own copy until the user retires that lane.

## 0. Takeover (≤ 5 tool calls)
1. `python -X utf8 agent_pool/scripts/pool.py status` and `pool.py next`.
2. Read `agent_pool/shared/orchestrator-state.md` (`## Resume here`, decisions inbox) and the newest dated sections of the active briefs.
3. Only when the handover is missing or contradicted: find the last orchestrator session with `node tools/knowledge/export-sessions.js` (skill `wi-knowledge`).

## 1. Review one ticket
1. Scope: `python -X utf8 -I tools/review/branch-scope.py --worktree <wt> --brief agent_pool/shared/briefs/WI-0xx.md`. Expected noise: pool projections, worker `MEMORY.md`, outbox.
2. Gates on the ticket branch: `scripts/check.ps1`, `scripts/verify-hygiene.ps1` (exit 0). Read the outbox: failures must be reported as failures.
3. Dry merge: `git merge-tree --write-tree main agent/claude/WI-0xx`. The usual conflict is only `agent_pool/agents/claude/memory/MEMORY.md` -> keep both sides.
4. OK -> merge (`--no-ff`), write `agent_pool/shared/reviews/WI-0xx.md`, `pool.py approve WI-0xx --reviewer claude-opus-orchestrator --evidence agent_pool/shared/reviews/WI-0xx.md`.
5. Never approve a ticket whose output you edited yourself beyond the merge (send it back).
6. **Knowledge update after every ticket (the graph is the point):** for each area touched make sure a wiki page exists and its `sources:` list the current files, with `[[wikilinks]]` both ways; `index.md` line, `log.md` entry. Then `node tools/knowledge/lint-wiki.js` (0 fatal), `node tools/knowledge/build-rag-index.js`, a case in `rag-evaluation.v1.json`, `python -X utf8 tools/knowledge/evaluate_rag.py`. Check `rag-query.js --query "<an owned path>" --top 8` returns the page under Graph neighbors.

## 2. Release next tickets (do not ask "mám připravit?")
1. `pool.py next` -> START/WAIT. Capacity: 2 slots, 1 slot = 1 implementation worker or 3 `Research` tickets. Do not list the shared `agent_pool/agents/claude/memory/MEMORY.md` as owned.
2. In the brief commit `### Exact write ownership` (paths from the acceptance — never trimmed so that `next` says START) and `## Prepared handoff — <date>`; fill `### Knowledge pointers` with `python -X utf8 tools/knowledge/brief-graph-check.py WI-0xx`.
3. `pool.py handoff WI-0xx` (queue + worktree + `make-prompt.py start`; launches nothing).
4. Final message: for every released ticket the folder to open and the prompt text in a code block. A prompt file alone is not a handover.
5. Worker launch stays with the user (AGENTS.md); under pool-supervisor the supervisor launches (`pool-supervisor`, skill `watch-the-watcher`).

## 3. Rework
1. Merge `main` into the ticket branch first. 2. Add a dated `## Rework N (orchestrator, <date>)` section: verdict verbatim, root cause, what stays, what changes, how verified. 3. `pool.py rework WI-0xx --reason "<verdict>"`; prompt `make-prompt.py rework WI-0xx --print`.

## 4. End of session
- Overwrite (not append) `agent_pool/shared/orchestrator-state.md`: `## Resume here` (this session only), decisions inbox, `## Next step` (imperative). Read the file before Edit/Write.
- New findings without a ticket -> `agent_pool/shared/follow-ups.md`. Before the commit: `python -X utf8 -I tools/review/trim-handover.py` (exit 1 over 12 kB: shorten by hand).
- Ask the user only for what AGENTS.md reserves (ADR / architecture decisions, acceptance change, licences/source of data, installs, C:, push, worker launches) — one block at the end.

## Traps (each cost a session in race_game)
- Worker loops are the expensive thing: look at a finished session with `python -X utf8 -I tools/knowledge/loop-stats.py --session <id> --detail`. Size the run time of measurement-heavy tickets in the brief; no single wait > 45 min (cache expiry); a plan over ~1 h of runs means split the ticket.
- Handover text "ask the user which ticket to launch" makes the next orchestrator wait: keep `## Next step` imperative.
- Worker `pool.py` from a worktree writes the main checkout and prints which one; evidence must be committed on the ticket branch, `done` needs it merged. A second `pool.py review` fails "requires in_progress" -> `review --amend`.
- Subagent-driven workers died on session limits with unknown results; the tested pipeline is orchestrator -> prompt -> worker -> orchestrator. Subagents are for bounded reads/summaries, Sonnet only.
- Never put a git-ignored file (`artifacts/`) into a wiki page's `sources:` — `evaluate_rag.py` passes in worktrees and fails on main.
- Bash heredocs with `'` or longer than ~7.5k characters break in this harness; write files with the Write tool (`wiki/concept/agent-harness-traps.md`).
- Detaching with `&` inside a `run_in_background` command loses the completion notification (this session, 2026-11-16, repeated the race_game RG-152 trap): run the real command itself with `run_in_background: true`, no `&`.
