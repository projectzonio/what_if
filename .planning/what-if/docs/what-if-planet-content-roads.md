# What If: planet 3 — planet content, sector contract, far-LOD seam, roads across edges (RG-160)

What If input (separate project); not a NightRacer ticket source.

- Ticket RG-160, Research class (ADR-035), author Claude Sonnet 5.5. Builds on `what-if-planet-engine.md` (RG-153) and `what-if-planet-adr-draft.md` (RG-155, WI-ADR-001 Proposed). Libraries stay Unity-free.
- Labels (research-rules): **V** = primary page read in this session (URL, quote <= 40 words), **M** = read in the project code (`file:line`) or measured, **O** = own reasoning/computation (formula shown), **E** = external, not re-verified. **Access date of all web reads: 2026-11-12 (UTC).** Nothing was cloned, built, run or downloaded; 10 pages read (7 more failed, below), raw text in git-ignored `artifacts/tmp/rg160/`.
- Not reachable (recorded, not bypassed): `dl.acm.org` (HTTP 403), `cgl.ethz.ch` (TLS timeout), `sci.utah.edu`, `hal.science`, `export.arxiv.org` (certificate verify failed in the Python reader), `spacedata.copernicus.eu` (connect timeout). Consequence: **no road/city paper was verified** (section 4 prior art is O from memory) and the licence *annex text* of Copernicus DEM was again not read.
- Size numbers: R = 6 378 000 m, sphere area 5.112e8 km2 (O: 4 pi R^2); land area ~149 M km2 (V, CDSE page below); level-10 region = 9784 m at face centre (RG-153 §5), = 38.2 x 38.2 sectors of 256 m (O); 6 x 4^10 = 6 291 456 level-10 regions (O).

## 1. Relief source

Variants for the planet's height field. Sizes are O: samples = area / resolution^2, 16-bit signed (2 B/sample; the DTED form is 16-bit, V CDSE "32 Bit, floating (DGED & INSPIRE format) 16 Bit, signed (DTED format)"), uncompressed, whole planet = 5.112e8 km2, land only = 1.49e8 km2.

| variant | whole planet @ 30 m | @ 90 m | @ 1 km | one level-10 region (9784 m) @ 30 / 90 / 1000 m | determinism | licence | effort |
|---|---|---|---|---|---|---|---|
| **A procedural** (domain-warped fBm continents + ridged mountain noise, plate-style masks) | 0 stored (evaluated) | 0 | 0 | 0 B stored; cost = octaves x noise evals per sample | exact if int64 lattice rule of WI-ADR-001 #4 is used | none (own code); Copernicus/OSM not involved | M: 1-2 days for a first look (O); tuning "looks like a planet" is open-ended |
| **B real DEM, GLO-30** | 5.68e11 samples = **1136 GB** (land only 331 GB 16-bit, 662 GB float32) | 126 GB (land 36.8 GB) | 1.02 GB (land 0.30 GB) | 326 x 326 = 106 363 samples = **213 KB** / 108.7^2 = 11 818 = 23.6 KB / 9.8^2 = 96 = 0.2 KB | deterministic only if file versions are pinned (SHA-256 list); "2019_1 ... 2024_1" releases exist, V below | free, attribution obligations (V below); **annex unverified** | download + tile + resample to cube faces (SFSIM did this for Mercator maps, V RG-153 §3 item 7); weeks; storage does not fit the notebook for the whole planet |
| **B' real DEM, GLO-90** | - | 6.31e10 samples = **126 GB** (land 36.8 GB) | - | 23.6 KB per region | as B | as B (same page covers GLO-30 and GLO-90) | same, 9x smaller than 30 m |
| **B'' coarse real relief 1 km class** (ETOPO 2022 30"/60" or GLOBE-type) | - | - | 0.3-1.0 GB | 0.2 KB per region | pinned file | **unverified**: the NOAA ETOPO product page lists 15/30/60 arc-second grids and a DOI but no licence text on that page | small; no detail below ~900 m |
| **C hybrid** (coarse base from A or B'' + procedural detail below ~90-250 m) | base 0.3-1.0 GB (real) or 0 (procedural) | - | - | base only: 0.2 KB / region | base pinned or procedural; detail is the existing WorldGen-style fBm on global coordinates | as the base source | medium: needs the sector contract (section 2) |

