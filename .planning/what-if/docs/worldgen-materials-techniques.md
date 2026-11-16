# WorldGen materials: techniques, URP 6000.3 support and cost on the UHD notebook

Ticket RG-145 (research, ADR-035). Written 2026-11-09. Desk research and code reading only: no Unity build or run, no measurement. Evidence classes follow `research-rules.md`: **V** verified (primary page read, quote), **M** project measurement (path), **O** opinion/estimate. A cell without a label is O.

Installed: Unity 6000.3.26f1, URP 17.3.0 (`engine-unity/NightRacer/Packages/manifest.json:4`). The URP 17.3 package URLs redirect into the 6000.3 manual on docs.unity.com (same pages). Local package docs in `Library/PackageCache` are index stubs only, so all V quotes come from the web manual (access date for every quote: 2026-11-09; the pages show only relative "last updated" ages, 11 days to 2 months).

## 1. Goal and baseline

Best possible look of the WorldGen test tile from native textures, within what the i3-1215U / Intel UHD can run (`task_pool.json` `performance_targets`: 1280x720 Low 30 FPS aspirational; the notebook is the only measured device).

Today WorldGen draws flat colours: `Make(Color)` clones `Kn5/Kn5_Opaque` (URP Lit material, shader GUID `933532a4...`, `Kn5_Opaque.mat:24`) and sets `m.color` (`WorldGenSectorSource.cs:1196-1210`).

Measured frame cost (M, `wiki/concept/perf-matrix.md` "Plna matice quick", 2026-11-08, `50a2ad1`, P95 ms, Low / Medium / High):

| Scene | Low | Medium | High |
|---|---|---|---|
| worldgen | 2.557 | 4.215 | 6.656 |
| gunsai | 6.771 | 11.262 | 19.696 |
| sierra-stream | 1.951 | 3.982 | 7.000 |
| playground | 1.753 | 2.563 | 2.592 |

Worldgen is cheap now because it has no textures, no grass and a flat shader. Gunsai (many multi-texture KN5 materials) is the best available stand-in for "what textures cost". Other M anchors: anisotropy 4 on track textures +0.65 ms Gunsai, +0.33 ms Kanazawa (RG-140, `wiki/entity/features.md:71`); Gunsai texture memory 114.7 MiB native, 18.4 MiB at 512 px, 46.5 MiB at 1024 px, 60.5 MiB at 2048 px (`kn5-material-profile.md` section 5.1); Low is GPU-bound at 99 % (features.md:71).

## 2. Reuse map

### 2.1 Shaders and tools

| Asset | file:line | Use for WorldGen | Change needed |
|---|---|---|---|
| `Kn5Multilayer` | `Assets/Kn5/Shaders/Kn5Multilayer.shader:85-99` | Closest thing to a splat shader: base map + mask texture (RGB weights, A = remainder) blending four detail maps at per-layer UV multipliers `_Mults` (lines 89-94). That is a control-texture splat with 4 layers and 6 texture reads | Mask UV = base UV, so the mask is per tile, not per vertex. Needs: (a) mask generated from WorldGen data (slope, height, road distance) per sector; (b) detail colour mode already exists (`_DetailColor`, line 99); (c) a world-space UV path for tiling across sectors; (d) a height-blend option; (e) a vertex-colour input (there is none, `Attributes` line 70 has no COLOR) |
| `Kn5Lit` | `Kn5Lit.shader:11-216` | Albedo + normal + spec/gloss/refl map + detail maps with per-material float switches (lines 1-10, 22-27). Natural road shader: `_UseNormal`, `_UseSpec`, `_UseRefl` already work; sky env cube `_Kn5EnvCube` for reflections | Wet-road roughness mask is not there (spec is Blinn-Phong from a maps texture); RG-114 probes not wired |
| `Kn5Grass` | `Kn5Grass.shader:1-25` | Instanced vertex-colour blades, `_Tint` "mean of the ground texture", fade `_MaxDist` 45 m, no textures | Tint should come from the splat result; density mask from the same control data (proposal G) |
| Atmosphere / fog | `Kn5Fog.hlsl` via `Kn5MixFog` (features.md:89), `Kn5Atmosphere` (features.md:80) | Fog and sky already apply in `Kn5Lit`/`Kn5Multilayer`; any new shader must call `Kn5MixFog` last (`Kn5Multilayer.shader:156`) | Include it in new shaders |
| Anisotropy presets | RG-140, features.md:71 | Low = aniso 4 for track textures (M +0.65 ms Gunsai). Applies to every texture, so splat/road textures inherit it | None; budget it |
| Texel density audit | `kn5-index.exe --materials`, RG-103 (features.md:73); texel checker sweep RG-104 (features.md:74) | Checks px/m of the chosen texture set against what the notebook can display, on AC maps as reference | WorldGen has no KN5 file; reuse the checker shader (`Kn5InspectChecker.shader`) on WorldGen meshes |
| `Kn5Textures` DDS/BC7 | features.md:88 | DX10/BC7 DDS loading exists | Mip streaming of loaded DDS is not covered |

