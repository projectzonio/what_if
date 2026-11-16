---
type: entity
sources:
  - src/WhatIf.Planet.Anchors/Settlements.cs
  - src/WhatIf.Planet.Anchors/Corridor.cs
  - src/WhatIf.Planet.Anchors/AnchorSet.cs
  - src/WhatIf.Planet.Anchors/Ground.cs
  - src/WhatIf.Planet.Anchors.Experiment/Program.cs
  - src/WhatIf.Planet.Anchors.Experiment/Measure.cs
  - src/WhatIf.Planet.Anchors.Experiment/Nets.cs
  - tests/WhatIf.Planet.Anchors.Tests/Program.cs
  - tools/whatif-anchors/run-anchors.ps1
  - src/WhatIf.Planet.Anchors/AnchorWorld.cs
  - src/WhatIf.Planet.Anchors.Experiment/SettlementRows.cs
  - .planning/what-if/docs/what-if-planet-anchor-spike.md
  - .planning/what-if/docs/what-if-planet-anchor-spike.csv
  - .planning/what-if/docs/what-if-planet-anchor-direction.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-177.md
  - src/WhatIf.Planet.Anchors/AnchorWorld.cs
  - src/WhatIf.Planet.Anchors/AnchorSector.cs
  - src/WhatIf.Planet.Anchors.Experiment/Sectors.cs
  - src/WhatIf.Planet.Runtime/PlanetStreamer.cs
  - .planning/what-if/docs/what-if-planet-anchor-spike-sectors.csv
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-178.md
  - src/WhatIf.Planet.Anchors.Experiment/Corridors.cs
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-181.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-184.md
  - src/WhatIf.Planet.Anchors.Experiment/Stalls.cs
  - src/WhatIf.Planet.Anchors.Experiment/DrivePath.cs
updated: 2026-11-16
last_verified: 2026-11-16
status: implemented
aliases:
  - kotvy planety
  - anchor spike
  - SettlementField
  - CorridorBuilder
  - sídla a koridory na planetě
---

# Kotvy planety What If (RG-177, RG-178, RG-181; WI-ADR-002 Accepted)

Varianta B ze směrového návrhu `what-if-planet-anchor-direction.md`: místo samostatného WorldGen světa na každý region level 10
(varianta A, [[what-if-planet-engine]], `RegionWorld.BuildSurface`) jsou obsahem globální **kotvy** — sídla na povrchu planety a
silniční koridory mezi nimi. Spike 1 měří síť a cenu kostry; sektory, obsah sídel a jízdu měří RG-178, který vyhodnotí pevné
rozhodovací pravidlo (A vs B). **Rozhodnuto 2026-11-15: B (WI-ADR-002 Accepted)**, what-if-planet-anchor-direction.md §8.

## API (`src/WhatIf.Planet.Anchors`, netstandard2.1, bez Unity)

- `SettlementField` (`Settlements.cs`): sídlo = čistá funkce (seed, buňka). Vesnice level 11 (~4,9 km), město (town) level 10, velkoměsto
  (city) level 9 (~19,6 km); pevnina mimo pláž, sklon <= 12 / 9 / 7 %, odstupy vesnice >= 2,5 km od města. `Cell`, `Within`, `CellsNear`.
- `LinksOf`: relative neighbourhood graph po vrstvách (secondary <= 10 km mezi všemi, primary <= 20 km mezi town + city, motorway <= 40 km
  mezi city); rozhodnutelné ze sídel v poloměru vrstvy, nikdy z celé planety.
- `CorridorBuilder` (`Corridor.cs`): `Domain`, `Route` (A* na mřížce 200 m v gnómonické mapě středu koridoru, voda zakázaná, cena sklonu,
  string pulling, oblouky 1,5 x min. poloměr třídy, stanice 10 m), `Profile` (segmenty <= 5 km, výška + sklon spoje připnuté = každý
  segment se řeší sám; řešič `WhatIf.WorldGen.Generator.SolveVerticalProfile`, viz [[worldgen]]).
- `AnchorSet` (`InRegions`, `Build`, `Hash`), `Ground` (povrch [[what-if-planet-surface]] + globální detail ~+-11 m), `AnchorParams` (konstanty).
- Nic není thread-safe: jeden field / builder / `PlanetSurface` na vlákno.

## Čísla (orchestrátorův rerun na klidném notebooku, 2026-11-15)

| | oblast A (pobřeží) A / B | oblast B (hrana krychle) A / B |
|---|---|---|
| n1 konce silnic na hranici regionu | 27 / 0 | 193 / 0 |
| n2 sídla v největší komponentě | 7 % / 98 % | 6 % / 99 % |
| n3 dosažitelné páry 5-40 km | 3 % / 94 % | 2 % / 98 % |
| c1 stavba regionu A medián / B segment medián | 0,12 s / 0,3 ms | 0,34 s / 0,3 ms |
| c1 B segment max | 38 ms | 2633 ms (dálnice přes hranu, stall) |

