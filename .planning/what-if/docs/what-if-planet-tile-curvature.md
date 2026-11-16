# What If: planet 4 — curvature of the deformed WorldGen tiles relative to the planet (RG-166)

What If input (separate project); not a NightRacer ticket source.

- Ticket RG-166, Research class (ADR-035), author Claude Fable 5.2 (allowed by the user for this ticket only). Builds on `what-if-planet-adr-draft.md` §3/§7 (WI-ADR-001 Accepted), `what-if-planet-content-roads.md` §2-§4 (RG-160) and the RG-165 adapter design. Libraries stay Unity-free.
- Labels (research-rules §1): **V** = primary page read in this session (URL, quote <= 40 words, table in §7); **M(s)** = measured by this ticket's script (source `agent_pool/agents/claude/outbox/RG-166_curvature.py`, full output `RG-166_results.txt` beside it; Python stdlib, 0.8 s); **M(c)** = read in project code (`file:line`); **M(r)** = an earlier reviewed measurement; **O** = own derivation, steps shown; **E** = none used. A table whose caption carries a label is labelled in every cell.
- Access date of all web reads: 2026-11-12. 9 pages read with the `urllib` fallback of research-rules §3 (WebFetch did not load in this session), 1 more unreachable (`jcgt.org/published/0007/02/01/`: 492-byte stub, no text; nothing is cited from it). Nothing was cloned, built or downloaded.
- Sites (M(s)): face 4 sectors (col, row) = (16384, 16384) "face centre", (32767, 16384) "cube-edge midpoint", (32767, 32767) "cube vertex". R = 6 378 000 m, c = d / R. A geographic pole is a face centre, i.e. the **least** deformed place.

**Answer.** RG-165's mapping stays: a gnomonic tangent-plane chart with radial heights and mesh vertices on the canonical lattice (variant A). It is an isometry to 1.5e-9; all deformation of the quad lands in the *sampling pattern*, none in the content. Variant B (WorldGen grid in the lattice parameters) breaks every metric rule (scale 0.80-1.38, right angles +-30 deg). For v2 only the chart **anchor** moves from the sector centre to the level-10 region centre (existing `RegionPlane`), so content crosses sector borders in one coordinate system and the 32 m taper is needed only on region borders (1.3-1.6 % of the area instead of 38-45 %).

## 1. Mapping variants

In every variant the mesh vertex is the canonical lattice direction `d` placed at `d * (R + h)` (ADR row 3). The variants differ only in which planar point (x, z) WorldGen is asked for.

| variant | planar point of vertex direction d | exact | bends | cost per vertex (O) |
|---|---|---|---|---|
| **A sector chart** (RG-165 as built) | gnomonic at the sector centre: `x = R (d.E)/(d.U)`, `z = R (d.N)/(d.U)`; inverse `d = normalize(R U + x E + z N)` | great circles are straight lines (V1), so the quad footprint and every lattice line are straight; heights are radial, so no vertical float; metric error <= sin^2 c = 1.5e-9 (derivation below) | the footprint is not a 256 m square (§2): WorldGen is sampled on a sheared 3.4-4.8 m pattern; neighbour frames are unrelated across cube edges (east rotated 90 / 120 deg, M(s)), so nothing planar may cross a sector border in sector coordinates | 3 dot + 1 div + 2 mul after the direction the lattice needs anyway; one WorldGen `Height(x, z)` point query (no `SampleTile`) |
| **A' region chart** (recommended v2) | the same formula with U, E, N of the level-10 region centre = `RegionPlane.ToLocal` (M(c) `src/WhatIf.Planet/Roads.cs:346-347`) | as A; one chart for 32 x 32 sectors, so two sectors give a shared lattice vertex the same (x, z) bits | metric error <= 1.6e-6 at the far corner of the region (16 mm per 10 km, M(s)) | identical to A |
| **B lattice parameters** | `x = 4 m * i`, `z = 4 m * j` (WorldGen square [0, 256]^2 = the quad in (a, b), bilinear over the 4 corners) | samples coincide with lattice vertices; `SampleTile` works unchanged | every metric quantity: scale 0.80-1.38, anisotropy up to 1.73, right angles up to +-30 deg (§2, §3) | 0 |
| C1 bilinear / affine fit of A | (x, z) interpolated from the 4 corner chart points | nothing beyond A | horizontal residual up to 1.15 mm (bilinear) / 3.45 mm (affine), different on the two sides of a border (M(s)) | saves ~10 flops, < 0.02 ms per sector: **rejected, no gain** |
| C2 another cube warp | e.g. the warp of the S2 library (its guide names `S2_QUADRATIC_PROJECTION`; that this is the one behind the statistics page is O, memory) | — | under A the warp does not touch the content at all; it only changes sample density. S2 level 15: 47 520-99 639 m2 = 2.10 : 1 (V3) against 66 100-93 478 m2 = 1.41 : 1 here (M(s)): the equal-angle warp is already the more uniform one | **rejected** |
| C3 geodesic (icosahedral) grid | hexagon cells, gnomonic per icosahedron face (V4) | lower shape spread | replaces the accepted quadtree, lattice, portals and far layer; still has 12 pentagons (V4) | **rejected** (ADR Accepted) |
| C4 per-quad conformal map | — | — | a conformal map keeps angles, so it cannot turn a 120 deg corner into 90 deg; a 1.41 : 1 rectangle maps to a square only with non-uniform scale (O) | **rejected** |

