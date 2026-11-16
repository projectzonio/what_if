# What If planet surface layer (RG-168)

- Hardware: 12th Gen Intel(R) Core(TM) i3-1215U; 8 logical CPUs; power plan: 381b4222-f694-41f0-9685-ff5bb260df2e  (Rovnováha); Microsoft Windows NT 10.0.19045.0; dotnet 8.0.403
- Commit: 3fe7c996ca884837b6921a4f827476cea48f74fd
- Wall time of the run: 280 s; FAIL rows: 1
- Library `src/WhatIf.Planet.Surface`, console `src/WhatIf.Planet.Surface.Experiment`, runner `tools/whatif-surface/run-surface.ps1`; seeds 20261112, 7, 424242.

## Design (pure function of (seed, direction), nothing stored)

- **Continents/ocean:** continent field = fBm (cells 2^21..2^17 m, 5 octaves) at a domain-warped 3D position (warp = 3 x fBm cells 2^20, 2 octaves, 1000 km). Sea level = the **per-seed quantile 0.68** of the raw field over 16 384 fixed Fibonacci directions (computed in the `PlanetSurface` constructor, ~10-40 ms, deterministic): land share is about 32 % for any seed instead of drifting with the few large noise cells. Elevation curve: abyss -4000 m, shelf -200..0 m (smoothstep), lowland 420 m (exponential), uplands/mountains to ~2700 m (ridged fBm cells 2^17..2^14, only above field value 0.22). The 150 m `PlanetNoise` detail on land is scaled by smoothstep(0..120 m) of the continent elevation, so the coast is drawn by the field; +-0.0008 field units of 3-octave coast wobble (cells 16..4 km). Kinds: Ocean < -200 m, Sea -200..0, Beach 0..2, Land, Lake.
- **Lakes = cells:** 3D int64 lattice 2^13 m; hash probability 0.22; centre = normalize(cell centre + jitter 0.25 cell) * R; accepted only if disc + bank + fill + fade (<= 1600 m) lies inside its own cell; radius 250-1000 m x angular wobble 0.8..1.2 (two harmonics); bed depth 3-12 m. Level = min over 128 rim samples (rings 40 m and 120 m outside the shore) - 0.5 m; rejected when any rim sample < 3 m or rim relief > 30 m (lakes sit on flat ground, keeps the bank below ~45 degrees). Outside the shore: bank ramp 100 m (`h = L + (A - L) * smoothstep`), `A` = terrain raised to the level inside 120 m and fading out to 400 m, so the water is enclosed by a ring >= level (no spill) and joins the natural terrain continuously. Per-cell cache (bounded, 4096 entries) + last-cell shortcut.
- **Forest/biome:** temperature = 30 - 55 sin^2(lat) - 6.5 K/km (pole axis = +Y); moisture = fBm (cells 2^17..2^15) + dry belt at ~24 deg; density = smoothstep(T) x smoothstep(moisture) x (0.4 + 0.6 patch fBm) x fades (coast height 2..40 m, tree line 1800..2200 m, upland field 0.35..0.6, lake distance 0..200 m). Biome ids: 0 water, 1 ice, 2 tundra, 3 boreal, 4 temperate, 5 grassland, 6 desert, 7 shore, 8 tropical. **Deviation:** no slope term (a finite difference costs 4 more height evaluations); steep ground only exists where the ridged term is on (field > 0.22) and forest is faded out there (0.35..0.6). Slopes > 30 deg are not measured separately.
- **Cost design:** one corner cache per octave (`FbmLayer`), early-outs: lake interior skips the terrain, forest only on land, mountains/detail only above their start; the lake rim sampling uses a second terrain instance so it does not thrash the sector's caches.
- `FbmLayer` copies the arithmetic of `PlanetNoise.Value` (private there; the library is not touched); the 150 m detail uses the public `PlanetNoise.HeightSampler`.

## Result table

