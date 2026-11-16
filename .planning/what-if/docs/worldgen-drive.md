# Driving the generated world (RG-115 / RG-117)

How to drive, look at and measure the `WhatIf.WorldGen` road graph in the KN5 test player (`--stream-map worldgen`). Generator and adapter internals: `worldgen-generator.md` §W4. Everything is a command-line switch of the existing player; there is no menu.

## Launch

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/kn5-unity/run-kn5.ps1 -Mode stream -WorldGen -Series <name> [-ReportDir <dir>] [-Warmup 10 -Duration 900] -TimeoutS 900 -PlayerExtra "<switches>"
```

Build first with `tools/kn5-unity/build-player.ps1`. `tools/kn5-unity/sync-plugins.ps1` copies `WhatIf.WorldGen.dll` (since `2bf54b1`); run it once in a fresh worktree and after every WorldGen change, or the build fails with CS0234 / runs an old generator. `artifacts/worldgen/world-rules.json` is git-ignored and must exist (or pass `--worldgen-rules`).

## Switches

| Switch | Meaning |
|---|---|
| `--worldgen-seed N` | overrides the spec seed (default spec `tools/worldgen/specs/default-10km.json`) |
| `--worldgen-spec <json>`, `--worldgen-rules <json>` | other spec / rules file |
| `--worldgen-route village\|bridge` | old route from the city centre: farthest village node / farthest border node past a bridge |
| `--worldgen-spawn bridge\|viaduct\|tunnel\|interchange\|t-junction\|x-junction\|city\|village` | start at a feature instead (see below); replaces `--worldgen-route` |
| `--worldgen-far-m <m>` | far-ring radius (default 4000, `0` = no far ring; terrain spec only) |
| `--worldgen-check-ground 1` | per built sector: road ribbon samples vs the ground tile (ground over road / terrain through a deck), job-stage timings, non-road steps by sector, in the run notes. Also the **seam check** (RG-119 rework): at every degree-2 node of a built sector the max distance between the matching end corners of the two ribbons (road edge and shoulder edge, both sides), plus the map-wide counts of mitred nodes, unchanged hairpins (turn > 120 deg) and width mismatches. Ribbons at degree-2 nodes share a mitre normal (scale 1/cos(theta/2), capped at 2), so the open wedge is gone; target max gap <= 1 cm |
| `--worldgen-minimap 0\|1` | minimap on/off (default 1 in `manual`, 0 in `drive`) |
| `--worldgen-capture-at <s>` | `drive` only: PNG of the screen (with minimap) `<s>` seconds into the measured loop, `shots-<series>/minimap-<spawn>.png` |
| `--graphics-lab <preset.json>` | `--stream-run manual` and `shots`: applies a graphics-lab preset (apply mode only, same `GraphicsLabSession` as the KN5 drive). The generated world has no KN5 textures / materials, so the preset must not request `aniso*`, `texture*`, `material*` or `reflections` (the verify step refuses it, `kn5-materials` missing). Ready: `tools/graphics-lab/presets/worldgen-max.json` (RG-127; moved from the RG-119 outbox, content unchanged) (1080p, MSAA 4, render scale 1.25, soft shadows, gradient sky, clouds, fog exp2 3000 m). Measurement runs stay 720p Low without it |
| `--worldgen-legacy-geo 1` | RG-115 geometry without the RG-117 fixes (before/after comparison) |
| `--stream-run drive` | PathBot drives the route in real physics, counters NoCollider / Fell / UnloadWhileContact / NotRoad, frame CSV |
| `--stream-run fly` | camera flythrough, same checks, no car |
| `--stream-run manual` | you drive: keyboard / pad, Esc exits, R resets to the last checkpoint (every 150 m of route), `--kn5-seconds <s>` time limit |
| `--stream-run shots` | PNGs to `<report>/shots-<series>/`: T / X junction, interchange node, bridge (on deck / from below / side / end), city street, primary curve, and `spawn-<feature>.png` for every feature |
| `--stream-run none` | generate, index, initial load, report |
| `--stream-start-s <m>` | start `<m>` metres along the route |
| `run-kn5.ps1 -TimeScale N` (= `--stream-timescale N`) | RG-135: `drive` only, correctness runs. `-Warmup` / `-Duration` stay **game** seconds; the player's wall deadline is Duration/N plus the streaming holds; `-TimeoutS` stays a wall limit. While the collision window around the car is missing (`CriticalMissing > 0`), time is held at 0 (`stream_holds`, `stream_hold_s`, `effective_speed` in the run notes). x3 verified: same distance (0-0.1 %) and NoCollider / Fell / UnloadWhileContact as x1 on tunnel + city. Frame time is judged at x1 only |

For the agent-port explorer use `tools/agent-port/explore.py --timescale 4 --no-memory` (RG-135): seeds 1-3 x4 give the same `stuck` / `fell` / `flipped` findings as x1 at ~3.4x shorter wall time (seed 1: 993 s -> 288 s); `frame_spike` is not measured above x1.

## Spawns

Each feature is picked by rule from the graph (`WorldGenMap.FindFeature`, shared with the shots): T / X junction and interchange = the node of that kind nearest the city centre; bridge = first crossing bridge (`Layer 1`); viaduct = the longest `Layer 0` bridge span (the ride starts 250-400 m before its entrance, or on the other end if that has more road; seed 1-3: only ~40 m, a tunnel precedes it); village = the junction nearest the mean of the ground junctions within 400 m of the old route's end; city = local street nearest the origin; The route starts at the graph node 250-400 m (road distance) before the feature, runs through it and on to the farthest node reachable without turning back (>= 2 km when the graph allows). Route 0 is the spawn, facing along the route, so the feature is 250-400 m ahead. Checkpoints and R reset use this route. A missing feature in a graph is reported in the run notes (`spawn <f>: no such feature`), and a route under 2 km too.

## Minimap (manual and minimap runs)

Top right, car-centred, north up, 1000 m window. Grey = local, yellow = primary / link, orange = motorway, magenta = bridge span, cyan line = the active route, white arrow = car. Icons: `B` bridge, `IC` interchange, `S` spawn feature (dim at the panel edge when outside the window). **M** toggles it. One RGBA32 texture over the 10 km square at 5 m/px (about 2000 x 2000), built once at load (log line `worldgen minimap ...` in the run notes).

## Limits

- Flat terrain on the default spec (see Terrain below for the relief), one asphalt material, no buildings or trees: a feature far away is only a notch on the horizon; use the minimap.
- Oneway flags are ignored by the spawn routes (no traffic).
- The village spawn of seed 1 gives a short route (the village component is small); the report says so.
- The interchange gore and steep ramp shoulders are not fixed (out of scope of RG-117).
- Manual drive is not a performance gate; PathBot drives are.

## Terrain (RG-119, W5b-1)

Drive the relief with `--worldgen-spec tools/worldgen/specs/terrain-10km.json` (RG-120: this is now the default; `--worldgen-spec tools/worldgen/specs/default-10km.json` stays the flat spec). On a flat spec the 4-vertex ground quad and no far ring stay; the only visible change is that ramp shoulders now follow the ramp height (spawn shots `city`, `t/x-junction`, bridge pixel-identical to the RG-117 build, `interchange` differs by a 1 % shoulder strip).

- **Near tile:** per 256 m sector a 65 x 65 grid from `TerrainModel.SampleTile(.., 256, 4)` (one extra sample ring for seam-continuous normals), triangles with a hole vertex left out (tunnel portals), a 2 m skirt on the tile edges, 16-bit indices. Tile data is computed on the thread pool (`BuildGround`, thread-safe, `TerrainModel` is read-only) and prefetched around the camera; the main thread uploads the mesh. The collider is baked with `Physics.BakeMesh` on a worker.
- **Road bench:** a 4 m grid cannot follow the 1:1.5 cut / fill that starts 1.5 m outside the road (the chord over the kink lies up to ~0.6 m above the road edge). Grid vertices within half width + 1.5 m + 5.7 m of a ground road are set to the road height - 3 cm (the lowest road if several are in reach); under a bridge deck they are only kept below its underside. Costs: the cut wall starts up to ~6 m further out and is steeper. Shoulders are `road y - Lift + 2 cm` (relative; they were absolute y = 0.02 before).
- **Bench not applied on the tunnel side of a portal (RG-119 rework 2):** a grid vertex whose nearest point on the whole tunnel polyline is inside the tunnel (not the portal end point, and deeper than one 4 m cell) and within tunnel half width + shoulder + 5.7 m keeps the carved model height; the bench would lift the tile over the descending tunnel road (`ground_in_tunnel`, RG-122). Side effect: the carved terrain over the mouth is now drawn as modelled, so the known portal classes (`portal_lid`, `portal_hole`, `fall_off`) count more clusters (RG-120).
- **Far ring:** 32 m chunks (one per sector, 6 m skirt) built from `Height` around the camera within `--worldgen-far-m`, at most ~1 ms of building per frame, hidden while the near sector is loaded. Camera far clip = radius + 300 m, raised only when a far ring exists. No fog, no horizon haze: the ring ends at the spec square (+256 m) and then the sky shows. Beyond the radius the ground simply stops.
- **Cost (i3-1215U, 720p Low, PathBot, terrain seed 1):** frame P95 2.19-2.64 ms (flat spec, same build, other run: 1.2-2.6 ms: the notebook varies more than the terrain costs), far ring on / off 2.56 / 2.64 ms, sector build P95 2.2-2.6 ms (tile data prefetched on the thread pool, main thread uploads), no frame > 33 ms during a load. Evidence: `agent_pool/agents/claude/logs/RG-119-results.txt`.
- **Limits:** tunnel tube, portal pit and viaduct piers: see the next section. Natural height is drawn under bridges, so a deck over a valley shows the valley. The `ridge` shot pose (city edge towards the highest point of the ridge centre line) and `viaduct-side` come with `--stream-run shots`.

## Rain + flood test (RG-122)

Runs **before every user ride in the generated world** (orchestrator rule, 2026-11-03). It walks the world block by block through the real `SectorStreamer` (no whole-map path) and tests what a car can actually hit or fall through.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/kn5-unity/run-kn5.ps1 -Mode stream -WorldGen -Windowed -Series <s> -ReportDir artifacts/sweep/<s> -TimeoutS 3600 `
  -PlayerExtra '--stream-run sweep --worldgen-spec tools/worldgen/specs/terrain-10km.json --worldgen-seed 1 --worldgen-spawn viaduct --sweep-area spawn:600'
