# World generator plan (RG-106)

Status: proposal for the user and the orchestrator, 2026-10-29. No ADR is changed here; section 6 holds drafts only. Planning ticket: no game code, no Unity run, no download.
Tags: **M** = measured or computed from files in this repo (source named), **O** = estimate or published practice, not verified here.

User intent (2026-10-29, verbatim): "plánovaná mapa je 60x60km s městem, vesnicemi, horami a sítí dálnic s tunely. kn5 mody jsou ideální \"tréningová\" data." Earlier the same day: the maps train a road-geometry tool, an "AI asysted script" generates a world "s logickými silnicemi v nativních Unity shaderech", the KN5 converter becomes a dev tool. "Nástroje mají být pro AI": agents drive and check the generator, the user judges only the resulting world.

## 1. Target and budgets

**Target world (O):** 60 × 60 km = 3600 km², one city (~100 km²), villages, mountains, a motorway network with tunnels, about 2000 km of road in total. The estimate uses the densities below: city 2.1 km/km², rural 0.45 km/km², motorways 300 km.

**Densities from the three training maps (M, `artifacts/worldgen/<map>.road-stats.json`, section 8):** Kanazawa 120.9 km of road samples in a 58 km² bounding box = 2.1 km/km²; Sierra 406 km in a 899 km² bounding box = 0.45 km/km². These are bounding boxes, not land areas, so both densities are lower bounds. The brief calls Sierra "about 20 × 15 km", but the checkpoint extent is 30.8 × 29.2 km (M, x −15389..15387, z −13642..15553); the two figures disagree and the real land area needs a mesh extent check in the dataset ticket.

| Budget | Value | Source |
|---|---|---|
| Frame | notebook i3-1215U/UHD, Kanazawa day P95 13.1 ms Medium, 17.4 ms High-lite, ~22 ms High; GPU-bound, main thread ~2.7 ms | M, `performance-budget.md` §1, §4 |
| RAM | peak private ≤ 6 GiB, cold start ≤ 40 s (ADR-024); 15.7 GiB installed | M, ADR-024 |
| Today's streaming | Sierra whole 2157 MiB → streamed 1571 MiB (-27 %), P95 3.7 → 1.8 ms, 600 m load radius, one ~40 ms hitch per sector build seen on Gunsai | M, review RG-044 |
| Largest map in RAM | Kanazawa mix-high 3703 MiB commit, GPU ~976 MiB | M, `map-memory-and-extent.md` |

**What is resident and what streams (O, to be measured by ticket W7):**
- Resident, always: the road graph of the whole world (centrelines, classes, junctions, tunnel/bridge spans), the heightfield at coarse resolution, the settlement and vegetation recipe seeds. Centreline cost: 1 point per 10 m × 12 B × 2000 km = **2.4 MB** (M arithmetic). The coarse heightfield at 32 m/px, 16 bit, is 3600 km² / 1024 m² per px = 3.5 M px = **7 MB** (M arithmetic).
- Streamed per cell: fine terrain tile, road mesh and collider, buildings, vegetation, lights. Never the whole world.
- Active window: 1 km cells; an LOD0 ring of about 3 × 3 cells plus a coarse terrain ring out to the horizon. Today's streaming loads 600 m (M), so a 1 km cell with 1 cell margin is the same order. The rendered triangle budget per frame stays inside the Kanazawa envelope (P95 ≤ 17.4 ms High-lite) until a W-ticket measures a different one.

**Disk per km² (reference vs plan):**
- KN5 maps as built: Sierra 8 static models 1.24 GB; with the 1.83 GB layout over the brief's 300 km² that is about 6 MB/km² (O: the area is uncertain, see above). At 3600 km² a baked KN5-style world would be about 22 GB (O). Rejected.
- Generator recipe (data only): heightfield at 4 m/px, 16 bit = 125 kB/km² → 450 MB for the whole world (M arithmetic); at 8 m/px 112 MB. Roads, settlements, vegetation seeds: a few MB. Target: **recipe ≤ 0.5 GB, no baked cell meshes on disk** (O).

**Generation time:**
- Offline bake of the recipe (graph, heightfield, placement seeds): minutes to tens of minutes on the notebook CPU (O, no measurement exists). Gate in W-tickets: ≤ 30 min full world, deterministic from a seed.
- Runtime cell build from the recipe: must fit a ≤ 4 ms main-thread slice per frame or run on a job thread with only the mesh upload on the main thread (O). Hitch rule: no frame above 33 ms during cell loads, as in the RG-044 gate (M, the 40 ms Gunsai hitch is the number to beat).

