# World generator: road dataset v1 from open data (RG-107, W0)

Status: implemented and run once on the Ústí nad Labem window (2026-10-29). Plan: `world-generator-plan.md` §3/§4/§5a. Decisions: ADR-030/031/032 (user: "Souhlasím s návrhy.").
Evidence tags: (M) measured in this ticket, (O) opinion/estimate.

## Purpose and limits of use

OSM and the DEM are used to **learn road rules only** (ADR-032): class mix, radius/grade distributions, junction spacing, tunnel/bridge lengths. No real road shape goes into the game. The dataset lives in `artifacts/worldgen/osm-ustinad/` (git-ignored), raw downloads under `D:/workspace/race_game/temp/opendata/` (git-ignored; override `NR_OPENDATA_DIR`). Only code, this page and aggregated numbers are committed.

## Sources, licences, credit texts

| Source | What | Licence / credit | Evidence |
|---|---|---|---|
| OpenStreetMap via Overpass API (`https://overpass-api.de/api/interpreter`) | `highway` ways of 14 drivable values (no `service`, no footway/path/cycleway) plus their nodes, 6 × 6 tiles of 10 × 10 km, 8 s pause between requests | ODbL 1.0 (https://opendatacommons.org/licenses/odbl/1-0/); credit "© OpenStreetMap contributors" (https://www.openstreetmap.org/copyright). A dataset derived from OSM is an ODbL database: it is not redistributed; rules/statistics only (plan §5a (c)). | (M) 36 answers, 57.6 MB; provenance.json has: url, date, bytes, sha256, query, `osm_base` timestamp per tile |
| Copernicus DEM GLO-30 (AWS Open Data, bucket `copernicus-dem-30m`, tiles N50 E013 and N50 E014) | 30 m digital *surface* model, COG float32 | Free for the general public under the Copernicus DEM licence (https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM), no registration or account for the public bucket (https://registry.opendata.aws/copernicus-dem/). Credit text: "© DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved. Copernicus Digital Elevation Model (DEM) was accessed on 2026-10-29 from https://registry.opendata.aws/copernicus-dem." | (M) 2 files, 58.9 MB, sha256 in provenance.json. The AWS registry page was read; the full licence annex was not (plan §5a already asks the user to confirm for any release). |

The GLO-30 tiles at 50° N have 2400 × 3600 pixels (1.5″ × 1″) and use PixelIsPoint georeferencing; `dem.py` reads both modes from the GeoKeys.

## Window and command

Window `ustinad`: centre 50.66° N 14.04° E, 60 × 60 km, bbox S 50.3902 W 13.6144 N 50.9298 E 14.4656. It contains the D8 motorway with its tunnels through České středohoří, the city of Ústí nad Labem, villages, the Elbe valley and the Krušné hory foothills (the default suggestion of the brief; no better window was tested).

```
python -X utf8 -I tools/worldgen/osm_extract.py all            # fetch (cached) + build + validate + stats; re-runs need no network, ~45 s
python -X utf8 -I tools/worldgen/validate_dataset.py --dir artifacts/worldgen/osm-ustinad
python -X utf8 -I tools/worldgen/dataset_stats.py     --dir artifacts/worldgen/osm-ustinad
python -X utf8 -I tools/worldgen/test_worldgen.py              # 27 unit tests, tiny inline inputs
```

Overpass was reached only over IPv4 (the IPv6 TLS handshake stalled; `osm_extract.py` resolves A records only). The log shows HTTP 502 retries on the last tiles (the fetcher waits 60 s × attempt and retries; the cache then holds the answers and re-runs need no network).

## Schema v1 as implemented

Each file is `{"schema": "nightracer.worldgen.road-dataset.v1", "items": [...]}` (provenance is a plain object). Units: metres, degrees, percent, 1/m. Frame: local equirectangular around the window centre, x east, y up (DEM), z north (not the ADR-015 KN5 mapping).

| File | Fields (as written) |
|---|---|
| `provenance.json` | schema, adr, extractor version, date, purpose, window, parameters, OSM and DEM licence/credit, counts, `sources[]` (kind, url, date, bytes, sha256, licence, file, osm_base, bbox) |
| `nodes.json` | `id`, `osm_id`, `pos [x,y,z]`, `degree`, `kind`, `layer`, `boundary`, `roundabout_radius_m` |
| `edges.json` | `id`, `a`, `b`, `osm_way`, `class`, `highway`, `lanes`, `width_m`, `surface`, `oneway`, `maxspeed`, `roundabout`, `layer`, `span`, `length_m`, `step_m`, `centreline` (flat x,y,z every ≈10 m), `bank_deg` |
| `profiles.json` | per edge: `curvature` (signed 1/m, left positive), `curv_valid` (index range of real 3-point values), `grade_pct`, `step_m` |
| `junctions.json` | `node`, `legs`, `bearings`, `leg_edges`, `leg_classes`, `min_leg_angle_deg`, `roundabout_radius_m`, `interchange_kind` |
| `spans.json` | `id`, `kind` (tunnel/bridge), `class`, `edges`, `start_node`, `end_node`, `length_m`, `cover_depth_m` (tunnel), `clearance_m` (bridge), `portal_heights_m`, `layer` |
| `stats.json`, `stats.md` | the tables below |

Differences from plan §4:
- **Classes:** `motorway` (motorway + trunk), `primary`, `secondary` (+ tertiary), `local` (unclassified, residential, living_street), `track`, plus **`link`** (all `*_link` ramps; they would pollute the motorway radii). Raw `highway` is kept.
- **Node kinds:** `end`, `junction`, `interchange` (degree ≥ 3 with link and motorway), `portal` (span change), plus **`joint`** (degree 2 without span change).
- `bank_deg` is `null` (OSM has no banking), `cover_depth_m` / `clearance_m` = median over the span of DEM minus centreline / centreline minus DEM (RG-109; the DEM is a surface model, so only motorway tunnel cover depth carries information, see `worldgen-rules.md`), `surface` is the raw tag (mostly missing). `centreline` is a flat array, `step_m` per edge (spacing is length / round(length / 10 m), so 7 to 15 m). `profiles.json` also carries the valid range.
- **No `roadside.json`:** OSM roadside objects/land use are dressing and are out of W0; the KN5 datasets have it (section "KN5 maps (RG-108)").
- `junctions.json`/`nodes.json` roundabout radius = mean distance of the ring nodes from their centroid.
- Heights: DEM bilinear at each sample. **Tunnel and bridge chains are not draped**: their heights are a straight line between the end heights (the DEM is a surface model: it shows the hill over a tunnel).
- Edges are cut at every node shared by two way pieces and at way ends, and clipped to the window (cut ends have `boundary: true`).

## Statistics (M, window ustinad)

72 006 nodes, 84 299 edges, 15 077 km of road (all classes incl. 9 291 km of `track`), 37 426 junction nodes (degree ≥ 3), 2 398 tunnel/bridge chains; 1 593 140 height queries, 0 DEM voids. Method and its limits are in the header of `tools/worldgen/dataset_stats.py`: 3-point circumradius with 30 m baseline on ≈10 m samples (edges < 60 m give none, capped at 10 000 m); grade over ±30 m (60 m baseline) on ground edges only; junction spacing between degree ≥ 3 nodes after contracting degree-2 nodes of the same class; "n<30" flags a count below 30.

| class | edges | km | no lanes | no width | no maxspeed |
|---|---|---|---|---|---|
| motorway | 683 | 230.6 | 1 % | 91 % | 8 % |
| primary | 2541 | 339.2 | 18 % | 98 % | 12 % |
| secondary | 14731 | 2624 | 76 % | 97 % | 40 % |
| local | 24149 | 2545.3 | 96 % | 97 % | 68 % |
| track | 41743 | 9290.9 | 100 % | 96 % | 100 % |
| link | 452 | 47.4 | 29 % | 96 % | 32 % |

Share of edges without lanes/width is a property of OSM (width is almost never mapped: use class defaults, plan §4 cross-section from KN5 in RG-108).

| class | radius samples n | radius p05 / p50 / p95 (m) | share < 100 m | share < 200 m | curves < 1000 m: n, p05 / p50 (m) |
|---|---|---|---|---|---|
| motorway | 19851 | 578 / 4545 / 10000 | 0.0004 | 0.0017 | 3360, 345 / 714 |
| primary | 23968 | 207 / 4545 / 10000 | 0.0097 | 0.0466 | 7474, 121 / 439 |
| secondary | 202787 | 96 / 1099 / 10000 | 0.0541 | 0.169 | 98646, 70 / 280 |
| local | 154347 | 51 / 709 / 10000 | 0.1295 | 0.2594 | 84488, 36 / 214 |
| track | 745814 | 55 / 490 / 10000 | 0.1373 | 0.2955 | 461150, 44 / 211 |
| link | 2742 | 45 / 264 / 10000 | 0.256 | 0.4322 | 1997, 41 / 153 |

| class | grade samples n | grade abs p50 / p95 / p99 (%) | share > 8 % |
|---|---|---|---|
| motorway | 20015 | 1.7 / 4.64 / 6.32 | 0.0073 |
| primary | 35823 | 1.88 / 7.33 / 10.82 | 0.0363 |
| secondary | 276191 | 2.52 / 9.13 / 12.95 | 0.0807 |
| local | 279540 | 2.79 / 11.52 / 16.47 | 0.1371 |
| track | 972042 | 3.59 / 14.21 / 21.29 | 0.2012 |
| link | 4963 | 2.01 / 6.57 / 9.21 | 0.0262 |

| class | junction spacing n | spacing p05 / p50 / p95 (m) | degree 3 / 4 / 5+ (nodes) |
|---|---|---|---|
| motorway | 155 | 84 / 490 / 6659 | 157 / 1 / 0 |
| primary | 1444 | 11 / 111 / 850 | 1213 / 151 / 8 |
| secondary | 10213 | 13 / 127 / 932 | 8801 / 942 / 25 |
| local | 15398 | 15 / 88 / 369 | 14147 / 1566 / 44 |
| track | 22331 | 18 / 192 / 901 | 19991 / 2398 / 77 |
| link | 313 | 24 / 112 / 385 | 410 / 43 / 3 |

| class | tunnels n | km | length p50 / p95 / max (m) | bridges n | km | length p50 / p95 / max (m) |
|---|---|---|---|---|---|---|
| motorway | 14 (n<30) | 7.92 | 294 / 2145 / 2168 | 168 | 27.4 | 63 / 544 / 1176 |
| primary | 2 (n<30) | 0.24 | 122 / 185 / 193 | 134 | 7.91 | 19 / 240 / 855 |
| secondary | 7 (n<30) | 0.27 | 28 / 69 / 70 | 711 | 17.36 | 12.5 / 79 / 589 |
| local | 27 (n<30) | 0.51 | 14 / 41 / 74 | 550 | 7.65 | 9.6 / 32 / 207 |
| track | 43 | 1.05 | 19 / 53 / 65 | 712 | 8.31 | 9 / 25 / 185 |
| link | 0 | 0 | - | 30 | 1.95 | 61 / 133 / 147 |

Junction minimum leg angle (n = 37 426): p05 24.6°, p50 71.2°, share < 70° = 48 %.

Tunnel counts per class are below 30 everywhere except `track` (43; mostly short underpasses/culverts-like pieces): **do not fit tunnel-length rules from this window alone** (motorway: 14 chains, two carriageways each, so about 7 tunnels). A motorway with two carriageways gives two chains/edges.

## Limits (M/O)

- **OSM is not surveyed geometry.** Vertex jitter σ gives a radius noise floor ≈ 30² / (2σ) m (σ = 0.5 m → ≈ 900 m): radii above ~500 m are an upper bound, radii below ~300 m are real. The p50 of straight classes is therefore not a design value; use the "curves < 1000 m" column.
- Resampling to ≈10 m chords cuts tight hairpins and small loops; edge lengths in `length_m` come from the original polyline (validator allows the sampled line to be up to 35 % shorter).
- Junction nodes are OSM node sharing, not intersections only: Y-splits of dual carriageways, ramps and way joins inflate degree-3 counts and give small leg angles (48 % below 70° is not a violation of the plan's 70° rule, it is a property of OSM modelling). Tracks are 62 % of all kilometres and dominate the totals; per-class rows are the usable view.
- The DEM is a 30 m surface model: grade is smoothed (60 m baseline) and biased by forest and buildings; bridge and tunnel heights are interpolated, so their grade is excluded from the grade table.
- About 50 % of the edges are `track`, so the "all classes" total is not a road network a car drives; no connectivity (components) analysis was made in W0.
- Classes come from `highway` only; no land-use or urban/rural split yet (the villages/city/mountain zoning of the target world needs it: next step RG-109).

## KN5 maps (RG-108, W1)

Status: implemented and run on Gunsai, Kanazawa and Sierra (2026-11-02). Same schema v1 and the same tools as the OSM dataset (`graph.profiles`, `finish_nodes`, `junction_records`, `span_records`, `dataset_stats.compute`, `validate_dataset.py`), plus `roadside.json`. The data lives in `artifacts/worldgen/kn5-<map>/` (git-ignored); no mesh, texture or KN5 node/material name is committed (ADR-032). Provenance `source_kind: "kn5"`: ADR id, archive sha256 (ADR-018 Gunsai, ADR-023 Sierra, ADR-027 Kanazawa; the Gunsai archive size is not recorded in the ADR, so `bytes` is `null` and the validator reports it), extractor commit, all parameters; no ODbL text.

### Mechanism (Unity exports raw numbers, Python makes v1)

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/kn5-unity/run-kn5.ps1 -Mode worldgen -MapName gunsai -Series gunsai     # -batchmode, no window (windowed: -Windowed)
python -X utf8 -I tools/worldgen/kn5_dataset.py all --map gunsai      # raw.json -> v1 files + roadside.json + validate + stats (+ kn5-report.json)
python -X utf8 tools/worldgen/render_net.py --dir artifacts/worldgen/kn5-gunsai --out artifacts/rg108/net-gunsai.png     # Pillow: no -I
```
Kanazawa: `-Fixture D:/workspace/race_game/temp/kanazawa -PlayerExtra "--kn5-layout-models all --kn5-surface-map <worktree>/tools/kn5-unity/kn5-surface-map-kanazawa.json --kn5-guardrails 0"`. Sierra: `-Fixture D:/workspace/race_game/temp/sierra_de_cadiz/sierra_de_cadiz -PlayerExtra "--kn5-layout freeroam"` (`-SkipFixtureHash`: the fixture is several GB). `-nographics` is not used: with no GPU every shader counts as unsupported and the RG-042 audit of the load fails (exit 1); `-batchmode` alone passes. The player argument is `--kn5-worldgen-export <dir>` (hook in `Kn5TestRunner`, work in `Assets/Scripts/Kn5/WorldGen/`).

1. **Raster:** `Kn5RoadRaster.Build(RoadMeshes, 4 m, side <= 8192)`, so Sierra (30 km) is 7704 x 7308 px at a true 4 m/px (the runtime default side 2048 would be 15 m/px). Two height layers per pixel; the pixels where a third layer was dropped are counted by an independent pass.
2. **Skeleton:** Zhang-Suen thinning on the 2.5D node grid (a node's 3x3 neighbours are `Kn5RoadNetwork.Neighbours`, layers thin separately), then a **simple-point sweep** (a skeleton pixel whose neighbours form one 8-connected group is removed). The sweep is an addition to the brief: Zhang-Suen on a 1-2 px wide road leaves staircase corners, which gave 696 vertices and edge ends moved by cluster merges on Gunsai; with the sweep Gunsai has 22 raw vertices.
3. **Graph:** vertices = skeleton nodes of degree != 2, edges = chains; spurs < 12 m pruned iteratively; vertices joined by an edge < 12 m merged into one at their centroid; degree-2 vertices contracted; components < 50 m dropped (counted). One patch class only (the sweep), no stacked heuristics.
4. **Centreline and width:** every edge is smoothed (two binomial passes), resampled at ~10 m, and at each sample `Kn5RoadNetwork.Frame` gives the centre, tangent, left + right = width and the road collider (surface via `Kn5Track.Assignments`). Where `Frame` fails (and at the two edge ends, which sit on junction blobs) `Clearance` measures across the skeleton tangent: width only, centre unchanged (`cross`). A skeleton point that is not on a road collider is snapped to the nearest road within 4 m (the skeleton is quantised to 4 m pixels); if there is none the sample is `none`. Edge `width_m` = median of the measured samples (capped 60 m); `bank_deg` = median over the samples of the height difference of the two road edges (0.5 m inside), + = left higher.
5. **Spans (per sample, then runs):**
   - *Tunnel:* an up-ray from 0.5 m above the deck hits terrain (collider, backfaces on) or a decoration that is not tree / lamp / sign within 15 m, continuously over >= 50 m.
   - *Bridge:* down-rays 2 m outside **both** road edges find the highest ground (terrain or another road collider, or a decoration >= 1.5 m below the deck that is not tree / lamp / sign / rail) more than 4 m below the deck over >= 30 m. **Deviation:** at least one side must have measured ground (Gunsai's outer road ends have no terrain collider below; they produced 100 m "bridges" before this rule). One missed sample inside a run is bridged (a ray can slip between roof triangles). Tunnel wins over bridge.
   - Decorations have no collider (Kn5Track), so the vertical queries use a triangle height grid of the visual meshes near the road (16 m cells within 48 m of a road pixel) instead of Moller-Trumbore on candidate meshes; for vertical rays the answer is identical (the height of the triangle at x, z) and no per-ray mesh scan is needed. Wall visuals (Kanazawa buildings and poles are wall colliders) are indexed; road and terrain visuals are queried through their colliders.
   - Edges are cut at span changes (portal nodes) so `span` holds per edge like in the OSM dataset; cover depth = median roof height above the deck, clearance = median deck minus ground (via `_dem` into `graph.span_records`).
6. **Class and lanes (estimates, written to provenance and stats):** `class_source: "width_rule_v1"`: width >= 13 m `motorway`, >= 9 `primary`, >= 6.5 `secondary`, >= 4.5 `local`, else `track` (no `link`). `lanes_source: "width_estimate"`: lanes = clamp(round(width / 3.5), 1, 6). An edge with no measured width gets `local` and `class_source: "default_no_width"`.
7. **Roadside (`roadside.json`):** per edge, 100 m bins, left and right: occupancy = class of the highest decoration hit by a top-down ray at 2 / 5 / 10 / 20 / 30 m beyond the road edge, every ~10 m (counts per class and offset, `cells` = queries, `occupied` = hits); small objects (bounds footprint <= 3 m) as one instance with the distance to the edge (<= 40 m, beside the road); `wall_share` = share of the bin samples with a wall collider within 3 m of the edge. Tunnel edges have bins with no counts. Class: `tools/worldgen/kn5-roadside-classes.json` (regex over node | material | **KN5 shader**: the Unity material only knows the URP shader, so the exporter pairs the i-th visual build record with the i-th renderer; generic words and generic AC shader names only). `ignore` (sky, water, road/tarmac/pavement/gravel dressing) is not an object. Assumption: an alpha-tested KN5 shader (`ksPerPixelAT*`, `ksTree`, `ksGrass`) = vegetation cards; fences and wires use it too. The local list of the top `other` names is written to `artifacts/rg108/` only.

### Result per map (M, 2026-11-02, i3-1215U / UHD, `-batchmode`)

| | Gunsai | Kanazawa | Sierra |
|---|---|---|---|
| raster px, m/px | 301 x 181, 4 | 1957 x 1896, 4 | 7704 x 7308, 4 |
| two-layer px / third layer dropped | 61 / 1 | 5151 / 1299 | 1120 / 6 |
| graph km (edges, nodes) | 6.8 (12, 12) | 99.9 (564, 454) | 402.2 (523, 404) |
| components after the < 50 m drop (largest km) | 3 (6.2) | 27 (100.0) | 20 (403.9) |
| junctions (degree 3 / 4 / 5+) | 6 (6 / 0 / 0) | 253 (184 / 52 / 17) | 261 (213 / 29 / 19) |
| km by class motorway / primary / secondary / local / track | 0.7 / 0.1 / 0.7 / 5.3 / 0 | 33.9 / 28.2 / 19.4 / 16.4 / 2.0 | 17.6 / 117.4 / 265.0 / 2.3 / 0 |
| width p05 / p50 / p95 (m): motorway | 16.5 / 30.5 / 40 | 14.5 / 21.5 / 40 | 13.5 / 18.5 / 40 |
| primary | 9.5 / 9.5 / 9.5 | 9.0 / 9.5 / 12.5 | 9.5 / 9.5 / 9.5 |
| secondary | 6.5 / 7.5 / 7.5 | 6.5 / 7.5 / 8.5 | 7.5 / 7.5 / 7.5 |
| local | 5.5 / 5.5 / 6.0 | 4.5 / 5.5 / 6.0 | 4.5 / 4.5 / 6.0 |
| track | - | 0.5 / 1.0 / 4.2 | - |
| tunnels (km) / bridges (km) | 0 / 0 | 29 edges (7.9) / 67 edges (16.9) | 1 edge (0.07) / 11 edges (1.2) |
| samples: frame / cross / none | 92.0 % / 8.0 % / 0 % | 77.4 % / 21.7 % / 0.97 % | 96.2 % / 3.8 % / 0 % |
| surface missing | 0 % | 0.43 % | 0 % |
| roadside hits tree / building / sign / guardrail / lamp / other | 764 / 0 / 1 / 153 / 0 / 0 | 1866 / 3066 / 1428 / 326 / 0 / 204 | no object in the KN5 |
| `other` share of occupancy hits (incl. small objects) | 0 % | 3.0 % (3.6 %) | no hits, not measurable |
| small objects (assigned / total) | 21 / 24 | 124 / 264 | 0 / 0 |
| load s / export s | 1.2 / 0.6 | 11.7 / 13.0 | 7.8 / 22.2 |
| peak commit MiB (OS, whole player) | 741 | 3177 | 3965 (7.5 m/px first run: 2902) |

Class mixes are width-rule estimates, not road classes: the `motorway` rows are wide plazas, aprons and open junctions where the probe is capped (40 m, "open side"), not motorways; Kanazawa's expressway is two carriageways of 9-10 m, i.e. `primary`. Sierra's roads are almost all 7.5 or 9.5 m.

### Differences from plan section 4

- `roadside.json` exists for KN5 only (per 100 m bin, per side). The OSM dataset has none.
- `class` and `lanes` are estimates from width (see above), `oneway` is false everywhere, `maxspeed`, `highway`, `osm_way` are null, `layer` is derived from the span (bridge 1, tunnel -1), `osm_id` is null.
- `surface` is the measured collider kind (Asphalt / Concrete), `width_m` and `bank_deg` are measured. Extra edge fields: `class_source`, `lanes_source`, `quality` (per-sample counts of frame / cross / none / surface missing / open side).
- Heights are the real deck heights (no DEM, no straight-line drape for spans). Frame: Unity world (ADR-015, x mirrored) minus the raster centre.

### Limits

- Three or more height layers in a 4 m pixel collapse into two (Kanazawa: 1299 pixels, 1.2 % of road pixels); a 4 m raster cannot separate lanes, so dual carriageways closer than ~8 m become one skeleton line.
- Frame stops at a 1 m gap, so a road with kerb strips or a median is measured as its asphalt only; the 20 m probe limit caps open junctions at 40 m.
- Roadside objects are only what the KN5 contains: **Sierra's trees live in the CSP extension (ADR-026, not read) and give 0 hits**, and Gunsai has vegetation cards only. Lamps are rare (Kanazawa 7 small objects).
- The tunnel and bridge rules are measurements of "something above / nothing below", not names: about 85 % of the Kanazawa tunnel samples hit a decoration roof, the rest terrain only.
- Gunsai is not one component (see the ticket outbox): the paddock road (0.76 km, 16 m below the main road) and a parking plaza are not connected to the main road by the 1.5 m step rule.
- Graph km versus road-mesh area / median width: 0.48 (Gunsai), 0.37 (Kanazawa), 0.82 (Sierra). The area includes plazas, aprons and kerb strips that the frame width does not; not fully explained.

### Reproduce one figure

Gunsai graph length: `python -X utf8 -I tools/worldgen/kn5_dataset.py all --map gunsai` then `kn5-report.json: graph_km` = 6.76 (`edges.json` sha256 `39214631...5f95` on two consecutive exports, i.e. deterministic).

## Comparison with the KN5 sample (plan §8, 100 m checkpoint spacing; not the same method)

| Figure | KN5 Sierra | KN5 Kanazawa | OSM `local` | OSM `secondary` | OSM `track` |
|---|---|---|---|---|---|
| Grade abs p50 / p95 (%) | 4.6 / 12.4 | 4.7 / 14.3 | 2.8 / 11.5 | 2.5 / 9.1 | 3.6 / 14.2 |
| Radius p50 / p05 (m) | 525 / 114 | 374 / 49 | 709 / 51 | 1099 / 96 | 490 / 55 |

Only the grade p95 and the order of magnitude of the small radii are comparable (KN5: 3 points ~100 m apart, which hides curves under ~50 m radius; OSM: 30 m baseline). The KN5 maps are mountain-road maps and sit at the steep end of the OSM `local`/`track` rows; motorway and primary are not represented in the KN5 sample, the OSM window is the only source for them so far. Tunnels/bridges and lane/width of KN5 maps are not in the sample (RG-108).

## Reproduce one figure

`grep -ho '<way id="[0-9]*"' D:/workspace/race_game/temp/opendata/osm-ustinad/tile_*.osm | sort -u | wc -l` → 56 242 = `counts.osm_ways_read` in provenance.json (raw ways after tile de-duplication).

## Geometry probe on the KN5 maps (RG-129)

Where the dataset (graph, centrelines) says **where** to look, vertical rays into the real road collider say what is there. Unity only measures (`--kn5-geometry-probe <grid.json> --kn5-geometry-cases <cases.json>` after the track load, `Kn5GeometryProbe.cs`: all `RoadSet` hit heights per 1 m cell, in cm); `tools/worldgen/geometry_probe.py` does the rest (`cases`, `analyse`, `picture`), `test_geometry_probe.py` is its unit test (picked up by `scripts/test.ps1`). Dataset positions are Unity world minus the raster centre, so `cases` adds `raw.json` `meta.raster` centre back (frame control: rays at 100 random centreline samples hit road within 0.2 m of the dataset height). Files are in `artifacts/worldgen/geometry-probe/` (git-ignored; heights only, ADR-032). Heights are bilinear between the 4 cell centres (nearest-cell reads cross slope x 1 m as a lip; a real step is spread over 1 m); the grid check is the second difference; a fork is a candidate at surface slope < 15 %, coverage < 70 %, angle >= 3 deg, legs >= 20 m apart. Result and limits: `agent_pool/agents/claude/outbox/RG-129-states.md`.

```
python -X utf8 -I tools/worldgen/geometry_probe.py cases --map sierra --out artifacts/worldgen/geometry-probe/sierra-cases.json
run-kn5.ps1 -Mode load -MapName sierra ... -PlayerExtra "--kn5-geometry-probe <abs>/sierra-grid.json --kn5-geometry-cases <abs>/sierra-cases.json --kn5-layout freeroam"
python -X utf8 -I tools/worldgen/geometry_probe.py analyse --cases ...-cases.json --grid ...-grid.json --out ....json    # no Unity, deterministic
```
Generator: harness `outbox/RG-129-gen-grid-Program.cs.txt` (`dump`, `grid`), `cases --dir <dump>`.
