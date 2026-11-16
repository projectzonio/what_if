---
type: concept
sources:
  - agent_pool/scripts/pool.py
  - scripts/check.ps1
  - scripts/verify-hygiene.ps1
  - scripts/new-worktree.ps1
  - scripts/test.ps1
  - tools/review/README.md
  - tools/review/branch-scope.py
  - tools/review/make-prompt.py
  - tools/review/trim-handover.py
  - agent_pool/shared/prompts/worker-start.md
  - tools/knowledge/export-sessions.js
  - tools/worldgen/geometry_probe.py
updated: 2026-11-16
status: implemented
aliases:
  - katalog úkonů
  - co tu umím udělat
  - existuje na to nástroj
  - než napíšeš skript
  - workflow tools
  - capability catalog
---

# Úkon → nástroj (What If)

Formulováno **úkonem**, ne jménem nástroje. Než napíšeš jednorázový skript, najdi úkon tady;
pokud chybí a nástroj existuje, doplň řádek. Externí Zonio nástroje: [[external-tools-zonio]].
Hledání: [[rag-knowledge-tools]]. Postupy: [[orchestration-workflow]].

## Pool a worktree

| úkon | nástroj | past |
| --- | --- | --- |
| stav ticketů, co dál, handoff | `python -X utf8 agent_pool/scripts/pool.py status|next|handoff WI-0xx` | `next` hlásí WAIT, když brief nemá `### Exact write ownership` |
| nový worktree | `scripts/new-worktree.ps1 -Agent claude -Ticket WI-0xx` (nebo `pool.py handoff`) | worktree vzniká v `D:\workspace\what_if-worktrees` |
| startovací prompt | `python -X utf8 -I tools/review/make-prompt.py start WI-0xx --print` | brief musí mít commitnutou „Prepared handoff“ |
| rozsah větve proti briefu | `python -X utf8 -I tools/review/branch-scope.py --worktree <wt> --brief <brief>` | soubor nad 1 MB padá |
| zkrácení handoveru | `python -X utf8 -I tools/review/trim-handover.py` | exit 1 nad 12 kB |
| dlouhý běh na pozadí | `tools/review/run-bg.ps1` + `tools/review/wait-run.ps1` | `&`, nohup, Start-Process notifikaci neposílají |

## Kontroly

| úkon | nástroj |
| --- | --- |
| lint, build, všechny testy | `scripts/check.ps1` (`WhatIf.sln`, `tests/*`, Python testy, cargo test když existuje `rust/`) |
| hygiena repa (binárky, > 1 MB, UI zbytky v research/) | `scripts/verify-hygiene.ps1` |
| test poolu (27 testů) | `python -X utf8 agent_pool/scripts/test_pool.py` |

## Svět a planeta

| úkon | nástroj |
| --- | --- |
| experimenty planetárního enginu | `tools/whatif-planet`, `whatif-surface`, `whatif-farlayer`, `whatif-anchors`, `whatif-runtime` (`run-*.ps1`), viz [[what-if-planet-engine]] |
| generátor silnic a terénu, extraktory dat | `tools/worldgen/` (`cli`, `geometry_probe.py`), viz [[worldgen]] |

## Znalosti a transcripty

| úkon | nástroj |
| --- | --- |
| hledání ve wiki, výzkumu a kódu | `node tools/knowledge/rag-query.js --query "<téma>" --top 8` |
| integrita wiki | `node tools/knowledge/lint-wiki.js` |
| regrese hledání | `python -X utf8 tools/knowledge/evaluate_rag.py` |
| přepis relace | `node tools/knowledge/export-sessions.js --session <id>` |
| cena a smyčky relace | `python -X utf8 -I tools/knowledge/loop-stats.py --session <id> --detail` |
