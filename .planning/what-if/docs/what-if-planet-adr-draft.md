# What If: planet engine 2 (RG-155) — projection choice, ADR draft, experiment protocol

What If input (separate project); not a NightRacer ticket source.

- Ticket RG-155, Research class (ADR-035), author Claude Sonnet 5.5. Builds on `what-if-planet-engine.md` (RG-153); SHAs of section 1 there are reused. **Access date of all web reads: 2026-11-12 (UTC).**
- Labels: **V** = raw file at a pinned SHA (URL, line, quote <= 40 words) or paper/standard; **M** = measured in this project; **E** = external, not re-verified; **O** = own computation (formula or script named). Nothing was cloned, built or downloaded; about 12 page reads, raw text files in git-ignored `artifacts/tmp/rg155/`.
- New pin used (not in RG-153): `OSGeo/PROJ` master `890f8a0599ee4600344c8b630df5e4bc4762c83f` (2026-11-11T19:32:10Z, V `api.github.com/repos/OSGeo/PROJ/commits/master`). Licence: MIT-style (V `raw.githubusercontent.com/OSGeo/PROJ/890f8a0/COPYING:5`: "MIT style license"). Raw base below: `PROJ@890f8a0`.
- Reproduction of all O numbers: `artifacts/tmp/rg155/proj_metrics.py` (pure Python, 0.7 s, not committed; formulas are in the table notes so it can be rewritten).

## 1. Projection table

Face coordinates (u, v) in [-1, 1]; "cell" = a cell of equal size in (u, v) of the projection. Area and stretch are taken over one whole cube face (numeric, 200 x 200 samples, central differences; points within 1e-3 of the face diagonals skipped because QSC is piecewise there). **Max stretch** = largest ratio of the two principal scales of the local map (1.0 = conformal), i.e. the angular distortion.

| projection | cell area max/min | max stretch | forward cost | inverse | invertible | seams | label / source |
|---|---|---|---|---|---|---|---|
| **linear (gnomonic) cube**: p = (1, u, v)/norm | **5.196** (= 3^(3/2), corner 0.192 of centre) | 1.732 (corner, = sqrt 3) | 1 sqrt, 3 mul, 1 div (~10 flops) | face by max-abs, then 2 div (~6 flops) | closed form | C0 across edges; cell size changes fast near corners; edges need the neighbour table (90-degree rotations) | O: density (1+u^2+v^2)^(-3/2); script gives 5.1753 on the sampled grid (closed form 5.1962) |
| **equal-angle (tangent) cube**: a = (4/pi) atan(u) | **1.414** (= sqrt 2) | 1.732 (corner) | 2 atan (inverse direction) / 2 tan (forward) + 1 sqrt | 2 tan + normalise | closed form | C0, equal cell widths along the edges, the slope is continuous in magnitude | O script 1.4141. **RG-153's "about 1.3:1" is wrong: it is 1.41 (sqrt 2).** The warp itself is the standard "equal-angle" cube-sphere (E, Ronchi et al. 1996, not re-fetched) |
| **COBE quadrilateralized spherical cube (QSC)**, O'Neill-Laubscher 1976 | **1.001** (equal-area, constant pi/6) | 1.55 | ~1 atan2, 2 acos, 1 atan, 1 sqrt, 3 trig, 1 div per point | closed form (V): `t = (pi/12)*tan(mu)`, then sin/cos/atan | closed form | C0; piecewise per face quarter (4 areas per face), mapping not smooth across the 45-degree diagonals | V `PROJ@890f8a0/src/projections/qsc.cpp:2`: "This implements the Quadrilateralized Spherical Cube (QSC) projection."; forward `:215-216`: `mu = atan((12.0 / M_PI) * (theta + acos(sin(theta) * cos(M_FORTPI)) - M_HALFPI));`; inverse `:267-268`: `t = (M_PI / 12.0) * tan(mu); tantheta = sin(t) / (cos(t) - (1.0 / sqrt(2.0)))`. Paper cited in file header `:6-8`: O'Neill and Laubscher, NEPRF 3-76 (CSC), May 1976. Area/stretch: O script (constant 0.5236 = pi/6 in 6 of 6 probe points off the diagonals) |
| lat/long (equirectangular), for contrast | cos(lat): **573 : 1 at 89.9 deg, 5730 : 1 at 89.99 deg** (vs equator) | 1/cos(lat): same figures | 2 trig | 1 asin, 1 atan2 | closed form, singular at poles | 360-degree wrap; the pole is a line | O: cell width 256 m * cos(89.9 deg) = 0.45 m (RG-153 section 5) |

