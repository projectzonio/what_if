---
id: KB-BLENDER-VEGETATION-006
type: rendering-architecture-research
domain: grass-and-ground-vegetation
status: research-baseline
tags: [grass, vegetation, cryengine, crysis-3, rendering, overdraw, instancing, lod, wind, unity, planetary-scale]
sources: [SRC-NVIDIA-GPUGEMS3-CRYSIS-VEGETATION, SRC-CRYENGINE-GRASS-MERGED, SRC-CRYENGINE-GRASS-PATCH, SRC-CRYENGINE-GRASS-SHADER, SRC-CRYENGINE-VEGETATION-PERF, SRC-CRYENGINE-BENDING, SRC-CRYSIS3-GRASS-INTERVIEW, SRC-UNITY-GPU-INSTANCING, SRC-UNITY-TERRAIN-GRASS]
---

# Grass and ground vegetation — rendering research

## 1. Objective

Build grass, sedges, reeds, meadow plants and low ground cover that look rich at player distance while keeping GPU time, power draw, memory and streaming cost predictable. The target is not "maximum blades per square metre"; it is stable perceived coverage and believable interaction at a controlled frame cost.

This document covers runtime rendering and asset design. Blender creates source meshes, clusters, impostors, material maps, vertex attributes and LODs. The runtime renderer owns culling, batching, density selection, wind, interaction and distance transitions.

## 2. Crysis / CryEngine findings

### 2.1 Crysis vegetation research (CryENGINE 2)

[VERIFIED] Tiago Sousa's GPU Gems 3 chapter describes the Crysis vegetation system as a combination of procedural breaking and physics interaction, shading, procedural animation and distant sprite generation. Global/local wind and fine leaf bending were computed procedurally on the GPU. Vertex colour channels carried per-vertex animation controls: leaf-edge stiffness, per-leaf phase variation, overall leaf stiffness and precomputed AO.

The chapter states that grass shading was performed per vertex because of heavy fill-rate requirements. It describes double-sided foliage, alpha-tested leaves and alpha-blended grass in that implementation. A low-resolution artist-authored subsurface/thickness map supported a cheap foliage-light transmission approximation.

Important scope: this is a documented Crysis/CryENGINE 2 technique, not a complete disclosure of Crysis 3's later grass renderer.

### 2.2 Crysis 3 grass (reported developer interview)

[REPORTED] In a PC Gamer interview with Crytek rendering director Rasmus Højengaard, the Crysis 3 grass technology was described as rendering thousands of individual blades with low cost and memory use, including on Xbox 360 and PlayStation 3. The interview says AI characters, projectiles, explosions and wind could affect grass. This supports the design goals and interaction behavior, but does not disclose the complete shader, culling or data layout.

[REPORTED] Crytek CEO Cevat Yerli described grass as enabling gameplay: players and AI could use fields for concealment and battlefield reading, with technology supporting systemic rather than staged interactions. This is an interview statement, not a performance benchmark.

[VERIFIED] Current CRYENGINE documentation includes a Crysis 3 merged-mesh grass example. Merged Mesh vegetation batches many vegetation items and can interoperate with vegetation, physics, explosions and global/local wind. The docs emphasize that assets must use very simple geometry; using the system with complex grass-patch geometry can be expensive.

### 2.3 CryEngine grass asset types

CryEngine distinguishes:
- Grass patches: multi-plane clumps; convenient coverage, but each placed patch has a draw call in the documented workflow. Current docs recommend keeping large patches around 8 m diameter as a practical balance and using smaller-footprint merged mesh grass where terrain angle variation is high.
- Grass merged meshes: extremely simple blade meshes, batched in larger sectors; suited to individual blades and higher instance variation.
- Vegetation shader grass mode: a cheaper shading path which largely ignores specular and normal-map settings; use the more complex Leaves option only where the visual gain warrants it.

CryEngine's performance guidance recommends minimizing vegetation materials and draw calls, reducing polygon counts because vegetation can be drawn many times on screen, combining texture use and matching leaf polygon geometry to alpha texture silhouettes to reduce overdraw.

### 2.4 What the evidence does not establish

- No verified source in this research establishes a universal "20–40 Gtexel/s grass" requirement or a Crysis 3 GPU texel-rate figure.
- Gtexel/s is a throughput unit (billions of texel operations/s under a defined measurement), not a direct count of visible grass texels, grass quality, texture resolution or frame cost.
- GPU peak texture throughput cannot be equated with actual shader throughput: cache behavior, texture format, filtering, compression, shader occupancy, memory bandwidth, fill rate, alpha coverage, depth rejection and render resolution all matter.
- The 1 GB VRAM observation is a historical GPU memory-capacity context, not proof that all game assets/render targets fit in 1 GB at every setting. Crysis 3 also shipped with high-resolution texture options and platform-specific memory budgets.
- Do not infer Crysis 3's exact internal renderer from CryEngine 5 documentation; use the docs to understand the engine family's published concepts.

