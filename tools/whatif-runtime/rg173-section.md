## RG-173: surface base + region build cost

Ticket RG-173 (2026-11-13): the streaming runtime of RG-172 on the planet surface base (`BuildSurface` of the region world (removed in RG-185), RG-169) and the cost of a region build. The Result table above is the run of this ticket (rows with the suffix `-land` = the drive over land, see b1); raw output `artifacts/whatif-runtime/20261113-055549/` (git-ignored, SHA-256 list in the outbox).

### Findings

- **Where the build cost is.** One `Generator.Run` stage dominates every region: `Assemble` -> `ProfileRoad` -> `VerticalCurves` (vertical-curve rounding) = about 99 % of the time and 99.9 % of the allocation of a region build (median 8699 ms and 1499 MB per build before). The other stages (settlements, motorway, streets, interchange, primary / secondary, water edges, width joints, terrain bind) together are about 100 ms and 30 MB. `BuildSurface` outside `Generator.Run` (ocean probe, city pick) is under 1 ms.
- **Cause of the allocation.** `VerticalCurves` calls `SolveProfile` up to thousands of times per road (alternating projections), and each call cloned `lo` and `hi` (two `double[m]`, m = samples of a road, ~4 m spacing): 1.46 GB of garbage per build, ~12 gen0 GC per second while a region builds (RG-172).
- **Lever (one, measured).** `SolveProfile` takes caller-owned scratch arrays (`lo2`, `hi2`) that `VerticalCurves` allocates once per call. Same arithmetic in the same order, so the output is bit-identical (b4). The old 6-argument overload stays (allocates; one call outside the loop). No algorithm change. The second lever of the brief was not needed: the next stage by allocation is `BuildStreets` at 13.6 MB (< 0.4 %).
- **Time.** The time is dominated by the arithmetic of the vertical-curve sweeps (`VcWorkBudget` 600 M station updates per call), so the build only got faster by the removed allocation and GC work (b3). Cutting it further would change the algorithm (sweep order, budget, tolerance) and with it the output: left for an orchestrator decision.
- **Parallel.** Two builds on 2 threads: per-build time +10..13 % vs sequential, wall ratio 2 threads / sequential = 0.65 for the 9 regions (ideal 0.50), the same before and after.
- **Drive path.** The RG-172 path (face 0 row 670) is **open ocean** on the surface base (no region world is generated there). It cannot show the build cost, so b1 / b3 use `GreatCircle.DriveLand`: the same drive across the same cube edge at row ~160 (face 2 column ~160), over the block B regions of RG-169: **0 % water / 100 % land**, 4 of 4 regions have land, 3 region borders + 1 cube edge. The RG-172 path was also run on the surface base after the change: P95 0.778 ms, 0 holes, 0 coverage violations, one 54 ms frame at t = 153 s (4 frames > 4 ms).
- **Thread safety.** `PlanetSurface` keeps caches and is not thread safe: the factory uses the removed region world `ThreadSurface(seed)` (one per worker thread, `[ThreadStatic]`); the far layer's height function is its own `PlanetSurface` (main thread only) returning `PlanetSurface.Height(dir)`, the value the region base `B(x, z)` uses.

### Rows (group b)

| row | value | threshold | result |
|---|---|---|---|
| b1 runtime on the surface base | `t1-drive-land` .. `t8-determinism-land` in the Result table: P95 0.559 ms, P99.9 1.489 ms, 1 frame > 4 ms (91 ms, one instant), 0 holes, 0 coverage violations, min lead 26.3 s, cache 59.7 MiB, slope -0.052 MiB/s, 0 near sectors above 1500 m, 200/200 bit-identical; cold spawn 9.2 s | P95 <= 1.0, P99.9 <= 2.0 ms, frames > 4 ms reported; t2 0; t3 0 without; t6 <= 150 MiB / <= 0.1; t7 0; t8 200/200 | PASS (frames > 4 ms reported) |
| b2 profile | tables below | report | report |
| b3 allocated bytes per build (median of 9) | 1499 MB -> 37.9 MB (**-97.5 %**) | <= -50 % | PASS |
| b3 sequential median build time | 8699 ms -> 7944 ms (-8.7 %) | <= +5 % | PASS |
| b3 gen0 per second in drive-4x (land path, 300 s, same build) | 12.88/s (3863 gen0, GC pause 1250 ms) -> 0.28/s (85 gen0, pause 212 ms) (**-97.8 %**) | <= -50 % | PASS |
| b3 region build P95 in drive-4x | P95 28.6 s, median 14.6 s, max 48.3 s (13 builds, 2 BelowNormal workers beside the 60 Hz main thread); before the change: longest 41.3 s | report | report |
| b4 identical output | 9/9 GenResult SHA-256 identical, 200/200 sectors bit-identical (before vs after), 2-thread hashes = sequential hashes 9/9; `WhatIf.WorldGen.Tests` golden hash unchanged (check.ps1) | identical | PASS |
| b5 no regression | check.ps1 + verify-hygiene.ps1 exit 0 (outbox); runtime run 435 s | <= 8 min | PASS |

