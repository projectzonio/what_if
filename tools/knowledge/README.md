# tools/knowledge — What If second brain (GraphRAG-lite)

Dependency-free Node (`node --version` = v22.14.0 on the dev notebook, 2026-10-11).
Ported from `zonio_app` tools on 2026-10-11; separate from Zonio, no Zonio pool or pool skill.
Generated output goes to `artifacts/knowledge/` (gitignored, per worktree).

| tool | purpose |
| --- | --- |
| `build-rag-index.js` | BM25 chunk index over docs, briefs, reviews, memory, wiki, skills and code + wiki link graph + C# labels |
| `rag-query.js` | query: Czech stemming, CS↔EN expansion (`cs-en-domain-synonyms.v1.json`), wiki alias boost, 1-hop neighbours, code cards, **skill cards** (which `.claude/skills` skill to load and why) |
| `code-graph-cs.js` | lexical C# graph: types, methods, `call` / `new` / `inherits` / `type-ref` edges with file:line |
| `lint-wiki.js` | wiki integrity; fatal on empty wiki and on Zonio pool references |
| `export-sessions.js` | exports Claude + Codex transcripts via the Zonio exporter (read-only) to `artifacts/session-export/`; `--session <id-prefix>` exports one Claude session |
| `evaluate_rag.py` | retrieval regression (Python stdlib) over `rag-evaluation.v1.json`: rank of the expected source, PASS / FAIL / INVALID RUN, known gaps; `expectedSkill` / `noSkillCard` check the skill cards |
| `brief-graph-check.py` | brief → wiki stránky a sousedé pro `### Exact write ownership`, `MISSING` pro cesty bez stránky (RG-121); test `test_brief_graph_check.py` |
| `audit-graph-areas.py` | audit oblastí kódu: stránka, zdroje, odkazy dovnitř/ven, verdikt covered/thin/missing (RG-121) |
| `measure-ticket-coverage.py` | podíl vlastněných cest ticketů, k nimž dotaz vrátí wiki stránku a souseda (before/after) |
| `loop-stats.py` | loopy relace ze syrového JSONL: requesty, kontext, probuzení, Monitor, největší výstupy nástrojů; tokeny z `usage`, USD nepočítá; test `test_loop_stats.py` |

New tools here are Python (stdlib); the JS files are the 2026-10-11 Zonio port and stay JS for now.
Occasional Zonio tools are used in place, not copied: catalogue `wiki/concept/external-tools-zonio.md`,
section *External tools* in `wiki/index.md`. Own tools by action: `wiki/concept/workflow-tools.md`.

```powershell
node tools\knowledge\build-rag-index.js
node tools\knowledge\rag-query.js --query "<dotaz>" --top 8
node tools\knowledge\code-graph-cs.js <Symbol>
node tools\knowledge\lint-wiki.js --write-report
python -X utf8 tools\knowledge\evaluate_rag.py [--verbose]
```

## Runtime graf scény (RG-125, 2026-11-03)

Snapshot běžícího playeru (agent port RG-124) jako uzel v tomtéž grafu: živý objekt → C# třída, která ho postavila → wiki stránka.
Deterministicky, bez LLM, stdlib Python. Výstupy jsou git-ignorované (`artifacts/knowledge/runtime/`).

```powershell
# 1) player s portem (viz tools/agent-port/README.md), pak snapshot; široký kořen StreamedSectors se čte po dětech (tree má strop 20 000 uzlů / 1 MB a --depth max 12)
python -X utf8 -I tools\knowledge\runtime-graph.py snapshot --report-dir artifacts\agent-port\<run> --label worldgen-seed1-tunnel --map worldgen-seed1
# 2) kolaps jmen na vzory + hrany (potřebuje artifacts/knowledge/latest-code-graph.json, vzniká v build-rag-index.js)
python -X utf8 -I tools\knowledge\runtime-graph.py build --label worldgen-seed1-tunnel
# 3) index a dotaz
node tools\knowledge\build-rag-index.js
node tools\knowledge\rag-query.js --query "Tunnel MeshCollider"
python -X utf8 -I tools\knowledge\test_runtime_graph.py
```

