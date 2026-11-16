# What If: planet — direction review, anchors instead of region worlds (proposal, 2026-11-15)

What If input (separate project); not a NightRacer ticket source. Status: **WI-ADR-002 Accepted 2026-11-15** (section 8; decided by the data of RG-177/RG-178 under the fixed rule, user: „vyzkoušet, změřit, určit postup na základě dat, ne odhadů.“). Sections 1-7 are the proposal as written before the data.

Trigger (user, 2026-11-15, verbatim): "Worldgen staví z dlaždic, ale to bude na planetě problém od 85. rovnoběžky a k blíž k polum se problém bude zvětšovat. Co použít silnice a souřadnice jako kotvy, systém dlaždic zahodit a dogenerovat svět?"

Labels: M(c) = read in code at `file:line`, M(r) = earlier reviewed measurement (review file named), O = own derivation.

## 1. Short answer

1. **There is no pole problem in the current design.** The planet does not use latitude/longitude tiles. It uses an equal-angle cube-sphere (M(c) `src/WhatIf.Planet/CubeSphere.cs:44-45`, `QuadSector.cs:37`); `src/WhatIf.*` contains no latitude, Mercator or pole code (grep 2026-11-15). A geographic pole is the **centre of a cube face = the least deformed place** (M(r) `what-if-planet-tile-curvature.md` §0). The worst cells are the 8 cube vertices (120 deg corners, area spread 1.41 : 1), and those are measured and closed: seams 0 mm (RG-162/167), chart metric error <= 1.6e-6 (RG-166/167). The "85th parallel" limit belongs to Web-Mercator tiles; this engine never had it.
2. **The user's intuition still points at the real weak spot**, just one level up: the *content* unit is the level-10 region (~10 km). Each region is an independent NightRacer WorldGen world (own city, own motorway edge to edge, own villages). Roads end at region borders; the borders are hidden by a 32 m taper. The world is a patchwork of 10 km islands, not a road network.
3. **Recommendation:** keep the cube-sphere tiles as *addressing, mesh, streaming, collision and LOD units* (they work and are cheap), but stop using any tile or region as a *content* unit. Content becomes **anchor-first**: global settlements + a global road network in double 3D coordinates are the anchors; terrain, carve, villages and scatter are "dogenerated" around them as a pure function of global position. This is the user's idea, with the tiles kept only where they cost nothing.

## 2. Current state (2026-11-15, after RG-176)

| layer | code | state | measured |
|---|---|---|---|
| sphere + lattice | `WhatIf.Planet` (`CubeSphere`, `QuadSector`, `Frame`) | WI-ADR-001 Accepted | seams 0 bits, frame transport ~1e-14 rad over pole and cube corner (RG-161/162) |
| surface | `WhatIf.Planet.Surface` (`PlanetSurface`) | pure function of direction: continents, ocean/sea/beach, lakes, forest | sector 1.26-1.59 ms (RG-168) |
| region content | `WhatIf.Planet.WorldGen/RegionWorld.cs` | one `Generator.Run` per level-10 region on its bounding square, gnomonic region chart, 32 m border taper, water mask, village-only fallback (RG-176) | region build median 0.24-0.30 s, max 2.3 s, 3.1 MiB (RG-175/176) |
| sector mesh | `SectorBuild` (65 x 65) | batch height, shorter diagonal | sweep median 1.13-1.39 ms, worst sector 2.5-4.7 ms (RG-170/169) |
| far layer | `WhatIf.Planet.FarLayer` (CDLOD) | hole + skirt, Range13 1950 | <= 480 nodes, 7.6 MiB (RG-171/174) |
| runtime | `WhatIf.Planet.Runtime` (`PlanetStreamer`) | region cache, background sector jobs | main thread P95 ~0.6 ms, 60-72 MiB, region lead >= 70 s (RG-172/173/175) |
| global roads | `WhatIf.Planet/Roads.cs` `CorridorGraph`, `Portal`, `RoadArc` | prototype: 49 152 settlements, 3-nearest great-circle arcs, **no land mask, not used by `RegionWorld`** (M(c) `Roads.cs:264-268`) | graph 0.04 s, 1.87 MB (RG-162) |
| Unity viewer | — | none yet (Unity licence was expired; renewed 2026-11-15) | — |