Derivation, gnomonic scale (O; checked M(s) at d = 100 km: finite difference 0.999754233 = cos^2 c, 0.999877109 = cos c):
1. A plane point at distance r from the tangent point has central angle c with r = R tan c; its arc distance is s = R c.
2. Radial scale ds/dr = (R dc) / (R sec^2 c dc) = cos^2 c.
3. Tangential scale: the plane circle 2 pi r maps to the sphere circle 2 pi R sin c, ratio = sin c / tan c = cos c.
4. So the singular values are {cos c, cos^2 c}; max |sigma - 1| = sin^2 c ~ (d/R)^2; two perpendicular directions leave 90 deg by at most asin((s1^2 - s2^2)/(s1^2 + s2^2)) ~ c^2/2.

Derivation, lattice metric used for B (O; finite differences agree to 4 digits, M(s)): p = normalize(1, tan alpha, tan beta), u = tan alpha, v = tan beta, D = 1 + u^2 + v^2. Then |p_alpha| = (1 + u^2) sqrt(1 + v^2) / D, |p_beta| = (1 + v^2) sqrt(1 + u^2) / D, cos(angle) = -u v / sqrt((1 + u^2)(1 + v^2)). Values (u, v) = (0, 0): 1, 1, 90 deg; (1, 0): 1, 1/sqrt 2, 90 deg; (1, 1): 2 sqrt 2 / 3, 2 sqrt 2 / 3, 120 deg. A lattice step is R * (pi/4) / 2^20 = 4.7772 m times these factors.

## 2. Distortion budget of one level-15 quad

Cell shape (all cells M(s); sides and diagonals are great-circle arcs; agrees with RG-162: 305.74 / 216.19 / 288.25 m, 66 100..93 478 m2):

| quantity | face centre | cube-edge midpoint | cube vertex |
|---|---|---|---|
| sides (m) | 305.74 x 4 | 305.74 (the two sides running to the cube edge) x 216.19 / 216.20 (the sides parallel to it; 216.19 lies on the cube edge) | 288.25 / 288.26 |
| opposite sides differ by | 0 | 10.4 mm | 4.6 mm |
| diagonals (m) | 432.38 / 432.38 | 374.46 / 374.46 | 288.26 / 499.27 |
| corner angles; max deviation from 90 deg | 90.000; 0 | 89.998-90.002; 0.002 deg | 60.002 / 119.997 / 120.000; **30.0 deg** |
| area (m2); ratio to 256 x 256 | 93 478; **1.426** | 66 100; **1.009** | 71 959; **1.098** |
| vertex spacing along the sides (m) | 4.777 | 3.378 and 4.777 | 4.504 |
| diagonals of one lattice quad (m) | 6.756 / 6.756 | 5.851 / 5.851 | **4.504 / 7.801** |
| footprint bounding box in the sector chart (m) | 305.74 x 305.74 | 305.75 x 216.20 | 432.37 x 249.64 (1.5 x the cell area) |
| sectors cut by a 600 m disc on the feature point; vertices | 16; 67 600 | 24; 101 400 | 27; 114 075 |

Jacobian planar -> sphere, singular values sigma (all cells M(s); 1 = isometry):

