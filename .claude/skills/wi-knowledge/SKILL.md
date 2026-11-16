---
name: wi-knowledge
description: What If knowledge layer (second brain, RAG, GraphRAG-lite, skill cards) - find facts before reading the tree, continue an interrupted session from its transcript, mine transcripts for repeated losses, write a wiki page / skill, rebuild and evaluate the index. Use for "najdi v RAGu", "projdi transcripty", "pokračuj v session <id>", "zapiš do wiki / paměti", "doplň skill", "nástroj pro práci s transcripty". NOT the Zonio second-brain skill or Zonio pool - what_if has its own layer (copied from race_game 2026-11-16).
---

# What If knowledge layer — tested procedure

Tools: `tools/knowledge/README.md`. Wiki rules: `wiki/concept/obsidian-wiki-conventions.md`. Separation from Zonio: `wiki/decision/knowledge-layer-separate-from-zonio.md`.

## Find (first step, before grep/reading docs)
- `node tools/knowledge/rag-query.js --query "<topic | WI-0xx stav>" --top 8`. It rebuilds a stale index itself (HEAD, main pool, wiki changed, > 24 h). A hit is navigation — open the cited `path:lines`. Limit the output with `--top`, not with `| Select-Object -First N`: PowerShell stops the pipeline early and the call reports exit 255 (4 sessions, 2026-10-29..11-01).
- The output ends with **Skill cards** (which skill to load for this intent), **Graph neighbors** (wiki links and cited sources) and **Code graph** (C# callers/callees; Rust is not graphed yet). Load a carded skill before acting.
- Ticket status = live chunk from the main checkout's `task_pool.json`; a worktree copy is never indexed.
- "Is there a tool for X?" -> `wiki/concept/workflow-tools.md` (action -> tool -> trap), then `wiki/concept/external-tools-zonio.md` (Zonio tools used in place, never copied).
- What already exists (planet engine, world generator) -> `wiki/entity/what-if-planet-*.md`, `wiki/entity/worldgen.md`. The research corpus (`research/`, `architecture/`, `integration/`, `decisions/`, `audit/`) is indexed too: query PE-/INT-/C-/OQ- IDs and contract names.

## Transcripts
- One session ("pokračuj v session 9ec35e67…"): `node tools/knowledge/export-sessions.js --session <id-prefix>` -> `artifacts/session-export/<stamp>/_all/sessions/<id>.md`. Read the last turns and the outbox, not the whole file.
- All sessions since a date (mining): `node tools/knowledge/export-sessions.js --since 2026-10-15T00:00:00Z`. Raw JSONL lives in `~/.claude/projects/D--workspace-what-if*` (the orchestrator runs in the `...-what-if` folder). Transcripts are private: never commit exports.
- A file destroyed by `git checkout` / overwrite: Zonio `recover-file-from-transcript.js` (see the external-tools catalogue).
- Workers do not dig transcripts for ticket facts — the orchestrator moves them into the brief.

## Mining a workflow (what this layer was built from)
1. Per session: human turns, `AskUserQuestion`, command families, tool errors (`is_error` results), last assistant text. Count before concluding; "1/20" beats "sometimes".
2. Classify each repeated loss: **tool** (wrap it — preferred), **skill** (procedure that cannot be a tool), **wiki** (trap, decision, boundary), **AGENTS.md line** (one rule every agent needs).
3. Put the result where it lives: skill `.claude/skills/<name>/SKILL.md`, wiki page, or the tool itself. Record the mining in `wiki/log.md`.

## Write
- Wiki page: `wiki/<entity|concept|decision>/<slug>.md` with frontmatter `type`, `sources` (repo paths the lint checks — a skill path here makes it a graph edge), `updated`, `aliases` (Czech + English phrasings agents type). Exactly one line in `wiki/index.md`, a dated entry in `wiki/log.md`. Ticket status never goes into the wiki.
- Skill: frontmatter `name` + `description` with the Czech and English intents it serves; body = order of steps that worked + traps with evidence; facts stay in docs/briefs and are linked. Register it in `wiki/entity/agent-skills.md` (`sources` + one row) so the graph and the skill cards know it.
- No Zonio pool, BRIEF/PLAN/STATE or router conventions here — `lint-wiki.js` treats Zonio pool references as fatal.

## Verify
```powershell
node tools/knowledge/lint-wiki.js --write-report        # 0 fatal
node tools/knowledge/build-rag-index.js                 # or let rag-query rebuild
python -X utf8 tools/knowledge/evaluate_rag.py          # exit 0; add a case to rag-evaluation.v1.json for every new page/skill
```
