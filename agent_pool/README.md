# What If — provozní agent pool

Backlog žije v `.planning/what-if/task_pool.json`; tato složka obsahuje předání agentům. Všechny příkazy spouštět z `D:\workspace\what_if` (z worktree čte i zapisuje `pool.py` hlavní checkout). Python standard library, bez npm a bez závislosti na Zonio. Převzato z race_game `agent_pool` (2026-11-16), bez Unity a bez slotu notebooku.

```text
agent_pool/
  _pool.md                     aktuální přehled (generovaný)
  jobs/                        přehled dispatchů (generovaný)
  events.jsonl                 jeden řádek JSON na přechod stavu
  shared/briefs/WI-*.md        brief ticketu (šablona _template.md)
  shared/prompts/              šablony worker-start / worker-rework / orchestrator-review + vygenerované prompty
  shared/reviews/              acceptance review orchestrátora
  shared/orchestrator-state.md předání orchestrace (## Resume here, ## Decisions inbox, ## Next step)
  agents/{claude,codex,gemini}/{inbox,outbox,state,logs,memory}
  scripts/pool.py              status, rezervace, lifecycle, validace, next, handoff
```

## Základní příkazy

```powershell
python -X utf8 agent_pool/scripts/pool.py status          # stav ticketů a dispatchů
python -X utf8 agent_pool/scripts/pool.py next            # ready tickety: START/WAIT + důvod (2 sloty, disjunktní write scope)
python -X utf8 agent_pool/scripts/pool.py handoff WI-002  # queue + worktree + startovací prompt; nic nespouští
python -X utf8 agent_pool/scripts/pool.py claim WI-002    # worker po handoffu
python -X utf8 agent_pool/scripts/pool.py review WI-002 --evidence agent_pool/agents/claude/outbox/<job>_WI-002.md   # worker na konci; evidence musí být commitnutá na větvi
python -X utf8 agent_pool/scripts/pool.py review WI-002 --amend --evidence <další soubor>
python -X utf8 agent_pool/scripts/pool.py rework WI-002 --reason "verdikt doslova"
python -X utf8 agent_pool/scripts/pool.py approve WI-002 --reviewer claude-opus-orchestrator --evidence agent_pool/shared/reviews/WI-002.md
python -X utf8 agent_pool/scripts/pool.py block|retry|cancel WI-002 --reason "..."
python -X utf8 agent_pool/scripts/pool.py validate        # DAG, stavy, evidence
python -X utf8 agent_pool/scripts/pool.py render          # TASKS.md, tasks/*.md, _pool.md z task_pool.json
python -X utf8 agent_pool/scripts/test_pool.py            # 27 testů (disposable temp pool)
```

`ready` = todo, všechny dependencies done, žádný pending dispatch. `queue` vytvoří časovaný job a rezervuje ticket doporučenému agentovi/modelu; proces se tím nespouští. Gemini jen pro WI-001 (read-only inventář); ostatní tickety vyžadují důvěryhodného vlastníka.

## Kapacita a rozsah

- 2 sloty (`SLOTS`), 1 slot = 3 jednotky: implementační worker 3, `Research` 1. `next` počítá jednotky sám.
- Vlastněné cesty z `### Exact write ownership` v briefu musí být disjunktní vůči aktivním ticketům (`next` hlásí WAIT s důvodem). Sdílený `agent_pool/agents/claude/memory/MEMORY.md` se do vlastnictví nepíše.
- `### Exact write ownership` a `## Prepared handoff` musí být commitnuté v briefu před `handoff`.

## Ruční spuštění workerů

Uživatel spouští Claude ručně (nebo pool-supervisor, skill `watch-the-watcher`). Orchestrátor připraví brief, rezervaci a izolovaný worktree a uživateli vypíše startovací prompt (`python -X utf8 -I tools/review/make-prompt.py start WI-0xx --print`) a složku, ve které se má relace otevřít. Povolení queue/claim dává uživatel tím, že prompt vloží; vypsání promptu samo nic nespouští.

## Práce → review → schválení

Worker: claim → práce → outbox → commit → `review --evidence` úplně na konci. Orchestrátor: `tools/review/branch-scope.py`, `scripts/check.ps1`, `scripts/verify-hygiene.ps1`, kontrola citací výzkumného výstupu, merge, `approve` s vlastním review souborem. `approve` znamená potvrzení všech acceptance; obsah důkazu ověřuje reviewer, nástroj ho neposuzuje. Failed výsledek nikdy nezíská done; `block --reason` + outbox s otázkou a doporučením (blokující otázka uprostřed práce je zakázaná).

Po pádu writeru může zůstat `agent_pool/.task_pool.lock`; smazat ho jen po ověření, že žádný `pool.py` neběží.
