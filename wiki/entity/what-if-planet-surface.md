---
type: entity
sources:
  - src/WhatIf.Planet.Surface/PlanetSurface.cs
  - src/WhatIf.Planet.Surface/Lakes.cs
  - src/WhatIf.Planet.Surface/SurfaceSector.cs
  - src/WhatIf.Planet.Surface/FbmLayer.cs
  - src/WhatIf.Planet.Surface.Experiment/Checks.cs
  - tools/whatif-surface/run-surface.ps1
  - tests/WhatIf.Planet.Surface.Tests/Program.cs
  - .planning/what-if/docs/what-if-planet-surface.md
  - .planning/what-if/docs/what-if-planet-surface.csv
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-168.md
updated: 2026-11-12
last_verified: 2026-11-12
status: provisional
aliases:
  - planet surface
  - povrch planety
  - PlanetSurface
  - kontinenty
  - jezera planety
  - lesy planety
  - oceán
---

# What If: povrchová vrstva planety (RG-168)

Planetární povrch pro projekt What If ([[what-if-planet-engine]]): **čistá deterministická funkce `(seed, směr)`**, nic se neukládá, švy mezi stěnami/regiony/sektory nevznikají, protože všechny vrstvy jsou 3D int64 lattice šum v pevné poloze planety (žádná parametrizace stěny). Je to báze, na kterou RG-169 posadí WorldGen regiony ([[worldgen]]) místo `PlanetNoise.Height`. Plná tabulka a návrh: `.planning/race-game/docs/what-if-planet-surface.md`.

## API (pro RG-169 a vzdálenou vrstvu)
- `new PlanetSurface(seed)` — konstruktor 10-40 ms (kalibrace hladiny moře přes 16 384 Fibonacciho směrů), **jedna instance na vlákno** (cache rohů nejsou thread-safe). Vytvořit jednou, ne na sektor.
- `Height(dir)` = metry nad koulí R (moře 0), bitově shodné s `Sample(dir).HeightM` (~0,7 µs/volání, náhodné vrcholy). `Sample(dir)` -> `HeightM`, `Kind` (Ocean < -200 m, Sea -200..0, Beach 0..2, Land, Lake), `WaterLevelM` (0 moře, hladina jezera i na břehu, jinak `NoWater`), `Forest` [0,1], `Biome` (0 voda .. 8 tropy).
- `SurfaceSector.Sample` = dávka 65 x 65 na vrcholech `QuadSector` (`streaming-sectors` (race_game) rozměr), medián 0,7-1,26 ms jedno vlákno.

## Návrh v kostce
- Kontinenty: fBm 2^21..2^17 m s doménovým warpem 1000 km; hladina moře = **kvantil 0,68 pole pro daný seed** (pevná konstanta dávala 25-40 % jen náhodou), takže pevnina ~32 % pro každý seed. Profil: abysál -4000, šelf -200..0, nížina do 420 m, hory (ridged) až ~2700 m jen nad hodnotou pole 0,22. Detail 150 m (`PlanetNoise.HeightSampler`) se na souši zapíná s výškou kontinentu -> pobřeží kreslí pole, ne tečky.
- Jezera = buňky 8192 m: jezero existuje jen celé uvnitř své 3D buňky (vzorek potřebuje jen svou buňku), hladina = min ze 128 vzorků okraje - 0,5 m, zamítnuto při reliéfu okraje > 30 m nebo okraji < 3 m (jezera jen na rovině). Břeh: rampa 100 m, terén zvednut na hladinu do 120 m a doznívá do 400 m -> voda uzavřená prstencem >= hladina (žádný únik).
- Les: teplota z šířky (pól = +Y) - 6,5 K/km, vlhkost fBm + suchý pás ~24°, plynulé útlumy (pobřeží, hranice lesa 1800-2200 m, horský člen, vzdálenost od jezera). **Bez členu sklonu** (stálo by 4 výšky navíc); svah > 30° neměřen.

## Výsledek RG-168 (3 seedy, orchestrátor přepočítal)
- Pevnina 32,0 %, 9-12 kontinentů >= 1e6 km², největší 26-51 % souše; les 22-27 % souše (Forest >= 0,5); jezera 6,8-8,9 / 10 000 km² souše; únik z jezer 0; max |dh| mezi vrcholy ~4 m: pobřeží 0,05 m, břeh jezera 2,1 m, souš 0,43 m (limit 4 m); s1 seamless/deterministické 0 bitů; paměť 0,06 MiB.
- Nálezy: pobřeží je **ploché a hladké** (max výška do 100 m od břehu 0,2-1,0 m, detail pod ~4 km jen z 3oktávového vlnění); souš většinou nížina (hory jen místy) — `LowlandM`/`MountainStart` v `SurfaceParams` mění všechny seedy. Běh experimentu 364 s (brief chtěl ~2 min).

Souvisí: [[what-if-planet-engine]], [[worldgen]], `streaming-sectors` (race_game).