## 3. Why a low-quality grass system can consume high power

Investigate the following bottlenecks independently. A single FPS number cannot diagnose the cause.

### 3.1 Alpha overdraw and fill rate

Crossed grass cards, multi-layer alpha textures and dense overlapping blades can shade the same screen pixels repeatedly. Alpha blending may require sorting or order-independent handling and often prevents early depth rejection from eliminating all fragment work. Alpha clipping can reduce blend/sort costs but still incurs fragment and texture work; thin silhouettes can shimmer without suitable temporal/spatial AA.

Measure:
- opaque and transparent GPU time separately;
- overdraw visualization / fragment invocation estimates;
- number of grass layers covering representative pixels;
- screen coverage at each camera angle;
- cost at native resolution and with render scale reduced.

### 3.2 Geometry and vertex processing

Thousands of independent GameObjects or tiny renderers create CPU submission and culling overhead. GPU instancing reduces repeated submission but does not automatically reduce vertex count, fragment work or overdraw. A batch that is too large also reduces fine-grained culling; a batch that is too small increases draw count.

Measure:
- draw calls / batches / instances;
- visible vertices and triangles;
- CPU render-thread time;
- GPU vertex and fragment time;
- bounds waste (fraction of batch bounds visible vs actually visible geometry).

### 3.3 Shading and texture bandwidth

Grass rarely needs a full general-purpose PBR material per blade. High-resolution normal, roughness, metallic, specular and opacity maps on tiny moving blades can add bandwidth without visible benefit. CryEngine's Grass shader deliberately removes some expensive material features.

Use a dedicated grass shading path:
- base colour / species tint;
- alpha coverage or cutout only where needed;
- optional low-frequency variation;
- simple diffuse or wrapped diffuse;
- controlled backlighting/transmittance;
- low-cost world-space wind;
- avoid per-blade high-frequency normal/specular unless a close-up test proves it matters.

### 3.4 Power cost

A system may hit the frame target but still keep the GPU fully loaded. Record GPU power (where available), GPU utilization, clock, temperature, frame time, resolution and scene coverage. Compare the same camera path and frame cap. Do not equate "FPS is high" with "power cost is low."

## 4. Planetary Engine rendering architecture

### 4.1 Multi-representation coverage

Use four representation bands, selected by projected screen size, visibility and gameplay relevance rather than fixed world distance alone.

| Band | Representation | Main purpose |
|---|---|---|
| G0 close | Individual blades / small clumps | Close-up silhouette, foot/vehicle interaction, wind detail |
| G1 mid | Batched clumps / merged meshes | Dense readable field with shared materials and aggregated wind |
| G2 far | Cluster cards, coverage meshes, low-frequency species texture | Preserve meadow density and colour without blade-level geometry |
| G3 planetary | Terrain biome/coverage data and macro material | Global continuity; no individual blades |

Transitions should preserve coverage and mean colour. Use overlapping transition bands, stochastic cross-fade/dither or complementary coverage masks. Avoid simply deleting the near representation at a single threshold. Ensure shadow representation follows the same LOD budget; distant grass should not cast thousands of independent shadows.

### 4.2 GPU data layout

[RECOMMENDATION] Store grass as sector-local instance data rather than one scene object per blade:
- transform or compressed position/orientation;
- species/variant ID;
- scale and phase seed;
- terrain normal/alignment;
- tint/dryness;
- wind response class;
- interaction/damage mask only for nearby active cells.

Generate deterministic placement from biome ID + world cell + seed. The same cell must regenerate identical distribution after unload/reload. Keep high-frequency per-blade state only near the player; distant wind can be derived from world position and time.

Prefer GPU-driven indirect/BRG-style rendering or a consolidated instance buffer after profiling the target Unity pipeline. Built-in GPU instancing is a useful baseline, not necessarily the final architecture. Unity documentation warns that the CPU-side cost of collecting and uploading per-instance properties can outweigh draw-call savings on some platforms.

### 4.3 Density and species distribution

Do not distribute one uniform grass asset everywhere. Define biome-specific species mixtures:
- short lawn / pasture;
- meadow grasses and flowering forbs;
- dry steppe;
- wet sedges and rushes;
- reeds and marsh;
- alpine turf;
- crop residue / agricultural field;
- sparse arid ground cover.

Represent ecological composition as weights, height ranges, seasonal state, soil/moisture suitability and disturbance. Use a deterministic blue-noise/Poisson-like distribution or equivalent controlled sampling to avoid obvious grids and clumps. Keep a separate coverage scalar so the far representation matches the near geometry's aggregate density.

### 4.4 Wind and interaction

Use a hierarchy:
- macro wind field sampled in world space;
- sector-level phase/strength;
- per-instance phase and stiffness;
- vertex-level bending profile;
- local interaction field around player, vehicles, projectiles and explosions.

