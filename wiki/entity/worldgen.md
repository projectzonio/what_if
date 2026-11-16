---
type: entity
sources:
  - src/WhatIf.WorldGen/
  - tests/WhatIf.WorldGen.Tests/
  - tools/worldgen/
  - .planning/what-if/docs/worldgen-generator.md
  - .planning/what-if/docs/worldgen-rules.md
  - .planning/what-if/docs/worldgen-dataset.md
  - .planning/what-if/docs/world-generator-plan.md
  - src/WhatIf.WorldGen/Scatter.cs
updated: 2026-11-12
last_verified: 2026-11-04
status: implemented
aliases:
  - worldgen
  - world generator
  - generátor světa
  - generované silnice
  - terrain
  - terén
  - tunely
  - viadukty
  - tunnels viaducts
---

# WorldGen — generátor silniční sítě a terénu (headless)

`src/WhatIf.WorldGen` (netstandard2.1, bez referencí, bez Unity typů, metry v `double`, x východ, z sever, y nahoru)
převádí **spec + seed + `world-rules.json`** na silniční graf a exportuje dataset schema v1. Je to čistá knihovna;
do Unity ji vede `worldgen-unity-adapter` (race_game). Detailní popis, příkazy a naměřené hodnoty jsou v
`.planning/race-game/docs/worldgen-generator.md` — tady jen mapa a rozhodnutí, která se jinde snadno přehlédnou.

## Kde co je

| co | kde |
| --- | --- |
| fáze generování (osady → dálnice → ulice → křižovatka → primary/secondary → sestavení) | `src/WhatIf.WorldGen/Generator.cs` |
| uliční růst W3.1 (RG-116, nahradil čtvercovou mřížku) | `Generator.cs` (`BuildStreets`, `ProbeSegment`, `ApplySegment`) |
| osa silnice: přímka – klotoida – oblouk | `Alignment.cs` |
| graf: uzly (`end/joint/junction/interchange/portal`), hrany po ≈10 m, spany | `Graph.cs`, `Core.cs` |
| validátory (poloměr, sklon, křížení, mosty ≥ 60 m, tunely ≥ 150 m) | `Validators.cs`, testy `tests/WhatIf.WorldGen.Tests/` |
| terén W5a (RG-118): přirozená výška `N`, profil silnice, tunely/viadukty, vyhloubený terén `T` | `Terrain.cs`, `GeneratorTerrain.cs` |
| svislé oblouky W5c (RG-123): min. poloměr vrcholu/údolí po třídách, validátor `vcurve`, `vradius_*` v `profiles.json` | `GeneratorTerrain.cs`, `Validators.cs`, `Rules.cs`, `Export.cs` |
| export dataset v1 + hash | `Export.cs`, schéma `worldgen-dataset.md` |
| CLI (`--terrain-stats`, `--tile-bench`, `--terrain-dump`) | `tools/worldgen/cli/` |
| specy | `tools/worldgen/specs/default-10km.json` (plochý), `terrain-10km.json` (relief 60 m + hřeben 350 m) |
| vizualizace a statistiky pro agenty | `tools/worldgen/render_net.py`, `terrain_stats.py`, `street_stats.py`, `validate_dataset.py` |
| pravidla z OSM (ODbL) | `tools/worldgen/osm_extract.py`, `fit_rules.py` → git-ignorovaný `artifacts/worldgen/world-rules.json`, popis `worldgen-rules.md` |

## Rozhodnutí a pasti

- **Silnice je autorita, terén ji následuje** (RG-118). Profil silnice se počítá po třídách v pořadí dálnice, primary, link,
  secondary, local; link až po primary, jinak připnuté konce ramp zvednou páteř nad dálniční most.
- **Silnice opouštějící dálnici v uzlu sdílí její výšku, dokud se jejich plochy překrývají** (RG-128, `ProfileRoad`, `NearestOnMotor`). Rampa pod ~27° má 30–45 m uvnitř
  plochy dálnice; nezávislé profily tam dělaly až 0,98 m schod v pruhu, „retardér“ z jízd uživatele. Měří `tools/worldgen/roadfeel` (exit 1 při schodu ≥ 20 cm), sweep to nevidí.
  Seed 1: schod v mřížce rozdvojení 932 1,28 → 0,05 m, 935 1,06 → 0,12 m. Plochý hash seed 1 se tím nezměnil (`43551fac…`). Srovnání s mapami: `geometry-patterns` (race_game).
