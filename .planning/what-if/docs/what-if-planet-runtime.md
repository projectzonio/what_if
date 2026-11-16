# What If planet: streaming runtime (RG-172)

CPU-only, Unity-free library `src/WhatIf.Planet.Runtime` (netstandard2.1): the piece that keeps tiles appearing ahead of a moving camera. It ties together region worlds (RG-167), sector builds (RG-170) and the far layer (RG-171); the surface base of RG-169 plugs in through the region factory. Raw numbers come from `tools/whatif-runtime/run-runtime.ps1` (rows t1-t8 below).

## Design

`PlanetStreamer.Update(Vec3 cameraDir, double altitudeM, double dt)` runs on the **main thread** and does only bookkeeping:

1. **Wanted near sectors** (`SectorWanted`): level-15 sectors whose centre is within `LoadRadius` 600 m of the ground point, plus (hysteresis) previously wanted ones up to `UnloadRadius` 760 m; none above `MaxClearance` 1500 m. BFS over `SectorId.Neighbour` (crosses cube edges).
2. **Wanted regions** (`RegionPlan`, every 4 frames and on region change): the camera region plus every region with a sample inside the prefetch radius (rings x 32 azimuths + 5 points along the heading). Priority = distance x (1.6 - cos angle to heading), so regions ahead build first. Regions are only wanted below 3000 m clearance (a descent from there takes longer than a region build; at 5 km nothing is requested).
3. **Jobs and bounded integration**: new wanted sectors become jobs in a concurrent dictionary (priority = distance, refreshed every frame, unclaimed jobs of unwanted sectors are cancelled); finished results come back through a concurrent queue, at most `IntegratePerFrame` (4) per frame.
4. **Hole only when ready**: `NearSetBuilder` fills the far layer's `NearSet` (masks, ancestor keys, exposed sides - the same derivation as `NearSet.Update`, without its BFS) from READY wanted sectors only, and only when the level-13 far node containing the sector is built (a forced but unbuilt node would leave a gap; at most one such node is built inline per frame). A sector whose region world is not ready is not built from a fallback; it waits and the far layer keeps covering it.
5. **Far layer**: `Select` + `BuildPending` with a 2 ms/frame node build budget (RG-171). Included in the t1 timing.
6. **Caches**: sector LRU (96 sectors = 33 MiB, evicted outside the unload radius first, never a wanted one), region LRU (6 worlds, est. 3 MiB each, evicted when not in the current plan), byte cap 140 MiB over sectors + regions + far nodes. Evicted `AdaptedSector` buffers go to a pool (no allocation per build).

## Thread layout

| thread | job |
|---|---|
| main (viewer) | `Update`: sets, bookkeeping, integration, NearSet, far selection and node builds |
| `planet-regions-0/1` (BelowNormal) | `RegionWorld.Build` (3-50 s each, nearest/ahead first). **Two** of them: see decision 2 |
| `planet-sectors` (Normal) | `SectorBuild.Build` by distance (1-4 ms each, worst 15-40 ms under load); takes the job only if its region world is ready |

`RegionWorld` is not thread safe (noise corner cache, one-entry memo), so a per-region lock serialises sector builds of one region; region worlds themselves are independent (`[ThreadStatic]` scratch only). The far layer stays single-threaded on the main thread (its height source is a `Func`).

## Decisions

