# World generator: fitted rules `world-rules.json` (RG-109, W2)

Status: M (measured) for window `ustinad` only (OSM + Copernicus DEM, dataset v1 of RG-107, 2026-10-29). Fitted statistics, not a model (plan §3, ADR-030..032). The generator (RG-110) reads `artifacts/worldgen/world-rules.json` (derived, git-ignored, ODbL-derived: never redistributed). KN5 datasets (RG-108) are added with another `--dir`, no code change.

## Command

```
python -X utf8 -I tools/worldgen/osm_extract.py all          # rebuilds the dataset from the cache, ~45 s, no network
python -X utf8 -I tools/worldgen/fit_rules.py --dir artifacts/worldgen/osm-ustinad [--dir <more>] --out artifacts/worldgen/world-rules.json [--md rules.md]
python -X utf8 -I tools/worldgen/test_fit_rules.py          # 15 unit tests; test.ps1 runs all tools/worldgen/test_*.py
```

Run time 5 s. Result: 18 941 bytes, sha256 `24c9a9c6fc138f1e881ece77011d56bf81803e2df901894d4250a03daf3cc723`, identical in three runs (sorted keys, values rounded to 0.01, no dates or paths in the file).

Schema `nightracer.worldgen.world-rules.v1`: `classes.<class>.{radius, grade, segments, junctions.{spacing_m, degree, leg_angle_deg}, spans.{tunnel,bridge}.{length_m, cover_depth_m | clearance_m}}`, `junction_filter`, `parameters`, `provisional_rules`. Every block has `n`, `n_by_source`, `sources` (windows), `provisional` (n < 30). The generator may not use a provisional rule without an override.

Dataset change (RG-109, needed for the span triggers): `spans.json` now fills `cover_depth_m` (tunnel) and `clearance_m` (bridge) = median over the span of DEM minus centreline / centreline minus DEM, from the raw DEM heights that `apply_heights` kept before the straight-line span override. Nothing else in the dataset changed (validator OK, `stats.json` identical).

## Rules per class (n in samples/chains/runs; "prov." = n < 30)

Radius samples are ~10 m of road each (method of `dataset_stats.py`); minimum radius = p05 of the samples with radius < 1000 m. Grade: ground edges only; maximum = p99.

| class | radius n | **min radius (m)** (curve samples n) | radius p05 / p50 / p95 (m) | grade n | grade p50 / p95 / **max = p99** (%) |
|---|---|---|---|---|---|
| motorway | 19 851 | **344.83** (3 360) | 578 / 4545 / 10000 | 20 015 | 1.7 / 4.64 / **6.32** |
| primary | 23 968 | **121.31** (7 474) | 207 / 4545 / 10000 | 35 823 | 1.88 / 7.33 / **10.82** |
| secondary | 202 787 | **69.64** (98 646) | 96 / 1099 / 10000 | 276 191 | 2.52 / 9.13 / **12.95** |
| local | 154 347 | **35.98** (84 488) | 51 / 709 / 10000 | 279 540 | 2.79 / 11.52 / **16.47** |
| track | 745 814 | **44.07** (461 150) | 55 / 490 / 10000 | 972 042 | 3.59 / 14.21 / **21.29** |
| link | 2 742 | **40.84** (1 997) | 45 / 264 / 10000 | 4 963 | 2.01 / 6.57 / **9.21** |

Segments (straight = radius ≥ 1000 m; runs end at junctions and at edges < 60 m, so run lengths are biased short):

| class | straight runs n, p05 / p50 / p95 (m) | curve runs n, p05 / p50 / p95 (m) | curve : straight (length) |
|---|---|---|---|
| motorway | 1 184, 19.8 / 69.8 / 560 | 1 043, 10.0 / 20.1 / 118 | 0.20 |
| primary | 1 613, 10.1 / 84.9 / 423 | 1 326, 10.0 / 64.5 / 199 | 0.47 |
| secondary | 15 799, 10.0 / 50.7 / 249 | 16 233, 19.9 / 65.7 / 179 | 0.98 |
| local | 16 132, 9.9 / 57.8 / 175 | 17 218, 20.1 / 69.1 / 163 | 1.20 |
| track | 63 663, 9.9 / 30.5 / 173 | 72 914, 19.9 / 60.5 / 197 | 1.69 |
| link | 197, 9.9 / 47.3 / 182 | 313, 30.4 / 88.5 / 232 | 2.70 |