```

- **Walk:** serpentine over the sector grid; per step the cursor ticks the streamer until the 3 x 3 ring around it is loaded, then only the **centre** sector is tested (its neighbours are loaded, so seams are tested from both sides). The report logs per step the loaded sectors, Unity allocated memory and build / test time, and the peaks.
- **`--sweep-area`:** `spawn:<m>` (a square around `--worldgen-spawn`), `node:<id>:<m>` or `x0,z0,x1,z1`. Without it the whole map is walked. `--sweep-probe-nodes 935,945` lists the defects within 40 m of those nodes. `--sweep-shots 0` turns the shots off.
- **Rain** (every collider, the whole centre sector): rays down on a 1 m grid within 40 m of a road outline, 4 m elsewhere. Expected surface: the road (profile + lift) inside a road outline, the designed terrain tile elsewhere; tolerance 0.3 m.
- **Flood** (from the road): seeds every 2 m along every edge, at the lane centre and every <= 2 m across the width; breadth-first over a 2 m grid up to 60 m from the nearest road; step <= 0.3 m up / 1.0 m down, slope <= 35 deg, car clearance (0.3 m sphere up to 1.6 m). Lane probes: 0.3 m spheres at 0.8 / 1.2 m in 8 directions (the RG-099 logic).
- **Defect types** (`*` = known class, reported but not failing; owner in brackets):

| type | meaning |
| --- | --- |
| `hole` | rain: no collider within 20 m below the designed surface (RG-119) |
| `ground_over_road` | rain: inside a road outline the first hit is not a road collider (RG-119) |
| `surface_mismatch` | rain: hit > 0.3 m off the expected surface: visible but not solid, or the reverse (RG-119) |
| `seam_gap` | rain: rays every 0.1 m across the full width within 1 m of a degree-2 node or a junction patch border do not land on the road (RG-119) |
| `fall_off` | flood: a reachable cell whose neighbour has no ground within 20 m below (RG-119) |
| `wall_in_lane` | flood: a non-road face (normal.y < 0.7) inside the lane at 0.8-1.2 m (RG-119) |
| `step` | flood: consecutive lane-centre hits (2 m apart) depart from the profile grade by > 5 cm (RG-119) |
| `low_clearance` | flood: a ray up from the road hits something below 4.5 m (RG-120, structures) |
| `road_missing` | flood: a tunnel road seed finds no road at the profile (the rain only sees the terrain lid there) |
| `ground_in_tunnel` | flood: terrain above the profile inside a tunnel (the portal mouth tile lies on the lane; RG-120) |
| `trap` | reachable off-road cells with no way back to the road (positioned since RG-139) |
| `portal_hole*`, `portal_lid*`, `tunnel_edge*`, `bridge_edge*` | open tunnel mouth, terrain over it, the car can leave a tunnel without a tube / a deck without a railing (RG-120) |

- **Flood step-down (RG-139):** a valid neighbour cell lower than the local grade allows by more than `MaxUp` is a ledge: when a structure face (tube wall, viaduct railing) lies between the cells (`Barrier()`), the edge is skipped exactly like an empty neighbour. Before, the flood stepped 0.77 m from the widened span end straight over the railing onto the terrain (seed 1 `trap`). Thresholds unchanged. `trap` is now a positioned defect (cell, y) and appears in the gate list.
- **`bridge_edge` is a defect since RG-139:** a deck edge without railing and nothing within 20 m below is no longer a known class (RG-120 registered it for ramp-lane clusters that no longer occur on seeds 1-3), so the sweep exits 1 and the ride gate refuses.
- **Ride gate (RG-139):** `run-kn5.ps1 -WorldGen` with `--stream-run manual` (also through the ride scripts) first runs the same call with `--stream-run sweep --sweep-area spawn:600` (the ride's spec, seed, spawn; a `--sweep-area` in `-PlayerExtra` wins), series `<Series>-gate`, report `artifacts/sweep/<Series>-gate/`, about 6 s. Exit other than 0: the ride does NOT start, the unknown defects (max 8) and the report path are printed, script exit 3. `-SkipSweep` starts the ride anyway and prints `SWEEP GATE SKIPPED`. Measure runs (`sweep/measure/fly/drive`) are not gated.
- **Output:** `sweep-<s>.json` (every defect cluster with type, owner, sector, edge / station or x/y/z; counts per type; `excludedShare` = flood seeds without a road hit; peaks; wall time), `sweep-<s>.txt` (the one-line summary for `manual-test.py` notes), `sweep-shots-<s>/` (5 worst 4 m cells + the cell behind the spawn). Exit 1 = a defect outside the known classes.
- **Injects** (`--sweep-inject step,collider,wall,seam,hole` or `all`), proof that the passes detect: `step` detaches one road segment and lowers it 15 cm, `collider` disables one sector's road collider (found as `ground_over_road`, or `road_missing` in a tunnel), `wall` places a 1.0 x 1.3 x 2.0 m block in a lane, `seam` opens a 0.3 m wedge at a degree-2 node (fallback: a junction patch border), `hole` removes 4 terrain triangles 10 m beside a road. Each kind is tried in each walked sector until it applies; a window of only bridges and tunnels (the viaduct spawn) cannot take step / wall / hole. Use `--sweep-area node:935:600` on seed 1 (all five found, run `i10`). `--sweep-inject barrier` (by name, not in `all`; RG-139) switches off the viaduct railing collider of the first sector with a deck whose side has terrain 0.2-1.0 m lower: seed 1 `--sweep-area node:945:300` finds 24 drops / 13 clusters `bridge_edge` (none without the inject).
- **Cost (i3-1215U, build 7):** full 10 km map 38 s (flat) to 59-66 s (terrain seeds 1-3) of walk for 664-692 sectors, 600 m window ~1-2 s; peak 9 loaded sectors, ~70 MiB Unity allocated, ~460 MiB private. Evidence: `agent_pool/agents/claude/logs/RG-122-final-summary.txt`.

## Tunnels, portals, viaduct piers (RG-120)

- **Default spec:** `terrain-10km.json` (flat `default-10km.json` via `--worldgen-spec`). **Spawn `tunnel`:** 250-400 m before the first portal of the longest tunnel, heading in. Shot poses `tunnel`, `tunnel-top`, `tunnel-side`, `tunnel-inside`, `viaduct-pier` (`--stream-run shots`).
- **Tube** (`WorldGenTunnel.cs`): per `Span == "tunnel"` segment, inner faces only: walls 1 m below the road to 3.6 m, low arch roof, 5.5 m clear height, wall at half width + 1.5 m shoulder + 0.3 m; ground strip between shoulder and wall. One mesh + MeshCollider ("Tunnel", Concrete tag) per sector. Emissive roof strip; at most 4 pooled real point lights (range 70 m, no shadows) at the lamp positions (every 50 m) nearest to the camera within 150 m.
- **Portal pit** (`WorldGenTerrain.cs`, `PortalPit`): the tile triangles left out at a portal (hole mask dilated by one vertex) get a floor at road level and vertical wall columns up to the rim; where a wall crosses the tube it starts at the roof outline (the doorway). The first 16 m of the tube are double sided (visible concrete collar) and a ring frame closes the portal.
- **Viaduct:** `WorldGenBridge.cs`: 1 m barrier just outside the deck width (collider), piers (two 1.2 m columns) every 35 m down to the natural terrain; skipped where clearance < 2 m, where a road passes below, or where another road's lane meets the barrier (ramp merge, junction end).
- **Sweep adaptations** (detector bugs, see outbox): the rain passes through structures (`Tunnel` / `Viaduct` colliders); the flood treats a structure face next to an empty neighbour as a barrier; `Covered` needs a real collider above; the rain's hole mask is the one the tile was cut with (`GroundData.Omit`).
- **Result:** sweeps seeds 1-3 / flat 1 in `agent_pool/agents/claude/outbox/20261103*_RG-120.md`; PathBot through the tunnel 0 / 0 / 0 / 0; P95 2.54 ms (tunnel) vs 2.55 ms (open road), sector build P95 <= 3.4 ms.

## Road surface closure (RG-128)

- **Junction patch** (`WorldGenSectorSource.StarPatch`): the exact union of the legs' strips (road + shoulder, node to cut line) with the ribbons' cut corners as its own vertices, fanned from the node; each strip's road rectangle is laid over the fan (node corners at node height, cut corners = the ribbon's), because the fan dips under the road corner towards shoulder-height boundary points (up to 9 cm). `UntrimNested` and its nested-leg state are gone: the ramp is pinned to the motorway in the generator instead (`worldgen-generator.md`, "Ramp pin").
- **`tools/worldgen/roadfeel`**: headless tyre-feel check (lips on the driven surface, README there). Gate: exit 0 = no lip ≥ 20 cm; run it after every road-surface change, before the sweep.
- **Closed in RG-130:** the 5–7 cm `step` at s 4–5 of junction legs and at the link ends (s 391–411): minor-leg plateau in the generator plus level-leg node corners on the sloped leg (`worldgen-generator.md`, "Junction plateau"); `step` clusters 15 / 5 / 11 → 0 / 0 / 0, roadfeel lips ≥ 5 cm 46 / 30 / 37 → 6 / 10 / 5. Portal pits: `PitWall` (`WorldGenTunnel.cs`) puts a column pair at the door boundary |lat| = tw (bisection, 16 steps; door bottom, then floor bottom, so the wall bottom has a vertical edge there instead of a wedge into the tube wall) and clamps only the door bottom to the rim (a floor column above a shallow rim kept the floor edge, otherwise the floor and the wall bottom parted); `pitcheck --portals` open 8 / 3 / 4 → 0 / 0 / 0. `pitcheck` splits ground/pit edges at every vertex lying on them (any fraction; before: whole metres only, which by itself kept no portal open) and has `--drop-one` (removes one pit wall triangle; must report open).
- **Open residuals (RG-130 outbox, one line per cluster):** `seam_gap` (0 / 1 / 6 clusters) and `surface_mismatch` (0 / 5 / 1) all sit at bridge span ends (portal nodes 954/955, 960, 967, 991, 993, 1001 on seeds 2–3), in the shoulder band beside the deck end; the deck has no shoulder. Not touched (no new patch class).

## Viaduct railing (RG-136)

`WorldGenBridge.RailSkipReason` decides per deck segment and side whether the barrier is skipped: `junctionEnd` (first/last segment at a node of degree >= 3) or `roadBelow` (another road's lane within half width + 0.3 m in plan **and its surface within `RailHeightTolM` = 0.5 m of the deck**). Before RG-136 the height condition was missing, so a road passing 5.3 m under a viaduct removed the barrier on both sides over the crossing (since RG-120, not a later regression). Report: `tools/worldgen/pitcheck --rails` (see its README). Side effect: the sweep's `wall_in_lane` probe (2D lane test, no height) now reports those barriers as walls in the lane of the road below (11 / 15 / 24 clusters on terrain seeds 1 / 2 / 3, all `Viaduct`, normal.y 0); detector change is out of RG-136.

## Viaduct span ends (RG-138)

- **`wall_in_lane` fix (detector bug):** `WorldGenSweepFlood.Lane` counted a road as "the lane" in 2D only; it now also needs the road's surface within `WorldGenSweepRain.Tol` of the probe's height, so a road 5 m under the deck no longer makes the restored barrier a wall. Terrain seeds 1 / 2 / 3: 0 / 0 / 0 (was 11 / 15 / 24); inject `wall` still found.
- **Span-end transition (`WorldGenSectorSource.MakeGeo`, `WorldGenBridge.BridgeSegment`, rework 1):** at a plain span end (`WorldGenMap.PlainSpanEnd`: degree-2 node, no other bridge edge) the deck is widened by `SpanEndFullW` = `ShoulderM` = 1.5 m over the first `SpanEndHoldM` = 1.0 m and tapers to 0 at `SpanEndZoneM` = 10 m (`WorldGenMap.SpanEndWiden`, stations inserted). The barrier stands on the widened edge (half + Wx + 0.02) from the span end on, with no gap (rework 2: a gap over the hold zone let the flood step 0.75 m down beside the deck, seed 1 `trap`). Cause of the old `seam_gap`: span ends sit on bends with a mitre cut; the seam probe (0.1-0.9 m from the node, lateral up to half + shoulder) lands on the deck wedge.
- **Detector road model (`WorldGenSweepRoads`, rework 1):** the model now follows the generator: at a mitred degree-2 node `PastEnd`/`Query` cut the edge on the generator's mitre line (`WorldGenMap.MitreEnd` = `Geo.Nx/Nz` after `MitreDegree2`, one source) and measure the wedge lateral to the extended end segment; a bridge edge at a plain span end gets outline = half + `SpanEndWiden(distance to that end)`. Thresholds (`Tol`, `EdgeBand`, the 1.0 m overhang band) unchanged. Seeds 1 / 2 / 3: seam_gap 0 / 0 / 0, surface_mismatch 0 / 0 / 0 (was 0 / 5 / 1 with variant e), inject 5/5 found.
- **`pitcheck --node`** now also prints the node's legs (edge, span, width, length, end).