### 2.2 What WorldGen geometry provides now

| Data | Where | Value today |
|---|---|---|
| Ground mesh | `WorldGenTerrain.cs:143-200` | 65x65 vertices at 4 m step (`TileStep = 4f`, line 20) + 2 m skirt; one mesh per sector |
| Ground normals | `WorldGenTerrain.cs:165` | central difference of heights (slope is the normal's y); smooth, ok for slope-based blending |
| Ground UV | `WorldGenTerrain.cs:166`, `WorldGenSectorSource.cs:318` | world x/z divided by 4 (one tile repeat per 4 m); planar, so cut/fill slopes (1:1.5, `Terrain.cs:55`) stretch the texture |
| Vertex colour | none | `SetVertices/SetNormals/SetUVs` only (`WorldGenSectorSource.cs:1316,1334`); no colours, no tangents |
| Road mesh | `WorldGenSectorSource.cs:336-375` | Ribbon quads: `u` 0..1 across the carriageway, `v` = arc length `g.S[k]` along the spline (line 340); shoulders at `u` -1..0 and 1..1+us; cut/fill banks at -1..-1.5 and 2..2.5 (lines 361-362). So road UV along the spline already exists in metres |
| Junction patches and fills | `WorldGenSectorSource.cs:404,436,615,626` | UV is `Vector2.zero` (unwrapped): textured roads would show a smear at every junction until these get a world-planar UV |
| Road distance | none explicit | Available implicitly: `|u - 0.5| * 2 * half-width` on ribbons; ground has no road-distance value. `WorldGenTerrain.Flatten` knows road distance (bench, `WorldGenTerrain.cs:77`), so a per-vertex "distance to road" can be produced on the worker thread |
| Terrain tile source | `src/WhatIf.WorldGen/Terrain.cs:43-48` | `TerrainTile { N, Heights, Holes }` only. No material class per cell |
| Far ring | `WorldGenTerrain.cs:277` | 32 m chunks to 1.5 km, 64 m beyond, same `groundMat` (flat colour) |

Consequence: splatting by slope/height/road-distance is computable without new world data; any "biome"/surface-type classes need a decision on the data side (not in scope).

## 3. Techniques

Columns: gain (visual), cost class (M measured by this project, O estimate with reason), complexity (S/M/L for implementation), URP 6000.3 support with citation, Forward+/MSAA note. Costs are per frame on UHD, 720p-1080p, and refer to the Low/Medium/High sets in section 4.

### 3.1 Splatting

| Technique | Gain | Cost | Complexity | URP 6000.3 support | Forward+ / MSAA |
|---|---|---|---|---|---|
| Splat via vertex colour (4 weights from slope/height/road distance computed per vertex on the worker) | Medium. Granularity = 4 m vertices, so edges are soft and blurry; fine for grass/rock/dirt transitions, poor for sharp road verge | O: +1 vertex attribute (4 bytes) and no extra texture read; shader ALU only. The 4-layer sample cost dominates: 4 reads + base. Reason: no project data for ground sampling alone; Gunsai Multilayer materials (6 reads) sit inside the 6.8 ms Low figure | S-M | V: custom HLSL shaders with `UsePass` ShadowCaster/DepthOnly already ship (`Kn5Multilayer.shader:162-163`, M: project builds). No URP restriction on vertex colour | none specific |
| Control texture per sector (e.g. 64x64 RGBA8, bilinear) | High. Resolution independent of the 4 m mesh grid; can hold hand-placed, painted or world-gen-derived classes | O: one extra small read (cache friendly) and 64x64x4 B = 16 KiB per sector | M | V: ordinary texture sampling (Texture2D). One material per sector instance = no SRP-batcher break if the control map comes via per-material property, but each sector needs its own material or `MaterialPropertyBlock`; V: SRP Batcher requires "The GameObject mustn't use MaterialPropertyBlocks" (srpbatcher page). So per-sector material instances, or one shared `Texture2DArray` slice index per sector (below) | none specific |
| Texture2DArray for layer set | Medium: all layers in one texture object, one sampler, slice index per layer, no atlas bleeding; mipmaps per slice | O: same sample cost as 4 separate textures; saves binding/descriptors | M (no import pipeline, built in code) | V: "A texture array is a collection of 2D textures with the same size, format, and flags." and "In a shader, you can use an index to sample each slice." and DirectX11/12 supported (texture-arrays-introduction, 2026-11-09). V: "Unity doesn't support mipmap streaming with texture arrays, cubemap arrays, or 3D textures." (texture-streaming, 2026-11-09) | none specific |
| Height-based blending (layer weight * per-layer height map, sharpened) | High for rock/soil/pebble transitions; removes the "blurry cross-fade" of linear splat | O: layer height read: pack in the alpha of the albedo (BC7 alpha) = 0 extra reads; ALU only | S-M | V: HLSL, no special support needed; packing into BC7 RGBA allowed: "RGBA textures - BC7 (higher quality, slower to compress) or DXT5 (faster to compress)" (texture-choose-format-by-platform, 2026-11-09) | none |

Decision: control texture per sector is the best fit for the future; vertex-colour splat is the cheaper first step and needs no change to the shader's UV path. Both can use one 4-layer shader derived from `Kn5Multilayer`.

### 3.2 Slopes (cut and fill banks, steep terrain)

| Technique | Gain | Cost | Complexity | Support | Notes |
|---|---|---|---|---|---|
| Triplanar (3 projections, weights from normal) | High on cut/fill banks and rock faces: removes planar stretch | O: 3x texture reads for the triplanar layer (V: "Samples the input Texture 3 times, once in each of the world x, y, and z axes.", Shader Graph Triplanar node 17.3, no cost figure given). Applying it to all 4 splat layers = 12 reads, which is too expensive on UHD (O: Medium already costs +4.5 ms on Gunsai with its existing samples). Apply only to the rock layer, and only where the slope exceeds a threshold (dynamic branch) | M | V: Shader Graph node exists in the URP-compatible Shader Graph (the page lists only the 3-sample behaviour); hand-written HLSL is possible and unrestricted | none |
| Slope-aware UV (project along the dominant axis chosen per vertex/triangle, store UV per mesh) | Medium: no extra reads, seams at axis switches | O: 0 extra reads; costs a mesh with split vertices on switch lines | M | none needed | Fits cut slopes at 1:1.5 (`Terrain.cs:55`), which are planar and regular, so a single projection along the road normal gives acceptable results (O) |
| Stretch-limited planar | Low | O | S | none | what exists now |

Decision: slope-aware (road-normal) UV for banks first; triplanar only for the rock layer in High.

### 3.3 Anti-tiling

| Technique | Gain | Cost | Complexity | Support | Notes |
|---|---|---|---|---|---|
| Cheap macro variation (second sample at ~1/17 scale or a low-res world-space noise multiplier) | Medium-high for the price | O: +1 read (low res) or 0 reads with analytic noise | S | custom HLSL | the existing `Kn5Multilayer` detail trick (detail * 2 neutral at 0.5, `Kn5Multilayer.shader:97`) is this principle |
| Stochastic texturing (Heitz and Neyret 2018) | High: tiling nearly invisible | O (literature, not a Unity doc): about 3 reads plus a blend per layer, 3x of the plain cost for textured layers; requires a histogram-preserving transform (precompute) for good quality | L | custom HLSL, no URP restriction | too expensive on UHD for 4 layers; use for 1 hero layer in High only |
| Hex tiling (Mikkelsen 2022) | High | O (literature): 3 samples per layer | M-L | custom HLSL | same cost as stochastic; simpler, no precompute |
| Rotated/offset second sample per layer | Medium | O: 2x reads per layer | S | none | not recommended: same cost as hex with worse look |

Decision: macro variation in Low/Medium; hex tiling for one or two layers in High only after measurement.

### 3.4 Detail maps with distance fade

| Gain | Cost | Complexity | Support |
|---|---|---|---|
| Medium: close-up crispness | O: 1 read inside fade distance (dynamic branch skips beyond `~40 m`); `Kn5Grass` already fades at 45 m (`Kn5Grass.shader` `_MaxDist`) | S | custom HLSL; `UNITY_BRANCH` used in existing shaders (`Kn5Multilayer.shader:99`) |

### 3.5 Road

| Technique | Gain | Cost | Complexity | Support |
|---|---|---|---|---|
| UV along the spline (`v` = arc length in metres, `u` = across) | Required: repeating asphalt, patches and crack textures; already in the mesh (`WorldGenSectorSource.cs:336-340`) | M-free: no new vertex data. Junction patches need world UV | S | none |
| Markings as geometry (thin quads) | Crisp at all distances without texture sampling | O: +triangles; project data: shadow casters dominate the frame (study section 1: 70 % of triangles are shadow casters). Markings must not cast shadows (or be baked into the road mesh with `ShadowCastingMode.Off`) | M | none |
| Markings in the road texture (atlas: asphalt with painted line channel) | Cheap, but aliasing at grazing angles needs anisotropy: RG-140 already did that for Gunsai (user: "Znaceni je v dalce citelne, ok") | M for aniso (+0.33..+0.65 ms) | S | V: "Anisotropic filtering is good for floor and ground Textures but is resource intensive." (texture-type-default, 2026-11-09) |
| URP decals | Allows markings, patches and puddles over any surface | O: extra draw per decal, no SRP Batcher: V: "Decals do not support the SRP Batcher by design because they use Material property blocks." (renderer-feature-decal reference). DBuffer technique needs a DepthNormal prepass: V: "This technique requires the DepthNormal prepass" and Screen Space "recommended for mobile platforms that use tile-based rendering" (same page). The UHD is not tile-based, so DBuffer is the likely mode, and the extra prepass cost is unmeasured | M | V: Decal projector requires the Decal Shader Graph material: "A Decal Projector component must use a Material with the Decal Shader Graph assigned" (decal page). Interaction with Forward+ and MSAA: not stated on the pages read, so UNVERIFIED |

Decision: markings in the road texture with the existing aniso setting for Low/Medium; markings as shadow-less geometry strips or a second road UV set for High. Decals only if a later measurement shows the prepass is cheap.

### 3.6 Wet asphalt at night

| Technique | Gain | Cost | Complexity | Support |
|---|---|---|---|---|
| Roughness/puddle mask (noise or texture, modulated by a global `_Wetness`), additive sky/probe reflection with Fresnel | High at night: headlights on wet road is the signature look | O: 1 mask read (can share the macro-variation read, alpha channel) + one cube sample; `Kn5Lit` already has `_UseRefl` and additive night reflection (`Kn5Lit.shader:6-9`, `Kn5Multilayer.shader:148-154`, RG-081). M: reflection cost is already inside the Kanazawa/Gunsai figures | S-M | none |
| Reflection probe link (RG-114) | Better local reflections than the sky cube | O: probe cost and update policy are RG-114's measurement; for a static test tile a baked or once-rendered probe is cheap | M | not verified here (outside this ticket's V list; RG-114 owns it) |
| Screen-space reflections / planar | Not recommended | O: far beyond UHD budget | L | No SSR found in the Unity 6000.3 manual's URP renderer-feature pages (RG-156, 2026-11-12: built-ins listed are Decal, SSAO, Screen Space Shadows, Render Objects; full TOC not readable, so absence on the pages read, not a proof) |

