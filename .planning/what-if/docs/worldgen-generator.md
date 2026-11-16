# World generator: road graph on flat terrain (RG-110, W3)

Status: implemented and run for 3 seeds (2026-10-29); W3.1 street growth replaced the square grid (RG-116, 2026-11-02). Plan: `world-generator-plan.md` §2.4/§3/§7, rules: `worldgen-rules.md`, schema: `worldgen-dataset.md`. Evidence tags: (M) measured in this ticket, (O) design decision.

## What it is

`src/WhatIf.WorldGen` (netstandard2.1, no references, no Unity types, world metres in `double`, x east, z north, y up) turns **spec + seed + `world-rules.json`** into a road graph and exports it as dataset **schema v1**. Flat terrain: terrain grade is 0 and there are **no tunnels** (they need terrain, W5); the tunnel validator passes vacuously and is unit-tested on synthetic input. The only non-zero grade is the motorway ramping up to a bridge deck (3.75 % maximum, smoothstep, 200 m each side; class maximum is 6.32 %).

## Command

```
dotnet run --project tools/worldgen/cli -- --spec tools/worldgen/specs/default-10km.json --rules artifacts/worldgen/world-rules.json --out artifacts/worldgen/gen-1 --seed 1 --source-provenance artifacts/worldgen/osm-ustinad/provenance.json
python -X utf8 -I tools/worldgen/validate_dataset.py --dir artifacts/worldgen/gen-1
python -X utf8 tools/worldgen/render_net.py --dir artifacts/worldgen/gen-1      # PNG for agents (Pillow: no -I)
dotnet run --project tests/WhatIf.WorldGen.Tests -c Release                  # ALL PASSED, in scripts/test.ps1
```

CLI exit 0 = generated and every validator passed, 1 = a validator failed or a settlement is unconnected, 2 = usage/input error. `--seed` overrides the spec. `--source-provenance` (O): `validate_dataset.py` requires `osm`, `dem` and `overpass-tile`/`dem-tile` sources in `provenance.json`; the rules are ODbL-derived, so the CLI copies those blocks from the dataset the rules were fitted from. Without it the export is honest but `validate_dataset.py` fails on missing sources.

## Model

- **Spec** (JSON): `seed`, `size_km`, `city.{x,z,radius_m}`, `villages`, `village_spacing_km.{min,max}`, `motorway.{entry,exit}_{x,z}` on the border, `allow_provisional` (names like `motorway.junctions.spacing_m`). A provisional rule (n < 30) is used only when listed; otherwise a design constant replaces it (spacing floor 30 m, fork minimum 15°).
- **RNG:** SplitMix64, one stream per stage and per routing attempt (`Rng.ForStage(seed, name)`, FNV-1a of the name). `System.Random` is never used.
- **Nodes / edges / spans** (`Graph.cs`): edge centreline every ≈10 m (`step_m` = length / round(length / 10)), analytic curvature per sample, height per sample, layer, span. Node kinds `end`, `joint`, `junction`, `interchange`, `portal`.
- **Centreline** (`Alignment.cs`): waypoint polyline → line – clothoid – arc – clothoid. Clothoid by numeric Fresnel integral (Simpson, ≤ 0.25 m panels); tangent length from the exact end point. Spiral length 1.5 s of design speed; arc ≥ 3 s (radius is raised, never the arc shortened); radius ≥ class minimum; the first and last 30 m are straight. If tangents do not fit a segment, the radius is lowered, then the spiral halved, then a non-fixed waypoint is dropped; fixed waypoints (stubs, interchange) make the build fail and the caller retries with another attempt stream.
- **Run lengths:** per bend the straight run, the curve run and the radius are drawn from the class distributions of `world-rules.json` (log-normal through p05/p50/p95), floored by the geometry (3 s arc, tangent fit). The fitted motorway runs (p50 70 m straight, 20 m curve) are biased short by junction splits (RG-109), so the floors decide there.

## Stages (`Generator.cs`)

