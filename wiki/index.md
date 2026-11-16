# What If wiki — index

Jeden řádek na stránku: `- [type] slug — popis`. Konvence: [[obsidian-wiki-conventions]].
Stav ticketů sem nepatří: `task_pool.json` (main checkout) / `rag-query.js --query "WI-0xx stav"`.

## Decision

- [decision] knowledge-layer-separate-from-zonio — race_game pool/plán kanonický, Zonio pool zastaralý; nástroje se nekopírují, nové v Pythonu, občasné Zonio nástroje na místě
- [decision] source-precedence — zdroje se čtou odzadu: final_for_orchestrator -> GPT-01..08 -> supplementy -> originály -> audit (PD-002)

## Entity

- [entity] agent-skills — registr skillů (orchestrátor, worker, Unity notebook, znalostní vrstva, KN5) a jak je `rag-query` nabízí jako karty
- [entity] rag-knowledge-tools — RAG/GraphRAG-lite `tools/knowledge`: dotaz, C# graf, živý stav ticketů z main, auto-rebuild, evaluace, dovednostní karty
- [entity] review-tools — `tools/review` (S1, manual-test, branch-scope): co kontrolují a pasti
- [entity] what-if-planet-anchors — kotvy planety What If (RG-177 spike 1): sídla jako čistá funkce buňky (level 9/10/11), RNG síť po vrstvách, koridory A* 200 m na povrchu bez vody, segmenty <= 5 km s připnutými spoji; proti region světům n1 0 vs 27-193, komponenta 98-99 % vs 6-7 %
- [entity] what-if-planet-far-layer — vzdálená vrstva planety What If (RG-171): CDLOD quadtree na stěnu krychle level 0..13 nad povrchem, díra po sektorech level 15, sukně z naměřené mezery, rozpočet uzlů/trojúhelníků/paměti pro UHD
- [entity] what-if-planet-runtime — streamovací runtime planety What If (RG-172): PlanetStreamer na hlavním vlákně, region světy a sektory na vláknech na pozadí, prefetch podle rychlosti, díra jen pro hotové sektory, LRU cache; jízda 4x bez děr, úzké hrdlo stavba regionu
- [entity] what-if-planet-surface — povrch planety What If (RG-168): čistá funkce (seed, směr) — kontinenty s hladinou moře z kvantilu, oceán/šelf/pláž, jezera v buňkách 8 km bez úniku, les z teploty a vlhkosti; API `PlanetSurface.Height/Sample`, `SurfaceSector`, rozpočet sektoru
- [entity] what-if-planet-viewer — prohlížeč planety What If v0 (RG-179): `--stream-map planet` v KN5 playeru, kotvové sektory + far vrstva + floating origin, run-viewer.ps1, řádky v1-v6 na UHD, jízda OK (placeholder vzhled, chybí silnice a orbit)
- [entity] what-if-project — přehled projektu: kde je výzkum, pool, planetární engine, generátor, wiki a skilly
- [entity] worldgen-sweep — test deštěm a záplavou `--stream-run sweep` (RG-122): blokový fyzikální sweep, typy vad, injecty, pravidlo před jízdou
- [entity] worldgen — generátor silniční sítě a terénu (headless): fáze, uliční růst W3.1, terén/tunely/viadukty W5a, API dlaždic, determinismus jako brána

## Concept

- [concept] agent-harness-traps — spočítané pasti harnessu: heredoc nad ~7,5k znaků, Read před Write, sleep, Monitor, PIL s -I, robocopy
- [concept] external-tools-zonio — katalog externích Zonio nástrojů: úkon → příkaz → co odmítá, ověřeno nad race_game
- [concept] obsidian-wiki-conventions — typy stránek, frontmatter, wikilinky, index/log; Obsidian jako vault, ne zdroj pravdy
- [concept] orchestration-workflow — otestovaná pipeline orchestrátor → uživatel → worker (ADR-028), co se neosvědčilo, co zůstává uživateli
- [concept] planetary-engine-research — stav architektonického výzkumu: osm GPT návrhů, čtyři blokátory baseline, PE/INT/C rejstříky, OQ/USER otázky
- [concept] research-evidence — pravidla research ticketů proti AI slopu a AI pastem (třídy V/M/E/O), kapacita slotů ADR-035 (1 slot = 1 implementační / 3 research), externí workery ADR-036 (ChatGPT, intake do .planning/external), první dávka planet research
- [concept] what-if-planet-engine — vstup pro samostatný projekt What If (RG-153): připnuté SHA Exolune/SFSIM, rámce u pólů (paralelní přenos + cube-sphere id), tečný rámec na sektor 256 m, šum bez GPU FP64, rozpočet UHD a IO na km/h; RG-155 návrh WI-ADR-001 (equal-angle cube √2), SFSIM bez hystereze LOD, protokol CPU experimentu; RG-160 obsah planety (procedurální báze vs Copernicus DEM), kontrakt SectorPlanetInput, šev CDLOD + sukně, silnice přes hrany kostky (portály s, angle)
- [concept] workflow-tools — katalog úkon → race_game nástroj (pool, worktree, check, zámek měření, S1, KN5, review)
- [concept] worldgen-materials — materiály WorldGenu z jednobarevných na texturovanou testovací dlaždici: Kn5Multilayer je už 4vrstvý splat, UV silnic po délce, nulové UV křižovatek, limity URP 6000.3 (mip streaming polí, decaly vs SRP Batcher), pořadí Low/Medium/High a GPU měření T1..T8 (RG-145)
- [concept] worldgen-vegetation-buildings — vegetace a budovy pro WorldGen (RG-149): Unity-free fáze Scatter po sektorech, procedurální low-poly stromy/domy, RenderMeshInstanced bez GameObjectů, Low +1,0 ms (odhad), tickety V1..V4 navržené