- **Kolaps:** čísla (`Sector -11,-15` → `Sector *,*`, `010_01ROAD.004` → `*_*ROAD.*`, `cone1308` → `cone*`) se mění na `*`, krátké číslice uvnitř slova zůstávají (`Kn5Test`, `body_LOD1`). Graf drží vzory s počtem instancí, ne instance.
- **Hrany vzoru:** komponenty → C# soubor (třída stejného jména v code grafu) nebo `unity-builtin` (pojmenovaný seznam `UNITY_BUILTIN`, ne zahozený) nebo `unresolved` (cíl 0); C# soubor → wiki stránka přes `sources:` (prefix); tvůrce = `new GameObject("…")` a řetězcový literál jako argument volání v souboru, který vytváří objekty (`MakeStruct(…, "Tunnel", …)`), každá hrana nese `evidence` (`new-gameobject-literal` > `name-literal` > `add-component-own-type` > `parent-creator` pro dynamická jména jako KN5 meshe); `related` = C# soubor, jehož název obsahuje jméno vzoru jako celé camelCase slovo (`Tunnel` → `WorldGenTunnel.cs`).
- **Index:** `build-rag-index.js` přidá jeden chunk na vzor (`runtime:<label>:<pattern>`) a `ragIndex.runtime` s hranami; bez snapshotu se index nemění. `rag-query.js` pro takový zásah dá pod Graph neighbors builder (C#) a stránku, která ho cituje v `sources:` přesně, a do Code graph kartu typu v builder souboru. Změna snapshotů staví index znovu (`freshness.runtimeHash`).
- **Meze:** lexikální (žádný typový rozbor): tvůrce přes proměnnou a dynamické jméno je jen odvozený (`parent-creator`); scénu s 1 570 uzly vidí snapshot jen v tom, co bylo načtené v okamžiku snímku (sektory kolem auta).

## Stav (2026-10-24, větev `agent/claude/knowledge-skills` na `main` 29d3a20)

- **Skilly z otestované workflow** (36 neprázdných relací 2026-10-15 … 10-24): `rg-orchestrator`, `rg-worker`,
  `rg-unity-notebook`, `rg-knowledge`; `kn5-map-port` přepsán na ADR-028. Registr a pravidla pro nový skill:
  `wiki/entity/agent-skills.md`.
- **Dovednostní karty:** `rag-query.js` vypíše pod hlavičkou max. 2 skilly se zdůvodněním (chunk skillu ve výsledcích,
  wiki stránka ve výsledcích cituje skill v `sources`, ≥ 2 společné termy s popisem). Žádný router. Index se
  přestaví i po změně skillu.
- **Graf:** wiki 10 stránek / 37 vazeb (bylo 6); nové `orchestration-workflow`, `unity-notebook-runs`,
  `agent-harness-traps`, `agent-skills` — skilly jsou uzly přes `sources`.
- **Evaluace:** 24 případů (bylo 14), 23 PASS + 1 známá mezera (`data.acd`), exit 0.
- Index: 3 579 chunků / 751 zdrojů, C# 1 809 metod.
- **Mezera #6 (heredoc) uzavřena příčinou:** selhává délka příkazu (> ~7,5k znaků), ne uvozovky → řádek v
  `AGENTS.md` + `wiki/concept/agent-harness-traps.md`. Mezera #7 (karta v AGENTS.md) hotová od 2026-10-19.

## Stav (2026-10-19, větev `agent/claude/second-brain` na `main` 0ec19e9) — historie

- **Wiki založena** (byla prázdná, lint padal): `index.md` se sekcí *External tools*, `log.md`, 5 stránek
  (rozhodnutí o oddělení od Zonia, RAG, Obsidian konvence, katalog úkonů what_if, katalog externích Zonio nástrojů).
  `lint-wiki.js`: 5 stránek, 10 vazeb, 0 fatal. Kontrola zrcadla skillů `.agents/skills` (Zonio konvence pro Codex) běží jen když existují obě složky.
- **Živý stav ticketů:** index nese jeden chunk na ticket z `task_pool.json` **main checkoutu** (virtuální cesta
  `main:.planning/what-if/task_pool.json`); worktree kopie poolu se neindexuje nikdy (ani přes citaci ve wiki).
  Dřív worktree index z 2026-10-11 hlásil RG-077/078 jako `todo`.
- **Auto-rebuild:** `rag-query.js` index před dotazem přestaví (~4 s), když se změnil HEAD, hash main poolu, wiki, nebo je starší než 24 h; `--no-rebuild` vypíná.
- **Stop-slova dotazu** (CS/EN funkční slova): „proč je data.acd zakázané" už nevyhrávají krátké wiki stránky na `proc` + `je`.
- **Evaluace:** baseline 10/14 (exit 1) → 13/14 + 1 známá mezera (ADR-023 jako obří řádek tabulky v `decisions.md`), exit 0.
- Index: 2 821 chunků / 591 zdrojů, C# graf 189 souborů / 1 545 metod / 15 947 hran.
- Neběželo: `check.ps1` a Unity (souběžně běžely testy uživatele); změny jsou jen v `tools/knowledge/` a `wiki/`.

## Stav (2026-10-11, commity 3c1fd56 + 1f8b257) — historie

Hotovo a ověřeno ručně: index (1 605 chunků / 356 zdrojů), dotazy CS i EN, C# graf (90 souborů, 779 metod),
export relací (22 Claude + 2 Codex). Orchestrátor při integraci (2026-10-11): `check.ps1` exit 0, hygiene 101/101, index/dotaz/C# graf exit 0;
`lint-wiki.js` zatím končí výjimkou, protože `wiki/index.md` neexistuje (wiki je rozpracovaná). Testy těchto nástrojů nejsou.
`export-sessions.js` za běhu volá exportér z `zonio_app` (jen čtení) — jediná běhová vazba na Zonio repo.

**Upřesněný cíl (uživatel 2026-10-11):** ne další skilly a kontext, ale zavřít mezery ve workflow
tam, kde je agenti opakovaně platí — nástroj „na stole" (jako mutanty v Zoniu), který agent použije
bez čtení manuálu. Skill jen tam, kde postup nejde zabalit do nástroje.

### Mezery nalezené v transcriptech (22 relací + review), seřazené podle ceny

| # | opakovaná ztráta | důkaz | návrh (nástroj, ne text) |
| --- | --- | --- | --- |
| 1 | Unity `*Builder.Setup` přegeneruje materiály/scény a smaže `benchmark-playground.glb.meta`; revert `git checkout` zamítá auto-mode klasifikátor, jednou se smazání commitnulo | reviews RG-033 #7, RG-036 #6, RG-041 #2; 6+ relací | opravit příčinu v builderech (nepsat do trackovaných assetů) + hygiene check „smazaný trackovaný .meta" |
| 2 | Orchestrátor při každém review píše nový `*-orchestrator-independent-check.py` (přepočet P95 z raw CSV, hashe, vlastněné cesty) | logs RG-011/023/030/040 | jeden znovupoužitelný `tools/review/` (recompute S1, owned-paths, diff vs brief) |
| 3 | S1 běh ztratí exclusive fullscreen / jede 1080p a přesto `valid=true` | review RG-036 #1, RG-041 (3 neplatné pokusy), RG-030 | validity brána v S1 runneru: abort/INVALID při změně režimu, rozlišení, CPU pozadí > 5 % |
| 4 | Sdílený notebook: uživatel ručně přeposílá „teď neBuilduj, běží S1" mezi okny | relace RG-025/032/033/036 | zámek měření: série vytvoří lock, build skripty ho respektují a hlásí |
| 5 | `pool.py` z worktree místo main; `review` před koncem fází; worker zapomněl `review`; prázdný výstup `claim` | reviews RG-011 #1, RG-030 #1, RG-033 | `pool.py`: odmítnout worktree kopii, po přechodu vypsat stav |
| 6 | Bash heredoc s uvozovkami nic nezapíše (≥ 8×, i 2026-10-11 při portu) | `agent_pool/agents/claude/memory/MEMORY.md` | jeden řádek v AGENTS.md nebo hook; ne skill |
| 7 | Start každé relace = čtení 8 povinných dokumentů | AGENTS.md | `rag-query.js` / `code-graph-cs.js` jako první krok (karta v AGENTS.md), ne další dokumenty |

**#1 hotovo (2026-10-11, commit 1f8b257):** `BuilderAssets.cs` (materiál: URP validace + kopie do existujícího assetu; scéna: náhrada jen při
rozdílu po maskování lokálních fileID), `URP_Low` se znovu nevytváří, `tools/game-unity/sync-models.ps1` z každého `unity-batch.ps1`, lint
`Test-MetaDeletions`. Důkaz: Game/Camera/Kn5/Benchmark Setup v čistém worktree → `git status` beze změn assetů (logy `artifacts/rg030/logs/sb-*.log`);
jediná změna `Playground.unity` = chybějící pole `UseHudTap` z RG-033 (zastaralá scéna, commitnout). Player `Build` neběžel.