1. **Settlements:** the city from the spec, villages by dart throwing (≥ 3 km apart and from the city, ≥ 1.5 km from the motorway corridor, 900 m from the border). Fewer than requested are logged.
2. **Motorway:** corridor E → S0, straight S0–S1 (1.6 km, fixed), S1 → X. S0/S1 sit symmetric around the point P nearest to the city, 2 km outside the city radius. Random-walk waypoints with steering to the target; rejected if outside the world, self-crossing or near a settlement.
3. **Streets (local class), W3.1 (RG-116):** one seeded street-growth routine for the city (3–5 arterial seeds) and the villages (a main street through the centre); see the section "W3.1: street growth (RG-116)". Until RG-116 this stage was a rotated square grid (city) and a main street with 1–3 crossing stubs (village).
4. **Interchange:** a primary **spine** from the city streets to P and 390 m beyond (dead end), perpendicular to the motorway; the motorway passes over it on a bridge. Four `link` ramps (diamond): gore nodes on the motorway at ±380 m, ramps G → B → R ending as T junctions on the spine (90°, last 30 m straight), terminals at 90 m and 140 m from P.
5. **Primary:** Prim MST over the city and the nearer ⌈3/4⌉ of the villages, plus 1–2 loop edges (shortest non-tree pairs). **Secondary:** every remaining village to the nearest connected settlement.
6. **Routing of one road:** attach point on the street set of each settlement (an end of a street = continuation, else the side of a street = T), a stub along the street normal (2.5 × class minimum radius, 60–300 m), random-walk between the stubs, alignment build, crossing evaluation, up to 160 attempts. Crossing rules: lower roads cross each other only at an **X junction ≥ 70°** with junction spacing ≥ class floor on both roads; a crossing of the **motorway** (≥ 30°, ≥ 300 m from its ends, outside ±700 m of the interchange) is never a junction but a bridge; link roads may not be crossed.
7. **Assembly:** crossings of the motorway become bridge spans (length max(80, drawn) ≤ 160 m, merged when closer than 440 m), deck 5 m above the crossed road, ramps with smoothstep; nodes get their kind from degree, spans and flags; edges are cut at every node.

## Validators (`Validators.cs`; plan §3 points 1–3 with the RG-109 thresholds)

radius ≥ class minimum; curvature jump at degree-2 joins ≤ 0.002 1/m; grade ≤ class p99; one connected component; leg angle ≥ 70° at generated T/X nodes (fork nodes ≥ the motorway p05 when allowed, else 15°); no same-layer crossing of two edges without a shared node (spatial hash on the 10 m segments), different layers need the deck ≥ 5 m above; junction spacing along a class chain ≥ class floor; bridges ≥ 60 m (motorway, link; other classes the class p05) with clearance ≥ 5 m; motorway tunnels ≥ 150 m. Unit tests craft one bad graph per check and a good control.

## Results (M, notebook i3-1215U, headless, Release)

`default-10km.json` + the real `world-rules.json` (sha256 `24c9a9c6…cc723`), 3 seeds, **values after RG-116 (W3.1; before it seed 1 was `4a4c69b0…a19a48`, 669 nodes)**, all validators 0 findings, `validate_dataset.py` OK, same seed twice → same `hash.txt`:

| seed | nodes | edges | bridges | generation (s) | hash.txt (sha256) |
|---|---|---|---|---|---|
| 1 | 944 | 1187 | 2 | 0.25 | `5224865529e7d1204edbf6c6ef1683ed7264e16877f7da2d64e4c35474fb5269` |
| 2 | 958 | 1201 | 3 | 0.36 | `ce7e429383e0b8c50688cf0d2a7af94e63846c281b8aed20a25c685156aaa365` |
| 3 | 995 | 1266 | 4 | 0.26 | `94970d45f216704815e64eea3cb3a0ce427f18f887361f0b3b5cd863fbd3cf4f` |

Generated vs fitted (3 seeds pooled, **measured before RG-116**: the `local` row is superseded by the W3.1 table below; report only, no threshold; `dataset_stats.py` method):

| class | km | min radius rule (m) | generated curve radius p05 / p50 (n) | rule p05 / p50 | gen max grade % (rule p99) | gen junction spacing p05 / p50 (n) | rule p05 / p50 |
|---|---|---|---|---|---|---|---|
| motorway | 40.0 | 345 | 344.8 / 574.6 (1465) | 345 / 714 | 3.74 (6.32) | 760 / 760 (3) | 40 / 410 |
| primary | 73.6 | 121 | 121.3 / 366.0 (2811) | 121 / 439 | 0 (10.82) | 50 / 180 (15) | 32.5 / 168 |
| secondary | 13.8 | 70 | 69.6 / 232.7 (666) | 70 / 280 | 0 (12.95) | no junction pair (0) | 34 / 162 |
| local | 343.2 | 36 | no curves (0) | 36 / 214 | 0 (16.47) | 86.9 / 107.8 (2888) | 32 / 100 |
| link | 4.8 | 41 | 155.9 / 230.8 (110) | 41 / 153 | 0 (9.21) | 394 / 403 (12) | provisional |

Reading: curve radii follow the fitted lower tail (p05 sits on the class minimum because every draw below it is clamped; the generated p50 is 10–20 % below the rule p50, cause not analysed). Junction counts on motorway/primary/secondary are far below the OSM densities (3 to 15 pairs); the city grid is much more regular than real streets (local spacing p05 87 m against 32 m).

