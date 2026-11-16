Zrevíduj $ticket v jedné relaci podle PD-001 (bez průběžných potvrzení). Brief $brief, worktree $worktree.
Postup a pasti: skill `wi-orchestrator`.

1. Diff a rozsah: `python -X utf8 -I tools/review/branch-scope.py --worktree $worktree --brief $brief`.
2. `scripts/check.ps1` a `scripts/verify-hygiene.ps1` (exit 0). U výzkumného výstupu otevři aspoň 5 citovaných `path:řádky` a porovnej se zdrojem; tvrzení bez zdroje je nález. Čísla bez měření musí být označena "návrh, nezměřeno".
3. Rozhodnutí patřící uživateli (USER-001..010, OQ-xx) nesmí worker rozhodnout; ověř, že jsou jen vypsaná s doporučením.
4. Merge a `pool.py approve $ticket --reviewer <jméno> --evidence <review>` jen po zelených bránách; ticket, jehož výstup jsi sám upravoval, nemerguj, vrať ho workerovi.
5. Po approve aktualizuj wiki (stránka + `sources:` + wikilinky, `lint-wiki.js`, `build-rag-index.js`, případ do `rag-evaluation.v1.json`) a bez dotazu uvolni další tickety podle `pool.py next` (max. 2 workery): doplň a commitni v briefu `### Exact write ownership` a „Prepared handoff“, pak `pool.py handoff WI-0xx`. Text každého startovacího promptu vlož do závěrečné odpovědi v code blocku a nad něj napiš složku, ve které se má relace otevřít.
6. Uživateli sbírej jen to, co AGENTS.md nechává jemu (architektonická rozhodnutí/ADR, acceptance, licence a zdroje dat, instalace, C:, push, spuštění workerů), jedním blokem na konci.
$ask_user