### 3.7 Compression, packing, bias, anisotropy

| Item | Recommendation | Evidence |
|---|---|---|
| Format on DX11 | BC1 for opaque albedo (4 bpp), BC4 for single-channel (height, roughness), BC5 for tangent-space normals (RG with reconstructed Z), BC7 for albedo+height or when BC1 banding is visible (8 bpp) | V: Windows column of the supported-format table: BC1, BC4, BC5 yes, BC7 "yes (1)" with note "Except on pre-DX11 level GPUs, or macOS when using WebGL or OpenGL." (texture-formats-reference, 2026-11-09). Default normal-map format: NOT VERIFIED (page does not state it). The bit rates (4 bpp / 8 bpp) are O (general knowledge) |
| Channel packing | albedo.rgb + height.a (BC7), normal.rg (BC5) + roughness/AO in a separate BC1/BC4, or ORM in one BC7 | O |
| Mip bias | Global `Texture.mipMapBias` slightly negative (about -0.5) sharpens distance without extra samples; costs aliasing | V (summary, not verbatim): API page recommends not going past roughly -0.5. Only the scripting API names it; there is no import setting of that name in 6000.3 (default-type page: Priority "As a mipmap bias value when choosing a mipmap level that fits in the memory budget."). Effect on the UHD unmeasured |
| Anisotropy | 4 on track textures in Low (M +0.33..+0.65 ms); QualitySettings.anisotropicFiltering has Disable / Enable / ForceEnable | M (RG-140); V (enum page): "Enable anisotropic filtering, as set for each texture." |
| Texture resolution | 1024 px per layer in Low/Medium, 2048 px for road in High | M: Gunsai memory 46.5 MiB at 1024 vs 60.5 MiB at 2048 vs 18.4 MiB at 512 (`kn5-material-profile.md` 5.1). GPU memory shared with RAM: 92..224 MiB on Gunsai Low..native, 15.7 GiB total |