c1/c2 srovnávají nestejný obsah (A celý svět, B jen kostru). Determinismus n5 shodný ve 3 procesech.

## Sektory a jízda z kotev (RG-178, schváleno 2026-11-15)

- `AnchorWorld` (`AnchorWorld.cs`): thread-safe cache tras, líných profilů segmentů a WorldGen světů sídel; `Prepare(region)` = jednotka práce na region (region je jen klíč cache). `SettlementWorld` = `Generator.Run` s `Motorway = false` v gnómonické mapě středu sídla.
- `AnchorSector` (`AnchorSector.cs`): sektor level 15 = čistá funkce směru vrcholu + kotev v dosahu; terén sídla v poloměru + 200 m, pak carve nejbližšího koridoru (bez fade), výstup `AdaptedSector` jako region path. Žádný taper.
- `PlanetStreamer` bere `ISectorContent` (druhý konstruktor); region path obalený `RegionContent`, runtime testy beze změny ([[what-if-planet-runtime]]).
- Runner: `run-anchors.ps1 -Sectors` (řádky s1-s6 + rozhodovací tabulka; běh bez `-Sectors` přegeneruje doc a sekci smaže — pak pustit `-Sectors`).
- Rerun orchestrátora (klidný notebook): s1 0 z 3 281 919 bitů, s2 0 vrcholů > 0,10 m nad silnicí (A 749 / 2 943), s3 teplý P95 A 1,83 / B 2,01 ms a 2,03 / 1,93 ms, s4 43,2 / 3,6 MiB a 81,5 / 5,9 MiB, s5 medián 0,20 / 0,05 s a 0,35 / 0,05 s, s6 jízda 42,7 km přes 7 starých hranic t1 P95 0,628 ms, 0 děr.
- **Výhrady (vstup B2/B3):** světy sídel mají jen 6-12 uzlů i pro town/city (`Motorway = false` -> `village0`, `IsCity = false`) -> skutečné ulice měst + připnuté vjezdy = B3; studený první dotek 2,6-5,5 s (stall připnutého spoje dálnice) a rovné tětivy = B2.

## Koridory B2: meandry a proveditelné spoje (RG-181, schváleno 2026-11-15)

- `CorridorBuilder` default: string pulling s tolerancí 8 m (dřív 20 m) proti lineární výšce, pak **deterministický meandr** na stanicích 10 m (sinus, vlnová délka 1,8-3,2 km, fáze ze `SplitMix64(klíč spoje ^ salt)`, amplituda min(120 m, 5 % délky), náběh 500 m u vjezdů; lalok jen kde je suchý a rovný terén; žebřík měřítka {1, .6, .3, 0} dokud nejsou stanice na vodě a změřený poloměr >= 0,98 x min. třídy). Koridory < 800 m bez meandru.
- **Proveditelné spoje:** výška spoje = terén vyhlazený +-500 m v mezích výkop/násyp, omezená 0,8 x sklon třídy x vzdálenost; sklon vjezdu do sídla = sklon povrchu na 300 m (dřív 0 — skutečná příčina studeného doteku 2,7 s). `CorridorBuilder.StallOuter` 300 -> 5.
- `Corridor.MeasuredRadiusM` (kružnice přes stanice 30 m od sebe); `CorridorBuilder.Legacy` / `Corridor.Legacy` = chování RG-177/178 jen pro sloupec "před". Runner `run-anchors.ps1 -Corridors` (řádky r1-r7, PNG před/po). Diagnostický režim `Experiment seg` tiskne jeden pevný segment (odstranit nebo zobecnit).
- Čísla (orchestrátorův rerun 2026-11-15, klidný notebook): r2 tětivy A 0/68, B 0/95 (dřív 100 / 98 %); r3 0 porušení sklonu/poloměru; r4 segment max A 15 / B 28 ms, **stall exity B 9** (12 stanic, cíl 0 nesplněn: sklon + poloměr vrcholu proti reliéfu u vjezdů); r6 0 na vodě; r7 hashe A `c9069fd7b547` / B `51f53e8d8b32` shodné ve 3 procesech; s3 studený nejhorší sektor A 69 / B 197 ms (dřív 2,6-5,5 s), teplý P95 1,79 / 1,96 ms; s5 medián 0,05-0,06 s; s6 t1 P95 0,44 ms, 0 děr.
- Časy s3 na notebooku kolísají až 2-3x podle stavu stroje (první rerun 604 ms při plošně 2x pomalejším mediánu i v nezměněné oblasti A) -> srovnávat jen běhy na klidném stroji těsně po sobě.