Notebook limits: i3-1215U, 15.7 GB RAM, planet-layer budget <= 150 MiB RAM / <= 256 MiB GPU shared (O proposal, RG-155 §3 row 5). Free disk on D: was **not measured** (C: is nearly full, AGENTS.md). Consequence (O): a whole-planet 30 m grid (331-1136 GB) cannot live on the notebook at all; GLO-90 land (36.8 GB) can only be streamed per region from disk; only a 1 km-class base (0.3-1.0 GB) fits RAM+disk comfortably, and a 16 x 16 km window of GLO-30 (~0.5 MB) is trivial.

Evidence for the licence/data rows (V):

| claim | quote and source |
|---|---|
| Free for the public, terms live in a separate licence | V `registry.opendata.aws/copernicus-dem/`: "GLO-30 Public and GLO-90 are available on a free basis for the general public under the terms and conditions of the Licence". The page links the licence; **that licence text was not read** |
| 30 m is **not** global in the public bucket | V same page: "GLO-30 Public provides limited worldwide coverage at 30 meters because a small subset of tiles covering specific countries are not yet released to the public" |
| Oceans have no tiles | V same page: "in both cases ocean areas do not have tiles, there one can assume height values equal to zero" |
| It is a *surface* model | V same page: "a Digital Surface Model (DSM) which represents the surface of the Earth including buildings, infrastructure and vegetation" (so trees/buildings sit in the height; bad as road bed or collision ground, O) |
| Attribution when distributing GLO-30 | V `dataspace.copernicus.eu/.../COP-DEM`: "When communicating to the General Public or distributing the Copernicus WorldDEM-30 (GLO-30), the User shall inform the General Public of the source by using the following notice". Adapted data needs "produced using Copernicus WorldDEM-30 ..." |
| Page says what GLO-30/90 licence is | V same page: "The GLO-30 and GLO-90 datasets are available worldwide with a free license." Whether a derived game asset may be redistributed is **not stated** on this page: unverified |
| Support horizon | V same page: "The datasets were made available for use in 2019 and will be maintained until 2026." (today is 2026-11-12: maintenance statement is at its end; data on AWS stays as is, "Update Frequency None", V AWS page) |
| Access | V CDSE page lists "General public" among users who "can download the GLO-90 and the GLO-30 instances" (needs registration there); AWS bucket needs "No AWS account" (V AWS page): two pages, two access stories |
| Accuracy and datum | V CDSE: "Absolute vertical accuracy < 4m (90% linear error)"; vertical datum "EGM2008" (geoid, not the sphere: an offset to R must be handled, O); grid "1.0"" for GLO-30, "3.0"" for GLO-90; ~"149 M km 2" coverage |
| Natural Earth (coastlines, countries as vector/raster maps, not relief at metre scale) | V `naturalearthdata.com/about/terms-of-use/`: "All versions of Natural Earth raster + vector map data found on this website are in the public domain." |
| SRTM | V `earthdata.nasa.gov/data/instruments/srtm`: ~80 % of land, 2000 mission; **licence text not on the page: unverified** |

**Recommendation: C (hybrid), start with A as the base provider.** Reason (O): the first deliverable is a playable planet with curved tiles, not Earth; procedural base = 0 GB, exact determinism, no licence, no notebook storage problem, and it exercises the contract. Put the base behind one interface `IPlanetRelief.Height(dir)`; a DEM-backed provider (GLO-90 base for chosen regions, GLO-30 only after the licence annex is read) is a later drop-in. A DSM must not be the ground truth for roads without a filter (V: includes buildings, vegetation).