- **Vedlejší rameno křižovatky drží výšku uzlu do řezné vzdálenosti + 1 m** (RG-130, `PlateauLegs` v `GeneratorTerrain.cs`): vlastní stanice plató v R + 1 m (R = nejširší polovina šířky u uzlu + 2 m, jak řeže adaptér) a sklon 0 k ní. Hlavní pár = nejlepší třída, při shodě dvojice nejblíž 180°; uzly s dálničním ramenem vynechány (ty drží ramp pin RG-128). Samo plató lipy zhoršilo — druhou polovinu opravy dělá adaptér (`worldgen-unity-adapter` (race_game), `OnSlopedLeg`). Terénní hashe seed 1/2/3 po RG-130: `c03c3dc7…` / `6bacfacd…` / `13f254be…`; plochý `43551fac…` beze změny (rovinatá cesta nevolá `AssembleTerrain`).
- **Tunel** je tam, kde `d = y − N < −12 m`, **viadukt** tam, kde `d > +6 m` na ≥ 30 m; konce spanů jsou uzly `Portal`.
  Pro viadukty zatím neexistuje pravidlo pilířů ani světlé výšky (limit, ne chyba) — jde do Unity v RG-120.
- **Determinismus je brána:** jeden seed dvakrát = stejný `hash.txt`; plochý `default-10km.json` musí po změnách terénu dát
  stejný hash jako před terénními změnami: původně seed 1 `5224865529e7…`, od RG-128 `43551fac…` (aktuální, RG-130 ho nezměnil). `System.Random` se nepoužívá, jen SplitMix64 po fázích (`Rng.ForStage`).
- **API pro Unity:** `GenResult.Terrain` (`TerrainModel`): `Natural`, `Height` (vyhloubený), `Hole`, `SampleTile(x0,z0,sizeM,stepM)`;
  sousední dlaždice mají bit-identické okraje. `RoadGraph`/`GEdge` se kvůli tomu nezměnily.
- **Externí základní výška (RG-165, What If):** `new TerrainModel(spec, Func<double,double,double> baseHeight)` — provider nahradí nízkofrekvenční část `Natural` (oktávy hills pod `TerrainModel.DetailFirstOctave` = 2 a horský pás); oktávy >= 2 zůstávají jako detail, ořez silnic nad tím. `null` = beze změny (golden-hash test `D221EBA2351460E0` v `WhatIf.WorldGen.Tests`). Jediný uživatel: adaptér planety `src/WhatIf.Planet.WorldGen` ([[what-if-planet-engine]]). **RG-167:** `Generator.Run(spec, rules, baseHeight)` — celý generátor (silnice, profil, ořez) běží na terénu s tímto základem; `Run(spec, rules)` = overload s `null`, bitově beze změny (test hash datasetu + golden hash). Region planety level 10 = jeden svět 9,8-13,9 km, stavba 4-12 s na i3 (planetární reliéf zpomaluje routing 8-21x proti ploché základně).
- **Dávkový dotaz výšky (RG-170):** `TerrainModel.HeightBatch(xs, zs, count, outH, prune = true)` — bitově shodný s `Height` (test 24 000 bodů, golden hash beze změny): znovupoužití množiny kandidátů podle rozsahu buněk 64 m (`IndexCellM`) + přesné ořezání; při >= 400 segmentech u dávky (`GridMinSegments`) hledání po prstencích jemné mřížky 16 m (`FineCellM`, vítěz = min(vzdálenost, id)). Používá ho `SectorBuild` planety; `Generator` dál volá bodový `Height`. Sektor 65 x 65 na hustém sídle 2,5-2,9 ms (ořez ~1,7-2,2 ms), medián sweepu 1,13 ms ([[what-if-planet-engine]]).
- **Profilovací hook a alokace (RG-173):** `WhatIf.WorldGen.Prof.Hook` (`Action<string,double,long>`, null = beze změny výstupu) + `Prof.Scope` kolem stupňů `Generator.Execute` a `ProfileRoad`. `SolveProfile` má přetížení se scratch poli volajícího; `VerticalCurves` ho volá tisíckrát na silnici — dřív 2 klony na volání = 99 % alokace stavby (1,5 GB -> 38 MB na region planety). Golden hash beze změny. Čas generátoru na reliéfu planety je ~99 % `VerticalCurves` ([[what-if-planet-runtime]]).
- **Stall exit vertikálních oblouků (RG-175):** `WorldSpec.VcStallOuter` (výchozí 0 = vypnuto, NightRacer bitově stejný, test hashe `9857d480...`); > 0 ukončí smyčku `VerticalCurves`, když se počet stanic za `VcCountTol` N vnějších kol nezlepší (meze a oblouk si odporují). Planeta 300: stavba regionu 7,9 -> 0,30 s, porušení -0,3 %.
- **Svět bez dálnice (RG-176):** `WorldSpec.Motorway` (výchozí true = NightRacer, golden hashe beze změny); false = `BuildMotorway` a `BuildInterchange` se přeskočí, první sídlo je vesnice `village0` (`IsCity = false`) o poloměru `CityRadiusM` v `Spec.City`, vesnice bez vyloučení 1500 m od koridoru, `ConnectPrimary/Secondary` je propojí. Test `RG-176: Motorway = false ...` (0 hran motorway/link, >= 2 sídla, 1 komponenta). Používá ho jen planeta pro pobřeží bez místa pro město ([[what-if-planet-engine]]). Unity kopie `engine-unity/**` zatím nesynchronizovaná (výchozí cesta bitově stejná).
- **Neměnit `src/WhatIf.WorldGen` z Unity ticketu:** hash DLL je brána adaptéru (briefy RG-115, RG-119 ho mají jako read-only).
- `WhatIf.WorldGen.dll` kopírují `tools/game-unity/sync-sdk.ps1` i `tools/kn5-unity/sync-plugins.ps1` (od `2bf54b1`, 2026-11-02);
  chybí-li kopie, čerstvý worktree padá na CS0234. Nová assembly = zapsat do obou skriptů.
