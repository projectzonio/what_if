---
type: entity
sources:
  - src/WhatIf.Planet.Runtime/PlanetStreamer.cs
  - src/WhatIf.Planet.Runtime/WantedSets.cs
  - src/WhatIf.Planet.Runtime/SectorHash.cs
  - src/WhatIf.Planet.Runtime.Experiment/Program.cs
  - src/WhatIf.Planet.Runtime.Experiment/Scenarios.cs
  - tests/WhatIf.Planet.Runtime.Tests/Program.cs
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-185.md
  - .planning/what-if/docs/what-if-planet-runtime.md
  - .planning/what-if/docs/what-if-planet-runtime.csv
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-172.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-173.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-175.md
  - src/WhatIf.Planet.WorldGen/SectorAdapter.cs
  - src/WhatIf.Planet.Runtime.Experiment/AnchorRun.cs
  - tools/whatif-runtime/run-anchor-runtime.ps1
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-184.md
updated: 2026-11-16
last_verified: 2026-11-16
status: provisional
aliases:
  - streaming runtime planety
  - PlanetStreamer
  - prefetch regionů
  - vlákna na pozadí
  - region cache
  - díra jen když je sektor hotový
---
# What If — streamovací runtime planety (RG-172)

> **RG-185 (2026-11-16): region-first cesta je smazaná.** `RegionWorld`, `RegionContent`, region konstruktor `PlanetStreamer`, `RegionProfile.cs`, `run-runtime.ps1` a `run-profile.ps1` neexistují; jediný konstruktor bere `ISectorContent` (obsah z kotev, [[what-if-planet-anchors]]). Odstavce níže o regionových světech jsou historie (git). Měření jízdy dnes: `tools/whatif-runtime/run-anchor-runtime.ps1` (oddíl RG-184 B5). Absolutní základna B: s4 8,3 / 9,3 MiB, k4 P95 3,18-3,35 ms.

Unity-free knihovna `src/WhatIf.Planet.Runtime`, která drží dlaždice před jedoucí kamerou: spojuje region světy ([[what-if-planet-engine]] RG-167), stavbu sektorů (RG-170) a vzdálenou vrstvu ([[what-if-planet-far-layer]]). Povrch ([[what-if-planet-surface]]) se zapojí přes továrnu regionů `Func<RegionId, RegionWorld>` bez změny kódu. Vzor odpovídá NightRacer `streaming-sectors` (race_game) (`LoadRadius` 600 / `UnloadRadius` 760 m), jen bez Unity.

## Návrh
- `PlanetStreamer.Update(cameraDir, altitudeM, dt)` na hlavním vlákně dělá jen účetnictví: chtěné sektory level 15 do 600 m (hystereze 760 m, nic nad 1500 m nad zemí), plán regionů (kamera + vše do `prefetchM`, každé 4 snímky), joby, max 4 integrace na snímek, `NearSet` jen z **hotových** sektorů s postaveným uzlem level 13, výběr far vrstvy + stavba uzlů s rozpočtem 2 ms.
- Vlákna: 2x `planet-regions` (BelowNormal, `RegionWorld.Build`, nejbližší/vpřed první) + 1x `planet-sectors` (Normal; BelowNormal ho vyhladovělo na 2,4 s). `StreamerParams.RegionWorkers = 1` = rozložení z briefu.
- Prefetch = rychlost x max(60 s, 1,5 x nejdelší naměřená stavba regionu), spodní mez 2 km všemi směry, dál jen kužel +-12° kolem směru jízdy.
- Sektor bez hotového regionu nečeká na fallback — kryje ho far vrstva. LRU: 96 sektorů (~33 MiB), 6 regionů (odhad 3 MiB), strop 140 MiB; recyklace `AdaptedSector`.

## Výsledek (i3-1215U, review `agent_pool/shared/reviews/RG-172.md`)
- Jízda 4x (120 m/s, 300 s, 3 hranice regionů + hrana krychle): hlavní vlákno P95 0,52-0,59 ms, P99.9 1,1-1,3 ms; 0 děr do 300 m, 0 porušení pokrytí; náskok regionu min 19-33 s; paměť 60-72 MiB bez růstu; studený spawn 8,1-8,5 s; let 250 m/s v 5 km bez blízkých sektorů; 200/200 sektorů bitově shodných.
- **t1 max** jednou 4,86 ms (1 z 18 000 snímků, bez stavby) — přijato, řádek přeformulován na P95 <= 1,0 / P99.9 <= 2,0 ms + report snímků > 4 ms. Pravděpodobně GC: ~12 gen0 kolekcí/s ze stavby regionů.
- **Úzké hrdlo celé planety:** `RegionWorld.Build` (`Generator.Run`, jedno vlákno) 5-70 s na region 10 km; region workery vytíží ~65-72 % jádra, sektory < 1 %.