The gen0 baseline of the drive is the land path run with the one `SolveProfile` call in `VerticalCurves` temporarily on the allocating overload (same build, same path, back to back with the after run; that overload allocates exactly what the old `Clone` calls did). The region build P95 did not improve in the drive (28.6 s vs 41.3 s longest before, different load pattern): the build is CPU bound on the vertical-curve arithmetic and shares the notebook with the main thread.

### b2: stage profile per region build (9 regions of block B, before / after)

Sequential (one thread, warm-up build excluded):

| stage | median ms before / after | max ms before / after | median MB before / after | max MB before / after |
|---|---|---|---|---|
| TOTAL BuildSurface | 8699.3 / 7943.8 | 27789.5 / 26342.8 | 1498.82 / 37.92 | 4224.96 / 55.44 |
| Surface.ProbeOcean | 0.0 / 0.0 | 0.0 / 0.0 | 0.00 / 0.00 | 0.00 / 0.00 |
| Surface.PickCity | 0.0 / 0.0 | 0.3 / 0.3 | 0.00 / 0.00 | 0.00 / 0.00 |
| Generator.Run | 8699.3 / 7943.8 | 27789.4 / 26342.8 | 1498.82 / 37.92 | 4224.96 / 55.44 |
| Execute | 8693.7 / 7937.3 | 27784.2 / 26336.7 | 1495.31 / 34.46 | 4219.98 / 50.46 |
| PlaceSettlements | 0.0 / 0.0 | 0.2 / 0.3 | 0.00 / 0.00 | 0.01 / 0.00 |
| BuildMotorway | 4.8 / 6.4 | 12.8 / 12.3 | 2.52 / 2.52 | 3.59 / 3.58 |
| BuildStreets | 29.6 / 30.5 | 86.8 / 90.9 | 13.58 / 13.58 | 14.35 / 14.35 |
| BuildInterchange | 1.5 / 2.6 | 3.7 / 4.7 | 0.67 / 0.67 | 0.76 / 0.76 |
| ConnectPrimary | 18.9 / 17.8 | 32.7 / 33.9 | 8.42 / 8.42 | 13.42 / 13.42 |
| ConnectSecondary | 5.3 / 6.7 | 32.6 / 30.1 | 1.02 / 1.02 | 12.18 / 12.18 |
| Assemble | 8634.3 / 7883.1 | 27683.0 / 26235.1 | 1463.71 / 7.79 | 4177.91 / 8.39 |
| DropWaterEdges | 3.4 / 3.6 | 10.1 / 6.7 | 0.04 / 0.04 | 0.04 / 0.04 |
| MatchWidths | 0.2 / 0.3 | 3.6 / 3.5 | 0.24 / 0.24 | 0.25 / 0.25 |
| TerrainBind | 1.7 / 3.1 | 4.6 / 4.9 | 3.22 / 3.22 | 4.69 / 4.69 |
| Asm.PlanBridges (before: detail run) | 0.0 / 0.0 | 0.1 / 0.1 | 0.00 / 0.00 | 0.00 / 0.00 |
| Asm.PlateauLegs (before: detail run) | 0.6 / 0.5 | 3.9 / 4.2 | 0.38 / 0.38 | 0.41 / 0.41 |
| Asm.BuildSamples (before: detail run) | 8.1 / 8.0 | 23.3 / 23.7 | 3.58 / 3.58 | 3.66 / 3.66 |
| Asm.ProfileRoad (before: detail run) | 8608.2 / 7874.6 | 27199.8 / 26224.1 | 1458.37 / 2.43 | 4172.38 / 2.86 |
| PR.Smooth (before: detail run) | 0.5 / 0.5 | 0.6 / 0.5 | 0.31 / 0.31 | 0.32 / 0.32 |
| PR.Solve (before: detail run) | 0.8 / 0.8 | 1.1 / 6.0 | 0.33 / 0.33 | 0.41 / 0.41 |
| PR.VCurves (before: detail run) | 8589.5 / 7854.4 | 27179.5 / 26204.7 | 1456.45 / 0.54 | 4170.26 / 0.74 |
| PR.Relax (before: detail run) | 0.2 / 0.1 | 0.5 / 0.3 | 0.05 / 0.05 | 0.13 / 0.13 |
| PR.RampPin (before: detail run) | 14.3 / 15.1 | 15.3 / 16.0 | 0.00 / 0.00 | 0.00 / 0.00 |
| PR.Crossing (before: detail run) | 0.7 / 0.6 | 0.8 / 0.7 | 0.03 / 0.03 | 0.04 / 0.04 |