| row | kind | value | threshold | result | note |
|---|---|---|---|---|---|
| s1.two_process | protocol | 0 differing bits | 0 | PASS | 3 x 10000 random directions evaluated in 2 separate processes: sha256 of all sample bits 4e43069c92d76f31... |
| s1.edges | protocol | 0 | 0 of 35880 vertices differ | PASS | 35880 canonical lattice vertices on cube edges/vertices, each evaluated from both (all) sectors that share it, 3 seeds, 2 separate PlanetSurface instances; differing bits of height/kind/forest |
| s1.height_eq_sample | protocol | 0 | 0 of 30000 | PASS | Height(dir) bits == Sample(dir).HeightM bits on 30000 random directions (fresh vs warm instance); 150 cold-instance full-sample comparisons |
| s1.sector_eq_sample | protocol | 0 | 0 of 4290 | PASS | SurfaceSector.Sample (cached batch) == PlanetSurface.Sample (cold instance) at the same directions |
| s2.land_share | protocol | 32.07 % | 25..40 % | PASS | seed 20261112: Land+Beach+Lake share of 1000000 uniform directions |
| s2.kind_shares | report | ocean 61.82 % / sea 6.11 % / beach 0.086 % / land 31.96 % / lake 0.0193 % | report | report | seed 20261112: of all directions |
| s5.forest_share | protocol | 22.25 % | 20..55 % | PASS | seed 20261112: land samples with Forest >= 0.5; mean density 0.243 |
| s5.forest_on_water_or_beach | protocol | 0 | 0 | PASS | seed 20261112: samples with Forest > 0 and Kind != Land |
| s5.forest_above_treeline | protocol | 0 | 0 | PASS | seed 20261112: samples with Forest > 0 and height > 2200 m |
| s2.continents | report | 11 continents >= 1e6 km2; largest 32.4 % of land; land 163.6 M km2 | report | report | seed 20261112: 2048 x 1024 map, 4-connected, wraps in longitude, poles not joined; expect >= 3 and largest < 70 % |
| s4.lake_density | report | 7.87 per 10000 km2 land | report | report | seed 20261112: 15 lakes in 200 random land regions of 9.8 km (land 19050 km2); aim 2..20 |
| s4.no_spill | protocol | 0 | 0 | PASS | seed 20261112: 391490 samples at 0.5..300 m outside the shoreline (1 m steps along it); violations = inside the lake kind, or below the level within the 120 m guaranteed band (the water is enclosed by a >= level ring). Diag: 9158 samples at 200/300 m outside are below the level (natural terrain, fade zone, not adjacent to the water) |
| s4.level_constant | protocol | 0 | 0 | PASS | seed 20261112: lake samples whose WaterLevelM differs from the lake's level (constant by construction) |
| s4.level_above_sea | protocol | 7.23 m | >= 2 m | PASS | seed 20261112: lowest lake level over 15 lakes (max 479.8 m) |
| s6.max_dh.coast | protocol | 0.052 m | <= 4 m | PASS | seed 20261112: 100 sectors (coast), neighbour vertices ~4 m apart; P99 0.0356 m; worst sector F4.r7906.c16454 |
| s5.forest_continuity.coast | protocol | 0.0000 | <= 0.05 | PASS | seed 20261112: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s6.max_dh.lake_shore | protocol | 1.969 m | <= 4 m | PASS | seed 20261112: 100 sectors (lake shore), neighbour vertices ~4 m apart; P99 1.0340 m; worst sector F1.r1957.c16368 |
| s5.forest_continuity.lake_shore | protocol | 0.0371 | <= 0.05 | PASS | seed 20261112: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s6.max_dh.random_land | protocol | 0.413 m | <= 4 m | PASS | seed 20261112: 100 sectors (random land), neighbour vertices ~4 m apart; P99 0.3082 m; worst sector F4.r1296.c17539 |
| s5.forest_continuity.random_land | protocol | 0.0015 | <= 0.05 | PASS | seed 20261112: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s3.coast.seed20261112 | report | islands < 1 ha: 0 (0.00 % of land); coast 13.15 km; max height within 100 m of shore 1.0 m; land 51.2 % of the crop | report | report | crop of 612 x 612 at 16 m around F0.r22173.c894 (level-10 region F0.L10.r692.c27) |
| s3.lake.seed20261112 | report | islands < 1 ha: 0 (0.00 % of land); coast 0.00 km; max height within 100 m of shore n/a; land 99.8 % of the crop | report | report | crop of 612 x 612 at 16 m around F3.r24941.c4302 (level-10 region F3.L10.r779.c134) |
| s3.forest.seed20261112 | report | islands < 1 ha: 0 (0.00 % of land); coast 0.00 km; max height within 100 m of shore n/a; land 100.0 % of the crop | report | report | crop of 612 x 612 at 16 m around F1.r9877.c19355 (level-10 region F1.L10.r308.c604) |
| s2.land_share | protocol | 31.98 % | 25..40 % | PASS | seed 7: Land+Beach+Lake share of 1000000 uniform directions |
| s2.kind_shares | report | ocean 62.31 % / sea 5.71 % / beach 0.086 % / land 31.88 % / lake 0.0181 % | report | report | seed 7: of all directions |
| s5.forest_share | protocol | 25.30 % | 20..55 % | PASS | seed 7: land samples with Forest >= 0.5; mean density 0.275 |
| s5.forest_on_water_or_beach | protocol | 0 | 0 | PASS | seed 7: samples with Forest > 0 and Kind != Land |
| s5.forest_above_treeline | protocol | 0 | 0 | PASS | seed 7: samples with Forest > 0 and height > 2200 m |
| s2.continents | report | 10 continents >= 1e6 km2; largest 26.5 % of land; land 163.4 M km2 | report | report | seed 7: 2048 x 1024 map, 4-connected, wraps in longitude, poles not joined; expect >= 3 and largest < 70 % |
| s4.lake_density | report | 7.84 per 10000 km2 land | report | report | seed 7: 15 lakes in 200 random land regions of 9.8 km (land 19125 km2); aim 2..20 |
| s4.no_spill | protocol | 0 | 0 | PASS | seed 7: 500490 samples at 0.5..300 m outside the shoreline (1 m steps along it); violations = inside the lake kind, or below the level within the 120 m guaranteed band (the water is enclosed by a >= level ring). Diag: 7197 samples at 200/300 m outside are below the level (natural terrain, fade zone, not adjacent to the water) |
| s4.level_constant | protocol | 0 | 0 | PASS | seed 7: lake samples whose WaterLevelM differs from the lake's level (constant by construction) |
| s4.level_above_sea | protocol | 5.10 m | >= 2 m | PASS | seed 7: lowest lake level over 15 lakes (max 948.0 m) |
| s6.max_dh.coast | protocol | 0.053 m | <= 4 m | PASS | seed 7: 100 sectors (coast), neighbour vertices ~4 m apart; P99 0.0431 m; worst sector F4.r12668.c6196 |
| s5.forest_continuity.coast | protocol | 0.0003 | <= 0.05 | PASS | seed 7: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s6.max_dh.lake_shore | protocol | 2.097 m | <= 4 m | PASS | seed 7: 100 sectors (lake shore), neighbour vertices ~4 m apart; P99 1.0003 m; worst sector F0.r32222.c23763 |
| s5.forest_continuity.lake_shore | protocol | 0.0361 | <= 0.05 | PASS | seed 7: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s6.max_dh.random_land | protocol | 0.429 m | <= 4 m | PASS | seed 7: 100 sectors (random land), neighbour vertices ~4 m apart; P99 0.3174 m; worst sector F3.r3508.c4677 |
| s5.forest_continuity.random_land | protocol | 0.0038 | <= 0.05 | PASS | seed 7: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s3.coast.seed7 | report | islands < 1 ha: 0 (0.00 % of land); coast 14.46 km; max height within 100 m of shore 1.2 m; land 50.2 % of the crop | report | report | crop of 612 x 612 at 16 m around F2.r24937.c31765 (level-10 region F2.L10.r779.c992) |
| s3.lake.seed7 | report | islands < 1 ha: 0 (0.00 % of land); coast 0.00 km; max height within 100 m of shore n/a; land 99.8 % of the crop | report | report | crop of 612 x 612 at 16 m around F2.r12288.c23142 (level-10 region F2.L10.r384.c723) |
| s3.forest.seed7 | report | islands < 1 ha: 0 (0.00 % of land); coast 0.00 km; max height within 100 m of shore n/a; land 100.0 % of the crop | report | report | crop of 612 x 612 at 16 m around F4.r4684.c5162 (level-10 region F4.L10.r146.c161) |
| s2.land_share | protocol | 31.97 % | 25..40 % | PASS | seed 424242: Land+Beach+Lake share of 1000000 uniform directions |
| s2.kind_shares | report | ocean 61.22 % / sea 6.81 % / beach 0.091 % / land 31.86 % / lake 0.0197 % | report | report | seed 424242: of all directions |
| s5.forest_share | protocol | 21.28 % | 20..55 % | PASS | seed 424242: land samples with Forest >= 0.5; mean density 0.233 |
| s5.forest_on_water_or_beach | protocol | 0 | 0 | PASS | seed 424242: samples with Forest > 0 and Kind != Land |
| s5.forest_above_treeline | protocol | 0 | 0 | PASS | seed 424242: samples with Forest > 0 and height > 2200 m |
| s2.continents | report | 13 continents >= 1e6 km2; largest 27.5 % of land; land 163.3 M km2 | report | report | seed 424242: 2048 x 1024 map, 4-connected, wraps in longitude, poles not joined; expect >= 3 and largest < 70 % |
| s4.lake_density | report | 10.97 per 10000 km2 land | report | report | seed 424242: 21 lakes in 200 random land regions of 9.8 km (land 19147 km2); aim 2..20 |
| s4.no_spill | protocol | 0 | 0 | PASS | seed 424242: 759190 samples at 0.5..300 m outside the shoreline (1 m steps along it); violations = inside the lake kind, or below the level within the 120 m guaranteed band (the water is enclosed by a >= level ring). Diag: 11208 samples at 200/300 m outside are below the level (natural terrain, fade zone, not adjacent to the water) |
| s4.level_constant | protocol | 0 | 0 | PASS | seed 424242: lake samples whose WaterLevelM differs from the lake's level (constant by construction) |
| s4.level_above_sea | protocol | 9.64 m | >= 2 m | PASS | seed 424242: lowest lake level over 21 lakes (max 685.0 m) |
| s6.max_dh.coast | protocol | 0.062 m | <= 4 m | PASS | seed 424242: 100 sectors (coast), neighbour vertices ~4 m apart; P99 0.0488 m; worst sector F3.r12027.c1379 |
| s5.forest_continuity.coast | protocol | 0.0001 | <= 0.05 | PASS | seed 424242: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s6.max_dh.lake_shore | protocol | 2.031 m | <= 4 m | PASS | seed 424242: 100 sectors (lake shore), neighbour vertices ~4 m apart; P99 1.1619 m; worst sector F0.r32313.c28152 |
| s5.forest_continuity.lake_shore | protocol | 0.0360 | <= 0.05 | PASS | seed 424242: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s6.max_dh.random_land | protocol | 0.422 m | <= 4 m | PASS | seed 424242: 100 sectors (random land), neighbour vertices ~4 m apart; P99 0.3293 m; worst sector F3.r26046.c17998 |
| s5.forest_continuity.random_land | protocol | 0.0008 | <= 0.05 | PASS | seed 424242: max /delta Forest/ between neighbour vertices in the same 100 sectors |
| s3.coast.seed424242 | report | islands < 1 ha: 0 (0.00 % of land); coast 29.95 km; max height within 100 m of shore 0.7 m; land 45.8 % of the crop | report | report | crop of 612 x 612 at 16 m around F2.r29332.c9514 (level-10 region F2.L10.r916.c297) |
| s3.lake.seed424242 | report | islands < 1 ha: 0 (0.00 % of land); coast 0.00 km; max height within 100 m of shore n/a; land 99.8 % of the crop | report | report | crop of 612 x 612 at 16 m around F4.r10657.c8009 (level-10 region F4.L10.r333.c250) |
| s3.forest.seed424242 | report | islands < 1 ha: 0 (0.00 % of land); coast 0.00 km; max height within 100 m of shore n/a; land 100.0 % of the crop | report | report | crop of 612 x 612 at 16 m around F4.r31161.c13078 (level-10 region F4.L10.r973.c408) |
| s7.sector_median | protocol | 2.282 ms | <= 1.5 ms | FAIL | SurfaceSector.Sample 65 x 65, single thread, worst median over land / coast / lake-shore sets and 3 seeds (median of the processes); 100 sectors per set, caches warm, JIT warm |
| s7.sector.land_strip.20261112 | report | median 2.282 ms; P95 2.395 ms | report | report | median of 3 processes (2.014 / 2.282 / 2.514); spread 21.9 % |
| s7.sector.land_scattered.20261112 | report | median 0.863 ms; P95 1.055 ms | report | report | median of 3 processes (0.839 / 0.863 / 2.508); spread 193.3 % |
| s7.sector.coast.20261112 | report | median 0.655 ms; P95 0.778 ms | report | report | median of 3 processes (0.655 / 0.644 / 1.754); spread 169.4 % |
| s7.sector.lake_shore.20261112 | report | median 0.889 ms; P95 1.221 ms | report | report | median of 3 processes (0.852 / 0.889 / 1.776); spread 103.9 % |
| s7.sector.ocean.20261112 | report | median 0.423 ms; P95 0.548 ms | report | report | median of 3 processes (0.423 / 0.410 / 0.856); spread 105.4 % |
| s7.sector.land_strip.7 | report | median 0.893 ms; P95 0.924 ms | report | report | median of 3 processes (2.017 / 0.889 / 0.893); spread 126.3 % |
| s7.sector.land_scattered.7 | report | median 0.887 ms; P95 1.002 ms | report | report | median of 3 processes (1.879 / 0.846 / 0.887); spread 116.4 % |
| s7.sector.coast.7 | report | median 0.718 ms; P95 0.825 ms | report | report | median of 3 processes (1.749 / 0.645 / 0.718); spread 153.7 % |
| s7.sector.lake_shore.7 | report | median 0.776 ms; P95 1.056 ms | report | report | median of 3 processes (1.777 / 0.742 / 0.776); spread 133.3 % |
| s7.sector.ocean.7 | report | median 0.455 ms; P95 0.470 ms | report | report | median of 3 processes (0.923 / 0.412 / 0.455); spread 112.2 % |
| s7.sector.land_strip.424242 | report | median 0.994 ms; P95 1.017 ms | report | report | median of 3 processes (0.994 / 0.974 / 0.995); spread 2.1 % |
| s7.sector.land_scattered.424242 | report | median 0.826 ms; P95 0.999 ms | report | report | median of 3 processes (0.826 / 0.825 / 0.827); spread 0.2 % |
| s7.sector.coast.424242 | report | median 0.625 ms; P95 0.756 ms | report | report | median of 3 processes (0.608 / 0.625 / 0.712); spread 16.6 % |
| s7.sector.lake_shore.424242 | report | median 0.722 ms; P95 1.032 ms | report | report | median of 3 processes (0.714 / 0.722 / 0.760); spread 6.4 % |
| s7.sector.ocean.424242 | report | median 0.409 ms; P95 0.426 ms | report | report | median of 3 processes (0.398 / 0.409 / 0.468); spread 17.0 % |
| s7.height_ns | report | 451 ns/call | report | report | Height(dir) on directions inside 100 land sectors (cache-friendly walk order is NOT used: random vertices), median of all seed x process runs |
| s7.memory | protocol | 0.057 MiB | <= 16 MiB | PASS | managed memory of one PlanetSurface (layer + lake-cell caches) after 1000 random sectors (max over seeds and processes) |
| s7.planet_map | report | 6.57 s | report | report | 2048 x 1024 equirectangular map, single thread (about 2.1 M samples) |