**#2 hotovo (2026-10-11, commit 1f8b257):** `tools/review/recompute-s1.py` + `branch-scope.py` (viz `tools/review/README.md`), ověřeno na RG-030, RG-036 a RG-041.

**#3 hotovo (2026-10-11, commit 1f8b257, bez Unity běhu):** `summarize.py --resolution/--fullscreen` → `valid=false` při jiném rozlišení nebo ztrátě
exkluzivního fullscreenu (run.json na konci běhu); `run-benchmark.ps1 -BackgroundCheck` nad 5 % player nespustí (exit 3, důvod v `command.txt`,
výjimka `-AcceptBackground`), `run-series.ps1` pak sérii ukončí. Ověřeno offline: RG-036 warm 1 → INVALID (dřív valid=true). Mid-run abort v playeru
neřešen (vyžaduje C# + rebuild); `recompute-s1.py` kontroluje režim z run.json. Zjištěno: RG-030 cold měl CPU pozadí 10,03 % (přijato).

**#4 hotovo (2026-10-11, commit 1f8b257):** `tools/game-unity/measure-lock.ps1` — zámek v git common dir (`D:/workspace/what_if/.git/whatif-measure.lock`,
sdílený všemi worktree). Drží ho `run-series.ps1`/`run-benchmark.ps1` (game) a `run-series.ps1`/`run-load-series.ps1` (kn5); odmítají `env.ps1`
(game/camera/kn5: sync, unity-batch, testy, buildy) a `scripts/build.ps1`/`test.ps1`. Potomci série projdou (`NR_MEASURE_LOCK_PID`),
zamčení po zabitém procesu se smaže (pid + start procesu). Ověřeno: build/sync odmítnuty s hláškou, potomek povolen, `exit` ve try uvolní, kill → úklid.
`tools/benchmark-unity` (RG-011, needitovat) zámek nedrží.

Další krok: #5–#7 (pool.py, heredoc, karta v AGENTS.md) — mimo tento běh. Wiki zatím jen tam, kde fakt nejde dát do nástroje
(pasti KN5, rozsah determinismu). Unity ověření #1 po merge hotovo (orchestrátor 2026-10-11, df86eef): Game/Bench/Camera/Kn5 build → `git status` čistý, regrese pass (`agent_pool/agents/claude/logs/second-brain-orchestrator-integration.txt`).