Everything runs CPU-only in Unity-free netstandard2.1 libraries on the i3-1215U. Nobody has *seen* the planet yet except as PNG maps.

## 3. Where the region model breaks (why change direction)

| symptom | cause | evidence |
|---|---|---|
| motorways and primaries end at region borders | each region runs its own `BuildMotorway` across its square (`RegionWorld.cs:73` "motorway runs edge to edge") | block A PNG, handover follow-up from RG-169 |
| no long-distance network, no route from city A to city B 50 km away | `CorridorGraph` exists but is never consulted; per-region settlements | M(c) above |
| region border = flat seam band | taper of all WorldGen terms to the planet base over 32 m (1.3-1.6 % of the area) | RG-166/167 |
| content generated outside the region and thrown away | generator domain = bounding square, region = quad / 120 deg rhombus; polygon domain (e5) never done | `RegionWorld.cs:21,72` |
| the next planned ticket only treats the symptom | "road portals across region borders" pins a few road ends; the network is still 10 km islands with one city each | handover `## Planet lane plan` |
| region = minimum work unit | a car at a region border needs 2-4 whole regions built (0.3-2.3 s each) | RG-172/175 |

The geometry (sphere, lattice, seams) is solved. The content model is what needs to change.

## 4. Proposal: anchor-first content

### Kept unchanged
- Cube-sphere lattice, level-15 sectors, level-10 regions **as cache keys only**, far layer, `PlanetSurface`, `PlanetStreamer` job model, float32 sector frames, budgets.
- NightRacer WorldGen algorithms: settlement street networks, `VerticalCurves` (with `VcStallOuter`), carve, scatter, `Motorway = false` path. NightRacer's own golden hashes stay untouched (new behaviour behind spec flags, as RG-165..176 did).

### Dropped
- `RegionWorld` as the owner of roads and settlements; the region-border taper; per-region motorway; the region-portal ticket.

### New layers (each a pure, deterministic function of the world seed + its own key)
| layer | content | key / storage | influence radius |
|---|---|---|---|
| A0 surface | height, water, forest, biome (exists) | direction | none (pure function) |
| A1 settlements | city / town / village nodes **on land**, class from surface (coast, plain, slope) | level-6 or level-8 cell id -> node list | settlement radius (city <= 2.5 km, village <= 450 m) |
| A2 corridors | road network between A1 nodes: motorway / primary / secondary; route over the surface (water and slope cost), polyline in double 3D, split into segments of ~2-5 km whose joints have **pinned heights** | edge id (min node, max node) -> segment list | road half width + carve run (<= ~65 m, O from `Terrain.cs:55`) |
| A3 settlement content | WorldGen streets/lots/houses inside one settlement, generated in a gnomonic chart at the **settlement centre**; entrances = the A2 corridor ends, pinned | settlement id | settlement radius |
| A4 sector | mesh = surface base + carve of every A2/A3 piece whose radius touches the sector + global-coordinate detail noise; scatter masked by roads/lots | sector id (cache only) | — |

Contract: **a sector is a pure function of (A0, the anchors whose influence cap intersects it)**. Build order, load order and neighbouring sectors do not matter -> seams are 0 by construction, no taper anywhere. This generalises what the lattice already does for heights.

