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
| `planet-regions-0/1` (BelowNormal) | the region world build (3-50 s each, nearest/ahead first). **Two** of them: see decision 2 |
| `planet-sectors` (Normal) | the region sector build by distance (1-4 ms each, worst 15-40 ms under load); takes the job only if its region world is ready |

The region world type (removed in RG-185) was not thread safe (noise corner cache, one-entry memo), so a per-region lock serialises sector builds of one region; region worlds themselves are independent (`[ThreadStatic]` scratch only). The far layer stays single-threaded on the main thread (its height source is a `Func`).

## Decisions

1. **Prefetch lead is 60 s, not 30 s** (`PrefetchLeadS`), and at least 1.5 x the longest region build seen so far (`LeadBuildFactor`; 60 s assumed before one was measured, `ExpectedRegionBuildS`, because the cube-edge neighbour region needs 50-65 s here and runs with a 30-75 s lead entered it 0.6 s after it was ready or before it was ready). The first run with 30 s gave a region ready only 3.9 s before entry and one not ready at all (region builds took 18-57 s on this notebook while the main thread, the other workers and E-cores compete). Beyond the 2 km floor only a +-12 degree cone around the heading is prefetched (the first run without the cone built 12 region worlds for 4 needed: sideways builds occupied the single region thread for 20-57 s).
2. **Two region workers + one sector worker** instead of "A = regions, B = sectors, A also sectors": sector builds take under 1 % of a core, so B was idle while A was busy for 30-57 s with one region; one region thread cannot keep a 4x drive supplied. `StreamerParams.RegionWorkers = 1` reproduces the ticket's layout. Three background threads on an i3-1215U (2P + 4E, 8 logical CPUs) leave the main + render thread of Unity free; the region workers run at BelowNormal priority (a first run with the sector thread at BelowNormal measured one sector build of 2.4 s: starved by the region workers; sector jobs are short and must not wait).
3. Region world heights are the same planet base the far layer samples (`PlanetNoise`); RG-169's surface base will change the numbers, not the code (the region factory, `Func<Vec3, double>`).
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
