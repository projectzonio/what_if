# Automatizace workflow: méně „user", víc nástrojů (návrh, 2026-10-22)

Status: **ACCEPTED 2026-10-22 (sekce 3, ADR-028)**; fáze 1 = ticket **RG-086**.
Fáze 2–3 (pool, paměť) a fáze 4 (classifier) jsou zatím jen návrh bez ticketu;
classifier se nestaví, dokud neprojdou fáze 1–3.

Cíl: uživatel zůstává jen u úkonů nevhodných pro AI (ruční jízda, vizuál, světla,
odlesky, zvuk, pocit z řízení, ověření resetu) a u rozhodnutí o směru projektu.
Všechno ostatní, co dnes prochází přes „user", převezmou pool, zámky a politika.

## 1. Měření: co dnes dělá „user" (transcripty 2026-10-10 … 10-22)

Zdroj: všech 70 relací `~/.claude/projects/D--workspace-race-game*`, jen lidské
turny hlavního vlákna (bez task-notification a pokračování po compactu).
Klasifikace regexem + ruční kontrola; čísla jsou řádová, ne přesná.

| kategorie | orchestrátor | workery | automatizovatelné? |
| --- | ---: | ---: | --- |
| spouštěcí prompt („převezmi orchestraci, RG-0xx review" / „Pracuj na RG-0xx…") | 26 | 9 | **ano** — šablona + později classifier |
| holé potvrzení („pokračuj", „ano", „jeď", „zapni") | 22 | 67 | **ano** — politika autonomie (sekce 3) |
| „notebook je volný / smíš buildit" | 6 | 25 | **ano** — zámek notebooku (sekce 4.1) |
| výběr dalšího ticketu („připrav RG-0xx", „2 tickety podle logiky") | 10 | — | **ano** — `pool.py next` + priorita |
| přenos mezi relacemi („worker chce validaci", „pool.py asi neproběhl") | 5 | 3 | **ano** — events log + `review --amend` |
| oprava zastaralé znalosti (kamera, reset „stará feature") | 2 | 1 | **ano** — registr funkcí (sekce 4.4) |
| řízení rozpočtu („loop má 35k tokenů", „14 % limitu") | 2 | 2 | **ano** — rozpočet na relaci |
| **ruční jízda / vizuální verdikt** | ~15 | ~57 | **ne** — zůstává uživateli, ale dávkově |
| rozhodnutí: ADR, licence/zdroj assetů, změna kritéria, vize | ~16 | ~10 | **ne** — sbírat do jedné fronty rozhodnutí |

Worker `AskUserQuestion` (74×): ~35 = „smím spustit Unity build/player?", ~20 =
žádost o verdikt jízdy, ~10 = technická otázka, kterou má rozhodnout orchestrátor
(mapování povrchu GRASS, reuse MiniJson, merge main před úpravou).

**Závěr:** přibližně 70–75 % lidských turnů je mechanických. Nezbytné zbývají
ruční testy (~25 %) a rozhodnutí o směru (~8 %).

## 2. Kořenové příčiny

1. **Notebook je sdílený zdroj bez obecného zámku.** `measure-lock.ps1` chrání jen
   měřicí série; build a ruční běhy se proto „zamykají" dotazem na uživatele.
2. **Ruční test je vázaný na živou relaci workera.** Worker čeká u otázky, uživatel
   musí být u PC ve správný okamžik; orchestrátor pak často žádá druhou jízdu na
   integračním buildu („kombinaci nikdo neřídil" — RG-027+034, RG-044+071, c1+zvuk).
3. **Orchestrátor pracuje po krocích s potvrzením** („Pokračovat krokem 3?") —
   původně ochrana rozpočtu, dnes hlavní zdroj čekání.
4. **Paměťová vrstva je log, ne stav.** `agent_pool/agents/claude/memory/MEMORY.md`
   má 101 kB / 43 append-only checkpointů; každé převzetí orchestrace ho čte
   (~25–30k tokenů) a 3× bylo stejně nutné „projít transcripty".
5. **Pool nemá přechody pro opakování práce.** Druhé `pool.py review` po fázi B
   selže („Review requires in_progress"); `retry` maže evidence a vrací do `todo`
   (nový queue+claim); startovací prompt píše orchestrátor ručně.
6. **Žádný trvalý registr ověřených funkcí** → agenti navrhují znovu udělat reset
   na trať nebo řeší „sekání kamery" z dávného testu.

## 3. Politika autonomie (nahrazuje „pokračuj")

| úkon | dnes | návrh |
| --- | --- | --- |
| další krok review (diff, check, hygiene, branch-scope, recompute) | ptá se po každém kroku | **bez dotazu**, jedna souvislá relace |
| Unity build / headless běhy / smoke | ptá se | **bez dotazu**, pod zámkem notebooku |
| merge větve do `main` / integrace | ptá se | **bez dotazu**, když check+hygiene+branch-scope zelené a manuální testy ticketu mají verdikt OK |
| `pool.py approve` | ptá se | bez dotazu za stejných podmínek; nikdy na ticketu, který orchestrátor sám upravoval (místo úpravy → `rework`) |
| uvolnění dalšího ticketu + worktree + startovací prompt | ptá se „mám připravit?" | **bez dotazu** podle `pool.py next` (priorita, závislosti, max. 2 paralelní, max. 1 GPU-bound) |
| technická otázka workera v rámci briefu | jde přes uživatele | worker rozhodne sám a zapíše do outboxu; orchestrátor ji posoudí v review |
| ADR accept, změna/zmírnění acceptance kritéria, licence/asset, instalace, cokoli na C:, push, launch procesu | ptá se | **zůstává uživateli** — ale jako položka v `decisions-inbox.md`, ne jako blokující otázka uprostřed relace |
| ruční jízda / vizuál | ptá se živě | **manuální testovací fronta** (4.2) |
| pořadí u vizuálních ticketů (světla, stíny, materiály) | snímky → měření → jízda | **snímky → jízda uživatele → měření** na zmrazeném vzhledu (RG-081, 2026-10-23: vada stínů nalezená až po ~3 h měření); tady jízda ve worker relaci, ne v dávce |

Orchestrátor končí relaci jedním souhrnem: co sloučil/schválil, co uvolnil
(s cestou k promptu), co čeká na ruční test, co čeká na rozhodnutí.

## 4. Nástroje (fáze 1–3)

### 4.1 Zámek notebooku (fáze 1, malý)
Rozšířit `tools/game-unity/measure-lock.ps1` o druhý režim `slot` (Unity build,
player běh, Blender): exkluzivní, s čekáním (poll + timeout) místo odmítnutí;
měřicí série dál drží exkluzivní `measure`. Build skripty volají
`Enter-NotebookSlot`. Pravidlo do briefů/AGENTS: *„Před buildem se neptej, vezmi
slot; když čekáš > 15 min, zapiš to do outboxu a pokračuj prací bez Unity."*
Dopad: odpadá ~65 turnů „ntb je volný / můžeš build".

### 4.2 Manuální testovací fronta (fáze 1, střední)
- Worker po automatických testech nečeká na jízdu. Zapíše
  `agent_pool/shared/manual-tests/RG-0xx.json`: build/launcher příkaz, mapa, auto,
  fyzika, checklist otázek (světla, odlesky, reset na trať, …) a co je „OK".
- Worker jde do `review` s automatickou evidencí + odkazem na tento soubor.
- Orchestrátor po automatickém review sloučí do integrační větve a postaví **jeden**
  build pro všechny čekající ruční testy (jedna jízda pokryje i integraci).
- Uživatel: `Start-Graphics-Lab.cmd` (launcher) ukáže sekci „Čekající ruční testy",
  spustí správnou konfiguraci a verdikt uloží (`manual-test.py record RG-0xx`
  nebo odpověď v chatu, kterou orchestrátor cituje doslova).
- Approve až po verdiktu; FAIL → `rework` s citací verdiktu.
- Volitelně PushNotification „ruční test připraven" (remote-control).

### 4.3 Pool (fáze 2, malý)
- `pool.py review --amend` — doplnění evidence ve stavu `review` (fáze B).
- `pool.py rework RG-0xx --reason` — `review → in_progress`, zachová dispatch,
  worktree i evidence historii; vypíše rework prompt.
- `pool.py handoff RG-0xx` — queue + worktree (`new-worktree.ps1`) + startovací
  prompt ze šablony (brief „Prepared handoff") do
  `agent_pool/shared/prompts/RG-0xx.md`; orchestrátor ho jen odkáže.
- `pool.py next` — doporučení dalších ticketů (ready, priorita, disjunktní
  `write_scope`, konflikt GPU).
- `agent_pool/events.jsonl` — append při každém přechodu (`ts, task, from, to,
  agent, job_id`). Základ pro classifier; dnes jen log.

### 4.4 Paměťová vrstva (fáze 2, revize)
- `agent_pool/agents/claude/memory/MEMORY.md` → **jen aktuální stav** (≤ 8 kB):
  aktivní tickety, otevřená rozhodnutí, trvalá pravidla. 43 checkpointů přesunout
  do `memory/archive/2026-10.md` (Git historie stačí, archiv jen kvůli RAG).
- `agent_pool/shared/orchestrator-state.md` — handover orchestrátora, přepisuje
  se (ne append) na konci každé relace: `## Resume here`, čekající ruční testy,
  decisions-inbox, další krok. Převzetí = tento soubor + `pool.py status`.
- `wiki/entity/features.md` — registr ověřených funkcí (reset na trať: 2 způsoby,
  F5 záznam Gunsai, HUD, nitro, převodovka režimy, …): co dělá, jak se testuje,
  ticket, datum ověření. Brání halucinacím a opakování práce.
- `agent_pool/agents/claude/logs/` (493 MB, 263 souborů): retenční pravidlo —
  po `done` zůstává jen evidence odkazovaná v `task_pool.json`, zbytek mimo
  pracovní strom (nejdřív audit, nic nemazat bez potvrzení).

### 4.5 Šablona spouštěcích promptů (fáze 1, triviální)
Orchestrátor: `Převezmi roli orchestrátora. RG-0xx [a RG-0yy] je ve stavu review.`
Worker: `Pracuj na RG-0xx podle agent_pool/shared/prompts/RG-0xx.md.`
Rework: `Rework RG-0xx podle agent_pool/shared/prompts/RG-0xx-rework.md.`
Vše ostatní (povolení queue/claim, pool.py review na konci, sdílený notebook)
je v promptu souboru, ne v chatu.

## 5. Fáze 4: classifier (až po 1–3)

Nadstavba nad events log; stejná role jako Zonio `context-router.js`, ale
**spouští se jen na hranici relace**, nikdy do běžící relace (to byla příčina
loopu v Zoniu).

- Vstup: nová událost v `events.jsonl` **a** ukončený proces dané relace.
- `→ review`: spustí `claude -p --model claude-opus-5-5 --effort high
  "Převezmi roli orchestrátora, RG-0xx je ve stavu review."`
- Orchestrátor končí zápisem `orchestrator-state.md` + strojového
  `decision.json` (`rework` / `release` / `await_manual` / `await_decision`).
- `rework`/`release` → classifier spustí workera s promptem ze souboru.
- `await_manual` / `await_decision` → notifikace uživateli, classifier čeká.
- Pojistky: idempotentní klíč `job_id+přechod`, max. 2 reworky na ticket, denní
  rozpočet relací, žádné spuštění při držení `measure` zámku, kill switch soubor.

## 6. Rozhodnutí uživatele (2026-10-22)

Uživatel doslova: „1, 2, 3 ano, založ fázi 1 jako ticket a commitni".

1. Politika autonomie (sekce 3) — **ano** → ADR-028.
2. Merge do `main` a `approve` bez dotazu při zelených branách + OK ručním testu — **ano**.
3. Ruční testy dávkově na integračním buildu místo živě u workera — **ano**.
4. Fáze 1 jako ticket — **RG-086** (zámek notebooku, fronta ručních testů, šablony promptů).
