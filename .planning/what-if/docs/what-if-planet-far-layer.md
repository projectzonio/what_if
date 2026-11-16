# What If planet far layer (RG-171)

- Hardware: 12th Gen Intel(R) Core(TM) i3-1215U; 8 logical CPUs; power plan: 381b4222-f694-41f0-9685-ff5bb260df2e  (Rovnováha); Microsoft Windows NT 10.0.19045.0; dotnet 8.0.403
- Commit: 6bee973845d5231750e8a447e6d8e17b3df8c87e
- Wall time of the run: 30 s; FAIL rows: 0
- Library `src/WhatIf.Planet.FarLayer`, console `src/WhatIf.Planet.FarLayer.Experiment`, runner `tools/whatif-farlayer/run-farlayer.ps1`; seeds 20261112, 7; defaults Range13 1950 m, morph fraction 0.02525641025641024.

(design text missing)

## Camera directions (seed 20261112 and seed 7)

- a_coast_2m: dir (0.013910661, 0.971827338, 0.235283061) face 2 a 0.30244 b 0.01822 sector F2.r16682.c21339 ground 0.0 m
- b_lakeshore_2m: dir (0.280454147, 0.854620269, -0.437000763) face 2 a -0.60183 b 0.40373 sector F2.r22998.c6523 ground 180.0 m
- c_land_300m: dir (-0.214219478, 0.460059107, -0.861658652) face 5 a 0.62442 b -0.31025 sector F5.r11300.c26614 ground 99.1 m
- d_land_10km: dir (-0.214219478, 0.460059107, -0.861658652) face 5 a 0.62442 b -0.31025 sector F5.r11300.c26614 ground 99.1 m
- drive: great circle through the cube-edge point dir (0.665246597, 0.665246597, -0.338960071) face 0 a 1.00000 b -0.60000 sector F0.r6553.c32767 ground 208.1 m, tangent (-0.592739, 0.746624, 0.302016), s -1800..+1800 m, 30 m/s
- flight: start dir (0.874807648, 0.191878830, 0.444852890) face 0 a 0.27492 b 0.59898 sector F0.r26197.c20888 ground 493.0 m, tangent (0.163381, 0.747591, -0.643750), 250 m/s, 5000 m above sea level

## Result table

