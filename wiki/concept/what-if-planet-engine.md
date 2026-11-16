---
type: concept
sources:
  - .planning/what-if/docs/what-if-planet-engine.md
  - .planning/what-if/docs/race_game-lane-history/external/INTAKE-2026-11-06-chatgpt-planet-research.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-153.md
  - .planning/what-if/docs/what-if-planet-adr-draft.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-155.md
  - .planning/what-if/docs/what-if-planet-content-roads.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-160.md
  - src/WhatIf.Planet/QuadSector.cs
  - src/WhatIf.Planet/Roads.cs
  - src/WhatIf.Planet.Experiment/RoadChecks.cs
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-162.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-163.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-164.md
  - src/WhatIf.Planet/CubeSphere.cs
  - src/WhatIf.Planet/Frame.cs
  - src/WhatIf.Planet/PlanetNoise.cs
  - src/WhatIf.Planet.Experiment/Checks.cs
  - tools/whatif-planet/run-experiment.ps1
  - .planning/what-if/docs/what-if-planet-experiment.md
  - .planning/what-if/docs/what-if-planet-experiment.csv
  - tests/WhatIf.Planet.Tests/Program.cs
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-161.md
  - src/WhatIf.Planet.WorldGen/SectorAdapter.cs
  - src/WhatIf.Planet.WorldGen/SectorPlanetInput.cs
  - src/WhatIf.Planet.Experiment/WorldGenChecks.cs
  - .planning/what-if/docs/what-if-planet-worldgen-adapter.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-165.md
  - .planning/what-if/docs/what-if-planet-tile-curvature.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-166.md
  - src/WhatIf.Planet.Experiment/RegionChart.cs
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-185.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-167.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-170.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-169.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-176.md
  - src/WhatIf.Planet.Experiment/BlockChecks.cs
  - .planning/what-if/docs/what-if-planet-anchor-direction.md
updated: 2026-11-16
last_verified: 2026-11-16
status: provisional
aliases:
  - planetary engine
  - planetární engine
  - What If planeta
  - exolune
  - sfsim
  - cube-sphere
---

# What If: planetární engine (vstup pro samostatný projekt)

> **RG-185 (2026-11-16):** region-first obsah (`RegionWorld`, `RegionChecks.cs`, varianta A) je smazaný; obsah planety je jen z kotev (WI-ADR-002, [[what-if-planet-anchors]]). Zbyla geometrie mapy regionů `src/WhatIf.Planet.Experiment/RegionChart.cs` a `SectorPlanetInput` / `SectorAdapter` (používá je kotvová cesta). Oddíly o `RegionWorld` níže jsou historie. Starý runner `tools/whatif-planet/run-experiment.ps1` volá smazané režimy (follow-up).

Vstup pro rozhodnutí projektu **What If** (samostatný projekt, NightRacer/WorldGen je jeho základ); **není zdrojem NightRacer ticketů**. RG-153 (2026-11-11) převedl dávku ChatGPT (třída E, [[research-evidence]]) na doklady V u připnutých SHA a odpověděl na čtyři otázky ADR. Plné tabulky, citace a vzorce: `.planning/race-game/docs/what-if-planet-engine.md`.

## Připnuté snímky (V, 2026-11-11)
- exolune `main` `c71dacf8…` (PolyForm Shield 1.0.0 = jen clean-room, žádný kód ani parafráze).
- sfsim `main` `caff64fc…`, `windows` `398a4787…` (EPL-1.0). `main` se hýbe, platí jen SHA.
- Opravený lokátor: brána FP64 v Exolune je `D3DApp.cpp:570-571` (intake 567, dávka 520-524).
- Exolune nevykresluje šestistěnnou kostku: kreslí čtverec vystředěný pod kamerou (nadir) + až 2048 prstenců; kostka existuje jen jako data.

