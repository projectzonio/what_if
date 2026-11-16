# What If: planet engine research (RG-153)

What If input (separate project); not a NightRacer ticket source.

- Ticket RG-153, Research class (ADR-035), author Claude Sonnet 5.5, access date of all web reads **2026-11-11** (UTC).
- Basis: ChatGPT batch of 2026-11-06 (class E, `.planning/external/2026-11-06-chatgpt-planet-research/INTAKE.md`). This document replaces its locators with pinned ones and answers the four ADR questions.
- Labels (research-rules): **V** = verified in a primary raw file at a pinned SHA (URL + line + quote <= 40 words), **M** = measured in this project, **E** = external claim not re-verified, **O** = own computation or reasoning (formula given).
- Raw URL form: `https://raw.githubusercontent.com/<owner>/<repo>/<sha>/<path>`; `X:n` below means the raw-file line n. No clone, build, download or run was done; about 28 page reads in total.
- Licence rule: Exolune is PolyForm Shield 1.0.0 = clean-room only (ideas, no code). SFSIM is EPL-1.0; its FAQ allows study and independent reimplementation (E, intake V 2026-11-09). Nothing below is copied code.

## 1. Pinned snapshots

| repo / branch | SHA | commit date (UTC) | source |
|---|---|---|---|
| exolunehq/exolune `main` | `c71dacf824ac73733869bf96a116d6e6bb1f39dc` | 2026-11-11T22:27:50Z | V `api.github.com/repos/exolunehq/exolune/commits/main`; subject "Merge the hill city, its cliff and its guideway into main" |
| wedesoft/sfsim `main` | `caff64fcc18f08998ff7727a29adb90f0e4e8166` | 2026-11-11T22:27:37Z | V `api.github.com/repos/wedesoft/sfsim/commits/main`; subject "Catch infinity value in time-lapse-limit-rotation" |
| wedesoft/sfsim `windows` | `398a47873622a56ca60d9026628d7aba8fe94638` | 2026-11-10T22:36:58Z | V `api.github.com/repos/wedesoft/sfsim/commits/windows`; subject "Merge branch 'main' into windows" |

Both `main` heads moved within seconds of each other on 2026-11-11, so the pin is only valid as a SHA; "main" is not. Trees: Exolune 1156 entries, SFSIM tree read from `git/trees/<sha>?recursive=1` (neither truncated for Exolune; SFSIM listing used for `src/clj/sfsim/*.clj`, 51 files).

## 2. Intake claims re-located at the pinned SHA

| claim (intake 2026-11-09) | status at pin | evidence (V) |
|---|---|---|
| Exolune FP64 gate | **true; line moved again**: now 570-571 (intake said 567, package said 520-524) | `exolune@c71dacf/MyPlanetEngine/D3DApp.cpp:570-571`: `if (!options.DoublePrecisionFloatShaderOps)` / `throw std::runtime_error("This GPU has no double precision shader support, needed by the terrain noise.");` |
| Exolune licence PolyForm Shield 1.0.0 | true | `exolune@c71dacf/LICENSE.md:3`: "# PolyForm Shield License 1.0.0" (line 1 is the "Required Notice: Copyright 2025-2026 Etienne Lescot" line) |
| SFSIM licence EPL-1.0 | true | `sfsim@caff64f/LICENSE:1`: "Eclipse Public License - v 1.0" |
| `deps.edn` versions | true; file now 55 lines (was 54) | `sfsim@caff64f/deps.edn:1`: `{:deps {org.clojure/clojure {:mvn/version "1.12.6"}`; `deps.edn:19`: `org.lwjgl/lwjgl {:mvn/version "3.4.3"}` |
| `globe.clj` header | true, lines 1-5 (file 94 lines) | `sfsim@caff64f/src/clj/sfsim/globe.clj:2`: "SPDX-License-Identifier: LGPL-3.0-or-later OR EPL-1.0+"; `:4`: "licensed under the Eclipse Public License v1.0" |
| SFSIM latest ChangeLog | **0.38-2, 8 Nov 2026** (unchanged since intake) | `sfsim@caff64f/ChangeLog:1`: "sfsim (0.38-2)"; `:7`: "-- Jan Wedekind ... Sun,  8 Nov 2026 22:44:50 +0000" |
| SFSIM FAQ figures (Steam Deck 25 fps etc.) | not re-read; stays E (intake V on the rendered page, 2026-11-09) | `wedesoft.github.io/sfsim/faq/` |

