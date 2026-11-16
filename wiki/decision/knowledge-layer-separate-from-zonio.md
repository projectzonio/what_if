---
type: decision
sources:
  - tools/knowledge/README.md
  - tools/knowledge/lint-wiki.js
  - tools/knowledge/export-sessions.js
  - agent_pool/scripts/pool.py
  - agent_pool/README.md
updated: 2026-10-19
status: implemented
aliases:
  - oddělení od Zonia
  - zonio separation
  - external tools policy
  - python místo npm
---

# Znalostní vrstva NightRaceru je oddělená od Zonia

## Rozhodnutí

1. **Pool a plán race_game jsou kanonické a nejnovější.** Stav ticketů je jen v
   `.planning/race-game/task_pool.json`, mutuje ho jen `agent_pool/scripts/pool.py`.
   Zonio pool, jeho témata, handoff/router a vše související je pro race_game
   **zastaralé** a nikdy se nestává instrukcí (uživatel 2026-10-19). `lint-wiki.js`
   odkaz na Zonio pool ve wiki nebo skillu hlásí jako fatal.
2. **Nástroje se nepřebírají „tak jak jsou".** Race_game píše vlastní nástroje
   v Pythonu (stdlib), ne přes npm. Výjimka je znalostní vrstva
   `tools/knowledge/*.js`: port z Zonia z 2026-10-11, Node bez npm závislostí.
   Zůstává v JS (přepis 3 000 řádků funkčně nic nedoručí); potvrdil uživatel
   2026-10-19.
3. **Občasné nástroje se používají na místě** ze `D:\workspace\zonio_app\tools`,
   bez kopie do repa. Race_game o nich ví jen přes katalog
   [[external-tools-zonio]] (indexovaný v RAG) a sekci *External tools* ve
   `wiki/index.md`. Nástroj, který se začne používat pravidelně, dostane
   vlastní malou Python verzi, ne kopii JS.
4. **Bez npm závislostí v repu.** Volitelná embeddings vrstva Zonia (~380 MB
   npm balík) se nepřenáší; RAG je lexikální BM25 + wiki graf + C# graf.

## Proč (Why)

- Zonio vrstva řeší jiný projekt (7 repozitářů, FW, pool s BRIEF/PLAN/STATE);
  její pravidla v race_game dávají falešné instrukce.
- Kopie nástroje je druhý zdroj pravdy, který stárne bez vlastníka.
- Python je jazyk všech race_game nástrojů (`pool.py`, `tools/review/`,
  `summarize.py`); agent je umí číst a spustit bez další runtime vazby.

## Co by rozhodnutí vyvrátilo

Pokud by se externí nástroj volal v každé relaci (ne občas), patří do repa jako
Python. Pokud by se Zonio nástroj změnil nekompatibilně, katalog to zjistí až při
použití — proto má každý záznam datum ověření.
