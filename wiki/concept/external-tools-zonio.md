---
type: concept
sources:
  - tools/knowledge/export-sessions.js
  - tools/knowledge/rag-query.js
  - tools/review/README.md
updated: 2026-10-19
last_verified: 2026-10-19
status: implemented
aliases:
  - external tools
  - externí nástroje
  - zonio nástroje
  - transcript tools
  - obnovit soubor z transcriptu
  - kolik kontextu má relace
  - stáhnout webovou stránku jako zdroj
---

# Externí nástroje ze Zonia (používané na místě)

Spouštějí se přímo ze `D:\workspace\zonio_app\tools`, **nic se nekopíruje do
repa** ([[knowledge-layer-separate-from-zonio]]). Každý záznam: úkon → příkaz →
co nástroj **odmítá / kde lže** → kdy ověřeno na race_game. Záznam „nefunguje
pro race_game" má stejnou cenu jako funkční nástroj: nezkoušej ho znovu.

Cesty jsou Windows absolutní; `Z` = `D:\workspace\zonio_app\tools`.

## Použitelné (ověřeno 2026-10-19 nad race_game)

| úkon | příkaz | limity a pasti |
| --- | --- | --- |
| Export transcriptů relací (Claude + Codex) do Markdown/JSONL | `node tools\knowledge\export-sessions.js` (race_game wrapper volá Zonio exportér jen pro čtení) | výstup `artifacts/session-export/` je soukromý, nikdy necommitovat; redakce tajemství je best effort; Codex relace dostanou kosmetický štítek `v1.11` |
| Obnovit necommitnutou verzi souboru, kterou zničil `git checkout` / přepis | `node Z\agents\recover-file-from-transcript.js --file <část cesty> --list`, pak `--replay --base <soubor> --out <soubor>` | čte všechny Claude transcripty v `~/.claude/projects` (i race_game worktrees); přehrává jen `Write`/`Edit`/`MultiEdit`/`NotebookEdit` — změny přes Bash/PowerShell v transcriptu nejsou; replay je slepý, výsledek ověř testem; ověřeno `--list` na `tools/knowledge/README.md` (5 operací) |
| Kolik kontextu tahle relace spotřebovala | `node Z\agents\session-context-meter.js --session <id> --cwd <worktree>` | prahy (STOP 220k, ~1,6k tokenů/inference) jsou změřené na Zonio relacích — orientace, ne race_game pravidlo; ověřeno: 101k, zóna green |
| Stáhnout webový zdroj (fyzikální literatura, licence assetu) jako neměnný snapshot s hashi | `node Z\research\stage-external-source.js --url <https> --output-dir <race_game>\artifacts\research-staging --dry-run`, pak bez `--dry-run` | **vždy `--output-dir`**, jinak zapisuje do Zonio `tools/reports/`; Defuddle čištění potřebuje `--allow-npx` (ad hoc `npm exec defuddle@0.19.1`), `--cleaner basic` je jen pro fixture; stažená stránka jsou nedůvěryhodná data; do wiki jde až shrnutí s provenance; ověřeno jen `--dry-run` (nic nezapsal) |
| Najít mojibake (rozbitá čeština po cp1250/cp1252) | `node Z\maintenance\mojibake-fix.js --root <race_game> --ext ".md,.cs,.py,.ps1,.json" --exclude "Library\|Temp\|artifacts"` | bez `--apply` je dry-run; **`--report` zapisuje do Zonio `temp/`**, výchozí režim jen vypisuje; bez `--exclude` skenuje Unity `Library/` (14 114 souborů); ověřeno 2026-10-19: race_game zdroje čisté, jediný nález Clipper.cs v Unity PackageCache |
| Bezpečně přejmenovat wiki stránku s přepisem odkazů | `node Z\maintenance\rename-wiki-page.js --root <race_game> --from concept/a --to concept/b` (dry-run), pak `--apply` | dry-run je výchozí; validuje přes **Zonio** `lint-wiki.js`, ne race_game verzi — po `--apply` spusť race_game lint; přepíše řádek v `index.md` a starý slug přidá do `aliases`; `--json` vypisuje celé soubory v base64 (velký výstup); ověřeno dry-run 2026-10-19 |

## Nefunguje pro race_game (nezkoušet)

| nástroj | proč |
| --- | --- |
| `Z\maintenance\mutate.js` (mutační test: umí test zčervenat?) | kořen natvrdo Zonio: cíle i zálohy pod `zonio_app`; princip je cenný → až bude potřeba, malá Python verze v `tools/review/` |
| `Z\rag\evaluate-rag.js` | měří Zonio `rag-query` ranking, ne race_game port; race_game má `tools/knowledge/evaluate_rag.py` |
| `Z\maintenance\lint-instruction-links.js` | kořen natvrdo Zonio, `--root` nemá |
| Zonio pool, router, handoff, hot-worker launch skripty | race_game pool je kanonický; launch workerů je ruční (AGENTS.md) |
| `Z\agents\skill-uptake-meter.js`, `session-efficiency-meter.js` | vázané na Zonio skilly / zmrazené |

## Principy převzaté bez kódu

- **Test, který umí selhat** (Zonio skill `test-the-test`): PASS / FAIL /
  INVALID RUN, negativní kontrola, test musí zčervenat na neopraveném kódu. Race_game
  to už dělá v S1 validity bráně (`summarize.py` → `valid=false`) a v akceptaci
  ticketů („test, který na kódu před opravou selže").
- **Katalog úkonů** (Zonio `agent-capabilities`): tahle stránka je jeho
  race_game obdoba — formulace úkonem a co nástroj odmítá.
- **Obsidian konvence** wiki: [[obsidian-wiki-conventions]].