Junctions after the filter (intersections touching the class; a junction counts for every class with a leg left):

| class | spacing n, p05 / p50 / p95 (m) | intersections n, degree 3 / 4 / 5+ share | leg angle p05 / p50 (°), share < 70° | before the filter: p05, share < 70° |
|---|---|---|---|---|
| motorway | 20 **prov.**, 40 / 410 / 7951 | 20 **prov.**, 0.95 / 0 / 0.05 | 5.9 / 44.7, 0.75 | 4.3, 0.96 |
| primary | 1 041, 32.5 / 168 / 1062 | 1 034, 0.70 / 0.24 / 0.05 | 7.0 / 70.5, 0.50 | 14.2, 0.42 |
| secondary | 8 747, 34.1 / 162 / 995 | 8 401, 0.75 / 0.20 / 0.05 | 15.7 / 68.3, 0.52 | 26.5, 0.45 |
| local | 13 450, 32.3 / 99.5 / 394 | 13 569, 0.76 / 0.20 / 0.04 | 19.5 / 75.1, 0.43 | 30.3, 0.37 |
| track | 20 332, 37.7 / 216 / 936 | 20 267, 0.80 / 0.18 / 0.03 | 19.0 / 62.9, 0.59 | 24.0, 0.55 |
| link | 1 **prov.** | 0 **prov.** | - | 6.0, 0.80 |

Spans (trigger = cover depth for tunnels, clearance for bridges; median over each span, then quantiles over spans):

| class | tunnel length n, p05 / p50 / p95 (m) | tunnel cover depth n, p50 (m) | bridge length n, p05 / p50 / p95 (m) | bridge clearance n, p50 (m) |
|---|---|---|---|---|
| motorway | 14 **prov.**, 30 / 294 / 2145 | 14 **prov.**, 15.65 | 168, 20.5 / 62.6 / 544 | 168, 0.40 |
| primary | 2 **prov.**, 58.7 / 122 / 185 | 2 **prov.**, 0.65 | 134, 7.6 / 19.2 / 240 | 134, 0 |
| secondary | 7 **prov.**, 15.5 / 27.8 / 68.5 | 7 **prov.**, 0 | 711, 5.3 / 12.5 / 78.8 | 711, 0 |
| local | 27 **prov.**, 9.6 / 13.9 / 40.9 | 27 **prov.**, 0 | 550, 4.8 / 9.6 / 31.6 | 550, 0 |
| track | 43, 7.8 / 19.2 / 52.6 | 43, 0 | 712, 4.0 / 9.0 / 25.4 | 712, 0 |
| link | 0 **prov.** | 0 **prov.** | 30, 16.5 / 60.8 / 133 | 30, 0.15 |

**Provisional list (16 rules; `provisional_rules` in the file):** link: junctions.{spacing_m, degree, leg_angle_deg}, spans.tunnel.{length_m, cover_depth_m}; local, motorway, primary, secondary: spans.tunnel.{length_m, cover_depth_m}; motorway also junctions.{spacing_m, degree, leg_angle_deg}. Nothing else is below 30. There is no class with n < 30 that is not flagged (the fit flags by `n`, not by class).

## Junction filter (before / after)

Method (module docstring of `fit_rules.py`): junction nodes within 25 m are one cluster; legs of `link` edges are dropped; two legs of oneway edges of one class with opposite flow and bearings < 30° apart are one carriageway pair and become one leg; edges inside a cluster are not legs; a cluster with < 3 legs is no intersection. Two-way forks are kept.

| | raw nodes | clusters | intersections | legs inside clusters | link legs dropped | carriageway pairs merged |
|---|---|---|---|---|---|---|
| ustinad | 37 426 | 32 561 | 32 015 | 10 534 | 584 | 321 |

Leg angle (smallest angle between neighbouring legs): before p05 24.6° / p50 71.2°, share < 70° = **0.48** (n 37 426). After p05 19.8° / p50 68.2°, share < 70° = **0.52** (n 32 015).

The filter removes the dual carriageway and ramp artefacts but the share barely moves; it even rises, because the junctions that fell out (clusters left with < 3 legs, 546 of 32 561) and the merged raw nodes were mostly wide-angle ones (hypothesis, not separately measured). Cause of the remaining small angles, measured on the 3 539 intersections whose closest pair is < 30°: 3 333 are a pair of two-way edges (2 132 of the 3 539 are single nodes), only 206 involve a oneway edge. These are real acute forks and side roads that merge tangentially, mostly `track`/`local`. Per the brief they stay data; nothing is forced to 70°.

