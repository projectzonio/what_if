# What If planet viewer v0 (RG-179)

Status: built and measured on the i3-1215U / Intel UHD notebook, 2026-11-16. First look at the planet on the GPU; every number below is a measurement from `artifacts/whatif-viewer/<stamp>/` (git-ignored, paths listed). RX 470 not measured (unavailable).

## What it is
`--stream-map planet` in the KN5 test player (dispatched from `RunStreamKn5`, so the run still needs the default KN5 root; `tools/whatif-viewer/run-viewer.ps1` passes it).

- Code: `Assets/Scripts/Planet/` (`PlanetCore.cs` frame/colours/anchor adapter/spawns, `SectorMesh.cs` sector mesh + two emit modes, `FarMeshBuild.cs` far node + hole mask + skirt + water cap, `PlanetView.cs` streamer glue, budgeted upload, colliders, rebase, `Resources/Planet/PlanetTerrain.shader` vertex colour + lambert/SH + fog, `Kn5TestRunner.PlanetShots.cs` v4/v5), `Streaming/Kn5TestRunner.Planet.cs` (runs).
- Content: `PlanetStreamer` with an own copy of `AnchorContent` (anchor sectors, seed 1) as in `AnchorDrive.cs`; height function copied from the Experiment. Six `WhatIf.Planet*` assemblies are in both sync scripts.
- Floating origin: positions are double on the CPU (`PlanetFrame`), float only relative to the origin; `Rebase` every 1000 m moves origin, sectors, far nodes and the sea cap (the cap is rebuilt around the new origin).
- Emit modes: `own` (sector frame) and `shared` (shared axes + 2^-10 m grid, `what-if-planet-tile-curvature.md` section 4).
- Colour: by height / forest / "carved ground" flag (|contribution| > 0.05 m). Roads are NOT distinguishable (the flag paints whole settlements grey), far layer morph is not applied (static LOD, pops expected). Both are follow-ups, not part of v0.

## Runner
`tools/whatif-viewer/run-viewer.ps1 -Row fly|drive|seams|shots|manual [-Preset max|low] [-Spawn p1|p1c|p2|p2e|p3|p3k|fc] [-Emit own|shared] [-Bake main|thread] [-Empty] [-Duration s] [-Stamp x]`. Presets `tools/whatif-viewer/presets/planet-max.json` / `planet-low.json` (no `worldgen-Low` file exists in the repo; `planet-low` = s1-low without aniso/texture keys, `planet-max` = the `worldgen-max` copy). Run `*>` redirects from the PowerShell tool, not Git Bash.

Spawns (seed 1): p1 city 8 km from block A centre, sea 4 km at heading 158 (fly/drive bots use heading 45); p2 = cube vertex (1,1,1) (block B centre lies on an edge with no land vertex within 1350 km; this vertex is forest land); p2e = exact lattice edge in block B; p3 = 1000 m / p3k = 5000 m over p2; fc = face centre (open ocean).

## Rows (planet-low = 1080p exclusive, 1 MSAA; planet-max = strongest lab preset)
Reports: `full1/` (300 s rows incl. `-Empty`), `v6-10km/`, `variants/` (120 s), `final-seams/`, `final-shots/`, `step-b2/` (v1 split).