## Limits and known gaps (O)

- No terrain: no tunnels, no earthwork, no gradients except bridge ramps; roads are never routed around obstacles (there are none).
- The interchange is in the same place for every seed (it depends only on the spec's corridor and the city). Seeds change the walks, villages and attachments.
- Local streets are straight segments of 60–140 m (heading drift ±8° between them, so corners instead of curves); villages are one main street, a side street or two and a few stubs (the budget is the old village length, 550–900 m).
- Primary and secondary roads meander more than real roads (random walk with 45° steering limit); loops with the minimum radius occur (seed 1, first render).
- No roundabouts, no traffic direction (`oneway` is false on all edges, including ramps), width/lanes are class defaults (motorway 4 lanes / 23 m, primary 7 m, secondary 6.5 m, local 6 m, link 5.5 m).
- Exported provenance carries no date (deterministic export) and, with `--source-provenance`, the OSM/DEM licence blocks of the source dataset. The generated dataset itself contains no OSM geometry; the rules do come from ODbL data (plan §5a (c)).

## W3.1: street growth (RG-116)

Why: the user drove the first generated world (RG-115) on 2026-11-02 and found only a square grid. Seed 1 had 528 X and 7 T junctions; OSM `ustinad` local junctions are 89.8 % T. W3.1 fixes **topology only**; terrain and the primary/secondary routing are untouched.

Algorithm (`Generator.BuildStreets`, `ProbeSegment`, `ApplySegment`; one routine for city and villages, after Parish & Müller 2001, simplified):

1. Seeds from the centre node: city 3–5 arterial headings at least 72° apart (LegAngleMinDeg 70 + jitter; the brief said 60°, but the validator needs 70° at the junction they meet in); village 2 headings 180° apart toward the nearest other settlement (the road that will reach it).
2. A priority queue of proposals ordered by (generation, distance from centre, id). Continue = generation + 1, side branch = generation + 2.
3. Pop a proposal: length uniform 60–140 m along its heading; the end point b is checked in this order (`ProbeSegment`):
   - (a) the segment crosses an existing street → cut at the crossing, make a node there (`NodeOn`: the crossed street gets a T), stop;
   - a node lies within 25 m of the path before the crossing (a street may not graze a node) → end on that node;
   - (b) b within 25 m of a node → snap to it, stop;
   - (c) b within 20 m of a street away from its nodes → extend onto it as a T, stop;
   - (d) reject: shorter than 40 m, outside the settlement radius (±15 % noise: two sines of the bearing, seeded), a leg angle < 70° at either end, the node pair already joined, a snap/T whose segment crosses another street, a T closer than the class spacing floor (15 m) to another node of that street, a crossing of a non-local road.
4. An accepted segment is a straight local `Road` (`AddRoad`, `NodeOn`: the same junction mechanism as the later stages). A free end proposes continue (p 0.95, heading drift ±8°) and a side branch (p 0.8; 90° ± 15°; both sides with p 0.22). A segment that ends on a node or street stops growing.
5. Stop when the street length reaches the budget (grid-equivalent: city 2πR²/112.5 m = 109.5 km, logged with the real length; village 550–900 m) or the queue is empty.
6. Primary/secondary roads attach and cross through the existing `Candidates`/`Evaluate`/`Commit` code. The only changes there: a degree counter instead of a scan (`_cutCount`), and a relative parallel test in `Geo.SegCross` (a head-on join of two collinear roads at a shared node was read as a crossing by the validator).

Parameters (constants in `Generator.cs`): `GrowLenMin/Max` 60/140 m, `GrowDriftDeg` 8, `BranchDevDeg` 15, `SnapNodeM` 25, `SnapStreetM` 20, `MinSegM` 40, `RadiusNoise` 0.15, `PContinue` 0.95, `PBranch` 0.8, `PBranchBoth` 0.22, `GridEquivSpacingM` 112.5. The branch probabilities were tuned once on seed 1 (0.92 / 0.5 / 0.18 filled only 72 of 109.5 km); seeds 2 and 3 were not used for tuning.

Metrics (M; `tools/worldgen/street_stats.py`, the same code for the generated export and the OSM window; local class; T share = degree-3 nodes among nodes of degree ≥ 3; spacing = local chains between nodes of degree ≥ 3; φ = Boeing 2019 orientation order, 36 bins × 10°, length-weighted, both directions; city = local edges with midpoint within 1700 m of the city centre):

| | old seed 1 / 2 / 3 | new seed 1 / 2 / 3 | OSM ustinad |
|---|---|---|---|
| local T share (all local nodes) | 0.6 % / 1.2 % / 0.7 % | 77.6 % / 77.4 % / 76.4 % | 89.8 % (window), 78.6 % (3 × 3 km centre) |
| local junction spacing p50 (m) | 107.8 / 105.3 / 117.4 | 92.2 / 93.0 / 89.4 | 90.7 (this method), 88.1 (`stats.json`); centre 96.1 |
| city φ | 1.000 / 1.000 / 1.000 | 0.041 / 0.039 / 0.072 | local φ whole window 0.005; 3 × 3 km around the Ústí centre (\|x\|,\|z\| ≤ 1500 m of the window origin 50.66 N 14.04 E) 0.067 |
| local dead-end nodes (share) | 17.1 % / 16.2 % / 18.0 % | 15.3 % / 16.4 % / 14.2 % | 15.4 % (centre 12.4 %) |
| city street length (km) | 113.0 / 117.6 / 103.7 | 97.2 / 98.9 / 100.8 (budget 109.5) | not comparable |
| generation (s), `dotnet run -c Release` | 0.11–0.13 | 0.25 / 0.36 / 0.26 (0.17 on a warm run) | |

Acceptance thresholds (T ≥ 70 %, spacing p50 60–130 m, city φ ≤ 0.35) are met for seeds 1–3. Growth stops at 89–93 % of the budget because the queue empties (rejections at the radius and at crossings), so the city has 8–11 % fewer street kilometres than the grid. OSM φ depends strongly on the window (whole Ústí window 0.005 = no preferred direction at 60 km, centre 0.067).

New hashes (`hash.txt`; command from the Command section with `--seed 1|2|3 --source-provenance artifacts/worldgen/osm-ustinad/provenance.json`; the same value in two runs): seed 1 `5224865529e7d1204edbf6c6ef1683ed7264e16877f7da2d64e4c35474fb5269`, seed 2 `ce7e429383e0b8c50688cf0d2a7af94e63846c281b8aed20a25c685156aaa365`, seed 3 `94970d45f216704815e64eea3cb3a0ce427f18f887361f0b3b5cd863fbd3cf4f`. Before RG-116 seed 1 was `4a4c69b0…a19a48`.

Commands: `python -X utf8 -I tools/worldgen/street_stats.py --dir artifacts/worldgen/gen-1 --label gen-1 --city 0,0,1700` and `... --dir artifacts/worldgen/osm-ustinad --label osm --centre-window 1500` (writes `street-stats.json` next to the export); zoom views `render_net.py --dir ... --centre=X,Z --span-m 3000`.

Known limits: a primary–primary junction at 69.5° (seed 9, node 1015, loop routing, not a street) fails the leg-angle validator, and seed 9 generates in 0.97 s (old code 0.18 s); both are outside the three acceptance seeds, see the RG-116 outbox. Arterials are straight with kinks of up to 8° at degree-2 nodes; the village is small by design.

## W4: Unity adapter (RG-115)

The generated road graph drives in the NightRacer KN5 test player as a third stream source next to `kn5` and `long`; no new player. Source: `engine-unity/NightRacer/Assets/Scripts/Streaming/WorldGenSectorSource.cs` (`WorldGenMap` = pure data, `WorldGenSectorSource : ISectorSource`). `WhatIf.WorldGen.dll` reaches Assets/Plugins through `tools/game-unity/sync-sdk.ps1` (fixed GUID in `WhatIf.WorldGen.dll.meta`; the DLL is never committed).

Command (needs the git-ignored `artifacts/worldgen/world-rules.json`, rebuild: `osm_extract.py all` + `fit_rules.py`, sha256 `24c9a9c6…cc723`):

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/kn5-unity/run-kn5.ps1 -Mode stream -WorldGen -Series wg-s1 -Warmup 10 -Duration 900 -TimeoutS 1500 -PlayerExtra "--stream-run drive --worldgen-seed 1"
```

Parameters: `--worldgen-spec <json>` (default `tools/worldgen/specs/default-10km.json`), `--worldgen-rules <json>` (default `artifacts/worldgen/world-rules.json`, refused with a message when missing), `--worldgen-seed N` (overrides the spec), `--worldgen-route village|bridge`, `--stream-run drive|fly|manual|shots|none`. `shots` writes 8 PNGs (T / X junction, interchange node, bridge on the deck / from below / from the side, city street, primary curve) to `<report>/shots-<series>/`.

Mechanism: 256 m sectors; an edge sample belongs to the sector of its position, a junction patch to the sector of its node; sectors exist for road cells plus a ring of one cell. Ribbon at ±width/2, height H + 0.05 m, 1.5 m shoulders (outer edge 2 cm above the ground), UV v = metres along the edge; bridge spans get a closed 0.6 m underside box instead of shoulders. Each ribbon at a node of degree >= 3 is cut back by half the widest incident width + 2 m; the patch is the convex hull of the cut-end corners fanned around the node. Ground = one flat quad per sector (grass collider), road = one mesh per sector (asphalt collider). Route: Dijkstra from the node nearest to the city centre; `village` = farthest non-boundary node, `bridge` = farthest boundary node whose path crosses a bridge span; densified every 2.5 m.

Deviations from the brief: the patch corners keep the heights of their own ribbons (centre = node height + 0.05 m) instead of one flat polygon, to avoid steps on ramps; the ground is one quad, not a grid (flat terrain); `Kn5_Opaque` is the base material, as in `LongMapSource`.

Results (i3-1215U / UHD, 1280x720 Low, PathBot drive at 14 m/s, all runs valid):

| run | route | length (m) | junctions (T / X+) | bridge | NoCollider / Fell / UnloadWhileContact / NotRoad | P50 / P95 / P99 (ms) | frames > 33 ms (with sector work) | build ms p95 / max | peak private MiB | road mesh hash |
|---|---|---|---|---|---|---|---|---|---|---|
| seed 1 | village | 6542 | 18 (0 / 18) | no | 0 / 0 / 0 / 0 | 1.55 / 2.11 / 2.53 | 1 (0) | 1.9 / 14.3 | 476 | `6767eba612238c7e` |
| seed 1 again | village | 6542 | 18 | no | 0 / 0 / 0 / 0 | 1.66 / 2.28 / 2.72 | 0 | 2.9 / 33.8 | 463 | `6767eba612238c7e` (identical) |
| seed 1 | bridge | 10830 | 17 (1 / 16) | 1 edge, 80 m | 0 / 0 / 0 / 0 | 0.91 / 1.82 / 2.13 | 1 (0; one frame 550 ms) | 2.0 / 24.8 | 555 | `f09f2480de73df1f` |
| seed 2 | village | 9882 | 23 (2 / 21) | no | 0 / 0 / 0 / 0 | 1.32 / 1.74 / 2.03 | 1 (0; one frame 534 ms) | 1.8 / 16.8 | 522 | `a7e38f4792f3e8b1` |
| seed 3 | village | 7157 | 16 (1 / 15) | no | 0 / 0 / 0 / 0 | 1.44 / 1.81 / 2.09 | 0 | 1.7 / 16.7 | 492 | `5ae626b3e5f29a7d` |

NotRoad share 0 % of 24 546 / 38 810 / 37 480 / 26 410 steps; every drive reached the route end; no step below 3 wheels. Generation 0.2 s (graph) + 0.03 s (index), 697 sectors for seed 1, time to drivable 1.7–2.3 s. In view (world meshes only): at most 30–32 draw calls and 10.6–12.0 k triangles. Frame times are small because the scene is untextured flat colour at 720p; they are not a statement about the later textured world. The three isolated frames above 33 ms (up to 550 ms) had no sector work in the same frame; cause not analysed.

Limits seen in the shots: gore nodes of the interchange give a spiky hull (sliver legs); the 1.5 m shoulders stop at the cut-back, so small green notches show at junction corners; bridge decks have no piers and open box ends; ramp shoulders are near-vertical walls (up to 5 m high in 1.5 m); the route turns sharply at node centres. The village route has 0 T junctions and no bridge, hence the `bridge` route. W3 findings visible here (dead-end stub, motorway hook, meandering primaries, one regular city grid) are untouched.
## W5a: terrain (RG-118)

Procedural relief for the generated world, headless. The road is the authority; the drawn terrain follows it. Unity does not use it yet (RG-119); with the default spec everything is flat as before.

**Model** (`Terrain.cs`, `GeneratorTerrain.cs`):
- Natural height `N(x,z)`: own seeded fBm value noise in double math (stage RNG `terrain`, no `System.Random`), scaled by an amplitude field: hills (`relief_m`), one ridge band (`mountain`, smooth profile to 0 at ± width/2) and a flat basin around each settlement (r + `settlement_flat_margin_m`). Pure function of seed and spec; nothing is baked.
- Road vertical profile, a stage before the edges are cut at nodes. Order motorway, primary, link, secondary, local (link after primary, otherwise pinned ramp ends push the spine over the bridge deck). Target = `N` every 10 m, moving-average window per class (motorway 400, primary 200, secondary 120, local 60 m), grade clamped to 0.9 × class `MaxGradePct` by forward/backward passes, ≤ 3 % within 15 m of a junction. Nodes fixed by an earlier road stay pinned. A road under a motorway bridge stays ≤ deck − 5.6 m. Inside a settlement basin the road stays within N − 4 … N + 3 m (no tunnel or viaduct).
- Spans from `d = y − N`. Tunnel where `d < −12 m` (motorway, link, primary, secondary). A run shorter than the class minimum (motorway 150 m; others max(30, `TunnelLength` p05), else 60 m) is refit with a floor of N − 11.5 m. `CoverDepthM` = min(N − y − 7). Viaduct (bridge span, layer 0) where `d > +6 m` for ≥ 30 m, extended to the class bridge minimum; `ClearanceM` = min(y − N). A motorway crossing inside a tunnel stays a tunnel. Span ends become `Portal` nodes.
- Carved terrain `T(x,z)`: within half width + 1.5 m of the nearest ground centreline `T` = road y; beyond that cut/fill at 1 : 1.5 back to `N`. Bridges and viaducts leave `N` below; tunnels leave `N` above. Hole mask = tunnel footprint + 1 m each side within 8 m of each portal.

**Spec** `terrain` block (all optional, absent or `relief_m` 0 and no mountain = flat): `relief_m`, `base_wavelength_m`, `octaves`, `mountain {x0,z0,x1,z1,width_m,relief_m}`, `settlement_flat_margin_m`. `specs/terrain-10km.json` = `default-10km.json` + relief 60 m, wavelength 1400 m, 5 octaves, ridge band x ≈ −3300 … −2700 across the whole map (width 2400 m, relief 350 m), margin 300 m. Tuned once on seed 1. `N` spans about −19 … +332 m.

**API for RG-119** (additive, `RoadGraph`/`GEdge` unchanged): `GenResult.Terrain` (`TerrainModel`) with `Natural(x,z)`, `Height(x,z)` (carved), `Hole(x,z)`, `SampleTile(x0, z0, sizeM, stepM)` → `TerrainTile` with `float[]` heights and `bool[]` holes on an (n+1)² grid; neighbouring tiles share bit-identical borders. With relief off `Height` is 0 (ground level also under the flat-world bridge ramps).

**Validators** (new, terrain spec only): ground sample cut ≤ 12 m and fill ≤ 6 m; tunnel N − y ≥ 7.5 m at every sample; no tunnel/viaduct on `local`; the existing grade ≤ class p99.

**CLI**: `--terrain-stats` (`terrain-stats.json`: km per class in cut/fill/at grade/tunnel/viaduct, max cut/fill, spans), `--tile-bench` (adds `tile_bench`), `--terrain-dump <file> [--window x0,z0,size,step]` (raw heights for `render_net.py --terrain-dump`, hillshade with tunnels and viaducts). `terrain_stats.py --rules <world-rules.json> <out-dir>...` prints grade p50/p95 per class against the rule and Sierra.

**Results** (i3-1215U, headless, Release, 2026-11-02):
- Flat `default-10km.json` hashes unchanged: seed 1 `5224865529e7…`, seed 2 `ce7e429383e0…`, seed 3 `94970d45f216…`.
- Terrain spec hashes: seed 1 `d02133382f85…`, seed 2 `0f4337f32458…`, seed 3 `3a77459b3807…`.
- Seeds 1–12, both specs: rc 0, 0 findings; every terrain seed has ≥ 1 tunnel and ≥ 1 viaduct. Generation 0.18–0.21 s (seed 9 ~1.0–1.2 s on both specs).
- Seeds 1–3, terrain: tunnels 2–4 (0.74–1.58 km), viaducts 1–2 (0.17–1.90 km), max cut 10.1–11.0 m, max fill 5.5–5.9 m.
- Grade p50/p95 (seeds 1–3 pooled): motorway 1.8/5.7 (rule 1.7/6.3), primary 0.7/9.7 (1.9/10.8), secondary 1.8/11.7 (2.5/12.9), local 0.1/0.7 (2.8/16.5); all roads 0.2/4.2 against Sierra 4.6/12.4 %.
- `SampleTile` 256 m @ 4 m, 1521 tiles of seed 1: median 0.37–0.77 ms, max 7–11.5 ms (two runs).

**Limits**:
- Settlements sit in flat basins, so local streets are almost flat (p50 0.1 %) and the world overall is much flatter than Sierra. Relief lives on motorway/primary/secondary between settlements.
- Tunnels and viaducts are long (often > 1 km) because the ridge band is steep and the grade clamp is strict; there are few short ones.
- Viaduct `ClearanceM` is the minimum over the extended span, so it is small at the span ends (2–3 m); no pier/clearance rule for viaducts yet.
- Seed 9 drops one optional primary loop (village2–village4) on both specs and takes ~1.1 s (failed route attempts that existed before RG-118).
- Seed 9 fix: the leg-angle check is also measured on the 15/20 m chords (+0.3° margin), and an unreachable village pair links to another settlement. This did not change seeds 1–3.
- No DEM; the Copernicus licence annex is still open.

### Vertical curves (RG-123, W5c)

Cause of the RG-119 ride-3 "retardéry": `ProfileRoad` produced a piecewise-linear profile (grade-limited, no vertical-curve step) and `Validators.Grade` checked only |dh/ds|. The motorway ridge crossing had +5.69 % into −5.69 % at one 10 m station (vertical radius ≈ 88 m, lift-off √(g·R) ≈ 106 km/h) on terrain seeds 1, 2, 3; the sag at the viaduct foot had R ≈ 300–380 m.

**Design decision (orchestrator, RG-123; design values, not fitted data)** — minimum vertical radius, metres, crest / sag (`WorldRules.VRadiusMin`): motorway 6000 / 3000, link 1000 / 600, primary 2500 / 1500, secondary 1200 / 800, local 400 / 300, track 200 / 150. Spec `terrain.vcurve_scale` (default 1.0) multiplies them in the generator.

**Curve pass** (`Generator.VerticalCurves`, in the refit loop right after the bounded `SolveProfile`, so tunnel/viaduct spans follow the rounded profile): curvature of every station `k = (g2 − g1) / ((a + b) / 2)` is limited to `−1/R_crest … +1/R_sag` (horizontal chord distances, as the validator measures). Alternating projections: (a) per violating station the Euclidean projection onto its curvature half-space over the free neighbours (relaxation 1.95, aimed 3 % inside the limit; a grade break spread symmetrically = a parabolic vertical curve; pinned stations never move); (b) `SolveProfile` again with the rounded profile as target (lo/hi, pins, grade cap). Stops when no station exceeds the limit by more than 1 %. A pass that finds no violation changes nothing. Cost: terrain generation 0.2 s → 0.5–4 s (the grade-capped descent chain shifts only diffusively; up to 30 000 outer rounds, worst case about 7 s).
- Samples still beyond 2 % of the limit after the last solve are counted and logged (`vertical-curve infeasible sample(s)`, with the first position per road) like `infeasible`.
- **Fallback, logged, never silent:** inside a settlement basin (fill 3 m / cut 4 m) the basin limit can contradict a sag at the grade cap. Within 600 m of such a station the basin bounds widen to fill 5.9 m / cut 11.5 m (still inside the 6 m / 12 m validator limits). Seed 1 uses it on two primaries (`primary-city-village3`, `primary-village3-village1`).

**Validator `vcurve`** (`Validators.VCurve`, run with the terrain checks): radius from three consecutive stations at every interior station and across every degree-2 tunnel/ground/viaduct boundary; a finding below 0.98 × the class minimum names edge, position, height and lift-off speed. Joints between two road ends (same span kind) are not checked: those roads are profiled separately. The validator always checks scale 1.0 (the CLI passes no spec scale), so a spec that lowers `vcurve_scale` gets findings by design. Negative tests: sharp motorway crest (R ≈ 88 m, lift-off ≈ 106 km/h), sharp sag, crest at a span joint, class dependence.

**Export** (additive, schema v1): `profiles.json` items gain `vradius_crest_min_m` and `vradius_sag_min_m` (smallest radius over the interior stations of the edge, null = no curved station) **only when the terrain is enabled**, so flat datasets stay byte-identical (`validate_dataset.py` accepts null or positive numbers). `terrain_stats.py` prints the min radius and lift-off per class. Note: the exported centreline heights are rounded to 1 mm, which alone moves a radius read back from `edges.json` by about 6 % at R = 6000 m; the `vradius_*` fields come from the unrounded heights.

**Results (2026-11-03, notebook, headless)** — terrain `terrain-10km.json` seeds 1–3, all validators 0 findings, 0 infeasible samples; flat `default-10km.json` seeds 1–3 hashes unchanged (`5224865529e7…`, `ce7e429383e0…`, `94970d45f216…`).
- New terrain hashes (old in brackets): seed 1 `cd72872edac2…` (`d02133382f85…`), seed 2 `ed2f84b61bb5…` (`0f4337f32458…`), seed 3 `522c54468f46…` (`3a77459b3807…`).
- Minimum vertical radius over seeds 1–3 (profiles.json) crest / sag, lowest lift-off from the crest: motorway 5941 / 2973 m, 869 km/h; link 993 / 594, 355; primary 2476 / 1487, 561; secondary 1189 / 795, 389; local 403 / 369, 226 km/h (all ≥ 0.98 × the class minimum). Before: motorway crest 88–89 m (106 km/h), primary 53 m (82 km/h), secondary 45 m (75 km/h).
- Spans before → after (tunnels / viaducts): seed 1 4 (4.73 km) / 2 (1.85 km) → 4 (4.74 km) / 3 (2.16 km); seed 2 2 / 2 → 2 / 2 (viaducts 2.86 → 3.01 km); seed 3 2 / 1 → 2 / 1 (unchanged counts).
- The ridge crossing crest is rounded over about 700 m and lowered 8–9 m; tunnel and viaduct stay (images `artifacts/worldgen/rg123-profile-seed{1,2,3}.png`, local only).
- **Limits (RG-123):** seeds 4, 5, 7, 9–12 terrain: 0 findings. Seed 6 (8 findings, primary at about x 1150, z −3000…−3180) and seed 8 (268 findings, motorway sag at x −2100, z −3230; the 680 contradicting bound samples exist before RG-123) keep samples below the minimum radius because the bounds contradict the limit there; they are counted and logged, not hidden. Work per curve call is capped at 6e8 station updates (seed 8 ran > 10 min without it, now 7.5 s). Two-edge joints of different road ends are not checked.

## Ramp pin to the motorway (RG-128 rework 1, `GeneratorTerrain.cs` `ProfileRoad`)

A link leaves the motorway at a node at about 27°, so its footprint stays inside the motorway footprint for 30–45 m. Each road was profiled on its own, so inside that overlap the two ribbons differed by up to 0.98 m (seed 1, edge 5 s 45; edge 7 s 311) and a tyre met the edge of the higher one (user: "retardér před a za viaduktem", existing since RG-118). Rule: for a non-motorway road with a cut at a node that lies on the motorway, every sample from the cut while its plan distance to the motorway centreline is below `½ motorway width + ½ own width + 2 m` is pinned to the motorway height at the nearest motorway point (`lo = hi`, grade of the neighbouring segments reset). Motorway-only; the crossing-under rule that follows is unchanged. Test: `terrain: a road leaving the motorway at a node has the motorway height inside the motorway footprint`. Terrain hashes change (seeds 1–3 `b341d3fc…`, `eef5c3a8…`, `84bcc228…`; before this change `1e6ee710…`, `c73e21aa…`, `d6c0a803…`); flat `default-10km.json` seed 1 unchanged (`43551fac…`, no relief). Measured with `tools/worldgen/roadfeel`: lips ≥ 20 cm 0 on seeds 1–3, ≥ 5 cm 46 / 30 / 37.

## Junction plateau for minor legs (RG-130, `GeneratorTerrain.cs` `PlateauLegs`, `BuildSamples`, `ProfileRoad`)

The adapter cuts every ribbon at a degree ≥ 3 node back by R = widest half width + 2 m and lays each leg's road rectangle over the patch with its node edge at node height. A leg already falling from the node (≈ 1.6 cm/m) was 5–7 cm below a sibling's rectangle where that rectangle ended (s 3–5): pattern A of RG-128, and pattern B is the same seen from the other end of a link. Rule: at a node of degree ≥ 3 without a motorway leg (those are left to the ramp pin above), the major pair = the two legs with the best class, ties broken by the pair closest to 180°; every other leg gets a station of its own at R + 1 m from the node (stations are ~10 m apart; a regular station closer than 1 m to it is dropped) and grade 0 on the segments up to it, so it holds node height to 1 m past the cut. The major legs keep their profile (`wiki/concept/geometry-patterns.md`: the minor road adapts, the major road keeps its grade). An edge shorter than its plateaus + 2 m gets none (log line `minor junction leg(s) with a level plateau, N skipped`; 0 skipped on seeds 1–3). Seed 1: 627 plateau legs. Mirror half in the adapter (`WorldGenSectorSource.cs` `StarPatch`): a level leg's node corners take the height of the sloped leg whose road rectangle holds them, otherwise the level rectangle stood above a major road falling away from the node (the major-leg half of pattern A, more than half of all lips). Tests: `terrain: a minor junction leg holds node height to the end of its plateau`, `terrain: the major pair of a junction keeps its profile`. Terrain hashes seeds 1–3 `c03c3dc7…`, `6bacfacd…`, `13f254be…` (before `b341d3fc…`, `eef5c3a8…`, `84bcc228…`): stations added and grades changed near junctions; flat seed 1 unchanged `43551fac…` (the flat path does not run `AssembleTerrain`). `roadfeel` lips ≥ 5 cm 46 / 30 / 37 → 6 / 10 / 5, ≥ 20 cm 0; sweep `step` clusters 15 / 5 / 11 → 0 / 0 / 0.