Source status of the other rows: gnomonic and equal-angle are closed-form O (derivation: area density of x = p(u,v)/|p| is (1+u^2+v^2)^(-3/2); the warp is evaluated numerically in the script, closed form sqrt 2 matches). SFSIM uses the **linear** variant: V `sfsim@caff64f/src/clj/sfsim/cubemap.clj:144-147` `project-onto-sphere` on linear face coordinates (`:32-37`, RG-153 section 3, SFSIM item 7).

Cell edge at a level-10 region (face edge pi R / 2 = 10 018 539 m, 1024 tiles = 9784 m at centre, O): corner width = centre width * sqrt(area ratio^-1): linear **4.3 km**, equal-angle **8.2 km**, QSC **9.8 km** (area-based mean). The 256 m sector grid is unaffected (it lives in the per-sector frame).

**Choice for the ADR: equal-angle (tangent) warp for ids, with QSC kept as a candidate only if the experiment shows the 1.41 : 1 spread hurts.** Reasons: (1) 3.7x less area spread than linear for two `tan`/`atan` calls (O); (2) tile edges stay straight lines in the gnomonic picture, so the SFSIM-style neighbour table (V `quadtree.clj:482-511`) carries over unchanged, whereas QSC's piecewise map has no such simple edge behaviour; (3) QSC's advantage (1.00 area) buys nothing for us because streaming is by 256 m sectors, not equal-area samples; its stretch is not better (1.55 vs 1.73). Exact equal area is only needed for statistics/biome counting: not an ADR concern.

## 2. RG-153 section 9 items closed from raw files

| open item (RG-153 §9) | result | label / evidence |
|---|---|---|
| SFSIM horizon culling | **No tile culling by horizon found.** The only "horizon" in the SFSIM modules read is camera attitude (`camera.clj:84`: "(defn horizon-system"; `:85` "Determine horizon-aligned camera matrix for given nose or speed vector and position"). `quadtree.clj`, `planet.clj`, `cubemap.clj`, `sphere.clj`, `render.clj`, `graphics.clj` contain no `cull`/`frustum`/`visible`/`horizon` use except GL face culling in `render.clj:55-65`. GLSL (`tess-evaluation.glsl`, `fragment-*.glsl`) not read; `core.clj` not grepped | V negative, grep on raw files at `caff64f` (absence in unread GLSL is UNKNOWN) |
| SFSIM LOD hysteresis | **None.** The drop test uses the same predicate as the increase test: `quadtree.clj:134`: `(and (is-flat? tree) (not (increase-level-fun? (::level tree) (::center tree)))) (sub-paths path)`; load list uses the same `increase-level-fun?` (`update-level-of-detail`, `:331-340`). A tile at the threshold can flip every frame; SFSIM masks this with its `future` update (`planet.clj:236`) | V |
| SFSIM "about 1.3:1" equal-angle figure | **Not in SFSIM and numerically wrong**: SFSIM uses linear face coordinates (above) so its spread is 5.2:1; an equal-angle warp is 1.414:1 (O). The intake figure stays E and is replaced by 1.41 | V (cubemap.clj) + O |
| Exolune runtime pole test, SFSIM runtime corner test, Intel UHD behaviour | UNKNOWN: need clone/build/GPU; unchanged from RG-153 | - |

## 3. ADR draft (Proposed; project What If; not `decisions.md`)

**WI-ADR-001: Planet addressing, frames and handoff to local sectors** — status **Accepted** 2026-11-12 (RG-164 all PASS; proposed 2026-11-12, based on RG-153/RG-155).

