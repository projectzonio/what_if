# WorldGen vegetation and buildings on the UHD budget (RG-149)

Research ticket (ADR-035), written 2026-11-10 without Unity, GPU, notebook slot, download or install. The only program run was the generator CLI (dotnet, CPU) to read the test tile (section 9). Rules: `research-rules.md`. Labels: **V** verified in this session (URL, access 2026-11-10, verbatim quote), **M** project measurement or code read (path given), **O** opinion or estimate (basis stated). An unlabelled table cell is O.

User request (2026-11-09): "4. ano. - bez gpu heavy work". Goal: real trees, grass and houses so the tile's village edge and forest edge become geometry (`test-tile.md` gap G1), designed to stay cheap on i3-1215U / Intel UHD.

## 0. Decisions in one screen

| # | decision | why |
|---|---|---|
| D1 | Placement = new Unity-free CPU stage `Scatter` in `src/WhatIf.WorldGen`, per 256 m sector, pure function of (seed, sector id, terrain, road graph, settlements). Own RNG stage name per sector. The road dataset hash does not change. | determinism, reuse for What If, checkable with the CLI (section 5) |
| D2 | Geometry v1 = **original procedural meshes** built in C# from a few parameters (tree LOD0/1/2, house box+gable). No CC0 models now. | no licence work, no schema v2, no download; decide CC0 later only if the look is judged poor (section 6) |
| D3 | Rendering = `Graphics.RenderMeshInstanced` batches per sector x LOD x kind, no GameObject per plant (the pattern `Kn5GrassField` already uses, M). **Opaque** low-poly crowns, no alpha test at Low and Medium. | alpha-tested foliage overdraw is the classic UHD frame killer (O); opaque cones cost almost nothing per pixel |
| D4 | Shadows: off at Low; at Medium/High only the LOD1 batch within 60 m casts. Billboards only at High. | WorldGen has no shadow casters today (M), so every caster is new cost (section 3) |
| D5 | Minimal first step (Low, tile only): houses along the 8 local street edges of village 3 and forest cones where the mask says forest, LOD1 up to 60 m, LOD2 (12 tris) up to 250 m. Estimated **0.4-0.8 ms** at Low (O, section 7). | meets "within about 1 ms" |
| D6 | Three tickets: V1 placement + CLI dump (CPU), V2 Unity rendering (GPU), V3 grass/billboards/shadows (later). Section 8. | smallest steps; V1 is verifiable without Unity |

## 1. Anchors: measured project numbers

| fact | value | label / source |
|---|---|---|
| worldgen warm P95, Low / Medium / High | 2.557 / 4.215 / 6.656 ms (`quick`, `50a2ad1`); Low `full` 2.586 ms, noise 0.47 % | M `benchmarks/perf-matrix/2026-11-08-50a2ad1-quick/report.md`, `wiki/concept/perf-matrix.md` |
| Gunsai High (heaviest cell) | 19.696 ms | M same report |
| Medium/High have `quick` only, no repeated-run noise | so a 0.3 ms change is not flagged by `compare.py` there | M `test-tile.md` section 5 |
| shadows are about 70 % of frame triangles | 0.99 M without, 3.27 M with 4 cascades; about 2.5-3 ms per million shadow triangles | M for the triangles (Kanazawa d8, RG-090 data), O for the ms/Mtri (`cryengine-optimization-study`) |
| WorldGen shadow share | **not measured**. WorldGen ground, road and structure meshes are built with `shadowCastingMode = Off` (`WorldGenSectorSource.cs:1253,1323,1339`), so today the world casts no shadows at all | M code read |
| RG-141 shadow-caster breakdown | **pending RG-141** (status `todo`, not merged) | brief asks to say so |
| frame time is vertex- and fill-limited on UHD, 720p Low vs 1080p | 1080p has 2.25 x the pixels of 720p (1920x1080 / 1280x720) | arithmetic; the effect on foliage is O |
| existing instanced vegetation | `Kn5GrassField`: 16 m cells, `Graphics.RenderMeshInstanced`, "Nothing here creates a GameObject per plant"; levels low/medium/high = radius 28/45/70 m, density 1/2/3, 3/4/5 blades | M `Assets/Scripts/Kn5/Vegetation/Kn5GrassField.cs:12-27` |
| sector build budget | `SectorStreamer.BudgetMs = 3`, `CriticalBudgetMs = 8` per pump; sector build has a "pure data" first stage (`StepCore` case 0) | M `SectorStreamer.cs:51`, `WorldGenSectorSource.cs:1265-1269` |
| sector size | 256 m (`WorldGenMap.CellSize`), grid origin 0,0 | M `WorldGenSectorSource.cs:24` |