## Doporučení ke čtyřem otázkám
| otázka | doporučení | klíčové číslo (O) |
|---|---|---|
| Q1 rámce u pólů | pozice = double 3D vektor, rámec vozidla/kamery paralelním přenosem; adresace dlaždic cube-sphere (face, level, row, col); lat/long jen pro UI/import | lat/long buňka 256 m má na 89,9° šířku 0,45 m |
| Q2 předání patch → region → sektor | globální planetární vrstva = render/cache; sektory [[worldgen]] 256 m zůstávají autoritativní; **každý sektor má vlastní tečný rámec** | pokles roviny 1,3 mm na hraně sektoru vs 1,96 m pro jednu rovinu na 10 km region |
| Q3 šum bez GPU FP64 | CPU double určuje celočíselnou buňku mřížky, zbytek se do floatu převádí až relativně k dlaždici; GPU dvojice floatů jen jako volitelná úroveň | float32 na poloměru Země: rozestup 0,5 m |
| Q4 rozpočet na UHD | planetární vrstva ≤ 256 MiB sdílené, ≤ 150 MiB RAM, ≤ 6 ms P95; `LoadRadius` jako funkce rychlosti/výšky | jemná úroveň 0,76 kB/s na km/h; letadlo 900 km/h při 5 km = 38 sektorů/s (upload neměřen) |

- 256 m sektor nikdy není dlaždice quadtree kostky (úroveň 15 = 305,7 m); 256 m existuje jen v rovinné síti regionu.
- Kotvy M: `perf-matrix` (race_game) (worldgen Low 2,56 ms / 499 MiB / 63 MiB sdílené), rozpočty pumpy `streaming-sectors` (race_game).

## Otevřené (C = vyžaduje klon/build/běh, mimo NightRacer)
- Podpora FP64 shaderů na Intel UHD i3-1215U (rozhoduje, zda Exolune vůbec projde bránou).
- Chování LOD v 8 rozích kostky, cena nahrání sektoru při rychlosti letadla.
- ~~Horizon culling SFSIM~~ a hystereze LOD: uzavřeno v RG-155 (níže).

## RG-155 (2026-11-12): návrh ADR + protokol experimentu
Plný text: `.planning/race-game/docs/what-if-planet-adr-draft.md` (WI-ADR-001, status Proposed, projekt What If, ne `decisions.md`).
| projekce kostky | rozptyl plochy buňky max/min (O) | max protažení | poznámka |
|---|---|---|---|
| lineární (gnómonická) | 5,196 (3^1,5) | 1,73 | SFSIM ji používá (`cubemap.clj:144-147`, V) |
| **equal-angle (tan) warp — volba ADR** | **1,414 (√2)** | 1,73 | 2× tan/atan; tabulka sousedů ve stylu SFSIM platí beze změny |
| QSC (COBE, PROJ `qsc.cpp`) | 1,001 (equal-area) | 1,55 | po částech (4 oblasti na stěnu), bez jednoduché tabulky hran; jen záložní kandidát |
| lat/long | 573 : 1 na 89,9°, 5730 : 1 na 89,99° | stejné | jen pro kontrast |
- **Oprava RG-153:** equal-angle je √2 = 1,41, ne „asi 1,3“; číslo 1,3 není ze SFSIM (ta má lineární 5,2 : 1). `what-if-planet-engine.md` §5/§8 zatím nese starou hodnotu.
- SFSIM **nemá hysterezi LOD** (`quadtree.clj:134`: pokles používá stejný predikát jako zvýšení) a v přečtených modulech **nemá horizon culling** (GLSL nečteno = UNKNOWN). ADR proto chce hysterezi (pokles při 0,8 × prahu).
- Experiment (CPU, bez Unity): knihovna `WhatIf.Planet` (netstandard2.1, Unity-free) + testy + konzole; 4 kontroly (šev < 1 mm, šum bitově shodný, rámec < 1e-9 rad přes pól a roh, ≤ 0,5 ms/sektor medián), odhad 7-9 souborů, 700-1000 řádků. **Pozor:** „0,3-0,4 ms WorldGen dnes“ v návrhu je medián *umístění vegetace* na sektor, ne stavba celého sektoru 65 × 65 — cena sektoru zůstává neměřená, práh 0,5 ms je cíl O.
- Rozhodnutí uživatele před startem What If (§5 dokumentu): repozitář vs `src/WhatIf.*`, poloměr planety, equal-angle warp, běh experimentu na CPU notebooku, licenční postoj (SFSIM/Exolune jen jako zdroj nápadů).

