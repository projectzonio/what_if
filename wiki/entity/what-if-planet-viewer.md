---
type: entity
sources:
  - tools/whatif-viewer/run-viewer.ps1
  - tools/whatif-viewer/presets/planet-max.json
  - tools/whatif-viewer/presets/planet-low.json
  - .planning/what-if/docs/what-if-planet-viewer.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-179.md
  - .planning/what-if/docs/race_game-lane-history/manual-tests/RG-179-summary.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-183.md
  - .planning/what-if/docs/race_game-lane-history/manual-tests/RG-183-summary.md
  - src/WhatIf.Planet.Anchors/AnchorSector.cs
updated: 2026-11-16
last_verified: 2026-11-16
status: provisional
aliases:
  - planet viewer
  - prohlížeč planety
  - --stream-map planet
  - run-viewer
---
# What If — prohlížeč planety v0 (RG-179)

První pohled na planetu na GPU (i3-1215U / UHD): `--stream-map planet` v KN5 test playeru. Streamuje **kotvové sektory** ([[what-if-planet-anchors]], WI-ADR-002) přes `PlanetStreamer` ([[what-if-planet-runtime]]) a vzdálenou vrstvu ([[what-if-planet-far-layer]]); povrch a barvy z [[what-if-planet-surface]]. Celkový kontext: [[what-if-planet-engine]].

## Vstupní body
- Dispatch: jeden řádek v `RunStreamKn5` (`Kn5TestRunner.Stream.cs`), takže běh potřebuje výchozí KN5 kořen; `run-viewer.ps1` ho předává. Běhy a flagy v hlavičce `Kn5TestRunner.Planet.cs` (`RunStreamPlanet`).
- Runner: `tools/whatif-viewer/run-viewer.ps1 -Row fly|drive|seams|shots|manual [-Preset max|low] [-Spawn p1|p1c|p2|p2e|p3|p3k|fc] [-Emit own|shared] [-Bake main|thread] [-Empty]`; reporty `artifacts/whatif-viewer/<stamp>/`.
- Spawny (seed 1): p1 pobřežní město (blok A), p2 vrchol krychle (1,1,1) s lesem, p3/p3k 1000/5000 m nad p2, fc střed stěny (oceán).
- Kód: `PlanetCore.cs` (rámec, barvy, vlastní kopie `AnchorContent`, spawny), `SectorMesh.cs` (mesh sektoru, emit `own` / `shared` 2^-10 m), `FarMeshBuild.cs` (uzel far vrstvy, díra, sukně, mořský „cap“), `PlanetView.cs` (napojení streameru, rozpočtovaný upload, kolidery, rebase), shader `PlanetTerrain.shader`.
- Šest assembly `WhatIf.Planet*` je v obou sync skriptech (`unity-build-pipeline` (race_game)).

## Výsledek (review `agent_pool/shared/reviews/RG-179.md`)
- Let 120 m/s v 50 m: vrstva planety +0,85 ms P95 (low) / +3,23 ms (max) proti prázdné scéně; RAM +44..63 MiB, sdílená GPU +11 MiB.
- Švy: 0 magenta px na 36/36 snímcích (3 místa x 3 vzdálenosti x oba emit módy). `shared` emit stojí ~10 ms na každý rebase a švy nezlepšil.
- FAIL: integrace sektoru při studeném naplnění P95 1,30 ms (ustálený stav 0,17/0,42 ms PASS); díry pod horizontem z 1000 m (1 px) a 5000 m (2868 px), příčina neověřena.
- Kolider na hlavním vlákně 2,6-4,8 ms na sektor; `-Bake thread` 0,03 ms.
- Floating origin: rebase každých 1000 m, 10,78 km jízda, chyba polohy 9e-7 m.
- Jízda uživatele 2026-11-16 (OK, doslova v summary): bez děr a švů, horizont věrohodný; „Celé je to placeholder, chybí silnice ... Planetu jako celek z orbity nevidím“; 3 micro-lagy při přechodu sektorů na P1.

## Pasti
- `run-kn5.ps1` ani `run-viewer.ps1` nestaví player: po změně skriptů `tools/kn5-unity/build-player.ps1`, jinak běží starý player bez nových řádků.
- Přesměrování `*>` jen z PowerShellu (Git Bash ho rozbalí); background PowerShell po `cd` do artifacts zdědil starý cwd — skripty mají začít `Set-Location` na kořen worktree.
- Mořský cap se musí stavět kolem aktuálního počátku při každém rebase (jinak polovina obrazu clear colour na p2).
- Prázdná scéna při jízdě autem není baseline (auto propadne); odečet vrstvy jen z letu.

## v1 (RG-183, review `agent_pool/shared/reviews/RG-183.md`)
- **Silnice:** `AdaptedSector.Surface` (byte na vrchol: 0 terén, 1 koridor, 2 ulice, 3 krajnice) plní `AnchorSector.Build`. Koridor se bere z `bestEff`, ulice ze silničních hran sídla přes mřížku buněk 32 m. Cena stavby sektoru +0 %, výšky jsou beze změny (hash `5EB2BE55025B8900`). Viewer barví podle kódu vrcholu, takže okraje jsou zubaté (4,7 m) a zblízka rozmazané.
- **Orbit:** spawn `orbit` (20 000 km nad P1), řádky `-Row orbit|oshots|roads`. Plynulý sestup 120 s plus visení `--planet-hold-s`. Moře je ve far uzlech, vodní cap jen pod 1500 m. Near/far roviny a mlha se řídí výškou. Sestup max +5,67 ms P95 nad prázdnou scénu (rozpočet 6 ms).
- **Barvy:** sklon -> skála, sněžná čára podle šířky, ploché ambientní světlo. Bez textur a stromů; les je jen tmavší odstín.
- **Micro-lagy:** výchozí je `-Bake thread`. Log špiček `planet-*-spikes.txt` zapisuje události streameru u každého snímku nad 16,7 ms. Záseky 0,7-3 s bez události se objevily i v prázdné scéně, jsou podezřelé z prostředí. Uživatel cukání na P1/P2 neviděl.
- **Díry z 1-5 km v RG-179** byly artefakt počítání: horizont je na obrazovce kružnice, ne řádek. Přesné počítání (paprsek proti mořské kouli) dává 0 px.
- **Jízda uživatele 2026-11-16 (OK):** planeta z orbity ano, sestup plynulý; silnice „rozmazané, jen náznak“; tráva, silnice, písek a voda jsou rozpoznatelné. Auto se ve vodě potopí. Spawn P2 neměl co ukázat (zelená plocha).

## Otevřené
Maska silnic jako textura (256 R8) místo barev vrcholů; silnice ve far vrstvě (přechod near/far je vidět); kolize s vodou; morph a haze far vrstvy; agent port pro planetu; dispatch mimo `RunStreamKn5`; spawn P2 má smysl až se stromy.
