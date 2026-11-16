---
type: concept
sources:
  - tools/knowledge/export-sessions.js
  - tools/knowledge/loop-stats.py
  - agent_pool/shared/prompts/worker-start.md
  - tools/knowledge/rag-query.js
  - AGENTS.md
updated: 2026-11-03
last_verified: 2026-11-03
status: implemented
aliases:
  - pasti harnessu
  - harness traps
  - heredoc selhal
  - unexpected EOF while looking for matching
  - sleep blocked
  - Monitor description
  - No module named PIL
  - File has not been read yet
  - robocopy exit 1
  - ENDOFLINE
  - auto mode classifier denied
  - scratchpad na C
  - artifacts/tmp
  - No module named numpy
  - tail option used in invalid context
  - no manual test for
  - Exit code 1 ale exit=0
  - přetrhnout loop
  - nepřetržený loop
  - pauzy při měření
  - cena ticketu
  - Monitor budí
---

# Pasti harnessu (Claude Code na Windows notebooku)

Opakované chyby nástrojů, ne projektu. Spočítané nad **všemi** race_game transcripty
(`~/.claude/projects/D--workspace-race-game*`, 2026-10-10 … 10-24) jako `is_error` výsledky
nástrojů; skript a postup: [[rag-knowledge-tools]], skill `rg-knowledge`.

| past | chyb / relací | příčina | co dělat |
| --- | ---: | --- | --- |
| Bash heredoc: `unexpected EOF while looking for matching '` | 43 / 35 | **délka příkazu**, ne apostrofy (lichý počet `'` mají selhané i úspěšné stejně často): z 1 015 heredoců žádný pod 7 568 znaků neselhal, každý nad 7 680 selhal, mezi tím 4 prošly a 4 ne. Sedí to na limit příkazové řádky Windows ~8 191 znaků včetně obalu, který přidává harness; `bash -c` dostane useknutý text (hypotéza z prahu, obal harnessu neměřen) | soubor nad ~6 kB piš nástrojem **Write**, úpravu nástrojem **Edit**; heredoc jen pro krátké vstupy |
| `File has not been read yet` u Write/Edit | 17 / 8 | harness odmítne zápis do souboru, který relace nečetla (typicky `orchestrator-state.md`, `loader.py`, `test_pool.py`) | nejdřív Read (stačí `limit`), pak Edit/Write |
| `Blocked: sleep N followed by …` | 17 / 13 | harness blokuje čekací řetězy | `run_in_background` a čekat na notifikaci, nebo Monitor s until-smyčkou |
| Monitor `required parameter description is missing` | 9 / 6 | Monitor je deferred tool; volán bez načteného schématu a bez `description` | ToolSearch `select:Monitor`, pak volat s `description` |
| `.ps1 is not digitally signed` | 4 / 4 | ExecutionPolicy; skript v `%TEMP%` | `powershell -NoProfile -ExecutionPolicy Bypass -File <ps1>` |
| dotnet format `ENDOFLINE` v `check.ps1` | 4 / 4 | Python `open(..., 'w')` na Windows píše CRLF, repo je LF | Edit nástroj, nebo `newline=''` |
| robocopy „Exit code 1" | 3 / 3 | robocopy 0–7 = úspěch (1 = zkopírováno) | kontroluj `rc -lt 8`, ne `$LASTEXITCODE -eq 0` |
| `Remove-Item on system path … is blocked` | 3 / 3 | ochrana harnessu proti mazání se zástupným znakem v proměnné | mazat konkrétní soubory, nebo nechat na uživateli |
| `No module named 'PIL'` | 2 / 2 | Pillow je v **uživatelském** site-packages; `python -I` ho vypíná | Pillow skripty spouštět bez `-I` (`tools/kn5-unity/shot-diff.py`) |
| `gh: command not found` | 1 / 1 | GitHub CLI není nainstalované | licence ověřit přes web nebo nechat na uživateli (instalace = rozhodnutí uživatele) |

## Přírůstek 2026-10-24 … 11-01 (81 relací, 216 chyb `is_error`)

