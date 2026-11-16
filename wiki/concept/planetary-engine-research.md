---
type: concept
sources:
  - research/gpt/final_for_orchestrator.md
  - integration/INTERFACE_CONTRACTS.md
  - audit/CONFLICTS.md
  - audit/OPEN_QUESTIONS.md
  - audit/WORKER_10_REVIEW.md
  - architecture/README.md
updated: 2026-11-16
status: provisional
aliases:
  - výzkum W01-W10
  - master architecture baseline
  - blokátory
  - PE-001
  - cross-island
  - kauzální řez
  - revision vector
  - physics island
  - world mutation lifecycle
---

# Planetary Engine: architektonický výzkum (stav 2026-11-16)

Jeden perzistentní logický svět, sdílený čas a prostor, více specializovaných simulačních domén, nezávisle spravované fyzikální ostrovy a distribuovaná regionální autorita (`final_for_orchestrator` §1.1). Stav: **podmíněně vyřešeno, není formální baseline**.

- **Osm návrhů GPT** (čas, gravitace, regiony×ostrovy, identita entity, mutace, scheduler, determinismus, fidelity) jsou přijaty podmíněně jako základ (final §2). Kontrakty a vlastnictví: final §1.3, §3. Registr rozhodnutí PE-001..020: final §7.
- **Čtyři blokátory** formální baseline (final §9.2): autorita interakce mezi fyzikálními ostrovy (PE-017), kauzální obnova (PE-013), aktivace mutací (PE-011), přechody mezi doménami a větvení (PE-014/018/019). Tickety WI-002..005, výsledek WI-006 `architecture/MASTER_BASELINE.md`.
- **Rozpory:** `audit/CONFLICTS.md` (C-01..09, NC-01..52) a INT-01..12 z final §4.1. Otevřené otázky pro uživatele: `audit/OPEN_QUESTIONS.md` (OQ-01..33) a USER-001..010.
- **Validace** V1–V8 a benchmark brány: final §8.

Pořadí čtení zdrojů: [[source-precedence]]. Kód planetárního enginu a generátoru: [[what-if-planet-engine]], [[worldgen]]. Přehled projektu: [[what-if-project]].