### 3.8 Mipmap streaming and memory

| Fact | Evidence |
|---|---|
| Per-texture option "Stream Mipmap Levels" is visible only with Generate Mipmap on; applies to all cameras once enabled; budget is Quality > Textures > Memory Budget | V: "This property is visible only when Generate Mipmap is enabled." and "By default, all cameras in your scene use mipmap streaming when you enable it." (texture-type-default / texture-streaming, 2026-11-09) |
| Not supported for texture arrays, cubemap arrays, 3D textures, terrain textures | V (texture-streaming): "Unity doesn't support mipmap streaming with texture arrays, cubemap arrays, or 3D textures." and "Unity doesn't support mipmap streaming on terrain textures." |
| URP-specific behaviour | NOT VERIFIED: the manual page `texture-mipmap-streaming.html` returned 404 and the pages read do not say anything about URP |
| Consequence | A Texture2DArray layer set cannot be streamed. For a 4-layer 1024 px BC7 set that is 4 x 1.33 MiB = about 5.3 MiB with mips (O: 1024x1024x1 B x 4/3 per layer), so streaming is not needed at this scale. Plain `Texture2D` layers (not an array) can stream. Memory is not the constraint; bandwidth is (O, Low is GPU-bound at 99 %, M) |

### 3.9 Forward+ and MSAA