Do not run a full physics body per blade. Use shader deformation for visual response and a sparse interaction field for gameplay. Nearby grass can react to character/vehicle passage; projectiles and blasts can create temporary directional flattening or scorch/damage masks. Return to a species-specific recovery state over time.

CryEngine's Crysis vegetation chapter demonstrates vertex-colour stiffness/phase encoding and GPU procedural bending; its current docs describe merged-mesh vegetation reacting to wind and physics.

## 5. Blender authoring workflow

### 5.1 Species asset

Create one source object per species/variant:
- 3–8 representative blade shapes per species (planning range; benchmark, not a Crytek specification);
- curved blade profiles with taper and thickness or camera-facing cards only where appropriate;
- root pivot at soil line;
- consistent local up axis and scale;
- vertex colours/attributes for stiffness, phase, root-to-tip bend weight and optional AO;
- minimal material slots;
- clean silhouette and non-degenerate normals.

Do not create a separate Blender object for every blade in the final runtime scene. Blender may use collections/Geometry Nodes to build clumps, but export as a small number of reusable meshes and instance data.

### 5.2 Cluster generation

Use Geometry Nodes to:
1. sample points on a patch or terrain surface;
2. choose species by weighted biome composition;
3. instance blade meshes with deterministic random rotation/scale;
4. align roots to terrain normals within a species-specific slope limit;
5. write instance variation attributes;
6. build separate close and far cluster variants;
7. bake/realize geometry only when required by the export pipeline.

Keep patch dimensions small enough to follow terrain. CryEngine's documented patch recommendation is approximately 8 m for large patches; this is a reference, not a universal PE setting.

### 5.3 Material and texture strategy

- Use a texture atlas or texture arrays for related species.
- Pack only channels that the shader actually consumes.
- Use alpha clip for thin blade silhouettes where temporal stability is acceptable; avoid stacking many transparent planes.
- Use a small number of high-value species textures instead of one unique high-resolution material per blade.
- Bake macro colour/variation into cluster or terrain coverage for far LOD.
- Test compression and mipmaps at grazing camera angles; foliage alpha mipmaps need coverage-aware treatment to avoid disappearing blades.

### 5.4 LOD authoring

Generate and inspect:
- G0 individual blade source;
- G1 small cluster;
- G2 far cluster/coverage card;
- G3 terrain coverage material.

Render orthographic and grazing-angle comparison sheets. Compare projected coverage, colour histogram, silhouette density and shadow footprint. LOD must be visually continuous and deterministic.

## 6. Benchmark protocol

Build one fixed 100 m × 100 m meadow test cell and one 1 km streaming test. The dimensions are proposed test fixtures, not production density requirements.

Run a matrix:
- 3 density levels;
- 3 camera heights/angles (ground, low flight, aerial);
- 4 representation configurations (G0 only, G0+G1, G0–G2, full G0–G3);
- 2 material paths (simple grass shader, full PBR control);
- 2 wind states (off, procedural);
- 2 interaction states (idle, local disturbance).

Record:
- GPU frame time and CPU render-thread time;
- GPU power and utilization if available;
- VRAM and system RAM;
- draw calls, instances, triangles, vertex count;
- fragment/overdraw proxy;
- texture bandwidth/cache counters where supported;
- visible coverage and LOD pop score;
- streaming and regeneration time.

Capture the same camera path, resolution, quality settings and frame cap. Include a grass-free baseline. Test on the actual target GPU before choosing density. A "20–40 Gtexel/s" target is explicitly not adopted.

## 7. Acceptance criteria

- Ground-level close-up has distinct, non-repeating blade silhouettes.
- Mid-distance meadow preserves coverage without obvious card grids.
- Aerial view reads as biome-consistent coverage, not scattered dots.
- No large energy spike from enabling wind; measure shader cost.
- Player/vehicle disturbance is visible and localized.
- Same world cell regenerates identical placement after streaming.
- LOD transitions do not create visible holes or density pops.
- GPU cost scales predictably with screen coverage and visible sectors.

## 8. Sources and evidence quality

- [VERIFIED] GPU Gems 3 Crysis vegetation chapter is a first-party technical chapter by Crytek's Tiago Sousa.
- [REPORTED] Crysis 3 grass interaction and platform efficiency are from developer interviews, not a public shader specification.
- [VERIFIED] CRYENGINE docs document Grass patches, Merged Mesh grass, shader modes, bending and performance guidance.
- [VERIFIED] Unity docs describe GPU instancing and terrain grass behavior/limitations; these are API/rendering facts, not a recommendation that default Terrain grass is the final PE renderer.
- [RECOMMENDATION] G0–G3 architecture, data layout and benchmark matrix are proposed Planetary Engine design.
- [UNMEASURED] No PE grass benchmark has yet been run. No verified Crysis 3 20–40 Gtexel/s value is adopted.