1. **Prefetch lead is 60 s, not 30 s** (`PrefetchLeadS`), and at least 1.5 x the longest region build seen so far (`LeadBuildFactor`; 60 s assumed before one was measured, `ExpectedRegionBuildS`, because the cube-edge neighbour region needs 50-65 s here and runs with a 30-75 s lead entered it 0.6 s after it was ready or before it was ready). The first run with 30 s gave a region ready only 3.9 s before entry and one not ready at all (region builds took 18-57 s on this notebook while the main thread, the other workers and E-cores compete). Beyond the 2 km floor only a +-12 degree cone around the heading is prefetched (the first run without the cone built 12 region worlds for 4 needed: sideways builds occupied the single region thread for 20-57 s).
2. **Two region workers + one sector worker** instead of "A = regions, B = sectors, A also sectors": sector builds take under 1 % of a core, so B was idle while A was busy for 30-57 s with one region; one region thread cannot keep a 4x drive supplied. `StreamerParams.RegionWorkers = 1` reproduces the ticket's layout. Three background threads on an i3-1215U (2P + 4E, 8 logical CPUs) leave the main + render thread of Unity free; the region workers run at BelowNormal priority (a first run with the sector thread at BelowNormal measured one sector build of 2.4 s: starved by the region workers; sector jobs are short and must not wait).
3. Region world heights are the same planet base the far layer samples (`PlanetNoise`); RG-169's surface base will change the numbers, not the code (`Func<RegionId, RegionWorld>`, `Func<Vec3, double>`).
4. Coverage check (rows t2): 3000 random directions inside the visible cap (camera radius = ground height + altitude, horizon x 0.999) + every 8th vertex of every near sector, every 10th frame, outside the timed part. An early version used a wrong camera radius and reported horizon-culled directions as holes: fixed.

## Scenario paths

- **drive-4x**: great circle through the point (face 0, a = 1, b = 0.31) on the +X/+Y cube edge with the tangent perpendicular to the edge; starts 18 km before the edge, 120 m/s for 300 s = 36 km, ends 18 km after it. Seed 1. Crosses 3 region borders and 1 cube edge (listed in row t3). The start region and its 600 m near set are built before the clock starts (settle phase, untimed); the drive starts with the region ready.
- **cold spawn**: new streamer, empty caches, teleport to (face 4, a = 0.1, b = -0.2), clock runs until the 600 m near set is complete (includes the region build).
- **flight**: great circle from (face 2, a = -0.3, b = 0.2), 250 m/s at 5000 m above ground for 60 s.

## For the Unity viewer

- Call `Update` once per frame from the main thread; read `Near`, `Selection` (far nodes + masks + skirt) and `SectorData(id)` (`AdaptedSector`: float mesh `Lx/Ly/Lz`, valid while the sector is cached) to upload meshes - upload is the viewer's job and is not in the t1 numbers. Throttle uploads yourself.
- Mono/Unity has its own GC: the .NET pause numbers here (GC part of the t1 row) are indicative only. The runtime allocates little on the main thread; the heavy garbage comes from `Generator.Run` on the worker threads.
- Pass a region factory that picks up RG-169's surface base; keep the far layer's height `Func` on the same surface.
- Three background threads were measured at about 60 % of one core in total (t4), mostly the region worlds.

## Result (generated by tools/whatif-runtime/run-runtime.ps1)

- Commit `1cc211d5a9878478fff3de239b4fb9659f73a88a`; machine: 12th Gen Intel(R) Core(TM) i3-1215U; 8 logical CPUs; power plan: 381b4222-f694-41f0-9685-ff5bb260df2e  (Rovnováha); Microsoft Windows NT 10.0.19045.0; dotnet 8.0.403; wall time of the whole run 373 s; seed 1; height source for the far layer: planet base noise (`PlanetNoise.HeightSampler`).