## 2. Planet -> sector contract

**Principle (O):** matching neighbours needs *the same function of world position on both sides*, not shared edge samples. So the planet layer exposes a pure `PlanetHeight(dir)` (double, no seed beyond world seed) and the sector evaluates detail noise from **global** coordinates (WI-ADR-001 #4 int64 lattice). Edge samples would only be a cache of that function.

What WorldGen has today (M, read in this session):

| input today | where | problem for a planet |
|---|---|---|
| hills = `ReliefM*0.5*(...)*Fbm(x/BaseWavelengthM, z/BaseWavelengthM, Octaves)*1.6` | `Terrain.cs:182` (`Natural`, `:178-185`) | one map-local noise, coordinates are planar x,z of one 10 km map |
| per-map permutation table and offsets from `Rng.ForStage(spec.Seed,"terrain")` | `Terrain.cs:83-89` | seed per map; two neighbouring sectors/regions would get different noise |
| lattice index `(int)(((long)fx) & 255)` | `Terrain.cs:119` | period 256 cells (x 1400 m = ~358 km repeat at wavelength 1400 m), fine as local detail only, not as the planet base |
| mountain = one ridge band `MountainA/B/Width/Relief`, `HasMountain` | `Terrain.cs:12-17`, `:183` | a single hand-placed band |
| settlements from `City`, `CityRadiusM`, `Villages`, spacing | `Rules.cs:223-226` (`WorldSpec`, `:219-233`) | one city per map |
| motorway ends `MotorwayEntry/Exit` | `Rules.cs:227`; the end nodes get `Boundary = true` at `Generator.cs:269-270`; `GNode.Boundary` `Graph.cs:18`, node kind `Portal` `Graph.cs:8` | existing portal concept, but two fixed points |
| map extent `Half = SizeKm*500` | `Rules.cs:233` | square planar domain |
| carved terrain `Height(x,z)` and `SampleTile(x0,z0,size,step)` | `Terrain.cs:290`, `:323` (65 x 65 at 4 m = one sector, wiki `streaming-sectors.md`) | stays: authoritative, sits on top of `Natural` |

**Proposed record `SectorPlanetInput` (O; no code; produced by the planet layer, consumed by the sector's planar generator):**

| field | type / unit | resolution | replaces |
|---|---|---|---|
| `sectorId` | (face, level, row, col, ix, iz) | - | - (RG-155 #3) |
| `frame` | origin double3 (planet-fixed, m), up, east axes | per sector | planar x,z origin |
| `baseHeightM[19][19]` | float, metres above the sphere R (sea = 0) | 16 m, 17 x 17 interior + 1-sample apron for the bicubic (Catmull-Rom, C1) | `Natural` low-frequency part (`Terrain.cs:182` hills envelope, `Mountain`) |
| `baseSlopeMax` | float | per sector | tuning of `ReliefM` |
| `detail` | amplitude m, wavelength m, octaves (floats) from biome/roughness | per sector, with the 4 corner values blended bilinearly | `ReliefM`, `BaseWavelengthM`, `Octaves` (`Terrain.cs:9-11`) |
| `coastDistM[19][19]` | float, signed distance to the shoreline (negative = sea), clamped +-1024 m | 16 m | sea level / coastline handling (none today: flat map) |
| `biomeId`, `climate` | byte + (meanTempC, precipMm) float x4 corners | per sector | none today |
| `settlements[]` | (kind, local x,z double, radius m, seedKey) | list | `City*`, `Villages*` (`Rules.cs:223-226`) |
| `corridors[]` | polyline clipped to the sector (local x,z double, m), class, widthM | points at <= 16 m | `MotorwayEntry/Exit` |
| `portals[]` | see section 4 record | list | `Boundary` nodes (`Generator.cs:269-270`) |
| `worldSeed`, `regionSeed` | int64 | - | `spec.Seed` |

Grid size per sector (corrected after RG-165/166): >= 19 x 19 — deformed quads need 23 x 23 (face centre, edge midpoint) and 31 x 31 (cube vertex) at 16 m; the 19 x 19 figures below are the flat-256 m lower bound. Size per sector (O): 19 x 19 x 4 B + 361 B = 1.8 KB + lists; the 38 x 38 sectors of a region = ~2.6 MB if all baked (a region cache; lazily per sector it is 1.8 KB).

Rules the experiment must confirm: (1) the base grid is sampled from `PlanetHeight` at global positions, so the same point from two sectors gives bit-identical values (RG-155 check b); (2) detail noise amplitude/wavelength may change per biome only through the **continuous** per-corner blend, otherwise a biome border would be a height step; (3) the roads' carved terrain (`Terrain.cs:290`) stays sector-local and must taper to 0 at the sector border, or the corridor hint must carry its road height, else a road-cut would end in a cliff at the border (UNKNOWN: measure with check (a) of RG-155 extended to carved terrain).

## 3. Far-LOD / near-sector seam

All GPU-cost cells are **UNKNOWN on the target** (Intel UHD, shared memory; RX 470 unavailable; nothing was run). Statements about relative cost are O.

| technique | what it does (V) | CPU/GPU cost on an integrated GPU (O) | fit for "planet = render only, WorldGen authoritative within LoadRadius" |
|---|---|---|---|
| **skirts** | vertical strips hiding gaps at tile borders. Primary paper not read; V only that a library advertises avoiding them: `vterrain.org/LOD/Implementations/`: "guarantees that there won’t be seams — either gaps or skirts — between adjacent patches of terrain" | extra triangles on tile borders (a few %, O); no shader work; overdraw at the strips | **good for the near/far hole edge**: simplest cover of the height gap between a 4 m sector and the coarse planet mesh |
| **geomorphing** | vertex moves from the finer to the coarser position during a transition, so a LOD change does not pop. V Ulrich `tulrich.com/geekstuff/chunklod.html`: "It renders CLOD, with geomorphs, using batched primitives." | one extra lerp per vertex in the vertex shader (O); needs the coarser height per vertex | needed for the far-LOD rings, **not** for the sector hole (the sector is not morphed, it is authoritative) |
| **CDLOD** (Strugar) | V `github.com/fstrugar/CDLOD`: "the LOD function is the same across the whole rendered mesh and is based on the precise three-dimensional distance between the observer and the terrain"; "no need for stitching meshes". Code under MIT (repository header "MIT license", V page) and "free software license" (abstract) | quadtree of regular grids, morph constants per node; CPU selects nodes (SFSIM-style projected-size test in RG-155 uses the same idea); a few draw calls; needs the heightmap as a vertex texture or buffer | **best fit for the far layer**: height comes from the planet's `PlanetHeight`/baked mips; its LOD uses 3D distance, which works on a sphere with double camera position |
| **chunked LOD** (Ulrich) | V same Ulrich page: "The downsides include a long preprocessing phase, and some size overhead for the disk file." and "the data isn't in a convenient form for doing height/collision queries" | prebuilt chunks, geomorphs; cheapest at runtime | **poor fit**: offline preprocessing conflicts with a procedural/seeded planet; no collision queries (V) - those stay in WorldGen anyway |
| **geometry clipmaps** (Losasso, Hoppe) | V `hhoppe.com/proj/geomclipmap/`: "caches the terrain in a set of nested regular grids centered about the viewer"; GPU Gems 2 ch. 2 (V `developer.nvidia.com/gpugems/gpugems2/...chapter-2-terrain-rendering-using-gpu-based-geometry`): "The key idea is to introduce a transition region near the outer perimeter of each level" | vertex-texture fetch per vertex, constant frame cost; nested around the viewer on a plane | **poor fit for a cube sphere** (planar grid around the viewer, O); would be the natural choice for a flat 10 km map |
| T-junction stitching / tessellation neighbour mask | SFSIM: outer tessellation chosen from a 4-bit neighbour mask (V RG-153 §3 item 6, `tess-control.glsl:21,25`) | needs hardware tessellation (D3D11/12 level hardware; availability and cost on UHD UNKNOWN) | not needed with CDLOD morphing |

**Recommendation (O):** far layer = **CDLOD-style quadtree per cube face** (RG-155 #6 hysteresis added) drawing `PlanetHeight` at the node's resolution, with morph for LOD changes; near/far seam = the far mesh gets a **hole mask at sector granularity** (quads covered by loaded sectors are dropped, no tile rebuild) plus a **short skirt** on the hole edge whose depth equals the largest measured `|near height - far height|` on the sector border. Detail inside `LoadRadius` is full (the WorldGen height is the truth), so there is no morph across the seam; the visible step is bounded by the detail amplitude and is hidden by the skirt/fog. Unit to measure (not run): max and P95 of `|WorldGen(x) - Far(x)|` along sector borders per biome; far-layer ms/frame on Medium on the notebook GPU.

## 4. Roads across region borders and cube-face edges

**Today (M):** the road graph is planar per map: `RoadGraph` nodes/edges (`Graph.cs`, class/span/width/points `Graph.cs:26-33`), motorway entry and exit as two fixed boundary nodes (`Rules.cs:227`, `Generator.cs:269-270`), square domain `Half = SizeKm*500` (`Rules.cs:233`). No concept of a neighbour map.

**Geometry fact that makes shared portals cheap (O, derivation):** the equal-angle warp is applied to u and v separately (a = (4/pi) atan(u), RG-155 §1). Every region border is a line u = const or v = const **in the gnomonic picture**, i.e. the intersection of the sphere with a plane through the centre = a **great circle arc**, independent of the warp. A road polyline that is a great-circle arc therefore crosses a region border at the intersection of two planes through the origin: `p = normalize(n_road x n_border)` (one cross product, closed form, sign picked toward the road's direction). No iterative search; both regions can compute it.

| option | idea | evidence / reasoning | verdict |
|---|---|---|---|
| **A global coarse corridor graph + local refinement + shared portals** | nodes = settlements/junctions as double unit vectors, edges = great-circle arcs with class; deterministic from `worldSeed` per planet cell; each region takes the arcs touching it, converts to its local planar frame, refines with the existing planar generator between portals | O; analogous to the "highways first, local streets after" hierarchy of procedural city papers (**not verified**: ACM/ETH pages unreachable, see header) | **recommended** |
| B region-local generation, roads simply end at the border | no portals | O; trivial but roads break at every 8-10 km | rejected |
| C one global planar-like grid in an equirectangular map | lat/long | pole and corners problem (RG-155 §1: 573:1 at 89.9 deg) | rejected |
| D build a coarse graph on the sphere with a Voronoi/Delaunay triangulation of the settlement points | spherical Delaunay = convex hull of unit vectors (Euclidean 3D hull of the points) | O (known property: hull of points on a sphere equals the spherical Delaunay) | candidate for how A picks edges |

**Portal record (O; frame-independent):**

| field | meaning |
|---|---|
| `edgeId` | global corridor edge (int64 from the sorted pair of node ids) |
| `borderId` | (face, level, row, col, side) of the border, canonicalised: the side seen from the lower RegionId |
| `s` | arc length along the border great circle from its canonical start vertex, **quantised to 1 cm as int64** (so both regions read the same integer) |
| `angle` | road heading relative to the border tangent, radians (quantised); no tangent-frame rotation enters |
| `class`, `widthM`, `heightM` | from the global edge and `PlanetHeight(portal)` |

Each region rebuilds the portal position in its own sector frame; the planar generator sees an ordinary entry node (as `Boundary` today). A road that crosses a face edge is **identical on both sides by construction** because only `(s, angle)` cross; the 90-degree rotations of the neighbour table (RG-155 #2) never touch the road.

**What breaks at the 8 cube corners (O; numbers from RG-155 §1):**

| issue | why | rule |
|---|---|---|
| three regions meet in a point; angle around the corner is 360 degrees on the sphere but 3 x 90 degrees in face coordinates | the equal-angle cube has stretch 1.732 and a 120-degree wedge per region at the corner | the planar generator's square domain (`Rules.cs:233`) must become a polygon domain for every region off the face centre (RG-166: 9.78 x 6.92 km at a cube-edge midpoint, 9.22 km 120-degree rhombus at a cube vertex) |
| a road through the exact corner vertex hits all three borders at once | the three border planes share one point | forbid portals closer than a margin (e.g. 256 m = one sector) to a region vertex: the corridor graph moves its crossing along the border by `s` snap; the corner itself is never a junction |
| holonomy | parallel transport around a closed loop rotates a frame by area / R^2 (RG-155 §3) | roads store heading relative to the border tangent, not a global bearing; no consequence for the portal |
| region area differs (equal-angle: corner regions 8.2 km wide vs 9.8 km centre, RG-155 §1) | density of portals per region varies by 1.41x | acceptable, report in the experiment |

**Road density budget (O):** a global graph of settlements at one per (100 km)^2 on land = 1.49e8 / 1e4 = ~14 900 nodes, ~3 edges each = ~45 000 edges (arcs: 2 node ids + class = ~12 B each, < 1 MB). Portals per region border: one corridor edge per ~100 km means most 10 km regions have **no** through road; roads exist only along corridors (this is the user decision 3 below). Local streets inside a region need no portals if they stay inside the region; they only cross borders as sub-class corridors if the user wants a dense network.

**The next experiment must measure (UNKNOWN until run, CPU only, extends RG-161 `WhatIf.Planet`):**

| # | measurement | pass |
|---|---|---|
| r1 | for 10 000 random great-circle arcs crossing region borders and the 12 cube edges: portal position computed by the two regions | bit-identical `s` (int64), positions agree < 1 mm after mapping both sides to the planet frame |
| r2 | arcs passing within 256 m of the 8 corner vertices | portal snap keeps the road >= 256 m from the vertex, road length change vs the true arc < 0.5 % (O guess) |
| r3 | road heading relative to the border tangent from both sides | difference < 1e-9 rad |
| r4 | refined planar road in the corner-region wedge (120 degrees) | no self-intersection, curvature within WorldGen rules (`Rules.cs`), generator accepts a polygon domain: UNKNOWN effort |
| r5 | global graph generation for the whole planet at 100 km spacing | memory < 5 MB, time < 1 s (O guess) |

## 5. User decisions (<= 6)

| # | decision | recommendation | why it matters |
|---|---|---|---|
| 1 | Real Earth data or a fictional Earth-sized planet? | fictional, procedural base (section 1 A/C); keep the DEM provider as a later drop-in | DEM: 0.3-36.8 GB (land) to 1.1 TB (all, 30 m); licence annex unread; DSM includes buildings/trees |
| 2 | May I read the Copernicus DEM licence annex (HTML/PDF text, no data) and decide on tile redistribution inside a game build? | yes, as a small follow-up; until then the DEM is research input only | AWS page: "free basis ... under the terms and conditions of the Licence"; derived-asset redistribution unstated (V) |
| 3 | Road density goal for the planet: (a) corridors only (~45 000 arcs), (b) corridors + settlements, (c) dense network | (a) first | decides whether portals are per class or only for motorways/primary |
| 4 | Far-layer technique: CDLOD-style quadtree (recommended) or something simpler (fixed rings) for the first version? | CDLOD-style | section 3 |
| 5 | Is a measured step at the near/far border (<= a few m, hidden by skirt and fog) acceptable for the first playable version? | yes | the alternative (morph near sectors) would break authoritative collision |
| 6 | Run the follow-up CPU experiment (r1-r5, section 4) after RG-161, outside ride times? | yes | needs the notebook slot, CPU only |
