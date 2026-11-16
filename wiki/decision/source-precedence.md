---
type: decision
sources:
  - research/gpt/final_for_orchestrator.md
  - .planning/what-if/docs/decisions.md
  - .planning/what-if/project-policy.json
  - audit/CONFLICTS.md
updated: 2026-11-16
status: implemented
aliases:
  - pořadí zdrojů
  - čtení odzadu
  - GPT odzadu
  - precedence
  - source precedence
  - který dokument má přednost
---

# Zdroje se čtou odzadu (PD-002, user 2026-11-16)

GPT práce se bere od konce: nejdřív integrované hodnocení, pak jednotlivé GPT návrhy, pak doplňky a originály workerů, nakonec audit.

1. `research/gpt/final_for_orchestrator.md`: integrované hodnocení (rozhodovací matice GPT-01..08, sdílené kontrakty §3, rejstřík rozporů §4, chybějící architektura §5, registr PE-001..020 §7, validační plán §8, čtyři blokátory §9.2).
2. `research/gpt/gpt01.md` … `gpt08.md`: osm nezávislých návrhů řešení konfliktů (čas, gravitace, regiony vs. fyzikální ostrovy, identita entity, mutace, scheduler, determinismus, fidelity).
3. `research/worker-0N/w0N-supplement-01.md`: doplňky kola 1.
4. `research/worker-0N/w0N-original.md`: původní výzkumy W01–W09.
5. `audit/`: W10 audit, `CONFLICTS.md` (C-, NC-, V-), `OPEN_QUESTIONS.md` (OQ-01..33).

Pravidla: vyšší vrstva přepisuje nižší jen tam, kde to výslovně říká. Rozpory se hlásí s oběma citacemi (vznikly slepou paralelní prací, ne chybou modelu). GPT výstup je návrh: ADR je `Accepted` až po rozhodnutí uživatele. Čísla jsou „návrh, nezměřeno“, dokud je neměří benchmark.

Související: [[planetary-engine-research]], [[what-if-project]], [[orchestration-workflow]].