| id | kind | value | threshold | result | note |
|---|---|---|---|---|---|
| b1-path-land | report | land drive | report | **report** | water 0 % / land 100 % of 361 samples (100 m); 4 of 4 regions on the path have land |
| t1-drive-land | protocol | P95 0.651 ms / P99.9 1.887 ms / max 6.778 ms, 5 frames > 4 ms | P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported) | **PASS** | 18000 frames, Update + wanted sets + integration + NearSet + far selection + far node builds (budget 2 ms/frame); median 0.347 ms, P99 0.837 ms, P99.9 1.887 ms, 5 frames > 4 ms (t=5.47s 4.93 ms farBuilt=0 gc0/1/2=16/6/2; t=22.45s 6.78 ms farBuilt=0 gc0/1/2=25/10/3; t=28.49s 2.88 ms farBuilt=0 gc0/1/2=26/10/3; t=29.34s 4.19 ms farBuilt=0 gc0/1/2=26/10/3; t=29.67s 6.60 ms farBuilt=0 gc0/1/2=26/10/3; t=33.03s 2.26 ms farBuilt=0 gc0/1/2=27/10/3); GC during the run gen0/1/2 89/27/7 (gen0 0.30/s), pause 208 ms; without frames that built far nodes: P95 0.639 / max 6.778 ms (17807 frames); the coverage check runs outside the timed part |
| b3-regionbuild-land | report | P95 2.8 s, median 0.7 s, max 3.0 s | report | **report** | 13 region builds in the streamer (2 workers, BelowNormal) over the run; gen0 0.30/s, GC pause 208 ms; path: water 0 % / land 100 % of 361 samples (100 m); 4 of 4 regions on the path have land |
| t2-holes-land | protocol | 0 frames | = 0 | **PASS** | frames after the first 10 s with a wanted sector inside 300 m that is not ready |
| t2-coverage-land | protocol | 0 violations | = 0 | **PASS** | far coverage exactly once, every 10th frame after 10 s: 1741 checks x 3000 random directions + every 8th vertex of all near sectors (2840022 vertices in total); all 1800 checks incl. the first 10 s: 0 violations |
| t2-waiting-land | report | max 0 sectors, max wait 2.75 s | report | **report** | sectors inside 600 m that waited for a region world: 15 sectors in total; most not ready inside 600 m in one frame: 2 |
| t3-lead-land | protocol | min lead 71.5 s, 0 entries without a ready region | lead >= 5 s, 0 without | **PASS** | 3 region entries (F0.L10.r160.c1023 lead 71.5 s; F2.L10.r1023.c160 lead 89.7 s; F2.L10.r1022.c160 lead 89.6 s); path crosses 3 region borders, 1 cube edge(s): F0.L10.r160.c1022 > F0.L10.r160.c1023 > F2.L10.r1023.c160 > F2.L10.r1022.c160 |
| t4-throughput-land | report | B 2.1 sectors/s, A 0.00 sectors/s | report | **report** | over 300 s wall: B 630 sectors (max 24.1 ms each), A 0 sectors + 13 region worlds (longest 3.0 s) |
| t4-queue-land | report | max 15 jobs | report | **report** | pending sector jobs; request-to-ready P95 0.017 s (max 2.796 s) incl. waits for region worlds, 0.017 s without; 630 sectors |
| t4-cpu-land | report | A busy 5.6 %, B busy 0.8 % | report | **report** | worker wall busy share; process CPU 13 % of one core over 300 s (8 logical CPUs), main thread Update 2.2 % of a core |
| t6-memory-land | protocol | 60.0 MiB managed after the drive | <= 150 MiB | **PASS** | GC.GetTotalMemory(true) after the run minus before the streamer existed (caches, far layer, region worlds, sector pool); accounted cache bytes at the end 54.6 MiB (sectors 96 x 346 KiB, regions 6 x est. 3 MiB, far nodes 1862) |
| t6-slope-land | protocol | -0.002 MiB/s (GC), 0.005 MiB/s (accounted) | <= 0.1 MiB/s | **PASS** | last 100 s: GC.GetTotalMemory(true) at 200 s and at the end (60.4 -> 60.0 MiB); accounted bytes regressed over 1 Hz samples |
| t8-determinism-land | protocol | 200/200 bit-identical | = 200/200 | **PASS** | sectors sampled from the drive streamer (every 3rd build, hashed on the worker right after the build: all X, Z, PlanetBase, Contribution, Taper, EdgeDistM, World, Lx, Ly, Lz) vs a direct SectorBuild.Build in this fresh process (7 region worlds rebuilt in 3 s) |
| t5-cold | protocol | 1.2 s | <= 30 s (report) | **PASS** | empty caches, teleport to F4.L10.r409.c563: region world ready after 1.1 s, first sector 1.1 s, 600 m near set (12 sectors) complete 1.2 s; far layer coarse prebuild (levels 0-3) 0.5 s before the clock; main thread Update P95 3.228 ms / max 20.522 ms over 101 frames; request-to-ready sector P95 0.089 s without the region wait |
| t1-flight | protocol | P95 0.387 ms / max 4.961 ms | P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported) | **PASS** | 1 frames > 4 ms, P99.9 1.056 ms; 3600 frames at 250 m/s, 5000 m above ground, 222 far nodes built, far selection 286 nodes / 146432 triangles in the last frame, 1242 nodes cached |
| t7-flight | protocol | 0 near sectors requested, 0 region worlds built | = 0 above 1500 m | **PASS** | no near set, no region prefetch above the clearance cut |
| t2-flight-coverage | protocol | 0 violations | = 0 | **PASS** | far coverage exactly once, 301 checks x 3000 random directions after 10 s (all 360 checks: 0) |