## Fitted values vs the plan

**Plan §8 (KN5 sample; different method: 3 checkpoints 100 m apart, so tight curves are hidden; the KN5 maps are mountain maps):**

| Figure | KN5 Sierra | KN5 Kanazawa | OSM local | OSM secondary | OSM track |
|---|---|---|---|---|---|
| Grade abs p50 / p95 (%) | 4.6 / 12.4 | 4.7 / 14.3 | 2.79 / 11.52 | 2.52 / 9.13 | 3.59 / 14.21 |
| Radius p50 / p05 (m) | 525 / 114 | 374 / 49 | 709 / 51 | 1099 / 96 | 490 / 55 |

Grade p95 and the small-radius order of magnitude agree (track p95 14.2 vs Kanazawa 14.3; local p05 51 vs Kanazawa 49). The p50 values do not: OSM grade p50 is 2.5 to 3.6 % against 4.6 to 4.7 % for KN5, and OSM straight-class radius p50 is a noise-floor value (~900 m at σ 0.5 m), not a design value. The KN5 maps are steeper and winding than the average Ústí road; the fit says nothing about their span, width or lane data (RG-108).

**Plan §3 validator thresholds, which the data supports:**

| Threshold (plan §3) | Data | Verdict |
|---|---|---|
| radius ≥ class minimum | min radius per class 36 to 345 m (table above); not a design speed table | supported as measured, per class; pick the p05 of curves, not a universal number |
| grade ≤ class maximum | p99 6.3 % (motorway) to 21.3 % (track) | supported; use the p99 as the validator maximum (track 21 % is above the §8 KN5 p95 of 12 to 14 %: it is OSM/DEM noise on forest tracks, so cap by class) |
| every junction leg ≥ 70° | real intersections: 43 to 59 % of local/secondary/track/primary have a leg pair < 70° (p05 6 to 20°) | **not supported.** Applied to real roads the rule would fail every second junction. Use it as a generator design target for new T/X crossings only, and expect forks and merges below it |
| tunnels ≥ 150 m | motorway p50 294 m (14, provisional); primary 2 (prov.); secondary/local/track p95 41 to 69 m, p50 14 to 28 m | supported for motorway only (provisional); **not** for other classes: their real tunnels are 10 to 70 m underpasses |
| bridges ≥ 60 m | motorway p50 62.6 m, link p50 60.8 m; primary 19 m, secondary 12.5 m, local 9.6 m, track 9 m | supported for motorway and link; **not** for other classes (p50 below 20 m) |
| bridge clearance ≥ 5 m | median clearance 0 to 0.4 m in every class | **not measurable here.** The Copernicus DEM is a surface model: it contains the bridge deck, so deck minus DEM is ≈ 0 and the clearance trigger carries no information. Same for the tunnel cover depth of local/secondary/track (0 m: the surface model has no 10 to 30 m underpass). Only motorway cover depth (15.65 m, n 14, provisional) looks real |
| tunnel cover depth trigger | motorway 15.65 m | the only usable value, provisional; do not generate a tunnel trigger from terrain for other classes without an override |

## Limits

- One window (Ústí n. L., Czech). Czech roads are not Japanese or US-West mountain roads; rules from other windows or KN5 maps are added with `--dir`.
- Link junction rules are empty by design (ramp legs are dropped); motorway has 20 intersections after the filter (interchanges are links, not junctions of the motorway class), hence provisional.
- Radius p50/p95 of straight classes are noise-floor values (RG-107 limits); use `min_radius_m` and the curve-sample row.
- Run lengths and spacing are biased short/long at the edges of the fit as stated above; a motorway with two carriageways gives two spans/chains.
- `curve_to_straight` uses a 1000 m threshold; with the noise floor of 900 m for OSM jitter σ 0.5 m, straight runs on tracks are a lower bound.
- The filter does not test geometric consistency (no overpass vs at-grade check): layers are not used for clustering. A grade-separated crossing within 25 m of another junction would merge. Largest cluster in the window: 18 raw nodes (`junction_filter.ustinad.max_cluster_nodes`).
- Not done (outside the ticket): land-use/urban zoning, roadside objects, connectivity analysis (dataset limits, RG-107).