## 2. Architecture options

### 2.1 World cells and streaming
- **Option A (recommended): cell grid 1 km, recipe-driven.** Reuse `src/NightRacer.Streaming` (`SectorMap`, `StreamingPlanner`, `LongMap`; headless tests in `tests/NightRacer.Streaming.Tests`) and the Unity `SectorStreamer`/`ISectorSource` shape from RG-044 (M, files exist). New `GeneratedSectorSource` builds a cell from the recipe instead of from KN5 nodes.
- Option B: bake every cell mesh offline into asset bundles. Rejected: ~22 GB (O) and a slow edit loop for agents.
- Option C: Unity Terrain + SplineContainer packages. Rejected for now: new package dependency (user-reserved install, AGENTS.md), and holes for tunnels plus custom LOD are the same work either way. Revisit if W3 shows the own heightmap tile cost too much.

### 2.2 Precision: floating origin or origin rebasing
- float32 spacing at 30 km from the origin is 2^-9 m = **1.95 mm**; it becomes 3.9 mm only beyond 32.8 km (M, arithmetic). The brief's "4 mm at 30 km" is therefore rounded up. In a 60 km world centred on 0 the worst case is 1.95 mm in coordinates, but mesh vertices, wheel colliders and shadow/camera maths on the GPU are the real risk: jitter of lights and cascades was not measured at 30 km (unmeasured).
- Today there is no floating origin (M, no hit in `Assets/Scripts`). Sierra spans ±15.4 km and drives and streams without a rebase (M, RG-044/073 manual drives OK), so the existing code already tolerates 15 km.
- **Option A (recommended): origin rebasing every 5 km of travel.** Shift all active cell roots and the car together at a cell boundary while the car is not in a physics step. World coordinates are kept as `double` (or cell index + local float) in the recipe and streaming layers; Unity objects stay near 0.
- Option B: keep one global float space (30 km max). Zero code, but unmeasured jitter and 2 mm vertices; rejected unless W7 measures it clean.
- **Physics plugins (ADR-003/004):** the SDK is engine-neutral and the host passes state to plugins every step. A rebase teleports Rigidbody, wheel state and the car together, so plugin-internal positions must be relative. Rule: plugin state must contain no absolute positions; host subtracts the rebase offset before the next step. Needs a one-time audit of arcade-u2, sim-lite, carbon-drift state (O, not read in this ticket) and a headless test with a forced rebase mid-drift (W7 acceptance). No SDK change expected.

### 2.3 Terrain
- Heightmap tiles per cell, 16 bit, 4 m/px at LOD0 (1 km = 250 × 250 px = 125 kB), then 8, 16, 32 m LOD rings (O). Mesh built at load: regular grid with LOD skirts/stitching, collider = LOD0 mesh only inside the 3 × 3 window.
- The fine height is `coarse noise + road-carved corridors`: each road segment lowers/raises the terrain along its corridor (cut and fill) so the road surface is the authority and the terrain follows, not the other way round.
- **Tunnels:** the road graph marks a span as tunnel; the terrain tile gets a **hole mask** over the portal-to-portal footprint (cells of the heightmap flagged, mesh triangles skipped, collider omitted) and a separate tunnel mesh with lights is generated. Portals are placed where the road centreline crosses the surface offset by the lining height.
- Bridges: road surface above terrain by more than 4 m over a stretch longer than 30 m, support piers every 25–40 m (O). Stacked layers are the same concept `Kn5RoadNetwork` already uses (2 layers per pixel, M).

### 2.4 Road graph
Nodes = junctions and span boundaries; edges = road segments with a centreline (clothoid-line-arc sequence, sampled every 10 m), class, lanes, width, speed, banking, grade profile, tunnel/bridge spans.

| Class (O) | Design speed | Min radius at e 7 % | Max grade | Lanes × width |
|---|---|---|---|---|
| Motorway | 130 km/h | ~830 m | 4–5 % | 2+2 × 3.75 + shoulders |
| Primary | 100 | ~440 m | 6–7 % | 2 × 3.5 |
| Secondary | 80 | ~250 m | 8–10 % | 2 × 3.25 |
| Local / village | 60 | ~130 m | 10–12 % | 2 × 3.0 |
| Mountain / track | 40 | ~55 m | 12–15 % | 2 × 2.75 |