## RG-173: povrchová báze + cena stavby regionu (review `agent_pool/shared/reviews/RG-173.md`)
- Továrna `RegionWorld.BuildSurface` s `PlanetSurface` na vlákno (`[ThreadStatic]`, `PlanetSurface` má cache, není thread-safe); výška vzdálené vrstvy = `PlanetSurface.Height`.
- **Trasa RG-172 je na povrchové bázi otevřený oceán** (žádný svět) -> b1/b3 jedou `DriveLand` (stejný tvar: 3 hranice regionů + hrana krychle, bloky B RG-169, 100 % pevnina); `run-runtime.ps1` ji má jako výchozí, oceánská zůstává `-Modes drive`.
- **Stavba regionu = `VerticalCurves`** (`Generator.Run` -> `Assemble` -> `ProfileRoad`): ~99 % času i alokace. Páka: `SolveProfile` se scratch poli volajícího -> alokace 1499 -> 37,9 MB na build (-97,5 %), gen0 v jízdě 12,9 -> 0,28/s, čas 8,7 -> 7,9-8,1 s medián; výstup bitově shodný (golden hash, 9/9 GenResult, 200/200 sektorů).
- Zbývající čas (8-28 s, v jízdě P95 ~30 s, max ~49 s) je aritmetika vertikálních oblouků; zkrácení mění výstup -> následný ticket s nastavením jen pro planetu (golden hash NightRacer beze změny).
- Profil: `tools/whatif-runtime/run-profile.ps1` (`profile` režim, tabulka po stupních, hashe před/po), hook `WhatIf.WorldGen.Prof` ([[worldgen]]).

## RG-175: stall exit vertikálních oblouků (review `agent_pool/shared/reviews/RG-175.md`)
- **Diagnóza:** čas nežraly silnice na `VcWorkBudget` ani pomalá konvergence, ale volání, která **nikdy nekonvergují** (meze a limit oblouku si odporují): 2769 z 11 760 volání = 124,6 z 129,2 s (96 %), běží do `VcOuter 30000`; jen 15 volání narazí na rozpočet. Raw řádky `agent_pool/agents/claude/logs/RG-175-vcdiag.txt`.
- **Páka:** `WorldSpec.VcStallOuter` (výchozí 0 = vypnuto = NightRacer bitově stejný; test s hashem `9857d480...`), planeta `SectorAdapter.VcStallOuter = 300`: konec, když se počet stanic za `VcCountTol` 300 vnějších kol nezlepší. Ladění: N=30 +66 % porušení (FAIL), N=100 +7,5 %, **N=300 -0,3 %**, N=1000 -0,7 % ale 2x čas.
- **Čísla (orchestrátor, stejná relace):** medián stavby regionu 7918 -> 300 ms (-96 %), max 26,5 -> 1,6 s; 37,9 MB na build beze změny; stanice za limitem 1533 -> 1528 (127 747), nejmenší poloměr 57 m obojí; v jízdě (land) P95 3,0 s / max 3,1 s (dřív ~30 / ~49 s); t3 min lead 71,5 s (dřív 24,8 s). Planetové výstupy se mění záměrně (b4 0/9 GenResult, 194/200 sektorů).

## RG-178: obsah sektorů přes `ISectorContent` (review `agent_pool/shared/reviews/RG-178.md`)
- `PlanetStreamer` má druhý konstruktor s `ISectorContent`; region path je obalený `RegionContent` (bitově beze změny, runtime testy + t8 prošly), kotvy dodávají `AnchorSector` ([[what-if-planet-anchors]]). WI-ADR-002: obsah je od teď z kotev, region = klíč cache.
- Jízda s6 (B): 42,7 km přes 7 starých hranic, t1 P95 0,628 ms, 0 děr, min lead 10,3 s; jednotky `Prepare` max 6,3 s, request-to-ready max 5,0 s (A 1,4 s) — studený dotek dálničního segmentu, B2.

## RG-184 B5: jízda město -> město na obsahu z kotev (review `agent_pool/shared/reviews/RG-184.md`)
- `CorridorPath.CityToCity`: nejkratší cesta ulicemi centrum -> vjezd, stanice koridoru, vjezd -> centrum; dvojice měst Dijkstrou přes routované koridory (≥ 3 staré hranice regionů, hrana krychle preferovaná). Režimy `AnchorRun.cs` cpath / cdrive / cverify / ccold / cflight / creport, runner `tools/whatif-runtime/run-anchor-runtime.ps1`.
- Oblast B, 26,7 km (ulice 1,6 km > dálnice 23,2 km > ulice 1,8 km), 4 staré hranice, 1 hrana krychle. t1 120 m/s P95 0,656 / P99,9 1,561 ms; t2 0 děr; t3 min lead 41,3 s; t5 studený teleport 0,9 s; t6 48 MiB; t8 200/200; t9 0 mezer na hranicích. Vše PASS (orchestrátor přepočítal t1/t2 ze vzorku snímků).

## Pro navazující práci
- NoSite regiony: hotovo v RG-176 (vesnický svět, blok A 2 -> 0 regionů bez světa; runtime t1-t8 PASS, t3 min lead 71,6 s).
- Silniční portály přes hranice regionů (`Portal` RG-162/163) do `RegionWorld` — potřeba před jízdou mezi regiony.
- Viewer: upload meshe mimo t1; teleport (Update max 18 ms) za načítací obrazovku; zrušený `RegionEntry` u čekajícího jobu znovu vyhledat (při menší spodní mezi prefetch by sektor visel).

Souvisí: [[what-if-planet-engine]], [[what-if-planet-far-layer]], [[what-if-planet-surface]], `streaming-sectors` (race_game), `unity-notebook-runs` (race_game).