Vytěženo skriptem nad syrovými JSONL. Postup je ve skillu `rg-knowledge`, oddíl Mining.

| past | chyb | příčina | co dělat / co se změnilo |
| --- | ---: | --- | --- |
| PowerShell nástroj hlásí `Exit code 1`, i když skript vypsal `exit=0` | ≥ 13 | příčina neověřena; týká se `build-player.ps1`, `check.ps1` a `verify-hygiene.ps1` s přesměrováním `*>`, kde vypsaný `$LASTEXITCODE` je 0 | věř vypsanému kódu; kvůli tomu build nespouštěj znovu |
| `File has not been read yet` | 14 | stále hlavně orchestrátor (`decisions.md`, `features.md`, `orchestrator-state.md`) | nejdřív Read |
| robocopy „Exit code 1“ | 12 | kopie `Library` do nového worktree | beze změny: 0–7 = úspěch |
| auto-mode classifier: odmítnuto | 11 | skripty a stahování do C: scratchpadu, `$env:TEMP='D:\tmp'`; uživatel 2026-10-29: „máš zůstat v projektu na disku D“ | `<worktree>/artifacts/tmp/`; pravidlo je v `AGENTS.md` a ve skillu `rg-worker` |
| `manual-test.py show`: „no manual test“ | 3 | JSON existuje do merge jen na větvi ticketu | spouštět z worktree ticketu (skill `rg-orchestrator`, krok 6) |
| `Stop-Process` zbylého playeru: odmítnuto klasifikátorem | 2 (RG-123, RG-124) | auto-mode classifier nepustí zabití procesu | player s `--agent-port`: `nr.py --report-dir <dir> quit`; vlastní úlohy na pozadí: TaskStop; jinak nechat uživateli |
| jízda „nenaskočí“ (`-PlayerExtra '...'`) | 2 jízdy uživatele | rides běží přes `cmd.exe`, ten jednoduché uvozovky nezná | **nástroj:** `manual-test.py new` takový příkaz odmítne (test v `test_manual_test.py`) |
| `/tmp/...` „No such file“ | 5 | Git Bash `/tmp` ≠ `/tmp` Windows Pythonu | žádné `/tmp`, jen `artifacts/tmp/` |
| Monitor `description is missing` | 4 | agent poslal argumenty ToolSearch přímo Monitoru | nejdřív nástroj **ToolSearch** `select:Monitor` |
| `No module named 'numpy'` | 3 | numpy není nainstalované vůbec | `PIL.ImageStat`, `shot-diff.py`; Pillow bez `-I` |
| `rag-query.js` v PowerShellu exit 255 + `NativeCommandError` | 4 | exit 255 (-1) způsobí `\| Select-Object -First N`: PowerShell zastaví pipeline dřív, než node dopíše (ověřeno 2026-11-01: bez ořezu exit 0). Červený `NativeCommandError` byla hláška o přestavbě indexu na stderr pod `2>&1` | výstup omezuj přes `--top N`, ne přes `Select-Object -First`; **nástroj:** hláška jde na stdout (stderr jen v `--json`). Platí pro každý nativní program s dlouhým výstupem |
| `tail: option used in invalid context -- 5` | 3 | `tail -5` ve složeném příkazu | `tail -n 5` |
| `Assets/...` „No such file“ | 2 | cesty v briefu jsou relativní k `engine-unity/NightRacer` | skill `rg-worker`, Traps |
| `queue`: „reserved, not todo“ | 1 | `pool.py handoff` už úlohu zařadil do fronty | jen `claim` (skill `rg-worker`, Start) |

## Nepřetržený loop (nejdražší past, uživatel 2026-11-03)

Většina Sonnet ticketů stála 3–4 USD, někdy 6–7. RG-128 na Opusu uživatel zastavil při 18,22 USD (`/usage`), protože v promptu chyběly pauzy při měření. **Agent musí loop přetrhnout:** běh nad ~2 min spustit na pozadí a ukončit tah. Pravidlo je od uživatele z jeho nákladů, prompt a skill `rg-worker` ho nesou.

