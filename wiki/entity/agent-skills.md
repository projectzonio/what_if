---
type: entity
sources:
  - tools/knowledge/rag-query.js
  - .claude/skills/wi-orchestrator/SKILL.md
  - .claude/skills/wi-worker/SKILL.md
  - .claude/skills/wi-knowledge/SKILL.md
updated: 2026-11-16
last_verified: 2026-11-16
status: implemented
aliases:
  - skilly
  - skills
  - dovednosti
  - dovednostní karty
  - skill cards
  - který skill načíst
  - registr skillů
---

# Skilly What If (registr a dovednostní karty)

Skill = **pořadí kroků, které fungovalo, a pasti s důkazem**; fakta zůstávají v docs,
briefech a nástrojích. Skilly jsou v `.claude/skills/<name>/SKILL.md` (Claude Code je
nabízí sám). `rag-query.js` k dotazu vypíše sekci **Skill cards**: který skill načíst,
proč (shoda s popisem skillu, výsledek ze skillu, nebo wiki stránka ve výsledcích, která
skill cituje v `sources`). Router se nepoužívá, karta je jen navigace ve výstupu dotazu.

| skill | kdy | související wiki |
| --- | --- | --- |
| `wi-orchestrator` | převzetí orchestrace, review ticketu, approve, uvolnění ticketů, rework, handover, zpracování GPT výstupu, zdroje čtené odzadu | [[orchestration-workflow]], [[workflow-tools]], [[source-precedence]] |
| `wi-worker` | práce na ticketu ve worktree: claim, výzkumný/architektonický výstup s citacemi, outbox, `pool.py review` | [[orchestration-workflow]], [[agent-harness-traps]], [[research-evidence]] |
| `wi-knowledge` | RAG/graf/karty, transcripty (`export-sessions.js --session`), zápis wiki a skillu, evaluace | [[rag-knowledge-tools]], [[obsidian-wiki-conventions]] |

Skill `watch-the-watcher` (AI dohled nad pool-supervisorem) je v `D:/workspace/.claude/skills`, ne v repu.

## Jak přidat skill

1. Vytěžit opakovanou ztrátu (postup ve skillu `wi-knowledge`); nejdřív zkusit nástroj,
   skill jen tam, kde postup nejde zabalit do nástroje.
2. `description` nese české i anglické záměry (český dotaz anglický skill bez nich mine;
   `build-rag-index.js` ho indexuje jako `purpose`).
3. Řádek sem + cesta do `sources` (hrana grafu), případ do `tools/knowledge/rag-evaluation.v1.json`.

## Vznik

2026-11-16: skilly převzaty z race_game (`rg-orchestrator`, `rg-worker`, `rg-knowledge`, vytěženo z relací 2026-10-15 … 11-16),
Unity/KN5/notebook části vypuštěny, doplněno pravidlo čtení zdrojů odzadu a práce s GPT výstupem.
Zonio pool a router nebyly zdrojem ani cílem ([[knowledge-layer-separate-from-zonio]]).