Radius formula (O, published practice): R = v² / (127 (e + f)), v in km/h, e = superelevation, f = side friction (0.09 at 130 → 0.17 at 40). The table values are computed with it; they are rules to fit and check against the data in section 3, not standards text read here.

Rules to generate:
- Transitions: clothoid between a straight and an arc; arc ≥ 3 s at design speed. Superelevation 2.5 % crown to e over the transition.
- Junction types: T, X (priority), roundabout (urban), diamond/trumpet interchange (motorway-to-primary), grade-separated crossing (uses the layer concept above). Angle between legs ≥ 70° for priority junctions.
- Routing: least-cost path on the terrain with cost = length + earthwork + grade penalty + class constraint; where the cost of cut/fill exceeds a threshold, insert a **tunnel** (through a ridge, minimum 150 m) or **bridge** (over a valley, minimum 60 m). The threshold is a parameter fitted from Gunsai/Kanazawa/Sierra spans (section 3).
- Network: city grid/organic street pattern from a few parameters; villages on secondary roads at 3–8 km spacing; motorway corridors link the city to 2–3 edge exits (O).

### 2.5 Meshes, colliders, materials, placement
- Road mesh: cross-section extruded along the centreline (carriageway, shoulders, kerb, markings in the texture or a decal layer); collider = the same mesh, no separate physics mesh. Road surface tags feed the existing surface system (`Kn5Surfaces` concept; the generated world supplies tags directly).
- Materials: **native URP Lit/Simple Lit only**, no KN5 shaders (user: "v nativních Unity shaderech"). Road, kerb, terrain splat (3–4 layers), building facades via a small atlas. Night: lamps and tunnel lights through the existing light system (RG-081 CSP lights are a reader of `ext_config.ini`; the generator will emit light records in the same internal form, not INI).
- Settlements: parcel subdivision from the street graph, building footprints with 2–3 height classes, kit-of-parts prefab-free meshes generated at load. Vegetation: density maps from slope/altitude, instanced meshes with a 150 m cull (Kanazawa budget for vegetation: Medium ≤ 3 ms GPU, High-lite ≤ 2.5 ms, M `performance-budget.md` §5), no grass shadows (M, same section).
- Merge and cull: reuse the mesh merge + 400 m distance cull idea of RG-090 (M, gain up to ~0.9 ms, so not a first-order lever).

### 2.6 Reuse of existing modules
| Existing | Use |
|---|---|
| `src/NightRacer.Streaming` + `SectorStreamer` | cell scheduler, hitch gate, tests |
| `Kn5RoadNetwork` + `Kn5RoadRaster` (RG-085) | **analysis only**: dataset extraction from KN5 maps (section 4); not a runtime dependency |
| `tools/kn5-unity/reset-points/*.json` | first dataset sample (section 8), reset/checkpoint format for generated roads |
| RG-104 sweep (inspector, 3 × 3 picks, A/B captures) | automatic visual check of generated cells |
| CSP lights (RG-081..), URP profile | night lighting in tunnels/streets |
| Physics SDK plugins (ADR-003/004) | unchanged except the rebase rule in 2.2 |

## 3. What is learned from KN5 maps, and how

- **Sources:** open vector data first (OSM + DEM, CARLA `.xodr`), unlocked KN5 mods second (dressing and feel); see section 5a. The KN5 sample in section 8 stays as the proof that the schema is fillable.
- **Method: fitted statistics and rules, not a neural model (recommended).** Reasons: no GPU training on this notebook (AGENTS.md hardware), 3 maps are too few for a learned generative model (O), and fitted tables are inspectable and testable by agents.
- Fitted from the dataset: distributions per class of radius, grade, segment length, curve-to-straight ratio; junction spacing and degree; tunnel/bridge span lengths and what terrain cost triggered them; roadside object density per 100 m by class and land use. Output: `world-rules.json` (derived, outside Git) consumed by the generator; every rule keeps its source map and sample count.
- Optional learned component later (O): a small CPU model (e.g. gradient boosting or a Markov chain on curvature sequences) for "what comes after this curve" realism. Needs a Python package (user-reserved install); stdlib Markov chain first.
- **LLM-assisted authoring:** the agent edits a **world spec** (JSON: seed, bounding box, city centre, village count, motorway corridors, mountain regions, rule overrides) and may write small generator scripts; it does not place roads by hand. The generator is deterministic from the spec + seed.
- **Automatic checks an agent runs (validators, exit code ≠ 0 on failure):**
  1. radius ≥ class minimum at design speed; grade ≤ class maximum; clothoid continuity (curvature jump ≤ threshold);
  2. graph connected (one component for drivable roads); every junction leg angle ≥ 70°;
  3. tunnels ≥ 150 m with portals on terrain; bridges ≥ 60 m with clearance ≥ 5 m;
  4. no road mesh intersects terrain above the cut/fill margin; no collider gap (reuse the RG-044 "no collider / fall / unload in contact" counters, M);
  5. budgets: cell build ms, triangles, draw calls, RAM peak per window on the notebook, via the RG-044/090 series tooling;
  6. captures: RG-104 sweep poses along a route, A/B against the previous seed; the agent reads the images and `sweep.md`.