Protocol rows: 11 PASS, 0 FAIL; report rows: 6.

## Anchor content, city-to-city drive (RG-184 B5)

Generated by `tools/whatif-runtime/run-anchor-runtime.ps1`; commit `097b8efb`; 12th Gen Intel(R) Core(TM) i3-1215U; 8 logical CPUs; power plan: 381b4222-f694-41f0-9685-ff5bb260df2e  (Rovnováha); dotnet 8.0.403; wall 1186 s; seed 1. `PlanetStreamer` with the `ISectorContent` constructor (RG-178): the region worker runs `AnchorWorld.Prepare`, the sector worker `AnchorSector.Build`. Path: `CorridorPath.CityToCity` (streets of the settlement worlds, shortest street path centre -> entrance, then the corridor stations, entrance -> centre). Budgets unchanged; row t9 is new (road continuity at the crossed old region borders).

| id | kind | value | threshold | result | note |
|---|---|---|---|---|---|
| c-path-c120 | report | 26.7 km at 120 m/s, 221 s | report | **report** | area B; 26.7 km, 4 old region borders, 1 cube edges; streets City F0.L9.r80.c510 1.6 km > motorway 23.2 km > streets City F2.L9.r511.c80 1.8 km |
| t1-c120 | protocol | P95 0.656 ms / P99.9 1.561 ms / max 8.135 ms, 2 frames > 4 ms | P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported) | **PASS** | 13260 frames; median 0.311 ms, P99 0.820 ms; GC gen0/1/2 84/21/7, pause 123 ms; without frames that built far nodes P95 0.645 ms; slow frames: t=21.02s 3.94 ms farBuilt=0 integrated=0 gc0/1/2=20/6/2; t=27.40s 8.14 ms farBuilt=0 integrated=0 gc0/1/2=29/9/3; t=122.13s 3.03 ms farBuilt=0 integrated=0 gc0/1/2=52/16/7; t=165.40s 4.81 ms farBuilt=1 integrated=0 gc0/1/2=57/16/7; t=180.98s 2.70 ms farBuilt=0 integrated=0 gc0/1/2=63/17/7 |
| t2-holes-c120 | protocol | 0 frames | = 0 | **PASS** | frames after the first 10 s with a wanted sector inside 300 m that is not ready |
| t2-coverage-c120 | protocol | 0 violations | = 0 | **PASS** | far coverage exactly once, every 10th frame after 10 s: 1267 checks x 3000 random directions + every 8th vertex of all near sectors (2093688 vertices); all 1326 checks: 0 |
| t2-waiting-c120 | report | max 0 sectors, max wait 0.88 s | report | **report** | sectors inside 600 m that waited for their region's anchor preparation: 17; most not ready inside 600 m in one frame: 2 |
| t3-lead-c120 | protocol | min lead 41.3 s, 0 entries without a ready region | lead >= 5 s, 0 without | **PASS** | 4 region entries: F0.L10.r161.c1022 lead 41.3 s; F0.L10.r160.c1022 lead 94.6 s; F0.L10.r160.c1023 lead 90.8 s; F2.L10.r1023.c160 lead 90.3 s |
| t4-c120 | report | B 2.1 sectors/s (max 107.9 ms each), 15 region preparations (longest 0.87 s) | report | **report** | request-to-ready P95 0.033 s, max 1.080 s; queue max 17; A busy 1.5 %, B busy 1.1 %; process CPU 9 % of one core |
| t6-memory-c120 | protocol | 48.3 MiB managed after the drive | <= 150 MiB | **PASS** | GC.GetTotalMemory(true) after minus before the streamer and its anchor world; accounted cache 54.2 MiB; anchor world: 143 routes, 22 settlement worlds cached (6.0 MiB est., cap 6 MiB), built 89, evicted 67 |
| t6-slope-c120 | protocol | 0.023 MiB/s (GC), 0.006 MiB/s (accounted) | <= 0.1 MiB/s | **PASS** | last 100 s: 46.0 -> 48.3 MiB |
| t8-determinism-c120 | protocol | 200/200 bit-identical | = 200/200 | **PASS** | sectors sampled from the drive streamer (every build up to 300, hashed on the worker) vs AnchorSector.Build in this fresh process, fresh anchor world, reverse order (1 s) |
| t9-continuity | protocol | 0 gaps of 4 crossed old region borders | 0 gaps | **PASS** | 63 vertices within 2 m of the path centre line (+-10 m around each crossing, every 1 m); F0.L10.r161.c1021>F0.L10.r161.c1022 at 5.00 km: 16 vertices, codes ground/corridor/street/shoulder 0/16/0/0; F0.L10.r161.c1022>F0.L10.r160.c1022 at 11.39 km: 15 vertices, codes ground/corridor/street/shoulder 0/15/0/0; F0.L10.r160.c1022>F0.L10.r160.c1023 at 14.13 km: 16 vertices, codes ground/corridor/street/shoulder 0/16/0/0; F0.L10.r160.c1023>F2.L10.r1023.c160 at 23.20 km: 16 vertices, codes ground/corridor/street/shoulder 0/16/0/0 |
| c-path-c30 | report | 26.7 km at 30 m/s, 888 s | report | **report** | area B; 26.7 km, 4 old region borders, 1 cube edges; streets City F0.L9.r80.c510 1.6 km > motorway 23.2 km > streets City F2.L9.r511.c80 1.8 km |
| t1-c30 | protocol | P95 0.577 ms / P99.9 1.766 ms / max 21.505 ms, 28 frames > 4 ms | P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported) | **PASS** | 53280 frames; median 0.219 ms, P99 0.704 ms; GC gen0/1/2 80/20/6, pause 278 ms; without frames that built far nodes P95 0.572 ms; slow frames: t=40.50s 2.67 ms farBuilt=0 integrated=0 gc0/1/2=5/1/0; t=78.30s 4.53 ms farBuilt=0 integrated=0 gc0/1/2=12/4/1; t=147.18s 2.46 ms farBuilt=0 integrated=0 gc0/1/2=18/6/2; t=148.39s 4.02 ms farBuilt=0 integrated=0 gc0/1/2=18/6/2; t=150.80s 5.67 ms farBuilt=0 integrated=0 gc0/1/2=19/6/2 |
| t2-holes-c30 | protocol | 0 frames | = 0 | **PASS** | frames after the first 10 s with a wanted sector inside 300 m that is not ready |
| t2-coverage-c30 | protocol | 0 violations | = 0 | **PASS** | far coverage exactly once, every 10th frame after 10 s: 5269 checks x 3000 random directions + every 8th vertex of all near sectors (8418978 vertices); all 5328 checks: 0 |
| t2-waiting-c30 | report | max 0 sectors, max wait 1.08 s | report | **report** | sectors inside 600 m that waited for their region's anchor preparation: 17; most not ready inside 600 m in one frame: 2 |
| t3-lead-c30 | protocol | min lead 89.8 s, 0 entries without a ready region | lead >= 5 s, 0 without | **PASS** | 4 region entries: F0.L10.r161.c1022 lead 89.9 s; F0.L10.r160.c1022 lead 210.1 s; F0.L10.r160.c1023 lead 89.8 s; F2.L10.r1023.c160 lead 90.7 s |
| t4-c30 | report | B 0.5 sectors/s (max 30.3 ms each), 6 region preparations (longest 1.07 s) | report | **report** | request-to-ready P95 0.033 s, max 1.220 s; queue max 17; A busy 0.2 %, B busy 0.3 %; process CPU 8 % of one core |
| t6-memory-c30 | protocol | 48.8 MiB managed after the drive | <= 150 MiB | **PASS** | GC.GetTotalMemory(true) after minus before the streamer and its anchor world; accounted cache 54.2 MiB; anchor world: 83 routes, 26 settlement worlds cached (6.0 MiB est., cap 6 MiB), built 32, evicted 6 |
| t6-slope-c30 | protocol | 0.007 MiB/s (GC), 0.002 MiB/s (accounted) | <= 0.1 MiB/s | **PASS** | last 100 s: 48.2 -> 48.8 MiB |
| t8-determinism-c30 | protocol | 200/200 bit-identical | = 200/200 | **PASS** | sectors sampled from the drive streamer (every build up to 300, hashed on the worker) vs AnchorSector.Build in this fresh process, fresh anchor world, reverse order (1 s) |
| t5-cold-c | protocol | 0.9 s | <= 30 s | **PASS** | empty caches, fresh anchor world, teleport to the destination city centre (F2.L10.r1023.c160): region preparation ready after 0.7 s, first sector 0.8 s, 600 m near set (16 sectors) complete 0.9 s; far prebuild 0.5 s before the clock; main thread Update P95 4.509 ms / max 15.441 ms; anchor world 28 routes, 8 settlement worlds |
| t1-flight-c | protocol | P95 0.597 ms / P99.9 1.686 ms / max 3.960 ms | P95 <= 1.0 ms, P99.9 <= 2.0 ms (frames > 4 ms reported) | **PASS** | 0 frames > 4 ms; 3600 frames at 250 m/s, 5000 m above ground along the drive path, 301 far nodes built |
| t7-flight-c | protocol | 0 near sectors requested, 0 region preparations | = 0 above 1500 m | **PASS** | anchor world afterwards: 0 routes, 0 settlement worlds |
| t2-flight-coverage-c | protocol | 0 violations | = 0 | **PASS** | far coverage exactly once, 301 checks x 3000 random directions after 10 s |