## 3. Triage of the 36 UNKNOWN items

Status codes: **R** = resolved V (evidence given), **P** = partly resolved, **C** = needs clone-build-run (reason), **X** = out of scope for What If. "Feeds" = which of the four questions (Q1 poles, Q2 handoff, Q3 noise, Q4 budget).

### Exolune (`09_UNRESOLVED_QUESTIONS.md`, 16 items)

| # | item | feeds | status |
|---|---|---|---|
| 1 | exact HEAD SHA/date, diff vs `dc24e0b` | - | **R** SHA/date in section 1; the diff itself X |
| 2 | full clone, tree, submodules | - | **P** tree listing read (1156 entries); clone forbidden here, submodule state not checked; X for What If |
| 3 | `FaceCells`, `RingGrowth`, `MaxRings`, strides | Q1, Q4 | **R** `exolune@c71dacf/MyPlanetEngine/D3DApp.h:203`: `static const UINT FaceCells = 192;` `:204`: `static const UINT MaxRings = 2048;` `:207`: `static constexpr double RingGrowth = 1.0 + 2.0 / FaceCells;` Vertex stride/buffer sizes not read (C if needed) |
| 4 | non-Draw six-face terrain stitching | Q1 | **R (negative)** the render mesh is not a six-face cube: `D3DApp.h:201-202`: "The mesh: a face of FaceCells x FaceCells cells, then up to MaxRings square rings"; `D3DApp.cpp:2168`: "The face: centred on the nadir, half-width proportional to the height above the ground." A fixed-to-planet cube exists only as data: `docs/geometrie.md:148`: "the same function on a cube fixed on the planet, 6 × 1 024²" |
| 5 | pole-crossing continuity, camera teleport | Q1 | **P** frame maths is singularity-free by construction (`MyPlanetEngine/FlightFrame.h:54`: "A placement supplies a complete frame, including at exact nadir or zenith."; parallel transport `FlightFrame.h:94-103`); the captured runtime test is **C** (needs a build and a GPU with FP64) |
| 6 | mesh-frozen / max-ring behaviour | - | X (MaxRings value is R, behaviour irrelevant for the questions) |
| 7 | HdrFormat/SunFormat, material arrays, VRAM inventory | Q4 | X for formats; Exolune VRAM is not our budget (our anchor is M, section 6.4) |
| 8 | ocean constants, foam, destructors | - | X |
| 9 | vegetation LOD thresholds, impostors, strides | - | X (WorldGen has its own vegetation brief) |
| 10 | resize / recreation path | - | X |
| 11 | conventional shadow map | - | X |
| 12 | underwater renderer | - | X |
| 13 | FP64 shader dependency graph, no-FP64 feasibility | Q3 | **P** gate is unconditional at device creation (D3DApp.cpp:570-571); the noise has three lattice-split modes: `MyPlanetEngine/Noise.hlsli:5-7`: "exact, in doubles (gExactLattice); float, for cells of 4 m and more; in pairs of floats, about 48 bits, under LATTICE_PAIRS only"; `docs/geometrie.md:147`: "the splitting of each octave on the grid into two floats, 48 bits, instead of a double". Whether a build without the gate works = **C** |
| 14 | exhaustive dependency graph, build-only licences | - | X (clean-room only, we take no dependency) |
| 15 | reproducibility of published benchmarks | Q4 | X (E only: RTX 4070 Ti, 46-57 ms/frame per intake; not usable as a number for us) |
| 16 | Intel UHD / i3-1215U measurements | Q4 | **C** no published figure found; D3D12 device capability on our GPU is exactly the unknown that decides whether the gate even passes. Never invent it |

### SFSIM (`13_OPEN_QUESTIONS.md`, 20 items)