| variant | site | sigma max | sigma min | max \|sigma - 1\| | max deviation of a right angle | area factor |
|---|---|---|---|---|---|---|
| A sector chart | centre / edge / vertex | 1 | cos^2 c | 1.1e-9 / 0.9e-9 / 1.5e-9 (0.0004 mm per 256 m) | <= 4.4e-8 deg | 1 - 2.3e-9 |
| A' region chart, far corner at 6.9 / 6.0 / 8.0 km | centre / edge / vertex | 1 | cos^2 c | 1.18e-6 / 0.88e-6 / 1.57e-6 (0.30 / 0.23 / 0.40 mm per 256 m) | <= 4.5e-5 deg | 1 - 2.4e-6 |
| B | face centre | 1.1943 | 1.1943 | 0.194 | 0 | 1.426 |
| B | cube-edge midpoint | 1.1943 | 0.8445 | 0.194 | **19.47 deg** | 1.009 |
| B | cube vertex | 1.3790 | 0.7962 | 0.379 | **30.00 deg** | 1.098 |
| C1 | all | as A | as A | plus a horizontal shift of 0 / 0 / 1.15 mm (bilinear), 0 / 2.59 / 3.45 mm (affine) | as A | as A |

- Region chart against sector chart (M(s)): on the sector farthest from the region centre the region chart is a rigid motion of the sector's own chart within 0.25 / 0.16 / 0.38 mm. Moving the anchor to the region changes nothing a sector can see.
- Altitude (O: a sphere of radius R + h): horizontal metres grow by h / R at height h = 1.6e-5 (100 m), 1.6e-4 (1 km), 1.4e-3 (8 848 m). This is larger than every chart error of A and A' and is the same in all variants.

Sagitta and drop below the tangent plane, 2 R sin^2(d / 2R) ~ d^2 / 2R (all cells M(s)):

| where | d | drop |
|---|---|---|
| planar 256 m WorldGen tile, centre to edge | 128 m | 1.28 mm |
| quad centre to a side midpoint | 108-153 m | 0.92-1.83 mm |
| quad centre to its farthest corner (edge / centre / vertex site) | 187 / 216 / 250 m | 2.75 / 3.66 / **4.89 mm** |
| across a whole 256 m tile (tangent at one edge) | 256 m | 5.14 mm |
| streaming `LoadRadius` | 600 m | **28.2 mm** |
| far layer | 1 / 5 / 10 / 25 km | 0.078 / 1.96 / 7.84 / 49.0 m |
| chord sagitta s^2 / 8R of one lattice step; of one sector side | 4.78 m; 305.74 m | 0.45 um; 1.83 mm |
| tilt of the vertical across one quad (d / R) | <= 250 m | <= 3.9e-5 rad = 0.0022 deg |

## 3. Roads and buildings under the deformation

Rule values: M(c) `src/WhatIf.WorldGen/Rules.cs:209-214` (class min radius / max grade), `Rules.cs:99,127` (`LegAngleMinDeg` 70, vertical radii), `Terrain.cs:55` (shoulder 1.5 m, slope 1 : 1.5, `MaxDiffM` 20), `Roads.cs:344` (344.83 / 121.31 m). Factors: M(s). A circle of radius r maps to an ellipse with semi-axes s1 r, s2 r, whose smallest radius of curvature is r s2^2 / s1 (O); a height is not scaled, so a grade is multiplied by 1 / sigma along the road (O).

| WorldGen rule | A / A' (worst case) | B at edge midpoint / cube vertex | verdict |
|---|---|---|---|
| horizontal curve radius (motorway min 345 m) | x (1 - 2.4e-6): 0.8 mm on 345 m | x 0.597 / x 0.460: 345 m becomes **206 / 159 m** | **B breaks here first** |
| grade (motorway max 6.3 %) | x (1 + 1.6e-6) | x 1.184 / x 1.256: 6.3 % becomes 7.46 / 7.91 % | B breaks |
| lane and road width | 1.6e-6 | x 0.80..1.38 by heading (a 3.5 m lane: 2.79..4.83 m, O example); x 1.194 already at the face centre | B breaks everywhere |
| straight segment | exactly a great circle (V1) | straight in (a, b) is a geodesic only along a lattice line (V2, O) | A exact |
| right angles (footprints, junction legs >= 70 deg) | <= 4.5e-5 deg | +- 19.5 / +- 30 deg | B breaks |
| vertical curve (motorway crest >= 6 000 m) | the datum itself curves: 1/6000 + 1/R = 1/5994 m, 0.09 % (O) | same | holds |
| flat foundation of width w | sags w^2 / 8R: 0.05 mm at 50 m, 1.8 mm at 306 m (O) | same | holds |
| vertical (walls, gravity) | sector up against the true radial <= 3.9e-5 rad = 3.9 mm per 100 m of height; **region up** at 8 km = 1.25e-3 rad = 12.5 cm per 100 m (O from M(s)) | same | use the sector up, never the region up |

