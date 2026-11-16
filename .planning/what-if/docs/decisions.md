# What If — procesní rozhodnutí (PD-xxx)

Architektonická rozhodnutí (ADR-001..015, PE-001..020) žijí v `decisions/`. Tady jsou jen rozhodnutí o způsobu práce, převzatá z race_game a upravená.

## PD-001 — Autonomie orchestrátoru (z race_game ADR-028, 2026-10-22)
Rutinní kroky se nepotvrzují: review od začátku do konce v jedné relaci, merge a approve po zelených check/hygiene/branch-scope. Uživatel rozhoduje o: architektonických rozhodnutích a přijetí ADR (USER-001..010, OQ-xx), změně acceptance kritérií, licencích a zdrojích dat, instalacích a stahováních, C:, push, spuštění workerů. Sbírá se to na konci relace jedním blokem.

## PD-002 — Zdroje se čtou odzadu (user, 2026-11-16)
Pořadí: `research/gpt/final_for_orchestrator.md`, `research/gpt/gpt01..08.md`, `research/worker-0N/w0N-supplement-01.md`, `research/worker-0N/w0N-original.md`, `audit/`. Vyšší vrstva přepisuje nižší jen tam, kde to výslovně říká. Rozpory se hlásí (C-, NC-, INT- ID), nemažou. GPT výstup je návrh, ne rozhodnutí.

## PD-003 — Převzetí race_game (user, 2026-11-16)
Pool, ticketový systém, skilly orchestrator/worker, znalostní vrstva (wiki, RAG, GraphRAG-lite) a nástroje jsou převzaté z race_game jako základ. Planetary engine (`src/WhatIf.Planet*`) a world generator (`src/WhatIf.WorldGen`, `tools/worldgen`) vznikly v race_game jako testovací půda pro What If a jsou tady vlastní kód projektu. Převzato bez NightRacer části (Unity, KN5, fyzikální pluginy, závodní logika, manuální jízdy). race_game si svoji kopii ponechává, dokud uživatel nerozhodne o jejím vyřazení.

## PD-004 — Model a rozpočet (user, 2026-11-16)
Subagenti a běžní workeři Sonnet. Opus jen u WI-006 (Master Baseline) a WI-010 (ADR). Fable jen s výslovným svolením na ticket.

## PD-005 — Kapacita workerů (z race_game ADR-035)
2 sloty; 1 slot = 1 implementační worker nebo 3 `Research` ticketů. Výzkumný ticket nesahá do kódu ani nespouští dlouhé výpočty.

## PD-006 — Externí (GPT) práce
Externí workeři nemají slot a nepíšou do repa ani poolu. Výstup vkládá orchestrátor do `research/gpt/` samostatným commitem; UI zbytky (favicony, `utm_source=chatgpt.com`, `images.openai.com`, `GitHub+N`) se čistí a `scripts/verify-hygiene.ps1` je hlídá.