## RG-160 (2026-11-12): obsah planety, kontrakt sektoru, šev far-LOD, silnice přes hrany
Plný text: `.planning/race-game/docs/what-if-planet-content-roads.md` (review `agent_pool/shared/reviews/RG-160.md`).
| téma | doporučení (O, není-li uvedeno) | klíčový doklad |
|---|---|---|
| zdroj reliéfu | hybrid, začít procedurální bází za `IPlanetRelief.Height(dir)`; DEM později jako náhrada | GLO-30 celá planeta 1136 GB (pevnina 331 GB), GLO-90 126 GB, 1 km třída 0,3-1,0 GB; GLO-30 Public není globální a je to DSM vč. budov a stromů (V, AWS) |
| licence DEM | text přílohy Copernicus nepřečten = **unverified**; Natural Earth public domain (V); ETOPO/SRTM licence nepřečteny | údržba Copernicus DEM „until 2026“ (V, CDSE) |
| kontrakt planeta → sektor | planeta = čistá funkce světové pozice; záznam `SectorPlanetInput` (19 × 19 základních výšek po 16 m, vzdálenost od pobřeží, biom/klima, sídla, koridory, portály), ~1,8 kB na sektor | nahrazuje vstupy [[worldgen]] `Terrain.cs:83-89,178-185`, `Rules.cs:223-233`, `Generator.cs:269-270` (M) |
| šev far/near | vzdálená vrstva ve stylu CDLOD (quadtree na stěnu, morph), díra s granularitou sektoru + krátká sukně; chunked LOD a clipmapy zamítnuty | CDLOD MIT, „no need for stitching meshes“ (V); ceny na UHD UNKNOWN |
| silnice přes hrany | globální graf koridorů (oblouky hlavních kružnic) + lokální zjemnění; portál = `(s, angle)` kvantovaný na int64, bod = `normalize(n_road × n_border)` | hranice regionu je hlavní kružnice pro jakýkoli warp po osách (O); rohy: polygonová doména, portály ≥ 256 m od vrcholu |
- **Past:** mřížka šumu WorldGen `Terrain.cs:119` má periodu 256 buněk (opakování ~358 km při vlnové délce 1400 m) — není to planetární báze, jen lokální detail.
- Navazující experiment r1-r5 (CPU, rozšíření `WhatIf.Planet` z RG-161) a 6 rozhodnutí uživatele (§5 dokumentu) čekají na slovo uživatele.

## RG-161 (2026-11-12): CPU experiment WI-ADR-001 — (a) FAIL, (b)(c)(d) PASS
Knihovna `src/WhatIf.Planet` (netstandard2.1, bez referencí: `CubeSphere` + `RegionId` + tabulka sousedů, `Frame` paralelní přenos, `SectorFrame`/`SectorId`, `PlanetNoise` + `HeightSampler`), konzole `src/WhatIf.Planet.Experiment`, testy `tests/WhatIf.Planet.Tests` (v `scripts/test.ps1`), běh `tools/whatif-planet/run-experiment.ps1` (measure lock, 3 procesy pro (d), surová data git-ignored `artifacts/whatif-planet/<stamp>/`). Tabulka `.planning/race-game/docs/what-if-planet-experiment.{md,csv}`, výsledek v `what-if-planet-adr-draft.md` §6.
| kontrola | verdikt | čísla (M, i3-1215U) |
|---|---|---|
| a šev hran | **FAIL** | svisle ~2e-6 mm všude; vodorovně uvnitř regionu 0,263 mm; přes hranici regionu 56-300 m (mřížky sousedních regionů mají jinou fázi a natočení) |
| b šum ze dvou počátků | PASS | stejné double x: 0 rozdílných bitů z 3e6; origin + local v double ≤ 1 ulp |
| c přenos rámce | PASS | ≤ 2,3e-16 rad na krok, holonomie ~1e-14 rad (pól, roh kostky) |
| d cena | PASS | ~0,2 ms/sektor 65 × 65 (výška + rámec), ~0,8 % jádra při 900 km/h |
- **WI-ADR-001 zůstává Proposed.** Řádek 3 přerozhodnut 2026-11-12 (orchestrátor z mandátu uživatele: „S deformací dlaždic počítám…“; ADR draft §7): **sektor = list quadtree stěny kostky na levelu 15** (~306 m v osách; změřeno RG-162: 216-306 m, 288 m u vrcholů kostky; plocha 1,41 : 1, roh 120° u 8 vrcholů kostky), vrcholy z celočíselné mřížky stěny (2^21 na stranu) s kanonizací hran a rohů na nejnižší stěnu → šev nulový konstrukcí; stejný strom jako CDLOD vzdálená vrstva, hranice = hlavní kružnice pro portály RG-160. Ověřuje RG-162 ((a)-(d) znovu + silnice r1-r5); pak Accepted. Varianta A (planární sektory + šité pásy) zamítnuta. RG-160 D1-D6 přijata všechna (fiktivní planeta, jen koridory, CDLOD, sukně + mlha).
- **Past:** pravidlo §4 „rámec z kotvy regionu“ šev neopraví — osy z kotvy už experiment používá; čtvercové planární mřížky na sféru bez švu nepasují (geometrie, ne chyba párování).
- **Pravidlo:** lokální offsety zůstávají na CPU v double; zaokrouhlení local na float před složením x mění ~65 % výsledků až o 0,45 mm.
- Orchestrátor přepočítal: a/b/c bitově shodné (sha256 `abc.csv`), (d) +8 %, pořád PASS.