What breaks first under A / A' is not a metric rule:
1. **Generator domain.** The level-10 region is a straight-sided quad of 9.78 x 9.78 km (face centre), 9.78 x 6.92 km (cube-edge midpoint; the far side 6.93 km) or a 9.22 km rhombus with 120 deg (cube vertex) (M(s)), not the square `Half = SizeKm * 500` (M(c) `Rules.cs:233`). RG-160 saw this for corner regions only; it holds for every region off the face centre.
2. **Sampling of the carved terrain.** The carve is designed for 4 m samples; the lattice gives 3.38-4.78 m, sheared by up to 30 deg. Whether terrain pokes through a narrow road at 4.78 m is UNKNOWN (§6 e6). At the cube vertices the lattice quad must be split along its **short** diagonal: 4.50 m (equilateral triangles) against 7.80 m (30-30-120 slivers) (M(s)).
3. **Per-sector frames**, if sector charts were kept: a road would change coordinate system every 216-306 m.

**Decision: roads, lots and buildings are generated in the region chart (the plane), where every WorldGen rule holds to 1.6e-6. Nothing is warped horizontally.** The terrain mesh is the same chart function sampled at the lattice vertices; road and building vertices go through the same map `normalize(R U + x E + z N) * (R + h)` and are then written into the float frame of their sector. The opposite order (terrain grid native, roads warped = B) costs 40-54 % of the minimum radius.

## 4. Colliders and LOD seams

Neighbour frames (all cells M(s)):

| pair | tilt between the two ups | in-plane rotation of east |
|---|---|---|
| same face, face centre | 4.79e-5 rad (0.00275 deg) | 0.0000 deg |
| across the cube edge, at its midpoint | 4.79e-5 rad | 90.001 deg |
| across the cube edge, at the cube vertex | 3.91e-5 rad | 120.000 deg |

| seam | what keeps it closed | number |
|---|---|---|
| planet base, sector to sector, double | shared canonical lattice vertex (ADR row 3; RG-162 check a PASS, M(r)) | 0. Evaluating the shared direction on the other face differs by 1.0-1.6e-9 m (M(s)): canonicalisation is needed for equal bits, not for millimetres |
| WorldGen contribution, sectors of one region | A': same region chart -> same (x, z) bits -> same `Height`; no taper | prediction 0 differing bits (§6 e1) |
| WorldGen contribution across a region border | taper of the region-local terms to 0 over W = 32 m; roads only through portals (RG-160 §4) | band = 1.30 / 1.57 / 1.38 % of the area; a taper on every quad edge covers 37.6 / 44.7 / 45.2 % (M(s)) |
| float32 vertex in its own sector frame (ADR row 3) | rounding <= ulp / 2 per axis | error <= 7.8 um; gap between the two neighbours 1.1 / 8.8 / 13.6 um (M(s)) |
| after the float32 object matrix into the floating-origin frame | — | gap 0-22 um with the origin at the sector, **61 um** at 600 m, **0.98 mm** at 10 km; only 0-18 of 65 shared vertices are bit-identical (M(s), CPU emulation of M v + t) |
| the same with shared axes + positions snapped to 2^-10 m | exact float sums (below) | 65 of 65 bit-identical at 0 / 600 m / 10 km; position error <= 0.77 mm (M(s)) |
| collider | built from the same float vertices as the render mesh: same gap; crease between sectors = the tilt, 10 arc seconds (O) | as above |
| flat sector meshes (vertex on its own tangent plane) | would still meet within 8e-9 mm (M(s)) | nothing gained: the exact radial vertex is free |
| near 65 x 65 sector against a far CDLOD node | the far border vertices are every 2^(15-L)-th vertex of the same integer lattice (O), so there is no horizontal T-gap; CDLOD itself needs "no need for stitching meshes" (V5) | sphere share of the vertical gap: 0.002 mm (L 14), 0.11 mm (L 11), 0.46 mm (L 10), 7.3 mm (L 8) (M(s)); the rest is terrain detail -> skirt of RG-160 D5 (§6 e7) |

float32 spacing (M(s); Unity vertex positions are `Vector3` of `float`, V6; `float` = 4 bytes, ~6-9 digits, V7): 15.3 um below 256 m, 30.5 um at 256 m, 61 um at 600 m, 0.49 mm at 5 km, 0.98 mm at 10 km, 0.5 m at R.