## Images (raw folder, git-ignored; sha256)

- `planet_seed20261112.png` 213c860571a69724...
- `cube_seed20261112_f0.png` 7e8dc3b6d86fe965...
- `cube_seed20261112_f1.png` c9029733156cee70...
- `cube_seed20261112_f2.png` 387ca3804665bd6b...
- `cube_seed20261112_f3.png` f58ea7b404631245...
- `cube_seed20261112_f4.png` c11e388cee5287b7...
- `cube_seed20261112_f5.png` e9748ec594e21a2b...
- `region_coast_seed20261112.png` dec9bfc94cd22bd3...
- `region_lake_seed20261112.png` 10071c6a09bffb47...
- `region_forest_seed20261112.png` a6d8a22282e887aa...
- `lakeshore_seed20261112.png` c240b96ac08ca65e...
- `planet_seed7.png` 0d0c4017751c1c0f...
- `cube_seed7_f0.png` babd5024cf6c413d...
- `cube_seed7_f1.png` f30dad5607046ba9...
- `cube_seed7_f2.png` 645671f28f44ccdf...
- `cube_seed7_f3.png` ba3e83da8b1f4ff0...
- `cube_seed7_f4.png` 7fe48a99ef3275e9...
- `cube_seed7_f5.png` 082dbf321f9ba9b3...
- `region_coast_seed7.png` ea97614164fdca46...
- `region_lake_seed7.png` 72bbd05c3f38270f...
- `region_forest_seed7.png` cef5dce370929f5a...
- `lakeshore_seed7.png` d035614bae2eb7a5...
- `planet_seed424242.png` 230eed68e84b4627...
- `cube_seed424242_f0.png` 305fe7872a044a26...
- `cube_seed424242_f1.png` 7035b0f1fed5f60e...
- `cube_seed424242_f2.png` 665444b54bf82e50...
- `cube_seed424242_f3.png` b1af54ba2509ec59...
- `cube_seed424242_f4.png` 9c7e88c57bcc949e...
- `cube_seed424242_f5.png` f927276a3709ecfe...
- `region_coast_seed424242.png` 579e5665b9d3b514...
- `region_lake_seed424242.png` b044fafabb0ae9af...
- `region_forest_seed424242.png` 23926ce8d85efa10...
- `lakeshore_seed424242.png` 1408564a1f823511...