| # | item | feeds | status |
|---|---|---|---|
| 1 | clone, `git rev-parse` main/windows, diff | - | **R** SHAs in section 1; diff X |
| 2 | `core.clj` source vs rendered cache | - | **R** moot: all reads now come from pinned raw files |
| 3 | full `src/clj`, shaders, grammars, data tree | - | **R** `src/clj/sfsim/*.clj` listing (51 files) and `resources/shaders/planet/*` (9 files) from the tree API |
| 4 | build both branches | - | X |
| 5 | quadtree thresholds, hysteresis, balancing, culling | Q1, Q4 | **R/P** `src/clj/sfsim/quadtree.clj:26-31`: `fragment-size` = `radius*width / (distance * 2^level * (tilesize-1) * tan(angle/2))`; `:42-47` `increase-level?` compares it with `max-size`; `src/clj/sfsim/planet.clj:224`: `increase? (partial increase-level? tilesize (::radius config) width (to-radians 60.0) 5 7 position)` = max-size 5, max-level 7, fov 60 degrees (hard-coded, "TODO: use params"); neighbour flags `quadtree.clj:324-328` `check-neighbours`; tree update runs in a `future` (`planet.clj:236`, `:247`). Hysteresis: none seen in the lines read (`update-level-of-detail`, `quadtree.clj:331-340`, uses only `increase-level-fun?`); not exhaustively checked |
| 6 | vertex shader / `render-patches` / tessellation / edge flags | Q1 | **R** a patch is a 4-vertex quad: `resources/shaders/planet/tess-control.glsl:5` `layout(vertices = 4) out;`; the vertex shader only emits corner coordinates by `gl_VertexID` (`vertex.glsl:13-14`, "Vertex shader to pass through coordinates and texture indices"); outer tessellation chosen from a 4-bit neighbour mask (`tess-control.glsl:21` "Control amount of tessellation so that it matches with neighbouring patches.", `:25` `if ((neighbours & 1) != 0)`). Displacement itself lives in the evaluation shader (not read) |
| 7 | map-tiles, elevation-tiles, globe, util: projections, naming, encoding | Q1, Q2 | **R** source data are Mercator maps resampled into cube-face tiles: `map_tiles.clj:8` "Split up part of Mercator map into map tiles"; elevation tiles are raw 16-bit shorts (`elevation_tiles.clj:12,23` `slurp-shorts`); cube coordinate per tile `cubemap.clj:105-109` (`cube-coordinate`: `(tile + pixel/(tilesize-1)) / 2^level`); cube-to-sphere is plain normalisation: `cubemap.clj:144-147` `project-onto-sphere` ("Project a 3D vector onto a sphere") applied to `cube-map` (linear face coordinates, `cubemap.clj:32-37`). Tar layout and normal encoding not read (X) |
| 8 | horizon / frustum culling | Q4 | **C** the 9 files read contain no `horizon` token; absence in unread modules (render.clj 41 kB, graphics.clj) is not established |
| 9 | Blue Marble / Black Marble dimensions | - | X |
| 10 | NOAA `all10g` details | - | X |
| 11 | WGS84 outside `cubemap.clj` | Q2 | **P** spherical in `cubemap.clj:136`: "Convert latitude and longitude to cartesian coordinates assuming a spherical Earth" and `:162` (same for the inverse); other modules not checked |
| 12 | sea-level reference, bathymetry | - | X |
| 13 | DE430 coverage, SPK, clock conversions | - | X |
| 14 | aerodynamic coefficients, heat proxy | - | X |
| 15 | surface/orbit switching hysteresis | - | X for the four questions; noted for What If vehicle design (`core.clj:237-243` reads `get-position :sfsim.physics/surface`; the orbit domain was not read) |
| 16 | gravity terms | - | X |
| 17 | G-buffer, HDR, reversed-Z | - | X |
| 18 | profiling clouds, planet, IO, CPU on Intel UHD | Q4 | **C** needs JVM + LWJGL + OpenGL 4.5 (tessellation) on the target; not run here. FAQ's 25 fps Steam Deck stays E |
| 19 | cloud resolution scaling / bottleneck | Q4 | X (E: FAQ says clouds are the main bottleneck) |
| 20 | Jolt / LWJGL / assets licences | - | X (we neither depend on nor reuse them) |

Count: Exolune 16 + SFSIM 20 = 36. Resolved or partly resolved for the questions: Exolune 3, 4, 5, 13 (P), 16 (C); SFSIM 5, 6, 7, 11 (P), 8 and 18 (C). Everything else one line.

## 4. What the two engines actually do (V, short)