Two threads (`Parallel.For`, MaxDegreeOfParallelism 2):

| stage | median ms before / after | max ms before / after | median MB before / after | max MB before / after |
|---|---|---|---|---|
| TOTAL BuildSurface | 9755.5 / 8831.4 | 31796.2 / 29127.7 | 1498.82 / 37.92 | 4224.96 / 55.44 |
| Surface.ProbeOcean | 0.0 / 0.0 | 0.0 / 0.0 | 0.00 / 0.00 | 0.00 / 0.00 |
| Surface.PickCity | 0.0 / 0.0 | 0.0 / 0.0 | 0.00 / 0.00 | 0.00 / 0.00 |
| Generator.Run | 9746.5 / 8822.5 | 31796.2 / 29127.6 | 1498.81 / 37.92 | 4224.96 / 55.44 |
| Execute | 9739.8 / 8814.7 | 31790.5 / 29122.2 | 1495.31 / 34.46 | 4219.98 / 50.46 |
| PlaceSettlements | 0.0 / 0.0 | 0.9 / 0.1 | 0.00 / 0.00 | 0.00 / 0.00 |
| BuildMotorway | 6.8 / 5.1 | 11.0 / 12.4 | 2.52 / 2.52 | 3.58 / 3.58 |
| BuildStreets | 37.2 / 32.1 | 43.6 / 41.7 | 13.58 / 13.58 | 14.35 / 14.35 |
| BuildInterchange | 1.5 / 1.4 | 2.1 / 2.9 | 0.67 / 0.67 | 0.76 / 0.76 |
| ConnectPrimary | 12.0 / 11.8 | 23.5 / 21.9 | 8.42 / 8.43 | 13.42 / 13.42 |
| ConnectSecondary | 2.8 / 4.1 | 27.0 / 28.7 | 1.02 / 1.02 | 12.18 / 12.18 |
| Assemble | 9685.6 / 8751.8 | 31693.9 / 29035.2 | 1463.71 / 7.79 | 4177.91 / 8.39 |
| DropWaterEdges | 3.5 / 3.7 | 5.4 / 4.0 | 0.04 / 0.04 | 0.04 / 0.04 |
| MatchWidths | 0.1 / 0.1 | 1.7 / 1.0 | 0.24 / 0.24 | 0.25 / 0.25 |
| TerrainBind | 2.6 / 3.3 | 4.8 / 4.0 | 3.22 / 3.22 | 4.69 / 4.69 |
| Asm.PlanBridges (before: detail run) | 0.0 / 0.0 | 0.0 / 0.0 | 0.00 / 0.00 | 0.00 / 0.00 |
| Asm.PlateauLegs (before: detail run) | 0.5 / 0.5 | 1.4 / 0.7 | 0.38 / 0.38 | 0.41 / 0.41 |
| Asm.BuildSamples (before: detail run) | 8.5 / 8.7 | 9.9 / 20.1 | 3.58 / 3.58 | 3.66 / 3.66 |
| Asm.ProfileRoad (before: detail run) | 9555.8 / 8740.9 | 31430.7 / 29023.2 | 1458.37 / 2.43 | 4172.37 / 2.86 |
| PR.Smooth (before: detail run) | 0.6 / 0.5 | 0.6 / 0.5 | 0.31 / 0.31 | 0.32 / 0.32 |
| PR.Solve (before: detail run) | 1.0 / 0.8 | 1.2 / 2.7 | 0.33 / 0.33 | 0.41 / 0.41 |
| PR.VCurves (before: detail run) | 9535.9 / 8718.4 | 31409.2 / 29002.8 | 1456.45 / 0.54 | 4170.26 / 0.74 |
| PR.Relax (before: detail run) | 0.3 / 0.1 | 1.3 / 2.3 | 0.05 / 0.05 | 0.13 / 0.13 |
| PR.RampPin (before: detail run) | 15.9 / 16.5 | 18.5 / 17.4 | 0.00 / 0.00 | 0.00 / 0.00 |
| PR.Crossing (before: detail run) | 0.8 / 0.7 | 0.8 / 0.7 | 0.03 / 0.03 | 0.04 / 0.04 |

Per-build total: sequential 8699 -> 7944 ms median, 27790 -> 26343 ms max; 2 threads 9756 -> 8831 ms median. The sub-stage rows (`Asm.*`, `PR.*`) were added after the first profile cycle; their "before" numbers come from a second run of the unoptimised code (`rg173-detail2`), the other rows from the first (`rg173-before`).
