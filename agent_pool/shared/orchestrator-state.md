# What If — orchestrator handover

Přepisuje se (ne připisuje) na konci každé relace orchestrátora. Nástroj: `python -X utf8 -I tools/review/trim-handover.py` (limit 12 kB).

## Resume here
2026-11-16 23:15: **what_if pool PARKOVÁN** (uživatel: limit jen na demo). Demo planety se staví v race_game jako RG-186..190 (Unity přehrávač, auto, renderer jsou jen tam; `tools/whatif-viewer/run-viewer.ps1` zde volá chybějící `tools/kn5-unity`). Přenos Unity části do what_if až po demu.

2026-11-16 večer: projekt `what_if` založen jako převzetí race_game workflow (PD-003).
- Hotovo: pool (`.planning/what-if/task_pool.json`, WI-001..012, `pool.py` + 27 testů zelených), skilly `wi-orchestrator` / `wi-worker` / `wi-knowledge`, šablony promptů, znalostní vrstva (`wiki/`, `tools/knowledge`), C# planetární engine a world generator (`src/WhatIf.*`, `tools/worldgen`, `tools/whatif-*`) s `WhatIf.sln`, `scripts/check.ps1` (lint + build + 14 runnerů) zelené.
- Zdroj nejvyšší priority: `research/gpt/final_for_orchestrator.md` (PD-002, zdroje se čtou odzadu). GPT-01..08 jsou v `research/gpt/`, vyčištěné.
- Čtyři blokátory baseline = WI-002..005 (po WI-001), Master Baseline = WI-006 (Opus).
- Nic z poolu nebylo spuštěno: všechny tickety jsou `todo`, žádný worker neběží, supervizor není nastavený pro what_if.

## Decisions inbox (user)

(prázdné)

## Pending from the user

- Jazyk planetárního jádra a world generatoru (zůstat v C#, port do Rustu, nebo obojí): WI-012 / ADR-016 podklad; do té doby se kód nepřepisuje.
- USER-001..010 a OQ-01..33: podklad připraví WI-007.
- Co s What If lane v race_game (tickety RG-160..185 hotové, kód tam zůstává): ponechat jako kopii, nebo vyřadit.
- Push do `projectzonio/what_if` (commit je lokální).

## Next step

1. `python -X utf8 agent_pool/scripts/pool.py next` -> WI-001 je START. Doplnit v `agent_pool/shared/briefs/WI-001.md` `### Knowledge pointers` (`brief-graph-check.py WI-001`), pak `pool.py handoff WI-001` a předat uživateli prompt v code blocku.
2. Po schválení WI-001 uvolnit WI-002..005, WI-007, WI-009 (max 2 sloty; Research = 1 jednotka, takže až 6 výzkumných tiketů naráz, pokud jsou write scopy disjunktní).
3. Spuštění supervizoru pro what_if: `node D:\workspace\zonio_app\tools\agents\pool-supervisor.js run --project D:\workspace\what_if` až po rozhodnutí uživatele (viz paměť pipeline-start-cheatsheet).