Why the snapped grid is exact (O): a coordinate n * 2^-10 m with |n| < 2^24 (|x| < 16 384 m) is a float32. With all sectors on the same axes, translation t = m * 2^-10 and local vertex (n - m) * 2^-10 are floats, and t + local = n * 2^-10 needs no rounding, so both sectors output the same bits. This is how NightRacer's planar tiles are watertight today (origins on 256 m, vertices on 4 m); rotated per-sector frames lose it. Price: up to 0.49 mm of quantisation per axis and a re-emit when the shared axes change. Whether the 61 um gaps show as pixel dropouts on the Intel UHD is UNKNOWN (needs the GPU, §6 e9); until then the ADR row 3 frame stays and the RG-160 skirt is the cover.

## 5. Horizon and visual curvature

Smooth sphere, no refraction; pixel = 1080 px over an assumed 60 deg vertical field of view (O assumption). All cells M(s).

| eye height | horizon distance sqrt(2 R h + h^2) | horizon dip | hidden below the horizon at 10 / 25 / 50 km |
|---|---|---|---|
| 1.2 m (car) | 3 912 m | 0.035 deg = 0.63 px | 2.9 / 34.9 / 166.5 m |
| 50 m | 25 255 m | 0.227 deg = 4.1 px | - / - / 48.0 m |
| 1 000 m | 112.9 km | 1.01 deg = 18 px | - |

- A ground point at distance d moves by d / 2R rad against a flat world, at any eye height (O: depression (h + d^2/2R)/d minus h/d): 0.02 px at 256 m, **0.05 px at 600 m**, 0.4 px at 5 km, 0.8 px at 10 km, 1 px at 12.4 km, 2 px at 25 km (M(s)).
- The horizon dip exceeds 1 px from an eye height of 3.0 m (O: h = R px^2 / 2).
- **The near ring may be treated as planar per sector by content and physics** (drop <= 4.9 mm, vertical tilt <= 3.9e-5 rad inside one quad), but its vertices stay exact, `d * (R + h)`: that costs nothing and the drop is simply part of the local y. One flat plane for the whole 600 m ring is rejected: 28 mm step against the far layer at the rim and the shared lattice is lost.
- Curvature enters the picture through the sector frames (4.8e-5 rad per sector) and, visibly, only through the far layer: the horizon and the 2.9 m of relief hidden at 10 km from a car.
- Gravity taken constant in the floating-origin frame is off by d / R = 9.4e-5 rad at 600 m (lateral 9e-4 m/s2, O): fine inside the ring; the vehicle's up stays `Frame.Transport` (ADR row 1).

## 6. Recommendation

**RG-165's tangent-plane choice stays. The formula does not change; v2 changes the anchor of the chart and the scope of the taper.**

| item | v1 = RG-165 as built (worktree state 2026-11-12 17:46, M(c) `src/WhatIf.Planet.WorldGen/SectorAdapter.cs`, not yet reviewed) | v2 (one choice) | number the adapter must meet |
|---|---|---|---|
| mapping | gnomonic chart at the sector centre, radial height, canonical lattice vertices | the same map anchored at the **level-10 region centre** (`RegionPlane`); one WorldGen `TerrainModel` per region | chart metric error <= 2e-6, right angle <= 1e-4 deg (M(s) worst 1.57e-6, 4.5e-5 deg) |
| height | `h = base(d) + taper * (Height(x, z) - Base(x, z))`, base from the direction | unchanged; the base provider may call `PlanetNoise` directly (~0.2 ms per 65 x 65, RG-161 check d, M(r)) instead of the 16 m Catmull-Rom grid | shared-vertex height bit-identical inside a region |
| taper | 32 m smoothstep on every quad edge (38-45 % of the area; fine for the experiment) | **32 m, on region borders only**, and only on region-local terms (seeded detail, carve). Roads stay >= 63.5 m + half width from a tapered border except at portals (32 + `MaxDiffM * SlopeRun` 30 + shoulder 1.5, O from M(c) `Terrain.cs:55`) | added slope <= 1.5 A / W (O, smoothstep): <= 10 % for a contribution A <= 2.1 m; if RG-165 row w3 reports more, W = 15 A |
| sampling | `Height` per vertex | one batch call for the 4 225 points with a single box query, as `SampleTile` does (M(c) `Terrain.cs:331`) | < 3 ms per sector (RG-165 w5 threshold) |
| triangles | not decided | split each lattice quad along its shorter diagonal | cube-vertex sectors: 4.50 m, never 7.80 m |
| float data | own sector frame | unchanged (ADR row 3); shared axes + 2^-10 m grid only if e9 shows dropouts | gap <= 1 float spacing of the origin distance (61 um at 600 m) |
| up vector of objects | sector up | sector up; never the region up | lean <= 3.9 mm per 100 m |