> **Experiment RG-161 (2026-11-12): (a) FAIL at region/cube-corner borders, (b) (c) (d) PASS.** Rows 1, 2, 4 confirmed; see section 6.
> **Row 3 revised 2026-11-12 (orchestrator, on the user's mandate: deformed tiles accepted, exact squares not required): sector = leaf cell of the per-face equal-angle quadtree (level 15), see section 7.** Stays Proposed until RG-162 passes (a)-(d) on the revised row 3.
> **Experiment RG-162 (2026-11-12): (a) (b) (c) (d), r1, r3, r4, r5 PASS; r2 FAIL (road length change 0.5236 % vs < 0.5 %, 1 arc of 3000).** Orchestrator diagnosis: a portal near a region vertex lay off its border segment before the snap (max shift 368.7 m > the 256 m a snap can do) -> follow-up RG-163 (crossing attribution + joint snap), thresholds unchanged. Stays Proposed until RG-163 passes r2. Review `agent_pool/shared/reviews/RG-162.md`.
> **Experiment RG-163 (2026-11-12): crossing attribution fixed (off-segment portals 205 -> 0), group snap at region vertices; a/b/c bit-identical, all other rows PASS; r2 road length still 0.5236 % (FAIL, 1 arc of 3000: 30 km test arc ends 159 m after a portal that must move 160 m sideways — a lower bound for that arc).** Thresholds unchanged; the r2 metric is a user decision (Decisions inbox). Stays Proposed. Review `agent_pool/shared/reviews/RG-163.md`.
> **User decision 2026-11-12 ~16:20 (via AI dohled):** r2 road length is measured on arcs whose both ends are >= 1 km from the nearest shifted portal; the shift line is restated as single-portal `Portal.Snap` shift <= 256 m + 0 off-segment portals. Re-run = RG-164; all PASS -> Accepted. **Standing mandate (What If experiments only):** the orchestrator may adjust experiment acceptance itself when it justifies and reports the change (review + handover); the user is asked only about direction and licences. ADR-028 for NightRacer tickets is unchanged.
> **Experiment RG-164 (2026-11-12): ALL PASS (294 rows).** r2 length over eligible arcs 0.4029 % < 0.5 (2992 of 3000 eligible; 8 excluded = 0.27 %, max 0.5236 %), single-portal `Portal.Snap` shift 255.99 m <= 256, off-segment 0, s mismatch 0; a/b/c + r1 byte-identical to RG-163; orchestrator rerun reproduces every non-timing row. **WI-ADR-001 Accepted.** Review `agent_pool/shared/reviews/RG-164.md`.

**Context.** What If wants a persistent realtime planet; NightRacer/WorldGen is the base (planar 256 m sectors, 65 x 65 at 4 m, `SectorStreamer`, M). A sector is 256 m, a planet 6 378 km; a flat plane drops 1.3 mm at the sector edge and 1.96 m at 5 km (O, RG-153 §5), float32 has 0.5 m spacing at Earth radius (O). Exolune (PolyForm Shield, clean-room only) shows double 3D position + parallel-transported frame; SFSIM (EPL-1.0) shows cube-face quadtree ids + a separate small local mesh for physics (V, RG-153 §4).

**Decision.**

| # | decision | from |
|---|---|---|
| 1 | Authority = **double 3D vector in a planet-fixed frame**; attitude = (vertical, heading) carried by **parallel transport**; no lat/long in vehicle code. Lat/long only at UI/import edges | RG-153 §6.1 C |
| 2 | **Addressing = cube-sphere with equal-angle warp** (section 1): `RegionId = (face, level, row, col)`, level 10 as default region (~8-10 km); neighbour table with rotations for the 12 edges/8 corners | §6.1 B + this ticket |
| 3 | **(revised 2026-11-12, section 7)** **Sector = leaf cell of the per-face equal-angle quadtree at level 15** (~306 m along the face axes, ~256 m at the cube corners; deformed quads, area spread 1.41:1, one 120° corner at the 8 cube vertices); `SectorId = (face, level 15, row, col)`, region = its level-10 ancestor (32 × 32 sectors). Vertices on a face-wide integer lattice (64 intervals per sector, 3.38-4.78 m; RG-166), **world position computed in double from the canonical lattice point** (vertices on cube edges/corners canonicalised to one face), so neighbours share edge vertices exactly. Each sector keeps its **own tangent frame** (origin = centre, up = radial) only for float-local mesh data and vehicles; sector content = f(worldSeed, SectorId); no neighbour needed to load. ~~planar 256 m squares in a per-region tangent grid~~ (RG-161 (a) FAIL: 56-300 m at region borders) | §6.2 B, RG-161, section 7 |
| 4 | Noise: **CPU double authority**; lattice cell = int64 from `floor(x/cell)`, fraction computed in double then rounded to float; GPU receives tile-relative data; no GPU FP64 requirement; GPU double-float pairs only as later tier | §6.3 B |
| 5 | Planet layer = render/cache only; WorldGen sectors authoritative for collision/roads/buildings within a speed- and altitude-dependent `LoadRadius`; planet-layer budget (O, to be measured): <= 6 ms P95 Medium, <= 256 MiB GPU shared, <= 150 MiB RAM | §6.4 |
| 6 | LOD: projected-size test (SFSIM idea) **with hysteresis** (drop threshold at 0.8 x the increase threshold; SFSIM has none, V section 2), updated off the render thread | §7 take 4 + this ticket |

**Options rejected.**

| option | one line |
|---|---|
| lat/long grid as authority | pole singularity: 573:1 cell collapse at 89.9 deg, east/north undefined (O) |
| linear cube ids | 5.2:1 area spread; corner regions only 4.3 km (O) |
| QSC ids | equal area but piecewise map, no simple edge table, stretch not better (O/V) |
| one tangent plane per 10 km region | sector at region edge floats 1.96-3.9 m (O) |
| every vertex in double from the sphere | ~~exact but rewrites the planar 4 m WorldGen code~~ **adopted in revised row 3** (section 7): seamless only this way; WorldGen gets a deformed-quad adapter later |
| planar 256 m squares per region tangent grid (original row 3) | RG-161 (a) FAIL: neighbouring region grids differ in phase and rotation, 56-300 m at borders |
| planar sectors + stitched border strips (RG-161 option A) | needs irregular stitch triangles along every 10 km border and special road/collision hand-off; dominated by the quadtree cell once deformation is accepted |
| GPU FP64 noise (Exolune gate) | availability on Intel UHD unknown (C); RX 470 unavailable |
| copying Exolune code/shaders | PolyForm Shield: clean-room only |

**Consequences.** + No pole/face-edge branch in vehicle code; stable ids and caches; WorldGen math stays planar and Unity-free. - Cube corners (8, three faces) and 12 edges need explicit tests; heading drifts by holonomy (about 2e-5 rad on a 100 km loop, O) so "north" is a display-only concern; stable ids require the neighbour table to be written and tested by us; 256 m cannot be quadtree-aligned (305.7 m at level 15, O), so the sector grid exists only in the planar frame.

**Numbers the experiment must confirm (O predictions in brackets).**

| quantity | threshold | predicted |
|---|---|---|
| shared-edge vertical mismatch | < 1 mm | ~0 (both sides drop 1.3 mm, symmetric) |
| noise value at a point from two origins | bit-identical | identical |
| frame discontinuity over pole / cube corner | < 1e-9 rad per step | double round-off |
| ms per sector / sectors/s | within budget of section 4 | 0.3-0.4 ms (M, WorldGen today) |

## 4. Experiment protocol (CPU only; no Unity, no GPU, no engine; **no code written in this ticket**)

**Library.** New Unity-free project of What If, netstandard2.1 like `src/WhatIf.WorldGen` (V `src/WhatIf.WorldGen/*.csproj:6` `TargetFramework netstandard2.1`): `WhatIf.Planet` (cube ids, projection, frames, sector frame builder) + `WhatIf.Planet.Tests` + a console `WhatIf.Planet.Experiment` that writes the result table. It reuses `WhatIf.WorldGen` terrain height by project reference only for check (d). **Size estimate (O):** 7-9 files, 700-1000 lines, 8-12 hours Sonnet, no Unity.

**Inputs.**

| id | place | purpose |
|---|---|---|
| R0 | face 0 centre, lat 0, region level 10 | baseline |
| R45 | region containing lat 45 deg, lon 20 deg | mid latitude |
| R89 | region containing lat 89.99 deg (any lon) | pole (cube top face centre, no cube pole; the lat/long contrast must fail here, 5730:1) |
| RC | region at a cube corner (three faces meet; e.g. unit direction (1,1,1)/sqrt3) | corner / table test |
| seeds | worldSeed in {1, 42, 0x7FFFFFFF}; region seeds from `ForStage(worldSeed, "region"+face+level+row+col)` | determinism |
| speeds | 100, 200, 900 km/h | check (d) |

**Checks and exact pass thresholds.**

| # | check | procedure | PASS when |
|---|---|---|---|
| a | shared-edge mismatch | for each of R0/R45/R89/RC: build all 4-neighbour sector pairs in a 5 x 5 sector block (and the 4 sectors around each region-border and cube-corner vertex); take the 65 edge vertices of each side; compare **radial (vertical) distance** between the two world positions; also report horizontal | max vertical < **1 mm** and max horizontal < **1 mm** over all pairs; sectors across a region or cube-edge border included |
| b | noise from two origins | 10^6 random points per region, seeds above; evaluate each point from sector origin A, from origin B 256 m away, from an origin 100 km away; compare the float result bits | **0** differing bits (bit-identical) when the same double world x is used; if x is rebuilt as origin + local, report the count of differing results and require <= 1 float ulp |
| c | frame transport | drive a circle of radius 1 km around the cube-face centre at lat 89.99 deg (1 m steps) and a closed loop of radius 1 km over a cube corner; at every step compare the transported frame with the previous one | per-step rotation deviates from the analytic parallel-transport angle by < **1e-9 rad**; no step jumps > 2x median; closing holonomy = enclosed area/R^2 within **1e-9 rad** (R = 6 378 000 m) |
| d | cost | build 1000 sectors per region (65 x 65 vertices, height + frame, no vegetation), single thread, release build, warm; report median and P95 ms/sector; derive sectors/s needed from section 6.4 of RG-153 (0.5 / 1.0 at 600 m for 100/200 km/h; 4.6 at 600 m and 38 at 5 km for 900 km/h) | per-sector cost <= **0.5 ms** median (WorldGen today 0.3-0.4 ms, M) and P95 <= 3 ms (one pump); CPU share at 900 km/h / 5 km < **2 %** of one core (38 x 0.5 ms = 19 ms/s) |

**Output table (CSV + Markdown, one row per region x seed x check):** `check | region | seed | metric | measured | threshold | pass`. Plus a hardware line (CPU model, i3-1215U or other, power mode), commit SHA, and wall time. A table with an `n/a` for any cell is a FAIL of the protocol, not a pass. Check (d) must be run on the notebook i3-1215U with the measure lock/slot taken, not while the user rides; never invent RX 470 or Intel UHD GPU numbers (CPU only).

**Decision rule after the run.** All 4 PASS -> WI-ADR-001 becomes Accepted. (a) fails at region or cube-corner borders -> sector frame must be taken from the region anchor, re-decide. (b) fails -> int64 lattice rule is wrong. (c) fails at the corner -> add the missing cube-edge rotation. (d) fails -> keep the planet layer coarser (64 m grid) and restrict fine sectors.

## 5. What the user must decide before What If starts

| # | decision | recommendation |
|---|---|---|
| 1 | Is What If a new repository/solution, or a folder in race_game (`src/WhatIf.*`)? | separate repository later; start as `src/WhatIf.Planet` Unity-free library so WorldGen reuse stays trivial (memory: libs Unity-free) |
| 2 | Planet radius: real Earth (6 378 000 m) or a smaller playable planet? (affects drops, holonomy and level-10 region width) | real radius for the experiment; decide scale only after (a)-(d) pass |
| 3 | Is the equal-angle warp accepted, or is the 5.2:1 linear spread acceptable? | equal-angle (1.41:1) |
| 4 | May the experiment run on the notebook CPU (estimate: minutes) outside ride times? | yes, after the evening ride |
| 5 | Licence stance: SFSIM EPL-1.0 study and independent reimplementation OK; Exolune never copied. Any code reuse beyond ideas needs your explicit decision | keep both as idea sources only |

## 6. Experiment result (RG-161, 2026-11-12; review `agent_pool/shared/reviews/RG-161.md`)

Library `src/WhatIf.Planet`, runner `tools/whatif-planet/run-experiment.ps1`, table `what-if-planet-experiment.{md,csv}` (156 rows, i3-1215U, commit `ebc367f4`). Orchestrator rerun: a/b/c raw output bit-identical, (d) within 8 %.

| check | verdict | numbers (M) | meaning |
|---|---|---|---|
| a | **FAIL** | vertical ~2e-6 mm everywhere; horizontal inside a region 0.263 mm; across region borders 56-300 m; with B placed exactly 256 m across A still 184 mm (R45) / 94 m (RC) | row 3 incomplete: per-region planar grids differ in phase and rotation, so they cannot share edges at region borders. "Frame from the region anchor" (decision rule) is already used for the axes and does not close the seam |
| b | PASS | same double x from 3 origins: 0 differing bits of 3e6; origin + local in double: <= 1 ulp | row 4 confirmed; **add: local offsets stay double on the CPU** (float local -> ~65 % of results differ, up to 0.45 mm) |
| c | PASS | per step <= 2.3e-16 rad; holonomy error ~1e-14 rad (pole circle, cube corner) | row 1 confirmed; no pole/corner branch needed |
| d | PASS | ~0.2 ms/sector (65 x 65 height + frame), P95 <= 0.73 ms, ~0.8 % of a core at 900 km/h | planet layer cost is small; WorldGen content not included |

**Row 3 re-decision (user's decision, What If direction):**
- **A (recommended): planar sectors inside a region + explicit border.** Border sectors take their shared-edge vertices from the sphere in double, or a short skirt/overlap strip hides the seam; roads hand off through RG-160 portals `(s, angle)`. Keeps WorldGen planar; consistent with RG-160 (CDLOD hole mask + skirt). Needs a follow-up CPU check (border strip < 1 mm vertical + cost), best folded into RG-160 D6 (road experiment r1-r5).
- B: one sector grid per cube face in (a, b) space (cells ~256 m, sheared); seamless inside a face, 12 edges / 8 corners still special; breaks the square planar sector.
- C: accept seams every ~10 km: rejected.

## 7. Row 3 decision (orchestrator, 2026-11-12, on the user's mandate)

User via AI dohled, verbatim: „S deformací dlaždic počítám, jinak to nepůjde, hlavně v blízkosti pólu. To měla vyřešit research a agent má zvolit nejlepší postup.“ So exact 256 m squares are not a requirement and the orchestrator picks.

**Choice: sector = leaf cell of the per-face equal-angle quadtree (option B of section 6, made quadtree-aligned).**

Why (evidence):
1. **Seamless by construction.** For an axis-separable warp, every line a = const is a great circle (RG-160 §4, O), so every quadtree cell edge is a great-circle arc shared by exactly the two cells beside it. Vertices sit on one face-wide integer lattice (2^15 sectors x 64 intervals = 2^21 per face side); a = I / 2^20 - 1 is exact in double, so two sectors evaluate the same shared vertex from the same integers and get the same bits. Vertices on the 12 cube edges and at the 8 corners are canonicalised to one face (lowest face index) through the neighbour table that RG-161 already tests. Option A (planar sectors + stitch strips) would need irregular stitching along every region border; with deformation accepted it is strictly more work.
2. **Same tree as the far layer.** RG-160 D4 (accepted): CDLOD-style far layer = quadtree per face. Near sectors being level-15 leaves make the far-layer hole mask exact at sector granularity (RG-160 D5: step hidden by skirt + fog).
3. **Same borders as the roads.** RG-160 portals `(s, angle)` assume region borders are great circles — true for level-10 cell edges; nothing to adapt.
4. **Deformation is bounded and known.** Equal-angle: area spread 1.414:1, max stretch 1.73 only in the corner cells (RG-155 §1, O); the pole is a face centre (least deformed). Level 15 instead of a 256 m target keeps the quadtree aligned (ADR consequence "256 m cannot be quadtree-aligned" is dropped): ~306 m along the face axes, ~256 m wide at the cube corners (RG-155 corner region 8.2 km vs 9.8 km). **Corrected by RG-162 (M, recomputed by the orchestrator):** sector sides 305.74 m at the face centre, 216.19 m (= 305.74/sqrt 2) for the sides parallel to the cube edge at its midpoint (the sides running to the edge stay 305.74 m; RG-166), 288.25 m (= 305.74 * 2 sqrt 2 / 3) at the cube vertices with one 120° corner; area ratio 1.414 = face centre (93 478 m2) vs cube-edge midpoint (66 100 m2); the cube-vertex cell is 71 959 m2 (RG-166). The "~256 m at the corners" estimate was wrong.
5. **What RG-161 already proved stays valid:** (b) local offsets in double, (c) parallel transport, (d) ~0.2 ms/sector — the new build differs only in where the vertex directions come from (separable: 65 + 65 tan per sector, not per vertex).

Consequences: + no seam rule, no stitch strips, no border special case in rendering or collision; one tree for near and far; roads unchanged. - WorldGen's planar generator (square 256 m, 4 m) needs a deformed-quad adapter later (parameter (s, t) in [0, 1]^2 -> local position); spacing varies 3.38-4.78 m (RG-166); corner cells have one 120° corner (r4 checks a road refined there).

**Verification: RG-162** re-runs (a)-(d) on this geometry with the section 4 thresholds unchanged (+ a deformation report) and runs the RG-160 road experiment r1-r5. All pass -> WI-ADR-001 Accepted. (a) fails -> the canonical-vertex rule is wrong (fix, not a re-decision).
