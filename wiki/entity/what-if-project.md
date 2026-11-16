---
type: entity
sources:
  - README.md
  - AGENTS.md
  - .planning/what-if/README.md
  - .planning/what-if/task_pool.json
  - agent_pool/README.md
  - WhatIf.sln
updated: 2026-11-16
status: implemented
aliases:
  - what if
  - what_if
  - přehled projektu
  - project overview
  - kde co je
  - převzetí race_game
---

# What If: přehled projektu

Repo `projectzonio/what_if` (lokálně `D:\workspace\what_if`). Cíl: perzistentní fyzikální vesmír od povrchu planety po meziplanetární prostor; sdílený čas, souřadnice a identita objektů. Pracovní stack: Rust (autoritativní runtime), Node.js (control plane), Unity (jen klient), Python (věda a validace).

| oblast | kde |
| --- | --- |
| výzkum W01–W10, GPT-01..08, integrované hodnocení | `research/`, `audit/`, [[planetary-engine-research]], [[source-precedence]] |
| architektura, kontrakty, ADR | `architecture/`, `integration/`, `decisions/` |
| backlog a pool (WI-001..) | `.planning/what-if/task_pool.json`, `agent_pool/`, [[workflow-tools]] |
| planetární engine (C#) | `src/WhatIf.Planet*`, [[what-if-planet-engine]] |
| world generator (C# + Python) | `src/WhatIf.WorldGen`, `tools/worldgen`, [[worldgen]] |
| druhý mozek, GraphRAG-lite | `wiki/`, `tools/knowledge`, [[rag-knowledge-tools]] |
| skilly | `.claude/skills/wi-*`, [[agent-skills]] |

Původ workflow a kódu: převzato z race_game 2026-11-16 (PD-003). Planetary engine a world generator vznikly v race_game jako testovací půda pro What If; historie je v `.planning/what-if/docs/race_game-lane-history/`.
