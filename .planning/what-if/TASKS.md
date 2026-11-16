# What If — task index

12 ticketů. Stavy a dependencies: `task_pool.json`; tento index i tickety generuje `python agent_pool/scripts/pool.py render`.

| Ticket | Stav | P | Fáze | Agent/model | Dependencies | Název |
|---|---|---|---|---|---|---|
| [WI-001](tasks/WI-001.md) | done | P0 | M0 | claude/sonnet | — | Stopa zdrojů odzadu: final_for_orchestrator -> GPT-01..08 -> W01–W09 -> W10 (tabulka PE/INT/C/NC) |
| [WI-002](tasks/WI-002.md) | todo | P0 | M1 | claude/sonnet | WI-001 | Blokátor 1: autorita interakce mezi fyzikálními ostrovy (PE-017, INT-08, INT-11, C-05, C-06) |
| [WI-003](tasks/WI-003.md) | todo | P0 | M1 | claude/sonnet | WI-001 | Blokátor 3: aktivace mutací a závislé fyzikální reprezentace (PE-011, INT-03, INT-12, C-04) |
| [WI-004](tasks/WI-004.md) | todo | P0 | M1 | claude/sonnet | WI-001 | Blokátor 2: kauzálně konzistentní obnova více regionů (PE-013, INT-07, C-09, NC-37, NC-46) |
| [WI-005](tasks/WI-005.md) | todo | P0 | M1 | claude/sonnet | WI-001 | Blokátor 4: přechody mezi doménami a úrovněmi věrnosti, transakce, větvení (PE-014, PE-018, PE-019, INT-05, INT-06) |
| [WI-006](tasks/WI-006.md) | todo | P0 | M1 | claude/opus | WI-002, WI-003, WI-004, WI-005, WI-007 | Master Architecture Baseline (integrace blokátorů B1–B4 do jednoho dokumentu) |
| [WI-007](tasks/WI-007.md) | todo | P0 | M0 | claude/sonnet | WI-001 | Balík rozhodnutí pro uživatele: USER-001..010 a OQ-01..33 s doporučeními |
| [WI-008](tasks/WI-008.md) | todo | P1 | M2 | claude/sonnet | WI-006 | Doplnit W01–W09 odkazy na integrované kontrakty (bez přepisu originálů) |
| [WI-009](tasks/WI-009.md) | todo | P1 | M0 | claude/sonnet | WI-001 | Analýza mezer: C# What If kód (src/WhatIf.*) proti kontraktům z baseline |
| [WI-010](tasks/WI-010.md) | todo | P1 | M2 | claude/opus | WI-006 | Dokončení ADR-001..015 a registru PE-001..020 ve složce decisions/ |
| [WI-011](tasks/WI-011.md) | todo | P2 | M3 | claude/sonnet | WI-006, WI-009 | Rust workspace: kontraktní crates (čas, prostor, identita, revize) pro Stage 1 (jeden proces) |
| [WI-012](tasks/WI-012.md) | todo | P2 | M3 | claude/sonnet | WI-006, WI-009 | Rozhodnutí: co s C# planetárním jádrem a WorldGen (zůstane, port do Rust, nebo obojí) |