## 2. Unity 6000.3 / URP 17.3 facts

Fetched 2026-11-10 from docs.unity.com (6000.3). The old `docs.unity3d.com/6000.3/Documentation/Manual` URLs 404 or redirect (the script-reference URLs redirect with HTTP 301). My first guessed manual URLs (`.../manual/SRPBatcher.html`, `class-LODGroup.html`, `urp-asset.html`) returned 404; the real pages were reached through links.

| claim | label | source and quote |
|---|---|---|
| URP Max Distance for shadows | **V** | `https://docs.unity.com/en-us/engine/6000.3/manual/render-pipelines/universal-render-pipeline/urp-reference/universalrp-asset` (Shadows): "Unity does not render shadows farther than this distance." |
| Cascade count costs performance | **V** | same page: "Increasing the number of cascades reduces the performance. Cascade settings only affects the main light." |
| LOD cross-fade is available in URP | **V** | same page (Quality): "When a LOD group has **Fade Mode** set to **Cross Fade**, Unity renders the Renderer's LOD meshes with cross-fade blending between them using either alpha testing or stencil testing." |
| SRP Batcher vs Dynamic Batching | **V** | same page: "If both **SRP Batcher** and **Dynamic Batching** are enabled, SRP Batcher will take precedence over dynamic batching as long as the shader is SRP Batcher compatible." The page states no priority between SRP Batcher and GPU instancing. |
| GPU Resident Drawer draws with GPU instancing automatically | **V** | same page: "Instanced Drawing: Unity automatically draws GameObjects with GPU instancing." The page links a GPU Resident Drawer page that I did not read; it is the lead if V3 considers it. |
| `ShadowCastingMode.Off` | **V** | `https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rendering/shadowcastingmode`: "No shadows are cast from this object." (page does not mention the shadow-map pass; that part is inference) |
| `BatchRendererGroup` exists for custom high-performance rendering | **V** | `https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rendering/batchrenderergroup`: "A BatchRendererGroup is an object that lets you perform customizable high performance rendering." |
| `RenderMeshInstanced`: at most 1023 instances per call, 511 with the default two matrices per instance; needs `Material.enableInstancing` | **O** (page `.../script-reference/unityengine/graphics/rendermeshinstanced` was read but the fetch was truncated; these sentences come from the fetch tool's summary, not verified verbatim) | V2 must re-check before it relies on the 511 figure |
| `DrawMeshInstancedIndirect` is obsolete, replaced by `RenderMeshIndirect`, which needs compute-shader support | **O** (same truncation) | only relevant if V3 chooses GPU culling; not needed for the budgets here |
| `RenderMeshInstanced` is SRP Batcher compatible / honours `shadowCastingMode` | **unverified**. The page does not say. The project already uses the call in `Kn5GrassField` (M), but whether its shadows work is not recorded. V2 tests it. | gap |
| LOD Group component page (billboard, culled state) | **not reached** (not linked from any read page; the URP page links an unversioned `docs.unity3d.com/Manual/` URL, not 6000.3) | gap; V2 builds its own distance switch, so the LOD Group component is not needed |

Consequence: we do not use `LODGroup` or cross-fade. Instances switch LOD by distance with hysteresis in C# (per-sector rebuild of matrix arrays when the camera crosses a ring). A hard pop at 60 m and 250 m is accepted for v1; dissolve is cryengine proposal C, a shader ticket, not here.

## 3. Technique per class

Costs are per visible object. "ms" columns are O: triangles x 3 ms/Mtri (upper end of the study figure, applied to the main pass too), plus 0.015 ms CPU per instanced draw call (O, no project measurement), plus a fill-rate surcharge noted per row. They are budgets to be checked by the gate, not promises.

| class | technique | LODs and range | tris per object | draws | shadows | main risk |
|---|---|---|---|---|---|---|
| **Trees** | `RenderMeshInstanced`, matrix (position, yaw, scale) per instance, vertex colour, shared opaque material | LOD0 (High only) 220 tris, 0-30 m; LOD1 60 tris, to 60 m; LOD2 12 tris (hex pyramid crown + 3-sided trunk), to 250 m (Low), 400 m (Medium), 600 m (High); billboard (2 tris, alpha) only at High from 250 m | 12 / 60 / 220 | per sector x LOD x variant | Low none; Medium/High LOD1 only, within 60 m | pop at LOD switch (accepted); alpha billboard overdraw (High only) |
| **Bushes, forest edge** | same batch family, 24-tri blob, placed in a 6 m band along the forest boundary and road clearance line | one LOD, 0-80 m (Low 50 m) | 24 | per sector | none | density at the boundary; keep counts capped |
| **Grass** | existing `Kn5GrassField` approach (16 m cells, blade clumps, instanced) ported to WorldGen ground; **not in the minimal step** | radius 28 / 45 / 70 m (Low/Medium/High, M levels) | 3-5 blades per clump | per cell batch | none | needs ground triangles and a tint source; cryengine proposal G wants per-vertex shading |
| **Buildings** | procedural box + gable roof, one shared opaque material, per-instance colour via vertex colour variants (4 variants) | single LOD, 0-300 m (Low 250 m); beyond: culled | 24 (5 box faces + roof + gable ends) | per sector x variant | Low none; Medium/High: near buildings (<60 m) | lots overlapping roads or steep ground (placement rules) |

Shadow reasoning (M + O): the study's finding (shadows about 70 % of triangles, cost grows with cascade count) is from Kanazawa. WorldGen is different: its meshes cast nothing today (M code), so vegetation shadows would be the first casters, and we limit them to the LOD1 batch within 60 m. Count: 40 trees x 60 tris x at most 2 cascades that reach 60 m = 4.8 k shadow tris, about 0.015 ms at 3 ms/Mtri (O). The cost that matters is the extra draw calls in the shadow pass (instanced batches are drawn once per cascade), which is why shadow casting is a separate, optional step (ticket V3), not part of the minimal step.

## 4. Placement rules (Unity-free, `src/WhatIf.WorldGen`)

New file `Scatter.cs`. API shape (O, proposal):

```
public struct Instance { public byte Kind; public byte Variant; public float X, Y, Z, Yaw, Scale; }   // 24 bytes
public sealed class ScatterModel { ScatterModel(WorldSpec, GenResult); public List<Instance> Sector(int sx, int sz); public ulong Hash(...) }
```

Inputs available today (M code read): `TerrainModel.Height(x,z)` (carved), `TerrainModel.Natural`, `GenResult` road graph (`GEdge.Pts`, `WidthM`, `Class`, `Span`), tunnel/bridge spans (`GSpan`), terrain holes (`TerrainModel.Hole`). The settlement list (centre, radius, city/village) is `internal Settlement` in `Generator.cs:37`; V1 exposes centre and radius on `GenResult` (read-only fields; this must not change the dataset hash, and `Export.cs` does not write it).

| rule | value (O unless stated) | basis |
|---|---|---|
| Per-sector determinism | candidates from `Rng.ForStage(seed, "scatter" + sx + "," + sz)`, jittered grid 10 m (forest) / 14 m (bushes); no state shared between sectors; the edge sectors' candidates are generated from the same cell grid, so a lot or tree never depends on neighbours loading | `Rng.ForStage` is used the same way for terrain (`Terrain.cs:83`, M); a new stage name does not perturb existing stages (code read) |
| Forest mask | seeded fBm value noise, wavelength 500 m, forest where noise > 0.15 (about 40 % of land; tune from the dump) | same noise code as `TerrainModel` can be reused; value is O |
| Slope | trees only where terrain slope 0-30 deg (central difference over 4 m); houses only slope < 6 deg | on seed-1 window, 62.6 % of cells < 5 deg, 23.0 % at 15-25 deg, 4.4 % at 25-35 deg, 0 % > 35 deg (M, section 9) |
| Height band | no trees above the carved ridge height of 120 m (tree line), no trees at y < -3 m | O; spec ridge is 350 m high but the window max is 78 m |
| Distance from roads | no trunk within `halfWidth + 10 m` of any centreline sample (primary), `+ 6 m` (local); use a segment index like `SegIndex` | O; clearance is a design choice, not a standard |
| Curve sight line | on the inside of a curve with radius R below 250 m the clearance becomes `max(10, R*(1-cos(d/(2R))))` with d = 70 m, so the edge of the forest never blocks the line | the formula is geometry (M arithmetic); at R = 100 m it gives 5.0 m, so the 10 m rule dominates; the term only matters for very tight radii (not checked against the validators' minimum radius). Documented so a reviewer does not add a larger rule |
| Spans | no instance on a bridge or tunnel span; under a viaduct deck none within 6 m of a pier; none within `HoleMarginM` of a terrain hole | `TerrainModel.Hole`, `GSpan` |
| Village lots | for each local edge inside a village (edge centre within settlement radius x 1.1): both sides, lot frontage 22 m, setback 6 m from the road edge, building 9 x 11 m, yaw = edge bearing; skip if slope >= 6 deg, if within 14 m of a junction node, or if the footprint is closer than 5 m to another road | O design values; the 14 m is half the junction cut that `OnSlopedLeg` handles in the adapter |
| Village trees | street trees every 18 m on the lot side, 3 m behind the kerb, at most half of the lots | O |
| Forest/settlement separation | no forest within 40 m of a building lot; a 60 m fade band (probability falls to 0) from the settlement radius out | O; gives the tile a visible forest edge near the village |
| Caps | at most 1500 trees and 150 buildings per sector, else deterministic thinning (keep every n-th candidate) | memory bound, section 6 |

Expected quantity on the tile village (M for length, O for count): village 3 has 8 local edges, 725 m (CLI, section 9), two sides at 22 m frontage = 66 lots, minus junction exclusions and slope: roughly 45-60 buildings. That is a thin village, but with the mask and the street trees it reads as one at 250 m.

## 5. CLI dump and CPU checks (no Unity)

V1 adds `--scatter-dump <file> [--window x0,z0,size,step]` to `tools/worldgen/cli` (the tool is read-only in this ticket):

- output `scatter.csv` (`kind,variant,x,y,z,yaw,scale,sector_x,sector_z`) and `scatter-hash.txt`; two runs identical; the road dataset hash must still read `c03c3dc7...` for seed 1.
- checks the CLI prints and the unit tests assert: (a) min distance from every instance to any road centreline minus half width is at least the clearance of its class; (b) no instance lies on a span, in a hole, or above the height band; (c) per-sector counts below the caps; (d) per-sector time in ms (`Stopwatch`) for the whole seed-1 window so the cost of the stage is measured on the CPU before any Unity work.
- a picture for agents: extend `tools/worldgen/render_net.py` to plot instances over the road net (PNG, Pillow) so the tile's village and forest edge can be looked at before V2.

Placement cost is not measured today (no code). The risk is one `TerrainModel.Height` call per candidate: a fully forested sector has about 655 candidates at 10 m (256^2 / 10^2). V1 reports the ms per sector; if it is above 1 ms the stage stays inside the "pure data" step but must be split over two pumps (the streamer already budgets 3 ms per pump).

## 6. Geometry source, budget and streaming

**Geometry: procedural now, CC0 later (recommendation).**

| | original procedural | CC0 models |
|---|---|---|
| licence | none needed (own code), consistent with ADR-026 | ADR-037 covers textures only; models need a schema v2, provenance per file, and a user decision |
| download | none | forbidden in this ticket (ADR-028); the user decides |
| look | stylised cones and boxes; fine at 60 m+, plain at 10 m | better at near range, often 1-5 k tris per tree, too heavy for UHD without own LODs |
| determinism and size | meshes from 3-6 parameters, a few KB | files on disk, import pipeline |
| effort | M (the shader and four generators) | M + review + licence work |

Recommendation: ship procedural v1. If the user judges the near look weak after the tile ride, a later ticket evaluates CC0 trees as LOD0 only (this is the first thing to ask the user after V2).

**Instance budget per 256 m sector** (O, from the caps and densities above):

| item | count | memory |
|---|---|---|
| trees, fully forested sector | 655 candidates, cap 1500 | 24 B x 1500 = 36 KB stored; matrices 64 B x 1500 = 96 KB when active |
| trees, typical (40 % forest) | about 260 | 15 KB + 17 KB |
| houses, village sector | up to 150 | 3.6 KB + 10 KB |
| ring of active sectors | Low 3x3 = 9 sectors; Medium/High 5x5 = 25 | well under 5 MB in the worst case, far below the "peak RAM <= baseline + 10 %" gate (O) |

**Streaming.** Instances belong to the sector `Content`, produced in the pure-data stage of `BuildSector` (M, `WorldGenSectorSource.cs:313,1269`), not in a global array: this follows the rule "chunked loading always, no whole-map path" (memory `chunked-loading-always`). On load the sector keeps its `Instance[]`; the render component fills the per-LOD matrix arrays only for the distance ring around the camera and rebuilds them when the camera moves more than 16 m or crosses a LOD ring (not each frame). On unload the arrays are dropped with the sector. Nothing is kept for sectors outside the unload radius.

## 7. Cost and ms budget per level, with basis

Budget = additional warm P95 over the same row without vegetation (`worldgen-<level>`). Basis for each number is in the third column; none is measured (O).

| level | budget | basis |
|---|---|---|
| Low (720p) | **<= 1.0 ms** (brief); expected 0.4-0.8 | tris: about 60 trees in view at 60 m x 60 tris + 100 LOD2 cones x 12 + 40 houses x 24 = about 6 k tris, 0.02 ms at 3 ms/Mtri; draw calls: about 9 sectors x (3 tree LODs + 1 house) = 36 calls x 0.015 ms = 0.5 ms (the dominant term; reduced by merging sectors that have no visible instances and by culling sector bounds); matrix upload and ring rebuild about 0.1 ms amortised |
| Medium (1080p) | <= 2.5 ms | 25 sectors, LOD range 400 m, LOD1 shadows within 60 m (+2 cascades), 2.25 x the fill of Low; scaled from the Low basis with draw calls x 2.5 |
| High (1080p) | <= 4.0 ms | adds LOD0 and billboards (alpha), shadows within 90 m; the row stays well below Gunsai High 19.7 ms |

Combined with the texture lane (`test-tile.md` section 5: Low variant budget 4.1 ms total, i.e. baseline 2.586 + 1.5 ms): Low with vegetation and textures = about 5.1 ms (O), 15 % of the 33.3 ms gate. Each lane is gated alone against V0 first.

The draw-call term depends on one open fact: whether `RenderMeshInstanced` batches stay cheap (a few draws) or fall off the SRP Batcher path. V2 measures this first (section 8, step 1).

## 8. Implementation tickets

| id | title | size | scope (write ownership) | gate / acceptance |
|---|---|---|---|---|
| **V1** | `Scatter` placement stage and CLI dump (CPU) | M | `src/WhatIf.WorldGen/Scatter.cs`, `Generator.cs` (expose settlements read-only), `tools/worldgen/cli`, `tests/WhatIf.WorldGen.Tests`, `tools/worldgen/render_net.py`; not `Export.cs` | seed-1 road hash unchanged (`c03c3dc7...`); two scatter runs identical; section 5 checks pass; per-sector placement ms reported; tile strip shows at least one forest boundary within 150 m of the straight and 45+ houses in village 3, otherwise a documented parameter change; `check.ps1` and `verify-hygiene.ps1` exit 0. No Unity. |
| **V2** | Unity rendering of scatter, minimal step at Low | M | `Assets/Scripts/Streaming/WorldGenScatter*.cs`, a vegetation shader/material, procedural mesh builders, `WorldGenSectorSource.cs` hook (one owner for the main files) | first step: one batch of 60 trees and 40 houses on the tile; verify `RenderMeshInstanced` draw count and shadows in the frame debugger-equivalent counters; gate rows `worldgen-Low` and `worldgen-tile-Low` (new row, G4): added P95 <= 1.0 ms, no frame > 50 ms, RAM and load within the `compare.py` limits; pair of shots from the test-tile poses `village-edge-3q`, `straight-eye`, `forest-edge-proxy`. Needs a slot; GPU-bound, one at a time. |
| **V3** | Medium/High extras: LOD0, near-tree shadows, billboards, WorldGen grass | M | same files plus `Kn5GrassField` adapter | gate rows `worldgen-Medium` (+2.5 ms) and `worldgen-High` (+4.0 ms); measure Medium/High noise first (3 `quick` runs) as `test-tile.md` section 5 requires; shadow gate uses RG-141 breakdown if merged |
| V4 (optional) | CC0 tree evaluation | S | docs only until the user decides | needs the user's licence/source decision and a schema v2 |

Order: V1 -> V2 -> V3. V1 and the texture lane touch different files and can run in parallel. V2 must wait for RG-141 only if both change `WorldGenSectorSource.cs`; the orchestrator decides.

## 9. How the tile numbers were read (CPU only)

Command (from the worktree root, seed 1, rules file from the main checkout): `dotnet run -c Release --project tools/worldgen/cli -- --spec tools/worldgen/specs/terrain-10km.json --rules D:/workspace/race_game/artifacts/worldgen/world-rules.json --out artifacts/tmp/wg-s1 --seed 1 --terrain-dump artifacts/tmp/dump-s1b.bin --window -3950,300,1200,4`. Result: hash `c03c3dc75ea980140be0de019f263883b700c26a9748ce776b9e6cffd88eea98` (equals `test-tile.md`), `validators: 0 finding(s)`, 958 nodes, 1201 edges, generation 2.32 s, exit 0 (M).

Then `agent_pool/agents/claude/outbox/RG-149_stats.py` over `edges.json` and the terrain dump:

| fact | value |
|---|---|
| edges within 400 m of village 3 centre (-3793, 838) | 8 local edges, 725 m, width 6 m; 2 primary edges, 1029 m, width 7 m |
| farthest local point from the centre | 268 m |
| terrain window x -3950..-2750, z 300..1500 (4 m grid, 89401 interior cells) slope share | < 5 deg 62.6 %, 5-15 deg 10.1 %, 15-25 deg 23.0 %, 25-35 deg 4.4 %, > 35 deg 0 % |
| carved height at the forest-edge proxy (-3150, 1000) / (-3050, 1100) / (-2850, 1000) | -0.9 m / 17.8 m / 77.9 m |

Reading: the "forest edge" proxy (ridge foot) is 250-350 m from the straight, so at Low (LOD2 to 250 m) only the first row would show. That is why the rule "forest boundary within 150 m of the straight" is an acceptance line of V1: the noise mask, not the ridge, must give a forest edge near the road. Whether seed 1 satisfies it is **unknown until V1 runs the mask**; if it does not, the first fix is a mask offset, not a new feature.

## 10. Limits and gaps

- Nothing was rendered or timed in Unity; all ms are estimates from stated bases. The placement cost per sector is unmeasured (section 5).
- RG-141 breakdown is pending; the shadow reasoning for WorldGen rests on the code read that world meshes cast no shadows today.
- Gaps in section 2 (`RenderMeshInstanced` SRP Batcher/shadow behaviour, LOD Group page, `RenderMeshInstanced` instance limit verbatim). None of them blocks V1.
- No cost for trees swaying (wind) is included; wind is a High-only shader item for V3.
- Draw distances beyond 250 m with fog `exp2 3000 m` (`worldgen-max`) means distant cones are not hidden by fog; pop is visible. Accepted for v1.

## 11. V1 result (RG-151, 2026-11-11, CPU only, no Unity)

Implemented: `src/WhatIf.WorldGen/Scatter.cs` (`ScatterModel`, `Instance`, `ScatterAudit`), settlements exposed read-only as `GenResult.Settlements`, CLI `--scatter-dump <file.csv> [--window x0,z0,size,step] [--mask-offset v]`, `render_net.py --scatter`, unit tests. Labels: **M** = measured in this ticket.

Command (seed 1, tile window): `dotnet run -c Release --project tools/worldgen/cli -- --spec tools/worldgen/specs/terrain-10km.json --rules artifacts/worldgen/world-rules.json --out artifacts/tmp/wg-s1 --seed 1 --window -3950,300,1200,4 --scatter-dump artifacts/tmp/sc1/scatter.csv`.

| fact | value |
|---|---|
| road hash | `c03c3dc75ea980140be0de019f263883b700c26a9748ce776b9e6cffd88eea98` unchanged, 0 validator findings (M) |
| scatter hash, two runs (second from a fresh model) | `b4c1d501d39f5ac9488b1c341761c9dfb7bc6d0c28e279b2c8a040d4c810b771` identical (M) |
| window 1200 m = 30 sectors | 3714 instances: trees 3123, bushes 520, houses 55, street trees 16 (M) |
| checks (a) road clearance / (b) span, hole, band, slope / (c) caps | 0 / 0 / 0 violations, 0 excluded; max 628 trees and 36 buildings per sector (M) |
| (d) placement per sector, cold (first call of each village sector builds the lots, first call pays JIT) | min 0.14-0.21, median 0.32-0.44, max 8.5-11.3 ms |
| (d) warm repeat | min 0.17, median 0.32, max 4.3 ms |
| houses in village 3 | 55 (>= 45) |
| nearest tree to the 350 m straight | 131.4 m (10 trees within 150 m) |
| forest mask above threshold | 23.8 % of the window (design guess was about 40 %) |

Reading: the median sector costs about 0.3-0.4 ms, below the 1 ms line, but the dense forest sectors (600+ instances) and village sectors reach 4-11 ms. That is above 1 ms, so the two-pump split of section 5 applies (pump 1 trees and bushes, pump 2 lots and street trees); not built here. The main cost per candidate is the slope test (4 extra `TerrainModel.Height` calls) behind the cheap rejections; a coarser slope sample or a per-sector height grid is the first lever.

Rules as built (deviations from section 4 and design choices, each with its reason):

- Jittered grid: 26 x 26 cells per sector (9.85 m, trees) and 18 x 18 (14.2 m, bushes), not 10 / 14 m, because the cell count must divide 256 m so every cell belongs to exactly one sector. Six random draws per cell regardless of rejection, so thinning and rule changes never shift other cells.
- Mask: three octaves of seeded lattice value noise (own code, hash lattice; `TerrainModel`'s gradient noise is private), base wavelength 500 m, threshold 0.15, tree probability `smoothstep((mask - 0.15) / 0.10)`; `MaskOffset` is the first tuning knob. Default 0 reaches the acceptance line; an offset of +0.15 would give about 40 % forest (not applied: the tile already meets the line).
- Settlement factor: the forest probability is 0 inside a settlement radius and rises over 60 m beyond it (applies to the city too); plus a hard 40 m from any village lot.
- Spans: not modelled separately. A bridge or tunnel edge is a road edge like any other (half width + class clearance), so nothing stands on a span and nothing stands under a deck within the clearance. Piers are not in the road graph; the deck-wide exclusion is a superset of "6 m from a pier". Audit (b) additionally checks the half width of every span edge and the hole (+- `HoleMarginM`).
- Curve sight term implemented as written; with R >= 70 m it never exceeds the 10 m rule (4.2 m at R = 70, 16 m at R = 35).
- Village lots: villages only (the city gets no houses in V1; the lot code is per settlement and would accept it). Edges taken: local, ground, edge midpoint within 1.1 x radius. House = 9 m along the road x 11 m deep, 6 m setback from the road edge, front facing the road; footprint corners must keep 5 m from every road (own road included, checked at scale 1.05), 12 m between lot centres, 14 m from any junction node, slope < 6 deg at the centre.
- Street trees: every 18 m on both sides, 3 m behind the kerb, kept with probability 0.5; 3 m clearance to every road, 2 m from a house footprint.
- Caps: trees and bushes 1500 each (bushes were not named in section 4), buildings 150; deterministic thinning every n-th.
- The lot list of a village is cached after first use; it is a pure function of the graph, so a sector is the same whether computed alone or among neighbours (unit test).
- Audit (a) uses a brute-force scan over all edges with their own clearance function; the margin tolerance is 1 mm (instances are stored as float).

## 12. V2 result (RG-154, 2026-11-12, Unity, Low, UHD notebook)

Drawn behind `--worldgen-scatter 1` (default off; `--worldgen-scatter-mask <offset>` and the debug `--worldgen-scatter-path update` exist). Code: `WorldGenScatter.cs` (batches), `WorldGenScatterMeshes.cs` (original procedural meshes), `Resources/WorldGenScatter.shader` + `WorldGenScatterMat.mat`, hook in `WorldGenSectorSource.cs`.

- **Mechanism as recommended, with measured deviations.** One batch family per kind x LOD for the whole active ring (tree LOD1 64 tris to 60 m, tree LOD2 12 tris to 250 m, bush 24 tris to 50 m, house 14 tris to 250 m; street tree = tree), rebuilt every 16 m, 8 deg of turn or a sector change, widened-frustum + distance culling per instance, `Graphics.RenderMeshInstanced` in chunks of 1000 (the value Kn5GrassField already uses; the 6000.3 page was not opened, so the platform limit is not verified: observed 3 calls at <= 1000 instances per bucket). Submitted from `RenderPipelineManager.beginCameraRendering` for the target camera, because the agent port renders pose shots with an extra `Camera.Render()` that misses draws submitted earlier in the frame.
- **Trap 1 (cost two builds): the instancing variant was stripped.** A material made only at runtime lets Unity strip `INSTANCING_ON` (build log: variant space 2 -> 1) and `RenderMeshInstanced` draws nothing, without any error. Fix: a material asset in Resources with `m_EnableInstancingVariants: 1`; build log then keeps 2 -> 2.
- **Trap 2: a graphics-lab preset with `fullscreen: exclusive` makes a `-Windowed` run refuse** ("effective borderless") before its frame loop starts; shots use a borderless copy of worldgen-max.
- **Data:** instances are produced in the job's own pump step (after the pure-data step) and live with the sector (added on publish, dropped on unload). Pre-warm of the lot lists: 36 sectors, ~40 ms at load. Placement step per sector: sum ~55 ms over 49 steps, **max 6-10 ms** (still above `SectorStreamer.BudgetMs` 3: the placement call itself cannot be split, the API is read-only); matrices on publish max 1.5 ms. See the outbox for hitches.
- **Counters** (fly run of the tile row, 40 s window, `--render-counters`): draw calls p50 107 -> 108, SetPass 1 -> 2, triangles p50 121.5k -> 122.3k (+0.8k), max 145.2k -> 146.4k; shadow pass: the world still has 0 casters (everything `ShadowCastingMode.Off`). On this route at most 122 instances (3 draw calls) are in view.
- **Cost at Low** (`run-matrix.ps1 -Set full -Levels Low`, report `benchmarks/perf-matrix/2026-11-12-935c05f-full`): tile row P95 3.088 -> 3.519 ms = **+0.43 ms added** (pair warm spread 0.10, counts: >= 3x spread and >= 0.1 ms); interchange row 3.157 -> 2.975 ms = -0.18 ms (inside the 0.24 spread, not an effect; the interchange has few instances). Hitches > 50 ms: 0 in all veg rows (one 55.1 ms frame in the V0 `worldgen` warm2). Max frame veg rows 7.9-20.7 ms. Peak private RAM (report column) 491.0 -> 491.7 MiB (tile) and 488.8 -> 508.2 MiB (interchange; the warm runs alone show 485/498 vs 494/493 MiB, so the difference is run noise, not measured as an effect).
- **Not attributable to V2:** the V0 `worldgen` row is 2.557 -> 3.348 ms P95 (+31 %, quick vs quick) against `2026-11-08-50a2ad1-quick` with the flag off; the flag-off path only adds null checks. Known main drift candidates: RG-148 and others in `50a2ad1..935c05f`. Reported, not bisected.

### 12.1 Rework 1 (2026-11-15): distances per quality level + dither fade

User ride 2026-11-15 (fail): the LOD swap at 60 m was right in front of the car and the forest ended at a hard line at 250 m (the fog at worldgen-max reaches 3000 m). Tree / house geometry stays placeholder by user decision (textures will replace it).
- **Table** `WorldGenScatter.DistTable` keyed by the level name (graphics-lab preset file name, "Low" without `--graphics-lab`; unknown level = Low): Low 60/250/50/250 (unchanged), worldgen-max **200/1200/120/1200** (tree LOD1 / tree far / bush / house, m). `--worldgen-scatter-dist a,b,c,d` overrides it (measuring). Medium / High have no row (they use Low's) and are unmeasured.
- **Dither cross-fade** in `WorldGenScatter.shader` (per-bucket `_Fade` through a MaterialPropertyBlock, no new pass, no blending): LOD1 -> LOD2 over a band of 12 % of the LOD1 distance centred on it (both buckets drawn inside the band, inverted noise = complementary, no holes); every kind fades out over the last 10 % of its far distance. The CPU buckets carry a margin of the 16 m rebuild step, the shader decides per pixel.
- **Measurement** (fly run, tile route, worldgen-max 1080p, warm 30 s + 90 s window, sandwich old-a-b-c-old, `artifacts/rg154/rework1-cands`): warm P95 7.36 / 7.39 / 7.41 / 7.40 / 7.40 ms (60/250 , a 120/600 , b 150/900 , c 200/1200 , 60/250 again), P50 6.56-6.62, hitches > 50 ms 0 in every run, max frame 9.8-15.3 ms (no order by candidate), peak private 634-638 MB, sector placement pump max 2.9-3.8 ms. Added P95 of the largest candidate vs the mean of the two 60/250 baselines (7.38): +0.02 ms (<= 1.5 ms limit; the baselines differ by 0.04) -> **(c) chosen**. Drawn max 144 instances / 3 calls (60/250), 1086 / 4 calls (a), 2016 / 4 calls and 24.6k triangles (c). b and c draw the same count: the far reach is bounded by the loaded sector ring, not by the table.
- Low matrix row `worldgen-tile-veg` (report `benchmarks/perf-matrix/2026-11-15-08722a24-full`): P95 2.634 ms (warm runs 2.448 / 2.634 / third), not above the first round (3.47-3.57, a slower session).
- Shots `agent_pool/agents/claude/outbox/RG-154-rework1-shots/` (forest-edge-proxy, straight-eye, road-far; old = 60/250/50/250, new = chosen): the forest edge is a full slope forest instead of a thin strip, no hard line inside the fog, no holes.