- Forward+ is in use (`#pragma multi_compile _ _CLUSTER_LIGHT_LOOP`, `Kn5Lit.shader:70`, `Kn5Multilayer.shader:52`). The Forward+ manual page returned 404 (permanent), so light limits and MSAA interaction of Forward+ are **not verified** in this ticket; only the asset reference notes were read ("This property is only applicable if you select the Forward+ rendering path.").
- MSAA 4 is the High preset (features.md:72; M: High P95 Kanazawa 18.4 ms). V: "MSAA is more resource intensive than other forms of anti-aliasing on most hardware." and options "2x, 4x, or 8x." (anti-aliasing page, 2026-11-09). Texture work is per fragment, so the splat shader pays per shaded fragment, not per sample; MSAA multiplies edge resolve and bandwidth, not texture reads (O). Alpha-to-coverage exists for cutouts (RG-088, features.md:89).
- SRP Batcher: V: material properties must be "in a single constant buffer named" `UnityPerMaterial` (srpbatcher page). Existing shaders already follow this (`Kn5Multilayer.shader:61-68`). New splat shaders must do the same. Per-sector `MaterialPropertyBlock` is forbidden by the same page; use per-sector Material instances (cheap: one per 256 m sector, O) or a shared array index.

## 4. Recommended sets

