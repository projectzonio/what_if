---
type: entity
sources:
  - tools/review/
updated: 2026-11-02
last_verified: 2026-11-02
status: implemented
aliases:
  - review tools
  - tools/review
  - manual-test
  - ruční test
  - branch-scope
  - recompute-s1
---

# Nástroje pro review (`tools/review`)

Jen stdlib Pythonu, read-only kontroly, které nahradily skripty na míru každého ticketu (`tools/review/README.md`):

| nástroj | co dělá |
| --- | --- |
| `recompute-s1.py` | přepočet S1 z raw CSV snímků (protokol 1.2): P50/P95/P99, záseky > 50 ms, exkluzivní fullscreen, jeden `build_id` |
| `manual-test.py` | fronta ručních jízd: `new`, `run RG-0xx` (jízda → otázky → další jízda → souhrn), `--runs-file`, OS metriky a screenshoty automaticky; návod `agent_pool/shared/manual-tests/README.md` |
| `branch-scope.py` | porovná diff větve s `### Exact write ownership` briefu: dotčené read-only cesty = fail, nezařazené = warn |
| `make-prompt.py` | startovací prompty (vlastní orchestrátor) |

## Pasti

- `branch-scope.py` a `pool.py next` čtou jen **zpětně uvozené** tokeny; hlavička sekce se hledá jako `### Exact write ownership`
  (včetně `(draft)`), bullety míchají globy i cesty — [[workflow-tools]] řádek `brief-graph-check` na to navazuje.
- `manual-test.py new` odmítne příkaz jízdy s jednoduchými uvozovkami (cmd.exe), viz `test_manual_test.py` a [[agent-harness-traps]].
- JSON ručního testu je do merge jen na větvi ticketu; `show`/`run` se spouští z worktree ([[orchestration-workflow]]).
- Pool příkazy (`pool.py`) jsou v [[workflow-tools]], ne tady.

Související: [[agent-skills]], `unity-notebook-runs` (race_game).