Later target (v3, not part of v2): detail noise from global coordinates (RG-160 §2 principle) and portal-fixed road heights make the region-border taper 0.

What the next experiment must measure (CPU only unless marked; sites = the three of §2 plus one region corner where 4 regions meet):

| # | measurement | pass / prediction |
|---|---|---|
| e1 | shared lattice vertices of neighbour sectors inside one region, through the region chart, no taper: differing height bits | 0 |
| e2 | seam across region borders incl. a cube edge and a cube vertex, taper on the region border only: max world gap; share of the area inside the taper | < 1 mm; <= 1.6 % |
| e3 | chart metric: 10 000 random point pairs per region, chart distance against great-circle distance; angle of 1 000 right angles | <= 2e-6; <= 1e-4 deg |
| e4 | WorldGen validators on the roads mapped to the sphere (3D): min radius, max grade, width per class | every rule within 1e-5 of its planar value |
| e5 | generator on the true region polygon (9.78 x 6.92 km rectangle; 120 deg rhombus); portal approach: carve depth at the border | accepts the polygon (extends RG-160 r4); carve 0 at the portal |
| e6 | carved terrain sampled at 4.78 m, at 3.38 x 4.78 m and sheared 60 deg against the 4.0 m baseline: max terrain above the road inside its footprint; both diagonal choices | not worse than the baseline; else the road-band margin is raised |
| e7 | far-layer vertices on the hole border against the near sector, per far level: max and P95 \|near - far\| = skirt depth | report; sphere share <= 0.46 mm at level >= 10 |
| e8 | float emit: gap and bit-identical count of shared vertices, own frames against shared axes + 2^-10 m grid, origin at 0 / 600 m / 10 km | reproduces §4: 61 um / 0.98 mm and 65 of 65 |
| e9 | (GPU, later, Unity) pixel dropouts along sector seams on the Intel UHD for both emit modes | UNKNOWN |
| e10 | cost: sector build with the batch sample; region `TerrainModel` build time and memory; load of 16 / 24 / 27 sectors inside 600 m | < 3 ms per sector; report the rest |

UNKNOWN until run: cost of 4 225 `Height` point queries (RG-165 w5 pending), region model build time, carve sampling at 4.78 m, pixel dropouts on the UHD, skirt depth, the taper amplitude A (RG-165 w3 pending).

## 7. Sources (V, all read 2026-11-12)

| # | page, section | quote |
|---|---|---|
| V1 | `proj.org/en/stable/operations/projections/gnom.html` (PROJ 9.9.0, "Gnomonic", first paragraph) | "For a sphere, the gnomonic projection is a projection from the center of the sphere onto a plane tangent to the center point of the projection. This projects great circles to straight lines." |
| V2 | `s2geometry.io/devguide/s2cell_hierarchy.html` (coordinate systems, "(x, y, z)") | "Cells in (x,y,z)-coordinates are quadrilaterals bounded by four spherical geodesic edges." |
| V3 | `s2geometry.io/resources/s2cell_statistics.html` (table, row level 15) | "The average size is that returned by S2Cell.AverageArea(), guaranteed to be within a factor of 1.5 of the high and low end of the range." Row 15: "47520.30", "99638.93", "79172.67", "m2" |
| V4 | `h3geo.org/docs/core-library/overview/` (first section) | "the grid cells are then projected to the surface of the sphere using an inverse face-centered polyhedral gnomonic projection"; "must contain exactly 12 pentagons at every resolution" |
| V5 | `github.com/fstrugar/CDLOD` (README, abstract) | "better screen-triangle distribution, cleaner transitions between levels, and no need for stitching meshes" |
| V6 | `docs.unity3d.com/6000.3/Documentation/ScriptReference/Vector3-x.html` and `.../Mesh-vertices.html` | "public float x"; "Returns a copy of the vertex positions or assigns a new vertex positions array." ("public Vector3[] vertices { get; set; }") |
| V7 | `learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types` (table "Characteristics of the floating-point types", row `float`) | "~6-9 digits", "4 bytes", "System.Single" |