| topic | Exolune | SFSIM |
|---|---|---|
| terrain frame | camera-**nadir-centred** square face of 192 x 192 cells plus up to 2048 square rings, each ring 1+2/192 wider (D3DApp.h:203-207); face half-width proportional to height above ground, slides with the camera (D3DApp.cpp:2168-2169) | six cube faces, quadtree per face, 4-vertex patch quads tessellated on the GPU (quadtree.clj, tess-control.glsl) |
| poles | no pole special-casing needed: no lat/long grid in the mesh; frames by parallel transport (FlightFrame.h:94-103) | none: cube faces have no pole; face edges handled by an explicit neighbour table with rotation angles (`quadtree.clj:482-511` `neighbour-tile`, e.g. `face0 ... build-neighbour face3 180`) |
| precision | positions and frequencies in double, `Vec3d` shared between flight, placement and contact (FlightFrame.h:6-8, "Double precision is shared with terrain placement and contact at astronomical distances."); GPU needs FP64 | double on the CPU; local collision mesh = a 3 x 3 tile patch around the vehicle at fixed level, centred on `tile-center` (`planet.clj:393-413` `update-local-mesh`, `cubemap.clj:292-298` `tile-center`) |
| authority split | one function evaluates relief for render, contact and placement | global quadtree for render; **separate small local mesh for physics** (same file, `update-local-mesh`) |
| Earth shape | analytic body radius 6378000 m (D3DApp.h:200 `PlanetRadius`) | spherical (cubemap.clj:136) |

## 5. Numbers used below (O, formulas shown)