Protocol rows: 19 PASS, 0 FAIL; report rows: 6.

No Unity editor or NightRacer player was running at the start or end of the run.
Raw folder artifacts/whatif-runtime/20261116-144253 (git-ignored, sha256.txt).

## Region-first path removed (RG-185, 2026-11-16)

After RG-184 closed B4/B5 (city-to-city runtime drive t1-t9 on anchor content) the region-first path was deleted: `RegionWorld`, `SectorBuild` (src/WhatIf.Planet.WorldGen/RegionWorld.cs), the region constructor of `PlanetStreamer` and `RegionContent`, `RegionProfile` and the variant-A modes of the runtime experiment (`drive`, `verify`, `cold`, `flight`, `profile`, `report`; the `c*` modes of RG-184 are their anchor equivalents), `tools/whatif-runtime/run-runtime.ps1` and `run-profile.ps1`, and the region groups e and p of `WhatIf.Planet.Experiment`. `PlanetStreamer` takes only `Func<RegionId, ISectorContent>`; the viewer (`PlanetView`) is unchanged. Sections above that describe the region path stay as history. Absolute B baseline for future cost work (RG-184): s4 retained anchor cache 8.3 MiB (area A) / 9.3 MiB (area B) per 2031 km2 (the A-area figure is 43.2 / 81.5 MiB for the removed region worlds); k4 s3 warm sector build P95, 3 repeats on 2048 seeded sectors: area A 3.18 / 3.28 / 3.22 ms, area B 3.35 / 3.34 / 3.31 ms (`what-if-planet-anchor-spike-sectors.csv`).