| row | kind | value | threshold | result | note |
|---|---|---|---|---|---|
| f2.skirt | protocol | skirtM 3.0 m; covers 308230 of 308230 gaps | 100 % | PASS | skirtM = max(3.0 m, max gap of cameras a-d of all seeds (2.343 m) x 1.25 rounded up to 0.5 m); checked against all 304070 other gaps (seed 7 a-d, drive, flight, both seeds; overall max 2.343 m); 0 vertices without a far node |
| f2.gap.a_coast_2m.20261112 | report | max 0.150 m; P95 0.010 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (13 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f1.coverage.a_coast_2m | protocol | 0 violations | 0 | PASS | 154925 directions: 100000 random in the horizon cap (half uniform, half log-uniform in angle from 50 m) + all 65 x 65 vertices of the 13 near sectors; covered exactly once by one unmasked far quad or one near sector |
| f2.gap.b_lakeshore_2m.20261112 | report | max 1.242 m; P95 0.479 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (13 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f1.coverage.b_lakeshore_2m | protocol | 0 violations | 0 | PASS | 154925 directions: 100000 random in the horizon cap (half uniform, half log-uniform in angle from 50 m) + all 65 x 65 vertices of the 13 near sectors; covered exactly once by one unmasked far quad or one near sector |
| f2.gap.c_land_300m.20261112 | report | max 0.171 m; P95 0.100 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (13 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f1.coverage.c_land_300m | protocol | 0 violations | 0 | PASS | 154925 directions: 100000 random in the horizon cap (half uniform, half log-uniform in angle from 50 m) + all 65 x 65 vertices of the 13 near sectors; covered exactly once by one unmasked far quad or one near sector |
| f2.gap.d_land_10km.20261112 | report | max 0.171 m; P95 0.100 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (13 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f1.coverage.d_land_10km | protocol | 0 violations | 0 | PASS | 154925 directions: 100000 random in the horizon cap (half uniform, half log-uniform in angle from 50 m) + all 65 x 65 vertices of the 13 near sectors; covered exactly once by one unmasked far quad or one near sector |
| f2.gap.drive.20261112 | report | max 0.242 m; P95 0.090 m | report | report | 144040 hole-edge vertices on every 60th frame of 7200 (Height cast to float = SurfaceSector height); 0 vertices without a built level-13 node |
| f1.coverage.drive | protocol | 0 violations | 0 | PASS | 3025410 directions on every 10th frame of 7200: 3000 random in the horizon cap (half uniform, half log-uniform) + all hole-edge vertices; frames with an uncovered region counted as violations |
| f2.gap.flight.20261112 | report | max 0.249 m; P95 0.084 m | report | report | 128570 hole-edge vertices on every 60th frame of 7200 (Height cast to float = SurfaceSector height); 0 vertices without a built level-13 node |
| f1.coverage.flight | protocol | 0 violations | 0 | PASS | 2936620 directions on every 10th frame of 7200: 3000 random in the horizon cap (half uniform, half log-uniform) + all hole-edge vertices; frames with an uncovered region counted as violations |
| f7.morph_continuity.20261112 | protocol | 0.0000 mm | < 1 mm | PASS | 1000 random nodes of levels 1..13 on all faces: max /vertex at morph 1 - parent mesh (independently built parent node, triangle interpolation)/ over 289 vertices each |
| f7.morph0_bits.20261112 | protocol | 0 differ | 0 | PASS | morph 0: vertex = direction x (R + Height(direction)), double bits, same 1000 nodes |
| f2.gap.a_coast_2m.7 | report | max 0.045 m; P95 0.007 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (14 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f2.gap.b_lakeshore_2m.7 | report | max 2.343 m; P95 0.322 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (14 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f2.gap.c_land_300m.7 | report | max 0.018 m; P95 0.010 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (15 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f2.gap.d_land_10km.7 | report | max 0.018 m; P95 0.010 m | report | report | 1040 hole-edge vertices of 16 exposed sector sides (15 near sectors), SurfaceSector height (float) vs far mesh radius at the same direction (triangle plane, morph applied); 0 vertices without a built level-13 node |
| f2.gap.drive.7 | report | max 0.264 m; P95 0.103 m | report | report | 14950 hole-edge vertices on every 600th frame of 7200 (Height cast to float = SurfaceSector height); 0 vertices without a built level-13 node |
| f2.gap.flight.7 | report | max 0.107 m; P95 0.048 m | report | report | 12350 hole-edge vertices on every 600th frame of 7200 (Height cast to float = SurfaceSector height); 0 vertices without a built level-13 node |
| f7.morph_continuity.7 | protocol | 0.0000 mm | < 1 mm | PASS | 1000 random nodes of levels 1..13 on all faces: max /vertex at morph 1 - parent mesh (independently built parent node, triangle interpolation)/ over 289 vertices each |
| f7.morph0_bits.7 | protocol | 0 differ | 0 | PASS | morph 0: vertex = direction x (R + Height(direction)), double bits, same 1000 nodes |
| f8.level_balance | protocol | 0 violations | 0 | PASS | adjacent selected nodes (same face or across a cube edge) differing by more than 1 level, 479 frames (4 fixed cameras + every 30th frame of drive and flight) |
| f8.edge_bits | protocol | 0 differ of 4986848 | 0 | PASS | edge vertices shared by two adjacent selected nodes of the same level (incl. across cube faces): morphed double position and height bits, same frames |
| f8.t_junction | protocol | 0.0000 mm | < 1 mm | PASS | max distance of a fine node's morphed edge vertex to the coarse neighbour's morphed edge polyline (level l+1 next to level l), 112479 fine/coarse edges, same frames |
| f3.draw.a_coast_2m | protocol | 210 nodes; 107232 triangles | <= 480 nodes; <= 250000 triangles | PASS | max over 2000 frames, all around (no frustum), skirt triangles included (max 128); median nodes 210; 90x59 deg frustum: max 81 nodes, 41184 triangles |
| f4.select.a_coast_2m | protocol | median 0.096 ms | <= 1.0 ms | PASS | near set update + selection + masks per frame, single thread, median of 3 processes (P95 0.120 ms, max 1.498 ms; selection alone median 0.060 ms; frustum selection 0.035 ms) |
| f3.draw.b_lakeshore_2m | protocol | 322 nodes; 164576 triangles | <= 480 nodes; <= 250000 triangles | PASS | max over 2000 frames, all around (no frustum), skirt triangles included (max 128); median nodes 322; 90x59 deg frustum: max 105 nodes, 53472 triangles |
| f4.select.b_lakeshore_2m | protocol | median 0.066 ms | <= 1.0 ms | PASS | near set update + selection + masks per frame, single thread, median of 3 processes (P95 0.082 ms, max 0.215 ms; selection alone median 0.038 ms; frustum selection 0.019 ms) |
| f3.draw.c_land_300m | protocol | 315 nodes; 160992 triangles | <= 480 nodes; <= 250000 triangles | PASS | max over 2000 frames, all around (no frustum), skirt triangles included (max 128); median nodes 315; 90x59 deg frustum: max 111 nodes, 56544 triangles |
| f4.select.c_land_300m | protocol | median 0.037 ms | <= 1.0 ms | PASS | near set update + selection + masks per frame, single thread, median of 3 processes (P95 0.040 ms, max 0.183 ms; selection alone median 0.028 ms; frustum selection 0.014 ms) |
| f3.draw.d_land_10km | protocol | 421 nodes; 215264 triangles | <= 480 nodes; <= 250000 triangles | PASS | max over 2000 frames, all around (no frustum), skirt triangles included (max 128); median nodes 421; 90x59 deg frustum: max 95 nodes, 48768 triangles |
| f4.select.d_land_10km | protocol | median 0.051 ms | <= 1.0 ms | PASS | near set update + selection + masks per frame, single thread, median of 3 processes (P95 0.054 ms, max 0.146 ms; selection alone median 0.041 ms; frustum selection 0.013 ms) |
| f3.draw.drive | protocol | 379 nodes; 193648 triangles | <= 480 nodes; <= 250000 triangles | PASS | max over 7200 frames, all around (no frustum), skirt triangles included (max 176); median nodes 371; 90x59 deg frustum: max 130 nodes, 66224 triangles |
| f4.select.drive | protocol | median 0.050 ms | <= 1.0 ms | PASS | near set update + selection + masks per frame, single thread, median of 3 processes (P95 0.052 ms, max 0.213 ms; selection alone median 0.037 ms; frustum selection 0.018 ms) |
| f3.draw.flight | protocol | 437 nodes; 223488 triangles | <= 480 nodes; <= 250000 triangles | PASS | max over 7200 frames, all around (no frustum), skirt triangles included (max 144); median nodes 421; 90x59 deg frustum: max 114 nodes, 58512 triangles |
| f4.select.flight | protocol | median 0.053 ms | <= 1.0 ms | PASS | near set update + selection + masks per frame, single thread, median of 3 processes (P95 0.058 ms, max 0.186 ms; selection alone median 0.044 ms; frustum selection 0.017 ms) |
| f5.build_p95.drive | protocol | P95 0.000 ms; max 0.826 ms | P95 <= 2.0 ms | PASS | node build time per frame (budget 2.0 ms, coarse levels first, then nearest), 7200 frames; frames that built something: P95 0.301 ms; 81 nodes built = 0.7 per second |
| f5.no_gap.drive | protocol | 0 frames with a gap; 0 frames with an unbuilt parent | 0 | PASS | frames in which part of the visible area had no built node (gap) / a requested node whose parent is not built either; 20 frames drew a built ancestor instead of unbuilt children; start-up at the first pose (unbudgeted): 584 nodes in 72 ms |
| f5.build_p95.flight | protocol | P95 0.000 ms; max 1.945 ms | P95 <= 2.0 ms | PASS | node build time per frame (budget 2.0 ms, coarse levels first, then nearest), 7200 frames; frames that built something: P95 0.556 ms; 503 nodes built = 4.2 per second |
| f5.no_gap.flight | protocol | 0 frames with a gap; 0 frames with an unbuilt parent | 0 | PASS | frames in which part of the visible area had no built node (gap) / a requested node whose parent is not built either; 125 frames drew a built ancestor instead of unbuilt children; start-up at the first pose (unbudgeted): 601 nodes in 161 ms |
| f5.prebuild | report | 651 ms for 510 nodes | report | report | 6 roots + levels 1..3 (single thread); PlanetSurface constructor 21 ms |
| f6.memory | protocol | 8.3 MiB | <= 64 MiB | PASS | managed memory growth after the 4 fixed cameras + drive + flight (GC.GetTotalMemory(true), max over processes): 4038 nodes cached, 3785 built; per node 2312 B heights (kept) + 6936 B upload buffers (xyz + morph delta, created at upload, not cached) |

## Images (raw folder, git-ignored; sha256)

- `top_a_coast_2m.png` 41e4995a73decf54...
- `top_c_land_300m.png` 60dfb2dcf32e985a...
- `top_d_land_10km.png` 587739a25c5533ae...
- `overview_d_land_10km.png` 2c3fcccd1b19757b...