## RG-162 (2026-11-12): listy quadtree level 15 + silnice r1-r5 — vše PASS kromě r2
`src/WhatIf.Planet/QuadSector.cs`: `SectorId (face, row, col)` na levelu 15, region = předek level 10 (32 × 32 sektorů); `Lattice` = celočíselné `(face, I, J)`, I, J ∈ [0, 2^21], `a = I/2^20 - 1` přesně v double; vrchol s I nebo J na okraji stěny se kanonizuje na **nejnižší stěnu** (tan(π/4) ≠ 1 v double, test to hlídá); 65 + 65 tan na sektor (separabilní). `SectorFrame.cs` smazán. `src/WhatIf.Planet/Roads.cs`: `Border` (hranice regionu = hlavní kružnice, kanonická z nižšího `RegionId`), `Portal` (`s` int64 cm, heading, `Snap` ≥ 256 m od vrcholu), `RoadArc` (průchody hranicemi), `CorridorGraph` (2 sídla na buňku levelu 6 = 49 152, 3 nejbližší), `RegionPlane` (zjemnění v gnómonické rovině regionu, fillet). Experiment `RoadChecks.cs`, `Deform.cs`.
| kontrola | verdikt | čísla (M, i3-1215U) |
|---|---|---|
| a šev | PASS | double svět 0 mm (bitově) vč. 144 párů přes hrany a 24 párů v rozích kostky; z float lokálních dat max 0,020 mm |
| b, c, d | PASS | (d) 0,21-0,26 ms/sektor (worker / rerun orchestrátora) |
| r1 portály | PASS | 10 000 oblouků, 76 282 průchodů, všech 12 hran; `s` z obou regionů identické, poloha 2e-6 mm |
| r2 snap | **FAIL** | odstup od vrcholu 256,0000 m PASS; **změna délky max 0,5236 %** (< 0,5) u 1 z 3000 oblouků |
| r3, r4, r5 | PASS | heading 2e-13 rad; poloměry přesně na minimu třídy (344,83 / 121,31 m); celý graf 0,04 s, 1,87 MB |
- Deformace (ověřeno ručně): střed stěny 305,74 m, střed hrany kostky 216,19 m (= 305,74/√2), vrchol kostky 288,25 m (= 305,74·2√2/3) s rohem 120°. Předpověď ADR §7 „~256 m v rozích“ byla chybná, opraveno.
- **Past r2:** `Portal.Snap` posune portál nejvýš o 256 m, běh ale hlásí max posun 368,7 m → aspoň jeden portál u vrcholu regionu ležel před snapem mimo úsečku své hranice (s < 0 nebo > délka) — chyba přiřazení průchodu, ne příliš přísný práh. Oprava + společný snap průchodů u jednoho vrcholu: RG-163. WI-ADR-001 zůstává Proposed do jeho PASS.
- Orchestrátor přepočítal: a/b/c/r1-r4 bitově shodné, liší se jen čas r5.

