# What If — plán projektu

Canonical backlog: `task_pool.json` (tickety WI-001..012, fáze M0–M3). `TASKS.md` a `tasks/WI-xxx.md` jsou generované (`python -X utf8 agent_pool/scripts/pool.py render`). Procesní rozhodnutí: `docs/decisions.md` (PD-001..006). Politika projektu: `project-policy.json`.

## Fáze

| Fáze | Obsah | Brána |
| --- | --- | --- |
| M0 Základy a sledovatelnost | WI-001 stopa zdrojů odzadu, WI-007 balík rozhodnutí pro uživatele, WI-009 mezery C# kódu proti kontraktům | SOURCE_TRACE, USER_DECISIONS a CODE_GAP_ANALYSIS hotové |
| M1 Integrační blokátory | WI-002..005 (B1 cross-island autorita, B3 aktivace mutací, B2 kauzální obnova, B4 přechody domén), WI-006 Master Baseline | čtyři blokátory z final_for_orchestrator §9.2 mají kontrakty |
| M2 Propagace | WI-008 integrační poznámky k W01–W09, WI-010 úplná ADR | W01–W09 odkazují na sdílené kontrakty |
| M3 Stage 1 | WI-011 Rust kontraktní crates, WI-012 rozhodnutí o jazyku planetárního jádra | po schválení baseline a rozhodnutí uživatele |

## Zdroje (čtou se odzadu)

`research/gpt/final_for_orchestrator.md` → `research/gpt/gpt01..08.md` → `research/worker-0N/w0N-supplement-01.md` → `w0N-original.md` → `audit/`. Detailní stopa: `integration/SOURCE_TRACE.md` (WI-001).

## Kód a nástroje

- `src/WhatIf.Planet*` (kostka-koule, kotvy, povrch, far layer, runtime streamer), `src/WhatIf.WorldGen` + `tools/worldgen` (generátor silnic/terénu/scatter, Python extraktory): vznikly v race_game jako testovací půda What If. Wiki: `wiki/entity/what-if-planet-*.md`, `wiki/entity/worldgen*.md`. Experimenty: `tools/whatif-*`. Historie v race_game: `docs/race_game-lane-history/` (briefy RG-1xx) a `docs/what-if-planet-*.md`.
- `WhatIf.sln`, `scripts/check.ps1` (lint + build + testy), `scripts/verify-hygiene.ps1`.
- Znalostní vrstva: `wiki/`, `tools/knowledge/` (rag-query, lint-wiki, evaluate_rag, brief-graph-check, loop-stats, export-sessions).