Locality rules that make it work (O):
1. Every A2 segment's vertical profile is solved alone between its two pinned joint heights (joint height = surface height at the joint, or the settlement entrance height). No global solve; `VerticalCurves` already works per road.
2. Corridor routing is hierarchical: coarse route on a level-10 cell graph (A* with surface cost, deterministic tie-break), then per-segment refinement. A corridor is computed on demand when any sector near it is wanted and cached by edge id (corridors are tens to hundreds of km; the coarse route is cheap, the fine segment only near the camera).
3. A3 charts are small (<= 5 km), so the gnomonic error is <= 1e-7 (O, from RG-166 `sin^2 c`); settlements never straddle anything special because their chart is anchored at their own centre, not at a tile.
4. Spatial index: anchors registered by level-10 cell (the region becomes a *bucket*, not a world). A sector queries its cell and the 8 neighbours.

### Why not throw the tiles away completely
The tiles carry no content limitation any more; they are the cheapest correct way to do streaming, LOD (CDLOD is a quadtree on the same lattice), collision chunks and the 0-mm seam. A tile-free "evaluate everything per vertex from a global SDF" approach costs a query of all nearby roads per vertex with no batching and has no LOD story (rejected, O).

## 5. Options

| option | what it gives | risk / cost | verdict |
|---|---|---|---|
| A continue: region worlds + region portals | roads cross borders at a few pins | still 10 km islands, one city per region, taper stays; L ticket for a symptom | not recommended |
| **B anchor-first (section 4)** | continent-scale network, settlements where the land allows, no seams/taper, sector-local cost | reuses WorldGen pieces in a new driver; ~5 tickets; region pipeline kept until B passes the same rows | **recommended** |
| C global SDF, no tiles | conceptually simplest | no batching, no LOD, cost per vertex unbounded | rejected |

## 6. Ticket chain for option B (CPU, Unity-free unless marked; every ticket carries a budget row)