## RG-163 (2026-11-12): r2 — přiřazení průchodů opraveno, délka dál FAIL
- Příčina potvrzena měřením: s kódem RG-162 ležel **205 z 12 976** portálů r2 před snapem mimo úsečku své hranice. `RoadArc.Crossings` teď vydá průchod jen na úsečce, kterou silnice opravdu protíná (tolerance 5 cm); jinak přes region sousedící s oběma. Nový řádek protokolu: **0 mimo úsečku** (PASS). r1/r3 beze změny PASS.
- `RoadArc.Route`: průchody do 256 m od jednoho vrcholu regionu = skupina; kandidát A = hlavní kružnice posunutá 256 m od vrcholu, B = každý portál po své hranici (`Portal.Snap`); vyhrává kratší polyline. **Past:** čistý A je škodlivý pro silnici skoro tečnou k hranici (portály sklouznou km po hranici, změna délky 25,1 %); i hybrid hlásí „max posun“ 7 100,6 m (A na mělkých hranicích) — řádek acceptance „posun ≤ 256 m“ tak doslova neplatí.
- **r2 délka 0,5236 % FAIL** (1 z 3000): 30 km testovací oblouk běží 37-96 m vedle sloupcové čáry, protne 4 řádkové hranice a končí jen 159 m za portálem, který musí uhnout 160 m do strany → dolní mez pro ten oblouk, ne chyba algoritmu. Metrika r2 = rozhodnutí uživatele. WI-ADR-001 zůstává Proposed. Orchestrátor přepočítal: a/b/c a r1-r4 bitově shodné.
- **Rozhodnutí uživatele 2026-11-12 ~16:20:** r2 délka jen na obloucích s oběma konci >= 1 km od nejbližšího posunutého portálu; řádek posunu = posun jednoho portálu `Portal.Snap` <= 256 m + 0 mimo úsečku. Opakování = RG-164. **Trvalý mandát (jen experimenty What If):** acceptance smí orchestrátor upravit sám, když změnu zdůvodní a nahlásí (review + handover); uživatel se ptá jen na směr a licence. ADR-028 pro tickety NightRacer beze změny.

## RG-164 (2026-11-12): r2 podle upřesněné metriky — WI-ADR-001 Accepted
- r2 délka jen na obloucích, jejichž oba konce leží >= 1 km (po skutečném oblouku) od nejbližšího posunutého portálu (posun > 1 cm): **0,4029 % PASS**, 2992 z 3000 způsobilých; vyřazeno 8 (0,27 %, max 0,5236 % = oblouk z RG-163). Posun jednoho portálu `Portal.Snap` max **255,99 m** PASS; kandidát A jen jako diag (boční odsazení 256,05 m, klouzání po hranici 7 100,6 m). Mimo úsečku 0, s mismatch 0.
- Celý běh ALL PASS (294 řádků); orchestrátor přepočítal: všechny řádky kromě časů d/r5 shodné, a/b/c + r1 bajtově shodné s RG-163. **WI-ADR-001 Accepted** (ADR draft §3).
- Past: poznámku „Metric change“ v `what-if-planet-experiment.md` dopsal worker ručně — běh skriptu bez `-NoDocs` ji smaže.

## RG-165 (2026-11-12): adaptér WorldGen pro deformované sektory level 15 — w1-w5 PASS
- Knihovna `src/WhatIf.Planet.WorldGen` (jediný most mezi `WhatIf.Planet` a `WhatIf.WorldGen`): `SectorPlanetInput.Build(id, worldSeed)` = rámec sektoru, mřížka základní výšky 16 m z `PlanetNoise` v globálních pozicích (gnomonická tečná rovina, velikost podle sektoru 23 x 23 / 31 x 31 místo 19 x 19 z RG-160, protože deformovaný quad přesahuje ±128 m), region seed = SplitMix64(level-10 region, world seed). `SectorAdapter.Model(input)` + `Build(input, model, out)` = 65 x 65 vrcholů: `h = planet base + smoothstep(dist/32 m) * (WorldGen Height - base)`, okrajové vrcholy přesně planet base -> šev bez tolerance.
- Hook ve WorldGen: `TerrainModel(spec, baseHeight)` ([[worldgen]]); `null` bit-identické.
- Výsledek (426 řádků ALL PASS; orchestrátor přepočítal, a/b/c + r1 bajtově shodné s main): w1 base na sdílené hraně 0 m, w2 šev 0 mm (float lokální síť 0,015 mm), w3 příspěvek na okraji 0 m (sklon v pásu <= 0,158), w5 stavba sektoru 0,8-1,0 ms (< 3 ms).
- Mimo v1: biom, pobřeží, sídla, koridory/portály, silnice přes hranici sektoru. Další krok = adaptér v2 (silnice v sektoru, taper vs výškový hint na portálech) po doporučení RG-166 (mapování dlaždice vůči planetě).