- The user's judgement stays at the end: one drive in the generated world.

## 4. Dataset spec (`nightracer.worldgen.road-dataset.v1`)

**Location:** `artifacts/worldgen/<map>/` only (ADR-018/023/027: derived third-party data never goes into Git or a build). Only the schema, the script, and aggregated numbers in docs are committed.

**Units and frame:** metres, degrees, Unity axes of the player (x east-ish, y up, z north-ish, KN5 mapping (−x, y, z) per ADR-015), one file set per map.

| File | Content |
|---|---|
| `provenance.json` | map name, ADR id, archive sha256, extractor version, date, parameters |
| `nodes.json` | `id`, `pos [x,y,z]`, `degree`, `kind` (end / junction / interchange / portal), `layer` |
| `edges.json` | `id`, `a`, `b`, `class` (guessed: motorway / primary / secondary / local / track), `lanes`, `width_m`, `surface`, `length_m`, `centreline` (polyline every 10 m: x, y, z), `layer`, `span` (ground / bridge / tunnel), `bank_deg` array |
| `profiles.json` | per edge: curvature series (1/m) and grade series (%) every 10 m |
| `junctions.json` | node id, leg count, leg bearings, leg classes, roundabout radius if any, interchange kind |
| `spans.json` | tunnel and bridge segments: edge, start/end distance, length, clearance, portal heights |
| `roadside.json` | per edge and 100 m bin: counts per object class (tree, lamp, sign, guardrail, building front), distance to the road edge |
| `stats.json` | the distributions that `road_stats.py` prints today, per class |

**What exists today vs what is missing (M):** the committed `reset-points/<map>.json` carry only checkpoints every ~100 m (position, yaw, heading flag, verdict) and **no graph links, width, lanes, banking, spans or roadside objects**. The sample in section 8 rebuilds an approximate graph from them. A full dataset needs a Unity headless export of what `Kn5RoadNetwork` already holds in memory (parent chain, `DistM`, layer, road frame width from the `Frame` probe), plus mesh queries for width, tunnel detection (roof above the road in the same node) and roadside objects (node names of trees/lamps/signs). That is ticket W1.

**Provenance per map:** ADR id, source URL, sha256, unpack date, extractor commit. Dataset files never mention texture or mesh content, only measurements.

## 5. Selection criteria for more training maps

**Missing today (M, from the three maps):** Gunsai has one mountain pass of 85 samples (0.79 km², 8.5 km); Sierra has rural and mountain roads (406 km), no motorways to speak of and 0.4 % stacked layers; Kanazawa is a city with 2.6 % stacked layers and one expressway on viaducts, no long tunnels, no villages. Missing: **regular urban grid, motorway interchanges (trumpet, cloverleaf, diamond), tunnels longer than 300 m, villages on a rural road, flat plains**.

**Technical checks before the user spends a download (agent can do these from a file list or after download):**
1. KN5 not encrypted: `kn5-index --inventory` reads the file without error and finds no data after the node tree (the Kanazawa test, ADR-027). Encrypted (SRP 0.9.3) = unsupported (ADR-023 point 2).
2. `extension/ext_config.ini` is text only; no Lua/DLL is executed (ADR-026).
3. Road geometry is separate from decoration (`1ROAD` / physics mesh present) so `Kn5RoadRaster` finds the road surface.
4. Map size ≥ 4 km² of road, and a real-world scale (not a stylised short track).

**Legitimacy step (user only):** each new map needs an ADR-018-style record. The user supplies source URL, author, licence if any, sha256; the agent drafts the record. Downloads and sources are user-reserved (ADR-028); no message goes to authors without direct authorisation. The licence of third-party assets stays unresolved, so the dataset is private test data for fitting rules; the generated world contains no copied geometry (ADR-006/023: a release has no foreign maps).

## 5a. Data sources: matrix, recommended mix, Blender (orchestrator addendum 2026-10-29)