1. **B1 settlements on the surface (Sonnet, S-M):** `A1` from `PlanetSurface` (land, slope, coast), class rules, deterministic per cell; rows: 0 on water, density per land km2, stable ids under seed; whole-planet PNG + one continent PNG. Budget: whole planet <= 2 s, <= 20 MB.
2. **B2 corridor routing (Opus, M-L, architecture):** surface-aware coarse A* on level-10 cells, segments with pinned joints, per-segment profile with `VerticalCurves`; rows: 0 road on water (bridges = later), grade/radius rules per class (as e4), route length / great-circle ratio, determinism (two processes, different request order, same bits). Budget: one corridor coarse route <= 50 ms, one 5 km segment <= 300 ms.
3. **B3 settlement content (Sonnet, M):** `Generator.Run` in a settlement chart with `Motorway = false` and pinned entrances at the B2 corridor ends (extends RG-176's path); rows: entrances connect, 0 on water, NightRacer golden hashes unchanged.
4. **B4 sector build v3 (Opus, M):** sector = surface + anchors in radius + global detail noise, no region world; rows: seam 0 bits everywhere incl. old region borders and cube vertices, road continuous across every old region border, sweep median <= 2.0 / P95 <= 3.0 ms, memory per cached anchor set.
5. **B5 runtime switch (Sonnet, M):** `PlanetStreamer` factory on anchors; rerun t1-t8; drive from city to city across >= 3 old region borders.
6. **B6 Unity viewer (GPU, Opus, M):** first look at the planet (licence renewed 2026-11-15): sector mesh upload, far layer, e9 pixel dropouts on the UHD, one free drive along a corridor. Can start in parallel with B1-B2 on the current region pipeline, because the sector mesh format does not change.

The region pipeline (RG-167..176) stays in the repo as the reference until B4 passes; then `RegionWorld` is deleted, not patched.

## 7. Decisions for the user
- D1 direction: B (anchor-first) instead of A (region portals)? Recommendation: B.
- D2 start B6 (Unity viewer) now in parallel, so the planet is finally seen? Recommendation: yes, one GPU slot.
- Not needed from the user: method details inside B1-B5 (standing What If mandate).
- (closed 2026-11-15) D1 was replaced by measurement (user: „vyzkoušet, změřit, určit postup na základě dat, ne odhadů.“) -> section 8.

## 8. WI-ADR-002 — content is anchor-first (Accepted 2026-11-15)

**Decision.** Planet content is built from global anchors (settlements + corridors in double 3D, segments with pinned joints); a level-15 sector is a pure function of the surface + the anchors whose influence reaches it + global-coordinate detail. Level-10 regions remain cache / bucket keys only; no region-border taper; the region-portal ticket is dropped. `RegionWorld` stays as the reference until the anchor path passes B4/B5 in section 6, then it is deleted, not patched.

**Evidence** (rule fixed in `briefs/RG-178.md` before the data; orchestrator rerun on a quiet notebook, `claude-RG-178/artifacts/whatif-anchors/20261115-131636`, reviews `reviews/RG-177.md`, `reviews/RG-178.md`; doc `what-if-planet-anchor-spike.md`):
| rule row | area A (A / B) | area B (A / B) |
|---|---|---|
| n1 road ends at region borders | 27 / 0 | 193 / 0 |
| n2 settlements connected | 7 % / 98 % | 6 % / 99 % |
| s1 differing border bits (3 281 919 shared vertices) | 0 / 0 | 0 / 0 |
| s2 footprint vertices > 0.10 m above the road | 749 / 0 | 2 943 / 0 |
| s3 warm sector P95 (ms), rerun | 1.83 / 2.01 (<= 2.29) | 2.03 / 1.93 |
| s4 retained memory (MiB) | 43.2 / 3.6 | 81.5 / 5.9 |
| s5 cold-ready median (s), rerun | 0.20 / 0.05 | 0.35 / 0.05 |
| s6 drive 42.7 km over 7 old region borders (B) | t1 P95 0.628 ms, 0 holes, min lead 10.3 s | |

**Reservations carried into the next tickets** (do not change the verdict; the deciding rows n1/n2/s1/s2 do not depend on them):
1. **Settlement content is village-sized in B.** Orchestrator check (`artifacts/tmp/orch178-settle`, 2026-11-15): 56 / 94 settlement worlds, 0 failed, but 6-12 graph nodes each, also for towns/cities of radius 900/1400 m (`Motorway = false` makes the settlement `village0`, `IsCity = false`); build 0.3-40 ms. Real town/city streets + entrances pinned to corridor ends = B3; rerun s3/s4/s5 there with the same rule.
2. **Cold first touch:** worst sector 2.6-5.5 s = lazy profile of a motorway segment whose pinned joint grade + cut/fill bounds contradict the crest radius (stall exit). s6 hides it behind the region-unit prefetch; fix in B2 (joint feasibility before the solve).
3. **Corridors are straight chords** (RG-177) -> meander / surface-aware routing in B2 before any ride.
4. A's footprint row counts local streets, B's only corridors (B had no streets of its own).

**Consequences.** Next tickets follow section 6 in this order: B2 corridor routing (incl. reservation 2 and 3), B3 settlement content (reservation 1), B4/B5 = mostly done by RG-178 (`AnchorSector`, `ISectorContent`) and get closed by B3's rerun + a city-to-city drive. RG-179 (Unity viewer) consumes `AnchorSector` through `ISectorContent`, not `RegionWorld`.

## Region-first path removed (RG-185, 2026-11-16)

Variant A (`RegionWorld.BuildSurface` per level-10 region) no longer exists in the tree. The anchor experiments print B only: rows that were "A vs B" keep their columns, the A column is "-" and the A-relative targets (s2 footprint, s3 P95 <= 1.25 x A, s4 B <= 2 x A, s5, k4) are `report`; rows n1-n4 and a1 are B facts, the top views show B only. The geometry that B rows still need (region chart, border distance, block search) lives in `WhatIf.Planet.Experiment/RegionChart.cs`. Determinism: the anchor hash processes (forward, reverse, 4 threads) print the RG-184 hashes (area A `78159e87...`, area B `5b4dd85f...`). Absolute B numbers: see the same-dated paragraph in `what-if-planet-runtime.md`.