## RG-166 (2026-11-12): zakřivení deformovaných dlaždic vůči planetě — tečná rovina zůstává
- Doc `.planning/race-game/docs/what-if-planet-tile-curvature.md` (research, Fable; orchestrátor ověřil V1/V3 a nezávisle přepočítal strany, úhly, plochy a pokles).
- **Tečná rovina je *mapa* (parametrická doména), ne geometrie:** vrchol leží vždy přesně na `d * (R + h)` kanonické mřížky; WorldGen se jen ptá v gnomonickém bodě (x, z). Chyba metriky <= 1,5e-9 (sektor) / 1,6e-6 (region 10 km, 16 mm na 10 km). Odmítnutá varianta ADR „jedna tečná rovina na region: plovoucí 1,96-3,9 m“ se týká *geometrie*, ne mapy — nejde o rozpor.
- Varianta B (mřížka WorldGenu v parametrech (a, b)) láme každé pravidlo: měřítko 0,80-1,38, min. poloměr ×0,46-0,60, pravé úhly ±19,5/±30°. Odmítnuto spolu s bilineárním fitem, jiným warpem krychle, geodetickou mřížkou, konformní mapou.
- **Doporučení v2:** stejná mapa ukotvená ve středu regionu level 10 (`RegionPlane`), jeden `TerrainModel` na region -> obsah přes hranice sektorů v jedné soustavě, taper 32 m jen na hranicích regionů (1,3-1,6 % plochy místo 38-45 %); dělit lattice quad po kratší diagonále (vrchol krychle 4,50 vs 7,80 m); dávkové vzorkování; up vektor objektů = up sektoru, nikdy regionu.
- Zakřivení uvnitř `LoadRadius` 600 m: 28 mm / 0,05 px — obsah a fyzika smí brát sektor jako rovinný, vrcholy zůstávají přesné. Float švy: 61 µm při 600 m, 0,98 mm při 10 km s vlastními rámy sektorů; sdílené osy + mřížka 2^-10 m dávají bitovou shodu (CPU emulace; GPU = e9).
- Řádky e1-e10 pro adaptér v2 -> RG-167.

## RG-167 (2026-11-12): adaptér v2 — jeden WorldGen svět se silnicemi na region level 10
- `src/WhatIf.Planet.WorldGen/RegionWorld.cs`: `RegionWorld.Build(regionId, worldSeed)` = mapa `RegionPlane` (C, E, N středu regionu), polygon regionu (4 rohy mřížky, rovné strany), základ `B(x,z) = PlanetNoise.Height` přímo (bez mřížky 16 m), `SizeKm` = obalový čtverec mapy zaokrouhlený na 100 m (9,8 / 9,8 / 13,9 / 10,6 km), dálnice škálovaná `Half`, `Generator.Run(spec, WorldRules.Synthetic(), B)` (nový overload, [[worldgen]]). `SectorBuild.Build(world, sector, out)` = 65 x 65 vrcholů `h = pb + taper(dist k hraně regionu) * (T - B)`, taper 32 m jen na hranicích regionu, okrajové vrcholy přesně planet base; `SectorBuild.Triangles` dělí quad po kratší diagonále.
- Výsledek (575 řádků, orchestrátor přepočítal): e1 0 bitů na sdílených vrcholech uvnitř regionu i tam, kde silnice kříží hranici sektoru; e2 šev přes hranice regionu 0 mm (float 0,013-0,017 mm) i na hraně a vrcholu krychle, pás taperu 1,30-1,60 %; e3 metrika mapy <= 1,16e-6, pravý úhel <= 3,2e-5°; e4 pravidla silnic na kouli <= 1,5e-6; e8 float švy 61 µm / 600 m, 0,98 mm / 10 km (= RG-166 §4).
- **e10 FAIL na vrcholu krychle:** stavba sektoru se sídlem 3,14 ms (> 3 ms `BudgetMs`), jinde 1,3-1,5 ms; příčina: bodový `TerrainModel.Height` (dotaz do indexu + smyčka nejbližší osy přes hustou síť ulic ~2,5 ms). Oprava = RG-170 (přesný dávkový dotaz, sweep nejhoršího sektoru). Region svět 4-12 s jedno vlákno, ~3 MB managed; experiment 246 s (dřív 44 s).
- e6 (nález): ořez vzorkovaný ve vrcholech mřížky 3,4-4,8 m je o 0,04-0,10 m horší než rovinná mřížka 4 m, 0,83 m ve středu stěny — řeší až renderovací ticket (hustší vzorkování pásu silnice).
- Mimo v2: doména polygonu e5, skirt far vrstvy e7, GPU výpadky e9, výška silnice na portálech hranice regionu.

