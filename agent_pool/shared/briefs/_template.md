# RG-0xx — <title>

Read AGENTS.md, the pool README, `.planning/race-game/tasks/RG-0xx.md` and the docs named here: <list>.

## Why
<the user's words and the finding behind the ticket>

## Scope
1. <item>

## Acceptance
- <testable criterion; the manual drive is queued with `tools/review/manual-test.py`, not asked live>

### Exact write ownership
`tools/review/branch-scope.py` and `pool.py next` read this section. Top-level bullet starting with `New`/`Owned`/`Own` lists owned paths, `Read-only` lists the rest; only backticked tokens count.
`Assets/...` and `ProjectSettings/...` are relative to `engine-unity/NightRacer`; a bare file name matches any directory; `**` crosses folders.
- New or owned: `path/one/**`, `path/two.cs`
- Read-only: `AGENTS.md`, `.planning/race-game/docs/decisions.md`, `task_pool.json` except through `pool.py`

### Knowledge pointers
The knowledge layer (wiki + GraphRAG-lite) exists to hand you the dependencies of what you touch. Orchestrator: paste the output of `python -X utf8 tools/knowledge/brief-graph-check.py RG-0xx` here.
- Wiki pages to read first: [[page]], [[page]]
- Neighbours the graph returns for the owned paths: <page / source>
- `MISSING` (owned path with no wiki page): <path> — report it in the outbox
- Query yourself before editing: `node tools/knowledge/rag-query.js --query "<topic>" --top 8`; read Graph neighbors and Code graph, not only the top hits.

## Prepared handoff (orchestrator, <date>)
- Depends on <RG-0xx> (done). Owner: <Claude Opus | Sonnet>. <Unity build / manual drive needed or not>.
- Worktree / branch: create with `scripts/new-worktree.ps1 -Agent claude -Ticket RG-0xx` (or `pool.py handoff RG-0xx`, which also writes the start prompt).
- **Run pool.py from anywhere**: from a worktree it reads and writes the main checkout and prints which one. Commit the outbox on your branch, then `pool.py --agent claude review RG-0xx --evidence <outbox path>` at the very end (evidence is checked as a committed blob on your branch).
- Parallel: <ticket and what it shares>. ADR-028: do not ask for routine steps.