R = 6 378 000 m (Exolune's `PlanetRadius`, V above; WGS84 equatorial radius).

| quantity | formula | result |
|---|---|---|
| flat-plane drop at distance d | d^2 / (2R) | 128 m: **1.3 mm**; 181 m (sector corner, 128*sqrt2): **2.6 mm**; 256 m: **5.1 mm**; 357 m: **1.0 cm**; 5 km (10 km region, edge): **1.96 m**; 7.07 km (region corner): **3.9 m**; 10 km: **7.8 m** |
| arc vs chord at 5 km | d^3 / (24 R^2) | 0.13 mm (negligible; the drop is the whole error) |
| radius within which one tangent plane stays within 1 cm | sqrt(2 R 0.01) | 357 m |
| angle between neighbouring sector planes | 256 / R | 4.0e-5 rad = 0.0023 deg |
| float32 spacing (ulp) of a coordinate x | 2^(floor(log2 x) - 23) | at 6.378e6 m (Earth radius): **0.5 m** (round-off 0.25 m); at 100 km: **7.8 mm**; at 10 km: **0.98 mm**; at 1 km: **0.061 mm**; at 181 m: **0.015 mm** |
| float32 spacing for a unit-sphere coordinate near 1.0 | 2^-23 * R | **0.76 m** per ulp (this is why a float lattice split fails below metre cells in sphere units) |
| lat/long cell width at lat 89.9 deg | 256 m * cos(89.9 deg) | 0.45 m (a 256 m east-west sector collapses; the pole is a coordinate singularity) |
| gnomonic cube face area spread (linear face coordinates, then normalise) | (1+u^2+v^2)^(-3/2), u=v=1 vs 0 | corner cell has 0.192 of centre area: **centre : corner = 5.2 : 1** (linear 2.3 : 1) |
| cube-face edge length | pi R / 2 | 10 018 539 m; level 10 (1024 tiles): **9784 m** at face centre; level 15: **305.7 m** |

## 6. The four ADR questions

### 6.1 Frame transport at the poles

| option | how | evidence | cost / risk |
|---|---|---|---|
| A lat/long (+ east/north/up) | grid in (lon, lat) | O: east/north undefined at the poles; cell width collapses (0.45 m at 89.9 deg, section 5); SFSIM and Exolune both avoid it in rendering (V section 4) | every pole case needs a special branch; simple for UI and real-geography import |
| B cube-sphere faces | 6 faces, quadtree tile ids, neighbour table across edges | V SFSIM `quadtree.clj:482-511` (rotation per face pair), `cubemap.clj:144-147`; O area spread 5.2:1 with linear face coordinates (an equal-angle warp is the usual cure, not verified in SFSIM) | 12 face edges and 8 corners (3 faces meet) need tests; very good for stable IDs and LOD; no pole |
| C camera-local patch + 3D double position + parallel-transported frame | authority is a double 3D vector; the frame `(vertical, heading)` follows the motion by rotating about `vertical x next` | V Exolune `FlightFrame.h:94-103`, `:54`, `D3DApp.cpp:2168`; O: no singular direction exists, only the degenerate case `sine <= 1e-12` is guarded | no persistent address by itself: a stable ID needs B underneath; heading drifts under long loops (holonomy; O: area enclosed times 1/R^2, for a 100 km circumference loop about 2e-5 rad), so a "north" reference is a separate display concern |

**Recommendation: C for motion and cameras, B for addressing and caching, A only at the UI/import edge.** Position = double 3D vector in a planet-fixed frame; vehicle/camera attitude = (vertical, heading) pair carried by parallel transport (Exolune idea, clean-room: we write our own); tile and sector ids = (face, level, row, col) from a cube-sphere. Reasons: no pole or face-edge branch in vehicle code, and the discrete table is needed only where data are addressed (SFSIM proves that table is finite and small, `quadtree.clj:482-511`). Residual unknown: behaviour at the 8 cube corners under LOD balancing (**C**, SFSIM item 5 runtime).

### 6.2 Handoff planet patch -> region -> WorldGen sector

**Authority rule (E, intake; consistent with V in section 4):** the global planet layer is a render/cache layer only. WorldGen sectors (ADR-032, 256 m, 4 m terrain grid, `SectorStreamer`, M facts table in `worldgen-vegetation-buildings.md`) stay authoritative for collision, roads, buildings. SFSIM does the same with a separate local mesh for physics (V `planet.clj:393`).

| option | placement of a 256 m sector on the curved planet | error (O, section 5) | seed / id nesting |
|---|---|---|---|
| A one tangent plane per 10 km region, sectors in a flat grid | simplest; today's WorldGen math is unchanged | 1.96 m at 5 km, 3.9 m at the corner: the sector at the region edge floats 2-4 m above the sphere, planet patch and sector disagree visibly | region id -> seed; sector = (sx, sz) as now |
| B **one tangent frame per sector** (origin = sector centre, up = radial there), height = height above the sphere | local float math stays flat; curvature is a function of the sector centre | 1.3 mm at the sector edge, 2.6 mm at the corner, plane-to-plane angle 0.0023 deg; the shared edge is symmetric: both neighbours drop by the same 1.3 mm there (O), so the height seam stays below 1 mm vertically and about 0.1 mm horizontally | `ForStage(worldSeed, "region" + face + level + row + col)` -> sector stage `"sector" + ix + "," + iz` inside the region |
| C no tangent planes: every vertex generated in double from the sphere and rebased | exact | 0 by definition; costs the 4 m grid code a rewrite (WorldGen heights, roads, buildings are planar) | same as B |

**Recommendation: B.** Region = cube-face quadtree tile at level 10 (9784 m at the face centre, about 4.3 km at the corners with linear face coordinates; O), tile-centre anchor as in SFSIM's `tile-center` (V `cubemap.clj:292-298`); inside a region the 256 m sectors form a planar integer grid in the region's tangent frame **only for naming**, while each sector's mesh is built in its own centre-relative frame. 256 m sectors cannot be quadtree-aligned: edge/2^L = 305.7 m at level 15 and 152.9 m at level 16 (O), so an exact 256 m size exists only in the planar frame, never as a cube-face tile. Stable ids: `SectorId = (regionFace, regionLevel, regionRow, regionCol, ix, iz)`; sector content depends only on `(worldSeed, SectorId)` (this keeps the existing property of WorldGen vegetation: "no state shared between sectors", M brief). Seam rule: a sector near a region border takes its tangent frame from the region anchor of **its own centre**, not of the region, so no sector ever needs a neighbour region to load.
Residual unknown: the 5.2:1 area spread makes regions near face corners 4.3 km wide; decide whether an equal-angle warp is worth it (the ADR experiment, section 8).

### 6.3 Noise precision without GPU FP64

| option | how | evidence | verdict |
|---|---|---|---|
| A GPU FP64 | as Exolune: hard device gate | V D3DApp.cpp:570-571; E intake: RTX-class reference only | excluded: availability on the i3-1215U Intel UHD is the open C item (Exolune 16), and the RX 470 is unavailable |
| B CPU double authority + GPU float tile-relative | the CPU evaluates in double, cuts the position into an integer lattice cell + a float fraction relative to the tile origin, ships height/normal tiles; GPU only samples textures and adds float detail relative to the tile | V Exolune `Noise.hlsli:5-7` (split in double, noise in float); O: this is already how WorldGen works (CPU generation, `TerrainModel.Height` on the CPU, M brief) | **recommended** |
| C GPU pairs of floats (48 bit) | double-float arithmetic, Dekker/Veltkamp | V Exolune `Noise.hlsli:7` and `docs/geometrie.md:147` (idea only, no code taken) | fallback for far-LOD detail if the CPU cannot keep up; not needed for the first What If experiment |

Computed float32 behaviour (O, section 5): absolute world coordinates in float32 give **0.5 m spacing at Earth radius** and 0.76 m per ulp on the unit sphere, so a 1-4 m lattice is quantised and breathes while moving. Tile-relative float: 0.015 mm at 181 m, 0.98 mm at 10 km, 7.8 mm at 100 km. The rule: **the lattice cell index is an int64 produced on the CPU from double metres (`floor(x / cellSize)`), the hash takes (seed, cell), the fraction `x - cell*cellSize` is computed in double and only then rounded to float.** With that, noise values at a given world point are bit-identical whatever tile origin is used (testable: evaluate the same point from two origins, compare). Exolune's own comment gives a float lattice only for cells of 4 m and more (V `Noise.hlsli:6-7`); this matches 0.5 m / 4 m = 12 % cell jitter, the reason we do not rely on it. Hash choice: any published integer hash (PCG, xxHash); not Exolune's code.

**Recommendation: B**, per-patch/per-sector origin rebasing in double on the CPU, C as an optional GPU tier later.

### 6.4 VRAM / streaming budget on i3-1215U / UHD

Anchors (all M, `benchmarks/perf-matrix/2026-11-08-50a2ad1-quick/report.md`, i3-1215U + Intel UHD, shared memory, 15.7 GiB RAM, quick protocol, warm P95; RG-137):

| cell | P95 ms | peak private MiB | GPU shared MiB |
|---|---:|---:|---:|
| playground Low | 1.75 | 380 | 58 |
| worldgen Low | 2.56 | 499 | 63 |
| worldgen Medium | 4.22 | 566 | 135 |
| worldgen High | 6.66 | 714 | 221 |
| sierra-stream High | 7.00 | 2068 | 280 |
| gunsai High | 19.70 | 1390 | 418 |

M for sector cost: median WorldGen sector placement 0.3-0.4 ms, dense forest/village sectors 4-11 ms (`worldgen-vegetation-buildings.md` section 9, M); `SectorStreamer.BudgetMs = 3`, `CriticalBudgetMs = 8` per pump (M). Not measured (listed so nobody invents it): physics step cost, cost of one sector, any planet-layer cost, RX 470.

Proposed budget (O, to be replaced by a measurement in the experiment):

| item | proposal | why |
|---|---|---|
| frame (60 fps, 16.7 ms) | WorldGen Low-High already uses 2.6-6.7 ms; planet layer <= 6 ms P95 at Medium | leaves >= 4 ms for the car/aircraft and spikes; clouds/volumetrics are an optional tier (E: SFSIM's clouds are its main bottleneck, FAQ) |
| GPU shared memory | planet layer <= 256 MiB on top of worldgen High's 221 MiB, i.e. total < 500 MiB | gunsai High measured 418 MiB and ran 19.7 ms: the memory is not the wall, the frame time is |
| process RAM | planet layer <= 150 MiB on top of worldgen High's 714 MiB peak (< 1 GiB) | sierra-stream (a streamed 1.24 GB KN5 map) stays at 2.07 GiB, the notebook has 15.7 GiB but RG-148 and Unity share it |
| sector payload (O) | 65 x 65 vertices x 32 B (pos, normal, uv) = 135 kB + heights 4225 x 4 B = 17 kB, about **150 kB/sector** without vegetation (36 kB tree table when forested, M brief) | formula, to be measured |

IO per km/h (O): new data per second = v * 2 * R_load * rho, with rho = payload / sector area. Fine tier: 150 kB / 65 536 m^2 = **2.3 B/m^2**, R_load = 600 m (`LoadRadius`, M wiki): **0.76 kB/s per km/h** of speed. Planet tier (64 m grid, about 5 B/point incl. colour byte): rho = 1.2e-3 B/m^2.

| vehicle | speed | tier | R_load | new data per s | sectors/s |
|---|---|---|---|---|---|
| car | 100 km/h = 27.8 m/s | WorldGen sector | 600 m | **76 kB/s** | 0.5 |
| car | 200 km/h | WorldGen sector | 600 m | 152 kB/s | 1.0 |
| aircraft | 900 km/h = 250 m/s | WorldGen sector at 600 m | 600 m | 690 kB/s | 4.6 (above the 3 ms pump budget if each costs 0.3-0.4 ms plus mesh upload) |
| aircraft | 900 km/h | WorldGen sector at 5 km radius | 5 km | **5.7 MB/s** | 38 (CPU 38 x 0.35 ms = 13 ms per second is affordable by M; mesh upload and instantiation of 38 sectors/s is **unmeasured**, so feasibility is open) |
| aircraft | 900 km/h | planet tier only | 100 km | 60 kB/s | - |

Reading: the **finest tier inside the load radius dominates IO and CPU**, the global planet tier is cheap (tens of kB/s). Therefore the design rule is: fine WorldGen sectors only within a speed-dependent radius (car 600 m, aircraft: only below some altitude), everything beyond by the planet tier. Capacity is not the issue, the sector build budget is; this is measured only for the car case today.

**Recommendation:** budget the planet layer as above, make `LoadRadius` a function of speed and altitude, and require the experiment to report ms/sector, sectors/s and shared MiB at 100/200/900 km/h.

## 7. What to take / what to avoid

| # | take / avoid | label |
|---|---|---|
| 1 | take: double 3D position + parallel-transported frame; no lat/long in vehicle code | V Exolune FlightFrame.h |
| 2 | take: cube-sphere ids for tiles and caches, neighbour table with rotations | V SFSIM quadtree.clj:482-511 |
| 3 | take: separate small local mesh for physics around the vehicle (3 x 3 tiles) | V SFSIM planet.clj:393 |
| 4 | take: LOD decision by projected size `radius*width/(distance*2^level*(tilesize-1)*tan(fov/2))` vs a pixel threshold, computed off the render thread | V SFSIM quadtree.clj:26-31, planet.clj:236 |
| 5 | take: CPU double lattice cell + float fraction relative to the tile | V idea Exolune Noise.hlsli:5-7; O |
| 6 | avoid: a GPU FP64 requirement (hard gate) | V Exolune D3DApp.cpp:570-571 |
| 7 | avoid: copying or paraphrasing any Exolune code (PolyForm Shield, competing product) | V LICENSE.md |
| 8 | avoid: treating "10 km tile" as a unit; the unit is the WorldGen sector (256 m, 65 x 65 at 4 m) | M repo |
| 9 | avoid: linear-face cube coordinates without checking the 5.2:1 area spread | O |
| 10 | avoid: trusting any intake locator, benchmark or version number without a pinned SHA | M intake 2026-11-09 |

## 8. Next step (What If, not NightRacer)

- ADR draft title: **"What If: planet addressing, frames and handoff to local sectors"** (answers 6.1, 6.2; 6.3 and 6.4 are inputs).
- The one experiment that would decide it (CPU only, no Unity, no GPU, can be a unit test in the existing libraries): given a cube-sphere region id at several latitudes (0, 45, 89.99 deg and a cube corner), generate sectors in per-sector frames (6.2 B), check (a) shared-edge vertical mismatch < 1 mm, (b) the same noise point from two different tile origins is bit-identical (6.3 B), (c) a parallel-transported frame driven around a pole and over a cube corner returns without discontinuity, (d) time per sector and sectors/s at 100/200/900 km/h. Pass = all four numeric criteria; the result is a table, no engine.
- Then decide whether the equal-angle warp is needed (area spread 5.2:1 vs about 1.3:1; unverified for SFSIM).

## 9. Open items and caveats

- Not verified: any runtime behaviour of either engine (nothing was cloned, built or run); Exolune on Intel UHD; SFSIM horizon culling; hysteresis; displacement shader; FAQ figures (still E).
- Not measured here: RX 470, aircraft-speed streaming, planet-layer cost.
- The raw files used were read into `artifacts/tmp/rg153/` (git-ignored) and are not committed.