## RG-168 (2026-11-12): povrchová vrstva planety — kontinenty, moře, jezera, les
- Nová knihovna `src/WhatIf.Planet.Surface` (`PlanetSurface.Height/Sample`, `SurfaceSector` 65 x 65): čistá funkce (seed, směr), bez parametrizace stěny, tedy bez švů. Detail, čísla a nálezy: [[what-if-planet-surface]]. Je to báze, kterou RG-169 dosadí do `RegionWorld` místo `PlanetNoise.Height`.
- Rozpočet: s7 sektor 1,26 ms (worker) / 1,59 ms (přepočet orchestrátora při souběžné práci RG-170) vůči 1,5 ms -> v RG-169 jeden společný řádek „povrch + ořez <= 3 ms“.

## RG-170 (2026-11-13): stavba sektoru v2.1 — přesný dávkový dotaz ořezu, sweep všech sektorů
- `SectorBuild.Build` = průchod 1 (směr, planet base, bod mapy, taper), jeden `TerrainModel.HeightBatch` ([[worldgen]]), průchod 2; výstup bitově shodný s RG-167 (0 z 12 288 sektorů se liší). Sweep 1024 sektorů na místo: medián 1,3-1,5 -> 1,13-1,15 ms, P95 4,2-4,8 -> 2,2-2,5 ms.
- **Nejhorší sektor (husté sídlo) 2,5-2,9 ms FAIL proti cíli 2,0 ms** (přesný ořez se neaproximuje). Příčina: mapa regionu je pootočená proti hash mřížce, takže sektor 65 x 65 má 182-242 různých rozsahů buněk 64 m (ne „pár desítek“); rozklad: `Natural` 0,45-0,5, `Base` 0,23, ořez 1,7-2,2, zbytek ~0,5 ms.
- **Rozhodnutí orchestrátora (What If mandát):** stavba sektorů What If běží na vlákně na pozadí; rozpočet = propustnost (sweep medián <= 2,0 ms, P95 <= 3,0 ms), nejhorší sektor se reportuje proti 3,5 ms; hlavní vlákno platí jen upload meshe (Unity, později). RG-169 počítá základ jednou na bod (dávka s předpočteným základem), sweep jen v procesu 1, runnery čekají na zámek.

## RG-171 (2026-11-13): vzdálená vrstva — CDLOD nad povrchem, díra, sukně
- Knihovna `src/WhatIf.Planet.FarLayer`; detail a čísla: [[what-if-planet-far-layer]]. f1-f4, f6-f8 PASS (max 398 uzlů / 203 k trojúhelníků, výběr <= 0,11 ms, 7,6 MiB), f5 3 snímky bez rodiče u horizontu přijaté (0 viditelných děr).