| row | result | verdict |
|---|---|---|
| v1 sector integration, steady state (510 sectors, fly, 300 s) | low median 0.085 / P95 0.165 / max 1.28 ms; max median 0.273 / P95 0.420 / max 2.20 ms | PASS (<= 1.0 ms P95) |
| v1 sector integration, cold fill only (26 sectors, shots runs) | P95 1.30 ms (split: mesh upload P95 0.45, place 0.03, SetActive 0.07, rest = first-use object creation of the pool) | FAIL on that sample, and cold fill is not the steady state. Moving bounds to the worker thread gave 1.56 -> 1.30 ms; not iterated further |
| v1 far node build+upload (2026 nodes, fly) | low median 0.124 / P95 0.168 / max 2.2 ms; max 0.207 / 0.378 / 3.4 ms | report |
| v1 collider assign on the main thread (drive, 92-101 assigns) | low median 2.6 / P95 3.3 / max 3.6 ms; max 3.7 / 4.6 / 4.8 ms. With `-Bake thread` (120 s, low): median 0.027 / P95 0.031 / max 0.090 ms, frame P99 3.18 vs 2.79 ms (not worse) | main-thread assign exceeds the 1 ms sector budget; thread bake removes it |
| v2 fly 300 s, 120 m/s, 50 m, empty baseline subtracted | low P50 1.93 / P95 2.26 / P99 2.48 / max 6.17 ms vs empty P95 1.41 -> +0.85 ms; max P50 5.09 / P95 5.53 / P99 5.92 / max 10.4 ms vs empty P95 2.30 -> +3.23 ms | PASS (<= 6 ms P95) |
| v2 car drive 300 s at P1 | low P95 2.56 / P99 2.80 / max 65.8 ms; max P95 4.49 / P99 4.85 / max 37.0 ms | total frame time only: the empty drive baseline is INVALID (no ground, the car falls and covers 266 / 416 km), so no planet-layer subtraction for the drive. The fly rows are the subtraction rows |
| v3 memory (fly, OS peak commit vs empty) | low 549.5 vs 486.2 = +63 MiB; max 692.7 vs 648.5 = +44 MiB (managed 84-91 MiB end). GPU shared: low 71 vs 86 MiB, max 205 vs 194 MiB (+11) | PASS (<= 150 MiB RAM, <= 256 MiB shared GPU) |
| v3 draws / triangles | avg 271, max 294 draws; max 329k triangles (fly); 249 / 267 and 315k (drive) | report |
| v4 e9 seam dropouts | 0 magenta px on 36 of 36 captures (3 sites fc / p2 / p2e x 5, 120, 600 m x own + shared) in both presets | PASS. Own and shared emit give the same counts: no GPU evidence that the shared grid is needed at 5-600 m |
| v5 far layer shots | p1 street/air, p2 ground/air, p3 1.2 m / 50 m: 0 holes in both presets; p3-1000 m: 1 px; p3-5000 m: 2868 px magenta below the horizon (nodes 571, no near sectors) | FAIL at p3-1000 m (1 px) and p3-5000 m. Cause NOT verified (cap radius ~400 km covers the 252 km horizon at 5 km; suspected far-layer gaps or the sea edge at the horizon) |
| v6 floating-origin shift, 10.78 km drive at P1 (340 s, bot 30 m/s) | 10 rebases; cost low median 0.84 / P95 2.14 / max 2.14 ms, max 1.12 / 4.16 / 4.16 ms; carried position error 9e-7 m; 0 resets. Fly 37 rebases: low P95 1.03 ms, max 1.95 (max 5.0 ms) | report; visible jump: judged in the ride |
| `-Emit shared` (fly 120 s, low) | rebase median 7.9 / P95 10.8 ms (236 sectors re-emitted per rebase) vs own 0.8 / 0.9 ms; frame P95 2.18 vs 2.31 ms | shared costs a ~10 ms hitch on every rebase; only a candidate if seams appear |

Hitches in the 300 s drive rows (frame max 65.8 / 37 ms, low / max) happened in the 300 s runs only; the 340 s repeat had max 6.87 / 13.96 ms. Not explained; the P99 is within 0.4 ms of P95 either way.

## Defects found and fixed in this ticket
- The sea cap was built once around the first spawn: open ocean elsewhere showed the clear colour (v4 fc 806k px) and, at the cube vertex on the ground, about half the screen (935k px at p2-ground / p3-1.2 m). It was NOT a library bug (`NearSet` / `Far.Select` at a three-sector vertex): after rebuilding the cap around the origin on every rebase the cube-vertex captures are 0 px (the 197 px at p2 600 m are gone too).

## Out of scope / follow-ups
- (done in v1) road surface id; remaining: far-layer morph, agent port for planet.
- Washed-out colours (lambert + SH ambient too bright).
- Collider bake on a thread should become the default if the viewer goes on (main-thread assign 2.5-3.7 ms).
- Ride judgement (looks like a planet? seams? stutter? horizon?): queued manual test `agent_pool/shared/manual-tests/RG-179.json` (P1 drive, P2 drive, P3 fly).

## v1 (RG-183)
- Library: `AdaptedSector.Surface` (0 ground, 1 corridor road, 2 settlement street, 3 shoulder) filled in `AnchorSector.Build` (corridor `bestEff`, settlement streets via a 32 m cell grid over the settlement world's ground edges). Heights / hashes bit-identical (test pins the corridor-sector hash `5EB2BE55025B8900`); sector build time unchanged (settlement sector median 4.55 ms vs 4.5-4.65 ms before, corridor 3.2 vs 3.4 ms).
- Viewer: road / street / shoulder colours; slope tint (rock) and latitude snow line; flat ambient instead of SH; sea is part of the far mesh (vertices below sea level drawn at sea level), the near water cap only below 1500 m (R - 0.4 m); camera near/far and fog follow the altitude (`AdaptCamera`); thread collider bake is the default; spike log (`planet-<series>-spikes.txt`, frames > 16.7 ms with the streamer events of the previous Tick).
- Runs: `run-viewer.ps1 -Row orbit -Spawn orbit` (120 s log-altitude descent 20 000 km -> 50 m + hold), `-Row oshots` (o1 orbit shots), `-Row roads` (r1 road shots incl. a routed corridor).
- Trap: the v0 magenta count compared against a straight horizon row; the horizon is a circle on screen, so v0 "holes" at 1 / 5 km were the counting artefact. Now a ray vs sea-sphere test.
- Open: road mask texture (street vertices at 4.7 m are jagged), far-layer morph, forest density on far nodes, atmosphere haze.
