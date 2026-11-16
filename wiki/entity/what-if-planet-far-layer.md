---
type: entity
sources:
  - src/WhatIf.Planet.FarLayer/FarLayer.cs
  - src/WhatIf.Planet.FarLayer/FarNode.cs
  - src/WhatIf.Planet.FarLayer/FarMesh.cs
  - src/WhatIf.Planet.FarLayer/NearSet.cs
  - src/WhatIf.Planet.FarLayer/FarChecks.cs
  - src/WhatIf.Planet.FarLayer.Experiment/Checks.cs
  - tools/whatif-farlayer/run-farlayer.ps1
  - tests/WhatIf.Planet.FarLayer.Tests/Program.cs
  - .planning/what-if/docs/what-if-planet-far-layer.md
  - .planning/what-if/docs/what-if-planet-far-layer.csv
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-171.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-174.md
  - tools/whatif-farlayer/doc-design.md
updated: 2026-11-13
last_verified: 2026-11-13
status: provisional
aliases:
  - far layer
  - vzdálená vrstva
  - CDLOD planety
  - sukně
  - skirt
  - díra po sektorech
  - horizont planety
---
# What If — vzdálená vrstva planety (RG-171)

Hrubý mesh celé planety za `LoadRadius` 600 m blízkých sektorů až k horizontu; CPU knihovna bez Unity (`src/WhatIf.Planet.FarLayer`), Unity viewer později jen nahraje, co vybere. Výška = `Func<Vec3,double>` (v experimentu [[what-if-planet-surface]] `PlanetSurface.Height`); knihovna závisí jen na `WhatIf.Planet` ([[what-if-planet-engine]]).

## Návrh
- Uzly = equal-angle quadtree stěny krychle, level 0..13, 17 x 17 vrcholů na mřížce 2^21 (kanonické hranové vrcholy, stejný strom jako sektory level 15).
- Výběr podle 3D vzdálenosti a `min(d_kamera, d_zem)` (vynutí dělení u díry i ve výšce), ořez horizontem přes slupkový vrchlík (shell cap) s rezervou 40 m; `range13` = 1950 m (RG-174; bylo 1800); morph na CPU s automatickým podílem (spodní mez 2 %, `FarParams.MorphFraction`), protože 30 % zóna dávala trhliny 8-20 m. Bezpečná mez T-spoje `f <= 0,5 - 0,725 * 1250 / Range13 - 0,01` musí být >= 0,02 (od ~1930 m), jinak spodní mez 2 % trhá T-spoje (1800 m -> 954,85 mm po RG-169); hlídá unit test.
- Díra: 16bitová maska 4 x 4 sektorů level 15 na uzel level 13 (`NearSet`), sukně hloubky `skirtM` = naměřená max mezera near/far x 1,25 (kalibrace na kamerách a-d obou seedů, spodní mez 3,0 m, RG-174).
- Stavba uzlů s rozpočtem 2 ms/snímek (nejdřív hrubé úrovně, pak nejbližší), fallback na postaveného předka; představba 6 kořenů + level 1..3 = 510 uzlů ~0,8-1,7 s.

## Výsledek (seed 20261112 a 7, přepočet orchestrátora shodný)
- f1 pokrytí 0 porušení (statické kamery 100 000 směrů + všechny blízké vrcholy, jízda/let každý 10. snímek); f8 0 rozdílných z 4,7 M hranových vrcholů, 0 porušení <= 1 level, T-spoj 0 mm; f7 morph 0 mm.
- f3 max 398 uzlů / 203 472 trojúhelníků (let 250 m/s v 5 km, všude kolem; s frustum ~137 uzlů); f4 výběr <= 0,11 ms; f6 paměť 7,6 MiB (rozpočet 64); f2 mezera max 2,2 m (břeh jezera), sukně kryje 100 %.
- **f5.no_gap.drive FAIL přijatý:** 3 ze 7200 snímků jízdy chtějí uzel level 7 ve 176-182 km bez postaveného rodiče; viditelná díra 0 (kreslí se prarodič). Prefetch podle rychlosti -> Unity viewer.
- Běh `tools/whatif-farlayer/run-farlayer.ps1` 53 s (zámek měření, `-NoDocs` pro přepočet).

## RG-174 (2026-11-13, po pobřeží RG-169)
- Range13 1950 m: f8 T-spoj 0,0000 mm (oba seedy), sukně 3,0 m kryje 308 230 / 308 230 mezer (max 2,343 m břeh jezera seed 7), f5 0 děr i v jízdě.
- **Strop uzlů 400 -> 480** (What If mandát): let 437 uzlů / 223 488 trojúhelníků, d_land_10km 421; strop z rozpočtu 250 000 trojúhelníků / ~512 na uzel = 488. f4 výběr medián <= 0,13 ms, f6 8,3 MiB.
- Range13 2000 m padal na f5 (1 snímek s nepostaveným rodičem, 455 uzlů) — 1950 je těsně; další zvětšení rozsahu potřebuje prefetch podle rychlosti.
- Runtime ([[what-if-planet-runtime]]) přepočet s 1950: t1 P95 0,566 ms, t2/t6 PASS.

## Pro navazující práci
- Po změně povrchu (RG-169 pobřeží) znovu spustit `run-farlayer.ps1` — hloubka sukně se odvozuje z naměřené mezery.
- Ořez/násyp WorldGen u silnic zvětší mezeru near/far na hraně díry (měří RG-169 nebo viewer).
- Úzká zóna morphu (0,15 velikosti uzlu) = riziko popu; posoudí viewer při 108 km/h a v letu. Hladinu vody kreslí viewer, vrstva kreslí dno.

Souvisí: [[what-if-planet-engine]], [[what-if-planet-surface]], `streaming-sectors` (race_game).