- `world-rules.json` je ODbL-odvozené a git-ignorované; bez něj CLI i hra odmítnou start. Licence Copernicus DEM zůstává otevřená.
- Znám hranici: osady stojí v plochých pánvích, takže místní ulice jsou skoro rovné (p50 0,1 %) a svět je plošší než Sierra.

- **Jedna šířka na řetězec uzlů stupně 2** (RG-142, `RoadGraph.MatchWidthsAtDegree2Nodes` v `Graph.cs`, volá `Generator.Run` před `TerrainModel.Bind`). `GEdge.WidthM` je konstanta hrany, změna třídy v uzlu stupně 2 byla schod šířky (sweep `ground_over_road`). Pravidlo: nejširší šířka (dálnice se nezužuje); nejužší, když konec řetězce leží na křižovatce, jejíž nejširší rameno má menší **třídní** šířku (plató křižovatky v `GeneratorTerrain.cs` se počítá z třídy, širší rameno by přečnívalo); řetězec s mostem/tunelem vždy nejširší. `Class` zůstává, ID uzlů a hran se nemění (seed 1: 11 hran, jen `width_m`). Kdo potřebuje šířku silnice, čte `WidthM`, ne `RoadClass.Width(Class)`. Hashe po RG-142: terén seed 1/2/3 `c318a97b…` / `40dd7f19…` / `65d90691…`, plochý seed 1 `54ad5048…` (výše uvedené `c03c3dc7…`, `43551fac…` platí před RG-142). Návazné: plató z `WidthM` by pravidlo zjednodušilo na „nejširší“.
- GEdge.Curv je analytická křivost **jen podél hrany**; zatáčka mezi dvěma hranami v uzlu v ní není. Kdo počítá rychlost z křivosti (RG-060 `racing` (race_game)), musí napojení hran přidat sám.

## Související

`worldgen-unity-adapter` (race_game) (Unity zdroj sektorů, minimapa, spawny), `streaming-sectors` (race_game) (kontrakt `ISectorSource`),
`features` (race_game) (co už jezdí), [[workflow-tools]] (příkazy). Stav ticketů W3–W5 (RG-110, 115–120) je v `task_pool.json`, ne tady.