Co ukázal transcript RG-128 (`tools/knowledge/loop-stats.py`, součty sedí s `/usage`: 161 requestů, 44,6 M cache read, 175k output):
- První loop 82 requestů a 47 min bez přerušení, kontext 52k → 314k, 7 čekání v popředí po 2–4 min.
- Pak 31 probuzení na kontextu 315–422k, z toho 18 bez práce (1 request, ~0,35 M cache read). **Monitor budí model při každém řádku výstupu**; agent do něj posílal průběh ze souboru. Monitor má vypsat jen konec nebo selhání.
- Probuzení tedy nejsou zadarmo: přetrhnout loop ano, ale jedno probuzení na dávku.

Hranice měření: `loop-stats.py` čte `usage` z JSONL a USD nepočítá; hranice loopu je jeho definice (`end_turn` nebo příchozí zpráva), ne údaj z backendu. `export-sessions.js` měří transcript jako 1 loop, na ceny se na něj nespoléhat. V relacích nad 60 requestů od 30. 10. má Monitor hlavně RG-128 (5×), ze Sonnetu jen RG-084, RG-088, RG-117 a RG-123 (1–2×). Sonnet loop častěji trhá během na pozadí s notifikací (RG-108, RG-119, RG-120: 0× Monitor, 5–6 notifikací). Délka loopu sama cenu nevysvětlí: RG-120 (Sonnet) má nejdelší loop 113 requestů a 34,6 M cache read, RG-128 má 82 a 44,6 M, z toho 63 % v probuzeních na velkém kontextu.

## Mimo počty chyb (tiché ztráty)

- **Worker usne a nic ho neprobudí — notifikace se přitom neztrácí** (oprava 2026-11-11 z transcriptů,
  `loop-stats.py --session <id> --detail`). Budíček pošle jen nástroj s `run_in_background: true`
  (nebo příkaz, který harness po timeoutu přesune na pozadí) a jen když příkaz skončí:
  - RG-150 (2026-11-10): za heredocem zůstalo `cat > /dev/null`, `cat` čekal na stdin navždy, po
    120 s „moved to the background“, `check.ps1` se ani nespustil; spánek 96,7 min do zprávy
    uživatele, notifikace „killed“ až po 748,7 min. Dřívější zápis „check.ps1 doběhl, budíček
    nepřišel“ byl mylný.
  - RG-152 (2026-11-11): kontroly přes `( … ) > /dev/null 2>&1 &` — harness o nich nevěděl,
    doběhly za 2 min, spánek 86,3 min.
  Obrana: nikdy shell `&`, `nohup`, `Start-Process` bez `-Wait`, `Start-Job`; žádné `cat`/`read`
  bez vstupu; „moved to the background“ u příkazu, který měl být krátký, je signál zaseknutí.
  Kontroly před review v popředí s `timeout: 600000`. Pod pool-supervisorem (Zonio
  `tools/agents/pool-supervisor.js`) zakáže odpojené formy PreToolUse pojistka a hlídač probudí
  workera, který skončil tah s ticketem `in_progress` a pod oknem mu nic neběží (5 min) nebo visí
  běh bez CPU (20 min). V ruční relaci zůstává pojistkou poslední věta „… když se do 10 min
  neozvu, napiš *pokračuj*“. Postup: skill `rg-worker`, Break the loop.
- **Dlouhé běhy na pozadí** zabije harness při nedostatku paměti → měřicí série po
  částech ≤ ~10 min (`tools/graphics-lab/run-chunk.ps1`, rozhodnutí uživatele 2026-10-21).
- **Limit relace** přeruší i subagenta; výsledek pak není známý (RG-082, 2026-10-21) — proto
  workery spouští uživatel ([[orchestration-workflow]]).
- **Compact** zkrátí kontext, transcript zůstává celý: `export-sessions.js --session <id>`.

Související: `unity-notebook-runs` (race_game), [[workflow-tools]], [[agent-skills]].