All ms are O: estimated increments on top of the worldgen row of the perf matrix (M: 2.56 / 4.22 / 6.66 ms P95) for the notebook. Reasoning: increments are sized from the Gunsai delta of the matrix (6.77 to 11.26 to 19.70 ms Low/Medium/High, M) and from RG-140 (aniso 4: +0.33..+0.65 ms, M), scaled by the smaller area/texture count of the WorldGen tile. They are targets for a measurement ticket, not promises.

| Set | Contents | Added cost (O) | Total worldgen P95 (O) | Limit |
|---|---|---|---|---|
| **Low** | Vertex-colour splat, 3 layers (grass/rock/dirt) 512-1024 px BC1, macro variation (analytic), slope-aware UV on banks, road albedo+normal 1024 px with markings in the texture, aniso 4, no detail maps, no wet | +0.8..1.5 ms | about 3.4..4.1 ms | Low 720p, `performance_targets` 30 FPS = 33.3 ms; large margin |
| **Medium** | Control-texture splat (4 layers, 1024 px BC7 with height in alpha), height blend, triplanar rock layer on slopes > threshold, detail map to 40 m, wet-road mask + night reflection (sky cube), aniso 4 | +2.5..4 ms | about 6.7..8.2 ms | Medium (matrix Medium = 4.2) |
| **High** | Medium plus hex tiling on 1..2 layers, 2048 px road, normal maps on layers, probe reflection (RG-114), grass tint from the splat (G), markings as shadow-less geometry, MSAA 4 | +5..8 ms | about 12..15 ms | High (matrix High = 6.7); Gunsai High is 19.7 ms, so High stays under that |

Risk to the estimate: shadow casters dominate (study section 1: 70 % of triangles, M), so texture changes that do not touch triangle counts should not move the shadow pass; fragment cost is the part that scales with the number of reads (O).

## 5. GPU measurement tickets (turn O into M)

Tool for all: `tools/game-unity/run-lab.ps1` with a new preset, the perf matrix `run-matrix.ps1 -Set quick` and `compare.py`, row `worldgen` (and `gunsai` as a texture-heavy control). One GPU ticket at a time; shared notebook slot.

| Ticket (proposal) | Measures | Method | Map row |
|---|---|---|---|
| T1 Splat read cost | ms per added texture read on UHD: 1, 2, 4, 6 reads on the ground at 720p/1080p | one test shader with N reads of one 1024 px BC7 texture, matrix `quick` Low vs baseline | worldgen |
| T2 Splat variants | vertex-colour vs control texture vs height blend | same, three shader variants behind a `--worldgen-ground` flag | worldgen |
| T3 Triplanar vs slope UV | cost of 3 reads on banks only vs everywhere | compare 3 variants | worldgen (needs the cut-heavy spec) |
| T4 Anti-tiling | macro variation vs hex vs stochastic on 1 and 2 layers | compare | worldgen |
| T5 Road set | road albedo+normal+markings with aniso 0 / 4 / 8; markings as geometry vs texture | compare; pair with a readable-markings screenshot sweep | worldgen |
| T6 Wet night | wet mask + sky cube + probe (RG-114), headlights; night only | `run-night.ps1` row | worldgen night |
| T7 Decal prepass | DBuffer decals: cost of the DepthNormal prepass and one puddle decal | URP asset change needs ADR and user consent (study section 4) | worldgen |
| T8 Mip bias and memory | `mipMapBias` -0.5 vs 0, texture memory at 512/1024/2048 | memory counters as RG-041; frame time by matrix | worldgen, gunsai |

## 6. Gaps and what is NOT verified

- Forward+ manual page: 404 (checked 2026-11-09). Light limits, MSAA interaction and GPU support of Forward+ are not verified here.
- Mipmap streaming manual page: 404; URP-specific behaviour not verified.
- Texture aniso manual page (`texture-aniso.html`): 404; used the enum and texture-type pages instead.
- Default compression format for normal maps: not stated by the pages read.
- Stochastic texturing (Heitz and Neyret 2018) and hex tiling (Mikkelsen 2022) are described from general knowledge (O); the papers were not read in this session.
- BC bit rates and ORM packing: O.
- Decal interaction with Forward+/MSAA: not stated in the pages read.
- No measurement of any proposed shader exists; every total in section 4 is O.
