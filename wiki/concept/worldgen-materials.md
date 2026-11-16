---
type: concept
sources:
  - .planning/what-if/docs/worldgen-materials-techniques.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-145.md
updated: 2026-11-12
last_verified: 2026-11-12
status: provisional
aliases:
  - materiály worldgenu
  - textury dlaždice
  - splat
---

# WorldGen materials: from flat colours to textured test tile

**State (M, 2026-11-09):** WorldGen draws flat colours. `WorldGenSectorSource.Make(Color)` clones `Kn5/Kn5_Opaque` (URP Lit) per class: ground, road, tunnel, bridge, lamp. Worldgen is the cheapest row of the perf matrix (P95 2.6 / 4.2 / 6.7 ms Low/Medium/High) because it has no textures.

**Goal (user, 2026-11-09):** the best possible graphics on one WorldGen test tile, from **native** textures out of trusted open sources. No KN5 content, and reusable in What If (separate project). The research series: RG-143 sources and licences, RG-144 provenance + QA + ADR proposal, RG-145 techniques (done), RG-146 test tile. Evidence rules: [[research-evidence]].

## Verified facts that shape the implementation (RG-145, review-checked)
- **`Kn5Multilayer` is already a 4-layer control-map splat:** mask RGB + remainder, `_Mults` per layer, detail colour mode at line 99. Its `Attributes` have no COLOR (line 70) and the mask uses the base UV. For WorldGen it needs a per-sector mask from slope/height/road distance and a world-space UV.
- **Ground:** 65×65 vertices at 4 m (`WorldGenTerrain.cs:20`). Normals come from height differences, UV = world xz / 4. There are no vertex colours and no tangents. Cut/fill banks are 1:1.5 (`Terrain.cs:55`) and stretch a planar texture.
- **Road ribbons already carry UVs:** `u` across, `v` = arc length in metres (`WorldGenSectorSource.cs:340`). **Junction patches and fills have zero UVs** and will smear once textured; they need a world-planar UV first.
- **URP 6000.3 limits:**
  - "Unity doesn't support mipmap streaming with texture arrays, cubemap arrays, or 3D textures."
  - "Decals do not support the SRP Batcher by design because they use Material property blocks."
  - An SRP Batcher GameObject "mustn't use MaterialPropertyBlocks", so use per-sector material instances.
  Docs live under `docs.unity.com/en-us/engine/6000.3/manual/...`; the old `docs.unity3d.com/6000.3/Documentation/Manual/*.html` URLs 404 or redirect.

## Recommended order (O until measured)
Low:
- vertex-colour splat with 3 layers and macro variation;
- slope-aware UV on banks;
- road albedo + normal with markings in the texture, anisotropy 4 (RG-140, M +0.33..0.65 ms).
Medium adds:
- control-texture splat with height blend;
- triplanar on the rock layer only;
- detail maps to 40 m;
- a wet-road mask at night.
High adds:
- hex tiling on 1–2 layers;
- 2048 px road;
- probe reflections (`cryengine-optimization-study` (race_game) links, RG-114);
- grass tint from the splat.
The ms figures in the doc are an ordering, not a budget: they are anchored on Gunsai preset deltas (review finding 1).

## Next measurements (proposed GPU tickets T1..T8)
T1 is first: ms per texture read on the WorldGen ground (1/2/4/6 reads, BC7 1024 px, perf matrix `quick` Low). **T1 measured (RG-148, 2026-11-12, M):** 1 and 2 reads of a 2k RGBA32 asphalt on the ground at Medium/High are **below the detection floor** (0.53 / 0.52 ms; P50 hint +0.03 ms for 1 read, +0.10 / +0.19 ms for 2). Low and BC-compressed maps not measured. Probe flag `--worldgen-ground-tex` in `WorldGenGround.cs`; details `test-tile` (race_game). Consequence: judge texture variants by look first; cost becomes visible only with several layers. The others: splat variants, triplanar vs slope UV, anti-tiling, road set with aniso and markings, wet night, decal prepass (URP asset change = ADR), mip bias and memory. Detail: the doc §5.

Related: [[worldgen]], `features` (race_game), `perf-matrix` (race_game), `cryengine-optimization-study` (race_game).