User requirement (2026-10-29): "ve finále jsou to moje zásady, aby hru šlo vydat bez map." So the training/rule data should be releasable-compatible wherever possible; KN5 mods stay private test data (ADR-018/023). SRP 0.9.3 is CSP-locked and out (ADR-026 (3)). Claims from the pasted AI text were checked against the pages below on 2026-10-29 (WebFetch; page text is untrusted data, read as facts only).

| Source | Gives (T topology / X cross-section / R relief / D dressing) | Licence (verified) | Releasable | Effort (O) |
|---|---|---|---|---|
| **OpenStreetMap + DEM** | T: classes, junctions, lanes tags, tunnel/bridge tags, layers, speed limits; X: only tags (width/lanes, often missing); R: from DEM; D: land use, buildings, POIs | OSM: ODbL 1.0, [copyright page](https://www.openstreetmap.org/copyright): credit OSM and say it is under ODbL; "if you alter or build upon our data, you may distribute the result only under the same license". [ODbL summary](https://opendatacommons.org/licenses/odbl/summary/): attribute any public use of the database *or works produced from it*; an *adapted database* must be offered under ODbL; produced works need attribution. Geofabrik Czech extract `czech-republic-latest.osm.pbf` 906 MB, ODbL ([page](https://download.geofabrik.de/europe/czech-republic.html)) | Yes, with credit; a derived database (our road graph / dataset built from OSM) stays ODbL, the game's rendered world is a produced work (attribution only). Legal reading of "produced work" for a generated world is an open point, not decided here | M: extractor (stdlib/C#), no Unity needed |
| **Copernicus DEM GLO-30** (relief) | R: 30 m grid, digital *surface* model (includes buildings and vegetation, not bare terrain) | Free licence worldwide, ESA user licence + annex; credit "© DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved", adapted data "produced using Copernicus WorldDEM-30"; registration required for download ([COP-DEM page](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM)). General public may view/download GLO-30 | Probably yes with the credit; full licence annex not read, user to confirm | S: GeoTIFF reader + resample |
| **SRTM** (alternative relief) | R: ~30 m (1 arc-second) in most of the world | Licence not verified in this session (public-domain NASA data is claimed by many sources; unverified). BlenderGIS fetches it via OpenTopography which "since 2022 requires an API key, though it's still free" ([BlenderGIS](https://github.com/domlysz/BlenderGIS)) | Unverified | S |
| **CARLA OpenDRIVE towns** | T + X: lane-level junctions, lane widths, elevation, superelevation, in Town01..12 (Town04 mountain highway, Town05 grid, Town06 multi-lane highway, Town10 downtown, Town12 large). Town06 and Town07 are "additional content, not in the standard package" ([CARLA map docs](https://carla.readthedocs.io/en/latest/core_map/)) | Code MIT ([LICENSE](https://github.com/carla-simulator/carla/blob/master/LICENSE), CVC/UAB 2017); README: "CARLA specific assets are distributed under the CC-BY License" ([repo](https://github.com/carla-simulator/carla)). The LICENSE file says nothing about third-party assets and the map docs name no per-map licence, so each town's `.xodr` needs its own provenance check. ASAM OpenDRIVE spec 1.9.0: "free of charge", download needs ASAM login or contact details and acceptance of ASAM terms; royalty-free status not confirmed ([ASAM](https://www.asam.net/standards/detail/opendrive/)) | CC-BY: yes with credit, if the xodr is CARLA-authored; verify per town | M: OpenDRIVE geometry parser (lines, arcs, spirals, poly3, elevation, superelevation, lane sections) |
| **Unlocked KN5 mods** (Gunsai, Sierra, Kanazawa; more) | T: real-road topology (limited); X: kerbs, barriers, markings; D: props, vegetation, materials, CSP look | None found; private test data (ADR-018/022/023/027) | **No** (rules/statistics only, never geometry; release has no foreign maps) | M (RG-085/103/105 tools exist; W1 extension) |
| **BeamNG.drive content** | maps and meshes | Proprietary: [EULA](https://beamng.com/game/support/policies/eula/) §3.1 "limited, non-exclusive license and right to install the Software", §3.5 "All rights no expressly granted herein are reserved", §4.2 forbids using "separate component parts", modding only as the external guidelines permit | **No. Excluded**: the pasted claim that it is open source is wrong; an open file format is not an open licence | n/a |

**Verified corrections to the pasted text:**
- BeamNG is not open source (above). Excluded unless a licence file says otherwise.
- "Gunsai is locked" is wrong for this project: RG-041 reads it fully (brief addendum, orchestrator). Only CSP-encrypted content (SRP) is locked.
- "Peyregrosse & Semetin by JCRR shared as FBX" is **not confirmed**: search results describe both as SIM TRAXX Assetto Corsa rally stages (Peyregrosse a conversion of a JCRR/Thierrydu81 track, Semetin from the Czech Republic, originally made for RBR per a comment) on Overtake.gg, with no FBX source and no licence found. They would be KN5 mods of unknown licence, treated like any other KN5 map. Not cited as a source.
- CARLA: both licence claims are true for the repo (MIT code, CC-BY "CARLA specific assets"), but the repo does not say which maps count as "CARLA specific", and the OpenDRIVE spec is free to download but under ASAM terms.

**Recommended mix (O):**
1. **Topology and network at 60 km scale: OSM** (a Czech extract is ~900 MB pbf; cut a 60 × 60 km window; fit class mix, spacing of junctions and villages, network density per land-use zone, tunnel/bridge frequency). Highest value because it is real, lane-tagged, releasable with credit. Gap: no lane geometry, no banking, cross-sections only from tags.
2. **Relief: Copernicus GLO-30** (or SRTM) for height distributions (slope, relief, ridge spacing) and to drive terrain cost for road routing in the fitted rules. It is a surface model, so forests and buildings bias heights up; the plan uses it for statistics, not as the shipped terrain.
3. **Junction and lane geometry: CARLA `.xodr`** (superelevation, lane widths, junction connection shapes, interchange layouts from Town06/Town12), after a per-town licence check.
4. **KN5 mods: dressing and feel only**: cross-section kit, prop and vegetation density, material look, night lighting. Rules and numbers, never geometry.
5. Generation licence position: the shipped world is produced from fitted rules and seeds; the world must not embed OSM-derived geometry unless the user accepts ODbL attribution (and share-alike for any redistributed derived database).

**What ODbL means for us (O, from the summary above, not legal advice):** (a) public use needs credit "© OpenStreetMap contributors" and the ODbL statement, in a credits screen; (b) if we distribute a database derived from OSM (for example a shipped road graph of a real region) it must be ODbL; (c) a generated world that only uses *statistics* from OSM (distributions, rules) does not contain OSM data and avoids (b); (d) a world that copies real roads from OSM is a derived database. **Recommendation: use OSM for fitting rules (c) by default; shipping real roads (d) is a separate user decision.**

**Blender in the pipeline (recommendation: yes, as a dev tool, not at runtime):**
- Blender 5.2.2 LTS is installed (`tools/kn5/README.md`, user data under `D:\workspace\blender-data`, M).
- Uses: (1) visual preview and validation of generated roads/terrain through headless `--background --python` renders that agents read; (2) authoring the few handmade kit pieces (tunnel portal, bridge pier, kerb profile) as original assets, exported to glTF/FBX for Unity; (3) optional OSM/DEM import for inspection only.
- Add-ons (installs are a user decision, ADR-028; none made): **Blosm** (formerly Blender-OSM; GPL source, free base version with buildings, paid premium, default textures CC0, [GitHub](https://github.com/vvoovv/blosm), [OSM wiki](https://wiki.openstreetmap.org/wiki/Blender-osm)); **BlenderGIS** (OSM XML, shapefile, GeoTIFF DEM, SRTM via OpenTopography API key, minimum Blender 2.83, licence not read from the page, [GitHub](https://github.com/domlysz/BlenderGIS)). Neither is needed for the first tickets: the OSM and DEM extractors (W0) are plain code and agent-checkable; a Blender add-on adds GUI value for the human, which the project does not need ("nástroje mají být pro AI").
- Generator core stays Unity-native C# (section 2) so that world generation and validators run headless without Blender; Blender is optional and replaceable.

**Data sample for OSM (not run):** no OSM extract is on disk and downloads are user-reserved, so no OSM figure is given. Extraction spec for W0: read `czech-republic-latest.osm.pbf` (906 MB, `highway=*`, `tunnel=*`, `bridge=*`, `layer=*`, `lanes=*`, `maxspeed=*`, `junction=*`, `bbox` of the window), output the same `nodes/edges/profiles/junctions/spans/stats` schema as section 4 with `provenance.json` carrying the OSM replication timestamp and ODbL; curvature and grade computed from the DEM-draped centrelines. A PBF reader needs a dependency (osmium or an own protobuf decoder, O); stdlib-only option: the user downloads a small `.osm` XML export of the window instead of the pbf.

## 6. Proposed ADRs (drafts for the user; `decisions.md` is not edited)

> **Accepted 2026-10-29** (user: "Souhlasím s návrhy."), renumbered in `decisions.md` because ADR-029 is held by the open RG-079 sound proposal: draft ADR-029 -> **ADR-030**, ADR-030 -> **ADR-031**, ADR-031 -> **ADR-032**. OSM is used for learning rules only. Tickets: W0 = RG-107, W1 = RG-108, W2 = RG-109, W3 = RG-110.

**ADR-029 (proposed) — Generator direction: native Unity world generator, ADR-019 replaced.**
Decision: build an own procedural world generator in C# (netstandard2.1 core in `src/WhatIf.WorldGen`, Unity adapter for meshes) producing roads, terrain and settlements with native URP materials. ADR-019 (Blender → KN5 pipeline) stays DEFERRED as an alternative; the KN5 converter becomes a dev/training tool.
Trade-offs: (+) fully own content and no licence question for the shipped world, native shaders, deterministic and testable headless; (−) large work (7 tickets below), visual quality bar is open until W3 shows terrain, risk that generated roads feel flat compared with hand-made maps. Rejected: Blender pipeline (two toolchains, KN5 shaders), asset-store packages (installs, licences).

**ADR-030 (proposed) — World size 60 × 60 km and origin handling.**
Decision: target 60 × 60 km; 1 km cells; coordinates in the recipe as double / cell index; origin rebasing every 5 km of travel; physics plugin state must be position-free.
Trade-offs: (+) bounded float error, small engine change; (−) a rebase touches physics, camera, lights and streaming at once (one owner, W7), unmeasured jitter if skipped. Rejected: plain float32 world (unmeasured risk), 20 × 20 km start (can be the first milestone but does not change the architecture).

**ADR-031 (proposed) — Road-geometry dataset from KN5 maps.**
Decision: derived dataset per section 4 from the source mix in section 5a (OSM + DEM first, CARLA `.xodr` second, unlocked KN5 mods for dressing statistics), stored in `artifacts/worldgen/` (KN5-derived files never leave it), one provenance file per source, rules fitted into `world-rules.json`; no mesh, texture or name content from KN5 is stored; extra KN5 maps need an ADR-018-style record per map; OSM-derived data keeps ODbL attribution; BeamNG content is excluded.
Trade-offs: (+) statistics, not assets, is the strongest legal position available (ADR-023, personal use); (−) it does not create a licence, a release still has no foreign maps; it needs Unity runs for full extraction.

## 7. Phased tickets (proposed ids W1..W8; the orchestrator assigns RG numbers)

| # | Ticket | Size | Depends | Acceptance |
|---|---|---|---|---|
| W0 | Open-data extractors: OSM (pbf or `.osm` window) + DEM GeoTIFF → dataset schema v1; optional OpenDRIVE parser for CARLA towns. No Unity | M | ADR-031, user downloads of section 9 | one 60 × 60 km window produces nodes/edges/profiles/junctions/spans/stats with provenance (ODbL, DEM credit); validator exit 0; the section 8 command style reproduces; share of edges without lanes/width reported |
| W1 | Dataset extraction (KN5): Unity headless export of the `Kn5RoadNetwork` graph (links, layers, width) + span/roadside detectors; schema v1 validator | M | RG-085, ADR-031 | Sierra, Kanazawa, Gunsai produce all files of section 4 in `artifacts/worldgen/`; validator exit 0; spans, width and lane counts present with their share of "unknown" reported (≈0 required per audit-no-exempt-cases) |
| W2 | Rules fit: `world-rules.json` from W1 data (stdlib), per-class distributions, span thresholds | S | W1 | rules file with sample counts; report compares fitted vs the section 8 numbers; no class with n < 30 silently used |
| W3 | Road-graph generator on flat test terrain (`src/WhatIf.WorldGen`, headless tests, validators of section 3, JSON export, debug overlay) | M | W2, ADR-029 | seeded run makes a 10 × 10 km net; all validators exit 0; same seed → same hash; unit tests in `tests/` |
| W4 | Road mesh + collider + URP materials in the Unity adapter; surface tags; drive test | M | W3 | car drives 5 km of generated road, 0 no-collider / fall counters (RG-044 style), P95 on notebook reported |
| W5 | Terrain: heightmap tiles, LOD rings, cut/fill along roads, tunnel hole mask, bridges | M | W4 | 10 × 10 km world; tunnel portals on terrain; RG-104 sweep captures reviewed by the agent |
| W6 | Settlements and vegetation: street grid, parcels, buildings, lamps, density maps | M | W5 | city district + 3 villages; vegetation within the 2.5 ms GPU budget (M reference); night lights on |
| W7 | Streaming 60 km: `GeneratedSectorSource`, origin rebasing, plugin audit, hitch gate, RAM series | M | W5 | 60 × 60 km world loads; peak private ≤ 6 GiB; no frame > 33 ms in cell loads on a 20 km drive; forced rebase headless test passes for all three plugins |
| W8 | Optional: learned component / more maps rule update | S | W2 | only if W2 shows a gap; needs user approval of installs |

W1 and W2 can run while W3 starts on synthetic rules. W5 and W6 should not both touch terrain files (one owner).

## 8. Data sample (M)

Command (stdlib Python, reads the committed json, writes `artifacts/worldgen/<map>.road-stats.json`; run from the repo root):

```
python -X utf8 -I tools/worldgen/road_stats.py --map gunsai --map sierra --map kanazawa
```

Parameters: edges 60..150 m between checkpoint nodes, grade ≤ 20 %, edge direction within 50° of the growth yaw of each reliable end, Gabriel pruning with a same-layer witness rule. Reproduces in one command; output is deterministic.

| Figure | Gunsai | Sierra | Kanazawa |
|---|---|---|---|
| Checkpoints (every ~100 m) | 85 | 4060 | 1209 |
| Nominal network length (n × 100 m) | 8.5 km | 406.0 km | 120.9 km |
| Sum of rebuilt edges | 13.3 km | 422.6 km | 169.9 km |
| Extent x / z | 1.13 / 0.70 km | 30.8 / 29.2 km | 7.8 / 7.5 km |
| Relief (min..max y) | 42.9 m | 1418.7 m | 382.8 m |
| Rebuilt edges | 123 | 4041 | 1655 |
| Grade (abs, per edge) p50 / p95 | 5.1 / 17.9 % | 4.6 / 12.4 % | 4.7 / 14.3 % |
| Radius p50 / p05 (3-point arcs) | 156 / 55 m | 525 / 114 m | 374 / 49 m |
| Share of arcs with radius < 100 m | 26 % | 3.4 % | 21 % |
| Junction nodes (degree ≥ 3 in the rebuilt graph) | 44 | 568 | 425 |
| Upper-layer (bridge) nodes | 2 (2.4 %) | 15 (0.4 %) | 31 (2.6 %) |

**Limits (important, M of the method):**
- The rebuilt graph is fragmented (Sierra 304 components, Kanazawa 113, Gunsai 6 of which 5 are single nodes), so degrees, dead ends and junction counts are **not** road topology; they only show that the dataset spec needs the real graph from W1. The first, looser setting (Gabriel only, 150 m) gave 1 to 26 components but degrees up to 43, so the two settings bracket the truth; neither is a measurement of real junction counts.
- Radius comes from three checkpoints ~100 m apart: curves tighter than ~50 m are read too wide (Gunsai's hairpins), so the Gunsai and Kanazawa small-radius shares are lower bounds and the radius p05 is unreliable there. Sierra's p50 525 m and p95 grade 12 % are the more trustworthy figures (large sample, 2800 arcs).
- Grade is the rise over a ~100 m chord, and 4 m raster height noise is included; the Gunsai ≥12 % bin holds 19 of 123 edges, short hairpin chords included.
- Tunnels, width, lanes, banking, roadside objects are not in the json (section 4).
- Input is private third-party-derived data (ADR-018/023/027): output lives in `artifacts/` only.

## 9. Open user decisions

Collected in the outbox block at the end of the job (ADR-029..031 drafts, downloads and sources, the optional installs). Nothing here is decided. Downloads for W0 (user-reserved, exact files):
- `https://download.geofabrik.de/europe/czech-republic-latest.osm.pbf` (906 MB, ODbL; the dated `czech-republic-261027.osm.pbf` is 951 MB per the same page, size mismatch unexplained) or a small `.osm` export of the chosen window.
- Copernicus DEM GLO-30 tiles for the same window via the Copernicus Browser (registration needed).
- Optional: CARLA map `.xodr` files for Town04/05/06/10/12 from the CARLA repo or release (per-town licence to be checked), ASAM OpenDRIVE 1.9.0 spec (ASAM login/details).
- Optional installs: Blosm or BlenderGIS (Blender add-ons), a PBF reader library. None is required for W1-W3.
