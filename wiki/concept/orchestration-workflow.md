---
type: concept
sources:
  - .planning/what-if/docs/workflow-automation-plan.md
  - .planning/what-if/docs/decisions.md
  - agent_pool/README.md
  - agent_pool/shared/orchestrator-state.md
  - agent_pool/shared/prompts/worker-start.md
  - agent_pool/shared/prompts/worker-rework.md
  - agent_pool/shared/prompts/orchestrator-review.md
  - tools/review/make-prompt.py
updated: 2026-10-24
last_verified: 2026-10-24
status: implemented
aliases:
  - orchestrace
  - převezmi orchestraci
  - orchestrator workflow
  - pipeline orchestrátor worker
  - uvolni tickety
  - startovací prompt
  - rework ticketu
  - integrační build
  - ruční testy dávkou
  - subagent workflow
---

# Otestovaná workflow orchestrátor → uživatel → worker

Stav k 2026-10-24, ověřený na ~20 ticketech (RG-027 … RG-087, 2026-10-18 … 10-24).
Postup krok za krokem: skilly `rg-orchestrator` a `rg-worker`; pravidla: ADR-028 v
`.planning/race-game/docs/decisions.md`; návrh a měření: `workflow-automation-plan.md`.

## Tok

1. **Orchestrátor** (Claude Opus 5.5, relace ve složce `race_game-worktrees/claude-RG-013`)
   převezme stav z `orchestrator-state.md` + `pool.py status/next`.
2. Release: brief dostane `### Exact write ownership` a `Prepared handoff`, `pool.py handoff`
   připraví worktree a prompt; orchestrátor vloží text promptu do odpovědi.
3. **Uživatel** otevře relaci Claude Code ve worktree a vloží prompt (spouštění workerů
   zůstává uživateli — chce workflow vidět).
4. **Worker** pracuje bez rutinních dotazů (slot notebooku), ruční jízdu zařadí do fronty,
   na konci outbox + commit + `pool.py review`.
5. Orchestrátor: branch-scope, check, hygiene → integrační větev → jeden build → dávka
   ručních jízd uživatele → `manual-test.py record` → merge → review soubor → `approve` →
   registr `features` (race_game) → další release. Nebo `rework` s verdiktem doslova.
6. Konec relace: přepsaný `orchestrator-state.md`, rozhodnutí uživatele jedním blokem.

## Co se zkoušelo a neosvědčilo

- **Subagent jako worker** (2026-10-21, RG-081/082 s checkpointy přes `pool.py`): limit
  relace přerušil subagenta, výsledek nebyl známý, navazující worker musel začít z commitu.
  Uživatel se vrátil k „orchestrátor – user – worker" (2026-10-21 19:34).
- **Ptaní se po každém kroku**: ~70–75 % lidských turnů bylo mechanických (měření v
  `workflow-automation-plan.md`) → ADR-028.
- **Paměť jako append log** (`MEMORY.md` 101 kB, 43 checkpointů): převzetí stálo ~25–30k
  tokenů a stejně se procházely transcripty → RG-087: stav ≤ 8 kB + archiv + handover soubor.
- **Handover s větou „zeptej se, který ticket spustit"** → další orchestrátor čekal místo
  jednání a nevypsal prompty; uživatel relaci ukončil (2026-10-23 16:45).
- **Worker čte transcripty** kvůli faktům → uživatel 2026-10-23: „info patří do ticketu".

## Co zůstává uživateli

Ruční jízda a vizuální/zvukový verdikt (dávkou na integračním buildu), ADR, změna
acceptance, licence a původ assetů, instalace, cokoli na C:, push, spuštění workerů.
Když uživatel řekne „vypadá to dobře", worker okamžitě zastaví testy a zachytí stav
(tag + kopie buildu) — RG-081 přišel o schválený stav pokračováním (2026-10-23).

## Otevřené

- Fáze 4 (classifier spouštějící relace na hranici `events.jsonl`) — jen návrh; staví se
  až po fázích 1–3 a nikdy nesmí posílat prompty do běžící relace (příčina loopu v Zoniu).
- Orchestraci přes Codex uživatel 2026-10-24 označil za překonaný pokus; orchestruje Claude
  Opus (opraveno v `AGENTS.md`). Pole `agent: codex` v historii poolu zůstávají jako záznam.

Související: [[agent-skills]], `unity-notebook-runs` (race_game), [[agent-harness-traps]], [[workflow-tools]].
