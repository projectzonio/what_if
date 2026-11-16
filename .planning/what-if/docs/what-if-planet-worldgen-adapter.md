# What If planet: WorldGen adapter for deformed level-15 sectors (RG-165)

Status: v1 (RG-165) below; **v2 (RG-167, region chart + roads per region) in the last section**. v1: CPU only, all w rows PASS (run 20261112-175156, commit 363ed6c0), numbers only (no Unity, no rendering). WI-ADR-001 Accepted (2026-11-12). Implements the record `SectorPlanetInput` of
`what-if-planet-content-roads.md` §2 as a subset. Results: group `w` in `what-if-planet-experiment.{md,csv}`.

## What the adapter does

Library `src/WhatIf.Planet.WorldGen` (netstandard2.1, references `WhatIf.Planet` + `WhatIf.WorldGen`; the only bridge, neither library references the other).

1. `SectorPlanetInput.Build(sectorId, worldSeed)`
   - frame = `QuadSector(id)`: centre (double3, planet-fixed m), Up, East, North;
   - local plane = **gnomonic** projection onto the tangent plane at the sector centre (x along East, z along North). Constant-u / constant-v lattice lines of a cube face are great circles, so the deformed quad is a quadrilateral with straight sides in this plane (w4 measures it);
   - `BaseHeightM[Side x Side]` at **16 m**, centred, 1-sample apron, Catmull-Rom (C1) through `Base(x, z)`. Every sample is `PlanetNoise.Height` at the global position of its local point (`normalize(Centre + East x + North z) * R`), never a sector-local seed;
   - `RegionSeed` = SplitMix64 of the level-10 region key mixed with the world seed (all 32 x 32 sectors of a region share it; it is WorldGen's `spec.Seed`).
2. `SectorAdapter.Model(input)` = `new TerrainModel(spec, input.Base)`; spec = the existing "hilly" terrain values (relief 60 m, wavelength 1400 m, 5 octaves), no mountain.
3. `SectorAdapter.Build(input, model, out)` for each of the 65 x 65 lattice vertices (directions exactly as `QuadSector.Build`, canonical on cube edges):
   `pb = PlanetNoise.Height(dir * R)`, `(x, z) = gnomonic(dir)`, `c = model.Height(x, z) - input.Base(x, z)` (WorldGen's own contribution),
   `t = smoothstep(min(1, distance to the nearest quad side / 32 m))`, `t = 0` on the 256 rim vertices, `h = pb + t c` (`h = pb` exactly when `t = 0`),
   vertex = `dir * (R + h)`, float mesh relative to the centre in the sector frame (as `QuadSector`).

Why the vertex base is the exact planet height and not the 16 m grid: two neighbours interpolate their grids in different frames, so the grid base on a shared edge differs by up to 3 mm (w1 diag); the exact `PlanetNoise` value at the canonical vertex direction is bit-identical from both sides (w1 = 0). WorldGen still sees the grid as its base (`c` is measured against the same grid), so the grid only shapes WorldGen's view of the low-frequency relief (and, later, road planning), not the seam.

## The hook in `TerrainModel` (the only NightRacer change)

`src/WhatIf.WorldGen/Terrain.cs`, additive:

- `TerrainModel(WorldSpec spec, Func<double, double, double> baseHeight)`: with a provider, `Natural(x, z) = baseHeight(x, z) + hills octaves >= DetailFirstOctave (= 2)` with the same noise, weights, normalisation and basin factor; octaves 0-1 and the mountain band are replaced by the provider. A provider enables the model even with relief 0. Road carving (`Height`, `SampleTile`) is unchanged and sits on top.
- `null` provider = the single-argument constructor. Regression test in `tests/WhatIf.WorldGen.Tests`: an FNV-1a hash over `SampleTile` (3 tiles), `Height`, `Natural` of a generated hilly map and `Height` of an unbound model, golden value taken from the unmodified `Terrain.cs` (`D221EBA2351460E0`), still equal after the hook. All WorldGen tests pass unchanged.

With relief 60 m and wavelength 1400 m the detail octaves are 350 / 175 / 87.5 m; the measured contribution is 1.6-4.0 m max per sector (w3 diag).

## Taper

**32 m** band (local-plane distance to the nearest quad side), smoothstep. 38-48 % of the 4225 vertices lie in the band (w4). The taper raises the slope of the WorldGen contribution from <= 0.06 to <= 0.17 rise/run in the band (w3 diag; the planet base slope comes on top and is unchanged). That is the price of a 4 m contribution fading over 32 m; a wider band lowers it linearly.

## Results (group w, see the experiment table for every row)

| row | metric | result | threshold |
|---|---|---|---|
| w1 | shared-edge planet base height, 3 cases x 3 seeds (face centre 4 pairs, cube-edge midpoint 4 pairs, cube vertex 5 pairs incl. the two other faces' corner sectors) | 0 m, 0 of 260-325 vertices not bit-identical | = 0 |
| w1 diag | grid-interpolated base from both frames on the same edge | 0.5-2.9 mm | report |
| w2 | seam of the adapted mesh, double world positions | 0 mm | < 1 mm |
| w2 | seam, each side's float local mesh back through its own frame | 0.013-0.015 mm | < 1 mm |
| w3 | WorldGen contribution on the quad edge | 0 m | = 0 |
| w3 diag | slope change by the taper / tapered / untapered | <= 0.158 / <= 0.166 / <= 0.061 | report |
| w3 diag | 16 m Catmull-Rom grid vs exact planet base inside the sector | <= 3.1 mm | report |
| w4 | footprint face centre: sides 305.74 x4, 90 deg, area 1.426 x 256^2, grid 23 x 23 (+-160 m) | | report |
| w4 | footprint cube-edge midpoint: 305.74 / 216.19 / 305.74 / 216.20, 0.002 deg, area 1.009, grid 23 x 23 | | report |
| w4 | footprint cube vertex: 288.25-288.26 x4, 30.000 deg deviation (the 120 deg corner), area 1.098, grid 31 x 31 (+-224 m) | | report |
| w5 | adapter build of one sector (input + TerrainModel + 65 x 65 heights), single thread, 3 processes, i3-1215U | median 0.77-0.88 ms, P95 0.84-0.91 ms (parts: input 0.035-0.068 ms, TerrainModel construction 0.0013-0.0015 ms, heights 0.73-0.81 ms) | < 3 ms (median and P95) |

## What v1 leaves out

- Biome, climate, per-corner detail blend, `baseSlopeMax`, `coastDistM`, settlements (no basins: `BasinFactor` = 1), corridors, portals: later tickets (§2 record).
- **No roads**: the model is never bound to a road graph, so carving is 0 and `c` is pure detail. When a sector gets roads, the carved terrain is faded by the same taper; a road that crosses the sector border would then end in a 32 m ramp back to the planet base unless the corridor carries its road height as a hint (content-roads §2 rule 3). Road continuity across sectors is not addressed.
- The RG-160 grid size (19 x 19 = +-128 m) assumed a flat 256 m sector; the deformed quads need 23 x 23 (face centre, edge midpoint) and 31 x 31 (cube vertex) at 16 m. v1 sizes the grid per sector (`Side`): +0.7 KB (23 x 23) to +2.3 KB (31 x 31) of float per sector against 19 x 19.
- Whole-planet sampling, LOD rings, threads, Unity meshes.

## Recommended next step

Bind roads in one sector: let the planet corridor of a sector (content-roads §4 portals) produce a WorldGen road graph in the local plane, carve it, and measure the border: (a) with the taper only (expected: ramp at the border) and (b) with the corridor's road height on the border portals (the road height hint), so the taper only fades the terrain beside the road. Same w-style rows, CPU only.


## v2 (RG-167): region chart, one WorldGen world per region, taper on region borders only

Status: v2, CPU only, run `artifacts/whatif-planet/20261112-222133` (worktree on top of commit 98f12461; `sha256.txt` of the raw folder 27db321c...c7ee2), i3-1215U, wall time of the whole experiment 246 s. Every e row PASS **except e10 at the cube-vertex site** (sector build 3.14 ms median, 3.36 ms P95 against < 3 ms; cause below). Rows a/b/c/r1 (and r2-r4, w1-w4) are bit-identical to the main CSV. Design: `what-if-planet-tile-curvature.md` §6 (RG-166).

### What changed against v1

| item | v1 (RG-165, unchanged, still built and checked by the w rows) | v2 (RG-167) |
|---|---|---|
| chart | gnomonic plane at the **sector** centre | the same gnomonic map anchored at the **level-10 region** centre (`RegionPlane`: C, E, N); `RegionWorld.ToChart` / `ToDir` / `Offset` (metric-exact position relative to R C) |
| WorldGen | one `TerrainModel` per sector, no roads | **one `Generator.Run` per region** (`RegionWorld.Build(regionId, worldSeed)`): roads + carved terrain; every sector of the region samples it |
| base height | 16 m Catmull-Rom grid per sector | `B(x, z) = PlanetNoise.Height(normalize(R C + x E + z N) R)` direct, with a noise corner cache and a one-entry memo (the sector build and `TerrainModel.Natural` ask the same point once) |
| spec | `SectorAdapter.Spec(regionSeed)` | the same spec, `SizeKm` = the chart bounding square of the region polygon rounded up to 100 m (9.8 km face centre and cube-edge midpoint, 10.6 km at the generic corner site, **13.9 km** at the cube vertex: the 120 deg rhombus is wider than its sides); the default motorway entry/exit (+-5 km) scaled with `Half` so it still runs edge to edge |
| taper | 32 m on every quad edge (38-45 % of the area) | 32 m smoothstep on the **region borders only** (chart distance to the 4 straight region sides); `t = 0` exactly on the region border (integer lattice test), so the border vertex is the pure planet base, bit-identical with the neighbour region; band = 1.30 / 1.57 / 1.60 / 1.40 % of the region area (face centre / cube-edge midpoint / cube vertex / generic corner) |
| vertex | `h = pb + t (Height - Base)` in the sector chart | the same formula in the region chart: `h = pb(d) + t (T(x, z) - B(x, z))`, vertex `d (R + h)`, float mesh in the sector frame (ADR row 3 unchanged) |
| sampling | `Height` per vertex | `Height` per vertex (one carve-index box query per point inside `TerrainModel.Height`); the batch box query of `SampleTile` is private API (`HeightWith`) in a read-only file, and for a dense sector one sector-wide candidate list would be longer per point than the per-point query |
| triangles | not decided | `SectorBuild.Triangles`: each lattice quad split along its shorter world-space diagonal (cube vertex: 4.50 m, never 7.80 m); 0.019-0.026 ms per sector |

Generator hook (the only NightRacer change of RG-167): `Generator.Run(spec, rules, Func<double, double, double> baseHeight)`; the private constructor passes it to `new TerrainModel(spec, baseHeight)`; `Run(spec, rules)` = `Run(spec, rules, null)`. Regression test in `tests/WhatIf.WorldGen.Tests`: with `null` the road dataset hash equals `Run(spec, rules)` and the terrain golden hash (`0xD221EBA2351460E0`) is unchanged.

### Results (group e; every row in `what-if-planet-experiment.{md,csv}`)

Sites: face centre `F0.L10.r512.c512` (its low corner is the face centre), cube-edge midpoint `F0.L10.r512.c1023`, cube vertex `F0.L10.r1023.c1023`, generic corner where 4 regions meet `F0.L10.r256.c768` (low corner). Seeds 1, 42, 2147483647.

| row | measured | threshold |
|---|---|---|
| e1 | 0 differing height / position / chart bits on 455 shared vertices per site and seed (7 pairs: 3 crossed by a ground road of 3 classes, 4 around the region centre sector); 31-118 of them lie on a road surface | = 0, PASS (12 of 12) |
| e2 | 0 mm world gap (double; 0 vertices not bit-identical) and 0.013-0.017 mm through the float frames across region borders, incl. the cube edge (F0 / F2) and the cube vertex (3 faces); taper x contribution on the border = 0; band share 1.30 / 1.57 / 1.60 / 1.40 %; ground road samples inside the band 9 / 60 / 30 / 11 (0.06-0.39 % of the region's road samples): these ramp to the planet base until portals carry a road height | < 1 mm, <= 1.6 %, PASS (16 of 16) |
| e3 | chart vs great-circle distance max rel. error 6.8e-7 - 1.16e-6; right angles max 2.3e-5 - 3.2e-5 deg | <= 2e-6, <= 1e-4 deg, PASS (8 of 8) |
| e4 | min radius per class (3-point; sphere = geodesic curvature), max grade, width: max rel. deviation sphere vs chart 4.3e-8 - 1.5e-6 | <= 1e-5, PASS (36 of 36) |
| e6 | max terrain above the road surface inside the footprint, ~1.05-1.11 M points per site (seed 1): lattice short / long diagonal vs 4 m baseline (two diagonals): face centre **3.81 / 3.24 vs 2.98 / 3.23 m**; cube-edge midpoint 2.01 / 2.02 vs 1.94 / 1.96; cube vertex 1.49 / 1.54 vs 1.44 / 1.90; corner 2.72 / 2.72 vs 2.62 / 2.81 | report: worse than the baseline everywhere by 0.04-0.10 m, **at the face centre by 0.83 m** (4.78 m lattice spacing, square quads: the "short" diagonal is an arbitrary tie there) |
| e8 | float32 local mesh x float32 object matrix into a floating-origin frame: 15-31 um at 0 m, **61 um at 600 m, 977 um at 10 km**; 0-55 of 65 shared vertices bit-identical | report; reproduces RG-166 §4 (61 um / 0.98 mm) |
| e10 sector | 65 x 65 build median (3 `ecost` processes): 1.31 / 1.35 / **3.14** / 1.47 ms; P95 1.47 / 1.41 / **3.36** / 1.54 ms | < 3 ms: **FAIL at the cube vertex** (2 of 8 rows) |
| e10 region | `Generator.Run` + model, sequential, seed 1: 12.4 / 5.9 / 8.8 / 4.2 s, 3.0-3.3 MB managed retained; worlds built in parallel (15 at once) took 53-101 s each (upper bound under load); probe: the same spec with a flat base runs in 0.1-0.9 s, so the planet relief (+-150 m) is what makes routing slow, not the base callback (15-17 k calls, ~5 ms) | report |
| e10 memory | peak working set: checks process 274 MB (19 region worlds alive), each cost process 34 MB, each ecost process 90-92 MB (4 worlds) | report |

**Cause of the e10 FAIL**: the timed sector is the first sector a ground road crosses (deterministic); at the cube-vertex site with seed 1 that is `F0.r32741.c32736`, inside a settlement. Probe of that sector: `TerrainModel.Height` 3.40 ms of 3.44 ms for the 4225 points, `Natural` alone 0.86 ms, base noise 0.28 ms: the carve query over a dense street net dominates. Seed 2147483647 at the same site measured 2.86 ms median in the checks process (diag row; 2.78-3.01 ms in two earlier quick looks), every other timed sector 1.2-1.5 ms. Options for the orchestrator (not done here, thresholds not tuned): (a) a public batch sample in `TerrainModel` that keeps the carve candidates per 4-16 m cell (NightRacer change, outside RG-167's ownership), (b) cache the carved heights of settlement sectors in the region world (memory), (c) split a sector build over two frames when its candidate count is high.

### What v2 leaves out

- **Polygon domain (e5)**: the generator still works on the square `[-Half, Half]^2`; content outside the region polygon is generated and only cut by the taper. Settlements and roads near the border waste work, and a road may end in the band (e2 counts them).
- **Road height at region-border portals**: roads that cross a region border ramp to the planet base in the 32 m band (9-60 road samples per region); the portal road height of content-roads §4 is a later ticket.
- **Far-layer skirt (e7)** and **GPU dropouts along float seams (e9)**: not measured (CPU only / needs Unity).
- **Run time**: one region world takes 4-12 s single thread (up to ~20 s seen) on the notebook; the experiment runs e2, e6, e8 and the e10 protocol rows on seed 1 only and builds region worlds in parallel, untimed; e1, e3, e4 use all 3 seeds. Streaming must build a region world ahead of time (background thread), not inside a frame.
- v1 (`SectorAdapter`, `SectorPlanetInput`) stays as built for the w rows; `SectorPlanetInput.RegionSeedOf(RegionId, seed)` is the region-level overload.

## v2.1 batch query (RG-170)

`TerrainModel.HeightBatch(xs, zs, count, outH)` (additive; `Height`, `HeightWith`, `SampleTile`, `Hole` untouched, golden hash unchanged) returns `Height(x, z)` bit for bit (tests: 24 000 scattered + 24 000 compact-batch points on a dense settlement world; `SectorBuild` v2.1 == the RG-167 per-point build on every tapered vertex and on all 4 x 1024 swept sectors). `SectorBuild.Build` now does two passes: lattice / planet base / chart point / taper, then one batch query for all tapered vertices. Two exact levers, measured:

- **(a) candidate-set reuse** (points with the same 64 m hash-cell range share one query + sort): a 65 x 65 sector has **182-242 distinct ranges** (not a few dozen: the region chart is rotated against the hash grid), 10 candidates per set in light sectors, 57-67 in dense ones. Alone it halves the cost of light sectors (first-road sector 1.2-1.4 ms vs 1.3-1.5 ms before is within noise) and saves ~15-20 % on dense ones.
- **(b) exact pruning:** a per-segment box bound in id order barely helped (visits per point were already ~7 once the order is nearest-first); the kept form is a 16 m grid of the segments near the batch with a ring search that stops when the next ring is farther than the best (winner = minimum of (distance, id), order independent; a winner inside the carve pad is also the winner of `Height`'s own set, otherwise the point falls back to the set path). It is used only when >= 400 segments lie near the batch (`GridMinSegments`): on light sectors the grid build and the fallback cost more than they save (measured: all sectors 1.1 -> 1.8 ms when always on).

Result (e10, 3 processes, single thread, seed 1): sweep median of all 1024 sectors per site 1.31-1.49 ms (point query) -> 1.13-1.15 ms; sweep P95 4.3-4.8 -> 2.2-2.5 ms; P99 5.6-6.3 -> 2.6-3.0 ms; max 6.7-7.3 -> 2.9-7.3 ms (one noisy outlier). First-road sector 1.2-1.4 ms (PASS). **Slowest sector of each sweep (settlement sector, dense street net): 2.5-2.9 ms median, P95 2.7-3.1 ms: e10 FAIL against the 2.0 ms target** (before: 3.0-3.3 ms). Split of a worst sector (probe `split`, noisy shared notebook): whole build ~2.8-3.0 ms; `Natural` 0.45-0.5 ms; `Base` 0.23 ms; batch query incl. `Natural` 2.2-2.7 ms, i.e. carve ~1.7-2.2 ms, remaining (planet noise, chart, taper, emit) ~0.5 ms. The exact carve of a dense street net is still the bulk; reaching 2.0 ms needs a different mechanism (e.g. a per-region precomputed signed-distance / nearest-segment raster, which is an approximation and not allowed by the ticket) or a cheaper `Natural`.

## v3 surface base (RG-169)

Joins the RG-168 planet surface with the region worlds. Everything is additive; `RegionWorld.Build(id, seed)`, `SectorBuild.Build(world, sector, out)` and the v2.1 rows (synthetic `PlanetNoise` base) are unchanged.

- **API.** `RegionWorld.BuildSurface(id, seed, surface = null)`: base `B(x, z) = PlanetSurface.Height(dir)` at the chart point (memo), water mask `IsWater` = surface kind Ocean/Sea/Lake (Beach is land), `IsOcean` (17 x 17 lattice probe all Ocean/Sea: no WorldGen world, `Gen`/`Model` null, sector = surface heights only), `NoSite` (land but no dry city site that gives a motorway alignment within 4 tries: no world). `SectorBuild.Build` takes the surface path when `w.SurfaceBase` (one `PlanetSurface` per thread via `RegionWorld.ThreadSurface(seed)`).
- **Base once per point (RG-170 decision B).** `TerrainModel.HeightBatch(xs, zs, n, outH, baseH)` receives the base values; the carve no longer calls the hook. Bit-identical to the hook path when the same function feeds both (unit test, pruned and set path). In v3 the base value is the one evaluated at the lattice direction (`pb`), so `Contribution = T - pb` cancels exactly at taper 1; v2.1 keeps the hook.
- **Water mask.** `Generator.Run(spec, rules, baseHeight, isWater)` (null = bit-identical, hash test): village sites (centre + 4 points at 200 m), street segments and route candidates (every 16 m) on water are rejected; afterwards every road edge with a vertex on water is dropped (no bridges), orphan nodes and spans with a dropped edge are removed, ids stay dense. The city is moved to the nearest dry site on a 400 m spiral (`PickCity`).
- **Forest.** `ScatterModel.DensityFactor` (planet `Forest`, multiplies the mask probability) and `IsWater` (no tree, bush, house or street tree on water); null = bit-identical.
- **Surface (src/WhatIf.Planet.Surface).** Coast detail: 3 octaves 4..1 km (`CoastDetail` 0.004 field units, full inside `|x| < 0.004`, fading out at 0.010) added to the continent field near the sea threshold. Sea level is still calibrated on the field without it. Optimisation: the three warp fields share one `FbmLayer3` (bit-identical to three `FbmLayer`); octaves cut: warp 2 -> 1, continent 5 -> 4, ridge 4 -> 3, moisture 3 -> 2, patch 3 -> 2 (changes the surface: s1-s6 rerun). Cheap queries `Kind(dir)`, `HeightKind(dir, out kind)`.
- **Numbers** and block ids: `what-if-planet-experiment.md` group p (blocks, p-table, PNG sha256).

## Region-first path removed (RG-185, 2026-11-16)

Adapter v2 (`RegionWorld`, `SectorBuild`, section above) was deleted together with the region pipeline; v1 (`SectorPlanetInput` + `SectorAdapter`) stays because the experiment groups w1-w5 and `WorldGenChecks` use it. Sectors of the planet come from `AnchorSector` (`what-if-planet-anchor-direction.md`).