## B3 obsah sídel (RG-182, 2026-11-15)

- `WorldSpec.CentreIsCity` (null = dle `Motorway`, NightRacer beze změny) odpojuje třídu od dálnice: town/city světy dostanou městský rozpočet ulic. `WorldSpec.Entrances` (bod, směr dovnitř, připnutá výška, třída) -> `Generator.BuildEntrances` vede rovnou silnici po směru koridoru, dokud nezaklesne do grafu ulic (jinak lead-in + jeden ohyb); připnutí = předvyplněný `nodeY` (`_pinY`), řez/násyp +8 m na prvních 150 m.
- `CorridorBuilder.TrimToEntrances` ořízne koridor na poloměr sídla (max 40 % na konec), `AnchorSector` nekarvuje koncové čepičky. `SettlementWorld(AnchorWorld, Settlement, before)` bere vjezdy z `Field.LinksOf` + `Route`.
- Čísla (worker): e1 town/city uzly 391/948 proti vesnici 26; e2 vjezdy napojeny 100 % (99/153 přes fallback s ohybem); e3 0 m / 0 m; e4 0 na vodě; e5 city build max 0,17 s, 2,5 MiB. **FAIL (nezkoušeno ladit):** r4 B max 1237 ms (jiný segment dálnice po oříznutí), s3 studený B 1251 ms, s3 teplý A 1,38x, s4 cache 5,6x/4,7x (světy s ulicemi se drží celé), ulice nad 0,10 m max 5 m (sektor karvuje jen koridor).
- Další: stall exit pomalého segmentu B, držet jen terénní model světa/LRU, karvovat ulice za plnou vahou blendu, zaoblit roh vjezdu.

## RG-184: uzavření B4/B5 (review `agent_pool/shared/reviews/RG-184.md`, 2026-11-16)
- **Stall exits:** `CorridorBuilder.Fix = StallFix.Slide` (výchozí, jen planeta): vnitřní kloub, jehož sklon terénu (±300 m) se liší od připnutého sklonu o > 0,25 x sklon třídy, se posune až o 600 m k nejbližší souhlasné stanici (segmenty ≤ 5 km), pak znovu připnutí. B nejpomalejší segment 1258 -> 25 ms; 9 pojmenovaných zbytků (7 zlom grade capu, 2 vesnické linky). Diagnostika `Experiment stalls` (nahrazuje natvrdo `seg`).
- **Paměť:** `SettlementWorld` drží jen terénní model + kompaktní `StreetSet` (float, poloviční šířka na segment) + vjezdy; `Gen` jen s `AnchorWorld.KeepGraphs`. LRU podle bajtů `AnchorWorld.WorldByteCap` (výchozí 6 MiB, odhad 207 B / segment ulice); přestavba bitově shodná. k2: A 8,3 MiB (1,98x RG-181), B 9,3 MiB.
- **Stopa ulic:** `AnchorSector` po všech blendech zapíše nejbližší ulici na vrchol, pak kódy a karv pravidlem koridoru (polovina šířky + 1,5 m, svah 1 : 1,5); bližší silnice rozhoduje. Sektory jen s koridorem bitově shodné (`5EB2BE55025B8900`).
- **Přijaté zbytky (mandát What If):** k3 42 / 205 vrcholů > 0,10 m, všechny kód koridoru 1 = přesah oříznutého konce koridoru přes vjezd; k4 A medián 1,41x (sektory sídel), B 1,00x. Follow-upy (a)-(d) v `agent_pool/shared/follow-ups.md`.
- **Jízda město -> město** viz [[what-if-planet-runtime]] (RG-184 B5). Spuštění: `tools/whatif-anchors/run-anchors.ps1 -Closeout`.
- **`RegionWorld` smazán v RG-185** (`agent_pool/shared/reviews/RG-185.md`): hashe kotvových sektorů beze změny (A `78159e87…`, B `5b4dd85f…`); řádky A/B jsou teď jen B (`report` místo poměru k A).

## Pasti

- Oblast 5 x 5 regionů skládat **chůzí po sousedech**, ne offsety v mapě: offsety přes hranu krychle slučují regiony (24 místo 25).
- V namespace `WhatIf.Planet.Anchors` se `RoadClass` vyřeší na `WhatIf.Planet.RoadClass` dřív než alias na úrovni souboru: psát plné jméno.
- (vyřešeno RG-181) String pulling na mřížce 200 m narovnal skoro všechny koridory do tětivy; připnutý sklon vjezdu 0 + stall 300 dával studený dotek 2,6 s.
- Runner `run-anchors.ps1` počítá nečinný helper Unity Hubu jako běžící Unity (oprava v RG-178).

Souvisí: [[what-if-planet-engine]], [[what-if-planet-runtime]], [[what-if-planet-surface]], [[worldgen]].