## RG-169 (2026-11-13): povrchový základ v3 — regiony na povrchu planety, voda, les, pobřeží
- `RegionWorld.BuildSurface(id, seed)`: WorldGen svět regionu na základu `PlanetSurface` ([[what-if-planet-surface]]) s maskou vody (`IsWater`: Ocean/Sea/Lake), silnice a sídla mimo vodu (`DropWaterEdges`), detail pobřeží 1-4 km (poměr délka/tětiva 1,428). `RegionWorld.Scatter()` = jediná továrna `ScatterModel` pro What If: hustota = planetární Forest, maska vody, stromová a vodní čára otevřené (konstanty 120 m / -3 m jsou výšky dlaždice NightRacer; `ScatterModel.TreeLine/WaterLine` jsou od RG-169 instanční, výchozí hodnoty bitově shodné, [[worldgen-vegetation-buildings]]).
- Experiment bloků 3 x 3 regionů (A suchý pás u pobřeží, B les přes vrchol krychle): p1 švy 0, p2 0 silnic/sídel na vodě, p3 941 360 stromů v bloku B, 0 na vodě, 0 při Forest 0, hustota roste s Forest (Spearman 0,667), počítadlo zamítnutí podle důvodu; p5 sweep medián 1,32-1,39 / P95 2,38-2,52 ms PASS, nejhorší sektor 4,1-4,7 ms FAIL reportovaný; p7 stavba regionu sekvenčně 2,1-2,9 s, 3,1 MiB; souběžně až 54-64 s.
- Po merge: vzdálená vrstva na novém povrchu má 3 FAIL (T-spoj 0,95 m, sukně, 407 uzlů) — příčina je podlaha auto `MorphFraction` při Range13 1800 m (bezpečná hodnota -0,013), oprava RG-174 ([[what-if-planet-far-layer]]). Runtime na povrchovém základu + cena stavby regionu: RG-173 ([[what-if-planet-runtime]]).
- **RG-176 (2026-11-13): vesnický svět pro NoSite regiony.** Když 4 pokusy o město selžou, `BuildSurface` zkusí `WorldSpec.Motorway = false` s `CityRadiusM = RegionWorld.VillageOnlyRadiusM` (450 m), 2 pokusy; flag `RegionWorld.VillageOnly`. Bez dálnice a křižovatky, první sídlo `village0` (`IsCity = false`). Blok A: A1 5,6 km / 2 vesnice, A8 21,3 km / 4 vesnice, 1 komponenta, 0 na vodě; pevnina >= 25 % bez světa 2 -> 0; ostatních 16 regionů má stejný GenResult hash (řádek p2 „GenResult hash“ v `BlockChecks.GenHash`). Stavba A1 2,3 s (4 neúspěšná města + vesnice). Review `agent_pool/shared/reviews/RG-176.md`.

## RG-172 (2026-11-13): streamovací runtime — region cache, sektory na pozadí, far vrstva v pohybu
- Knihovna `src/WhatIf.Planet.Runtime`; detail a čísla: [[what-if-planet-runtime]]. Jízda 4x 300 s: 0 děr, náskok regionu >= 19 s, 60-72 MiB, hlavní vlákno P95 ~0,5 ms (max jednou 4,9 ms přijato). Úzké hrdlo = stavba regionu 5-70 s (`Generator.Run`).

## Revize směru 2026-11-15: kotvy místo region světů (návrh, čeká na uživatele)
Plný text: `.planning/race-game/docs/what-if-planet-anchor-direction.md` (návrh WI-ADR-002).
- **Problém pólů neexistuje:** equal-angle cube-sphere, žádná lat/long ani Mercator v `src/WhatIf.*`; pól = střed stěny kostky = nejméně deformované místo. Nejhorší buňky jsou 8 vrcholů kostky (roh 120°, plocha 1,41 : 1) a ty jsou změřené (šev 0, metrika <= 1,6e-6).
- **Skutečná slabina:** region level 10 (~10 km) = samostatný WorldGen svět (vlastní město, dálnice od kraje ke kraji, taper 32 m na hranici); silnice končí na hranicích regionů, `CorridorGraph` (bez masky pevniny) se nepoužívá.
- **Doporučení B (anchor-first):** sídla a koridory jako globální kotvy v double 3D (koridor = segmenty s pevnými výškami spojů), obsah sídla ve vlastní mapě sídla, sektor = čistá funkce povrchu + kotev v dosahu -> bez taperu, šev 0 konstrukcí. Dlaždice zůstávají jen jako klíč cache, mesh, streaming, kolize a LOD. Ticket B1-B6 v §6 dokumentu; ticket „portály přes hranice regionů“ pozastaven.

Souvisí: [[research-evidence]], [[worldgen]], `streaming-sectors` (race_game), `perf-matrix` (race_game).
