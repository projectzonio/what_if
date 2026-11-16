---
name: wi-worker
description: What If worker ticket lifecycle (převzato z race_game rg-worker, ADR-028) - "Pracuj na WI-0xx podle briefu", rework, continuing an interrupted worker; preflight, claim, research/architecture deliverables with citations, outbox, commit, pool.py review at the very end, handing out-of-scope findings to the orchestrator. Use in a ticket worktree (claude-WI-0xx).
---

# What If worker — tested ticket lifecycle

Rules: `AGENTS.md`, your brief `agent_pool/shared/briefs/WI-0xx.md` (newest dated section wins). Knowledge layer: skill `wi-knowledge`.

## Start
1. Report `git status --short`, branch, worktree. Read the brief: `## Prepared handoff`, `### Exact write ownership`, any `## Rework N`.
2. Queue/claim only when the prompt allows it. After `pool.py handoff` the job is already queued: run only `python -X utf8 agent_pool/scripts/pool.py --agent claude claim WI-0xx`. A rework prompt says the ticket is already `in_progress`: do not queue/claim again. `pool.py` works from the worktree (first output line names the main checkout it writes).
3. Before building something "new" check `wiki/` and `node tools/knowledge/rag-query.js --query "<topic>" --top 8`.
4. Do not read old transcripts for facts — the orchestrator puts them into the brief.

## Work
- **Read sources from the back:** `research/gpt/final_for_orchestrator.md` first, then `research/gpt/gpt0N.md`, `research/worker-0N/w0N-supplement-01.md`, `w0N-original.md`, `audit/`. Documents are 40–110 kB: grep the section you need (`Grep` with `-n`, `Read` with offset/limit), never open a whole file.
- **Every claim in a deliverable carries its source** (`file §section` or `path:lines`). Numbers are "návrh, nezměřeno" unless measured; never invent values. A contradiction between sources is reported with both citations, not resolved silently.
- Do not overwrite `research/**` originals; write only your owned paths.
- Decisions that belong to the user (USER-001..010, `audit/OPEN_QUESTIONS.md`) are listed with a recommendation, not decided.
- Out-of-scope bugs, ideas and user remarks go verbatim into the outbox section "Pro orchestrátora", not into the deliverable.
- Technical decisions inside the brief: decide and justify in the outbox. Ask only for ADR / architecture decisions, acceptance change, licence/data source, installs, C:, push — collected at the end.
- **Asking never blocks the pipeline** (user, race_game 2026-11-11): if you cannot go on without an answer, write the question with options and your recommendation into the outbox, commit the work in progress and run `pool.py block WI-0xx --reason "<one sentence + outbox path>"`.
- Code (C#/Rust/Python): tests next to the change; `scripts/check.ps1` before review. Raw run data > 1 MB stays local (commit a summary + SHA-256).

## Break the loop (user, 2026-11-03; cost evidence in race_game)
- Any run over ~2 min (build, test series, sweep): `run_in_background: true` for the real command (no `&`, `nohup`, `Start-Process`, `Start-Job`; no stdin reader like a bare `cat`), end the turn with one sentence, read the log on wake. The notification is sent only for a command that ends.
- One wake per batch; never tail a progress file into Monitor (each line wakes the model and re-reads the context). Long run: `tools/review/run-bg.ps1` + `tools/review/wait-run.ps1 -ExitFile ...` (usage in `tools/review/README.md`).
- No wait longer than ~45 min (prompt cache expires); split into batches or `wait-run.ps1 -TimeoutS 2700` and re-issue on exit 2. A plan over ~1 h of runs: write it to the outbox and stop.
- Keep context down: Grep/Read with offset+limit, filter logs, think in short steps. A failed batch is a wake: read the summary, think once, do not rerun blindly.

## Finish (in this order)
1. `scripts/check.ps1` and `scripts/verify-hygiene.ps1` exit 0 (foreground, `timeout: 600000`). Run PowerShell scripts as `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/<name>.ps1`: this machine has `LocalMachine = AllSigned`, plain `-File` fails with "not digitally signed" (WI-001).
2. Outbox `agent_pool/agents/claude/outbox/<job>_WI-0xx.md`: summary, exact commands + exit codes, changed paths, failures as failures, unmeasured items, citations spot-check list, "Pro orchestrátora", questions at the end.
3. Commit everything on `agent/claude/WI-0xx` (the evidence must be a committed blob on the branch).
4. `pool.py review WI-0xx --evidence <outbox>` once, at the very end (`--amend` to add evidence later). Check `pool.py status` shows `review`.
5. A short dated note in `agent_pool/agents/claude/memory/MEMORY.md` only if the orchestrator needs state that is not in the outbox. Do not approve, merge or start another ticket.

## Traps
- `pool.py review` twice -> "requires in_progress": use `--amend`.
- Output folders must exist before a log is written there (`mkdir` first).
- Python one-off patches via `open()` write CRLF on Windows: use the Edit tool or `newline=''`.
- Scratch scripts and throwaway outputs: `<worktree>/artifacts/tmp/` (git-ignored, on D:), not the C: session scratchpad, not Git Bash `/tmp`, do not override `$env:TEMP`.
- Waiting with Monitor: first call ToolSearch with `select:Monitor`, then Monitor with `description` + `command`.
- Shell/harness traps: `wiki/concept/agent-harness-traps.md`.
