# CC0 tree and vegetation models: sources, licences, cost on UHD (RG-157)

Owner: Claude (Sonnet), desk research 2026-11-12. Rules: `research-rules.md` (V/M/E/O). Input for the decision "procedural v1 (RG-149 D2, RG-154) vs CC0 models" after the RG-154 ride. **Nothing was downloaded**; page reads only (about 31 fetches via `web-fetch` helpers, URL list in the outbox). Access date of every V: **2026-11-12**. "not shown" = the page was read and does not say; it is not filled from memory.

## 1. Result in five lines

1. **Poly Haven** is the only source that is class A (texture-sources §2.1) and has real, scanned/modelled trees; but its trees are film-grade (0.3-17 M tris per tree, V). Only LODs exist as a tag; **per-LOD tri counts are not shown** on any page read (the model-standards page returned no text).
2. **Quaternius** (low-poly, game-ready) changed its licence: the page `license.html` is now "Quaternius Asset License (QAL) v1.0", last updated 2026-08-28, **not CC0**; pack pages and Poly Pizza still say CC0. Contradiction -> class B until the user decides.
3. **Kenney Nature Kit** is CC0 (V), 3D, tagged tree/foliage; formats, tri counts and size are not shown.
4. **ambientCG** has an asset type "3D Model" (V), but I could not confirm a single tree there.
5. **Recommendation (O):** keep procedural v1. If the look is judged poor after the ride, take **Kenney Nature Kit** (CC0) or hand-decimated Poly Haven shrubs/ferns as LOD1 candidates, after the user decisions in §7. Poly Haven LOD0 trees are out of budget on UHD.

## 2. Sources table

Class A/B/C per `texture-sources.md` (A = any asset after manifest + QA, B = per-asset user decision, C = excluded). Slop signals per research-rules §6.

| # | Source | Class | Licence (V, verbatim, primary page) | AI policy | Operator | Signals |
|---|---|---|---|---|---|---|
| 1 | **Poly Haven** | **A** (existing allowlist; models need a v2 schema, ADR-037 covers textures only) | https://polyhaven.com/license: "Our assets are all licensed as CC0, which is effectively Public Domain even in jurisdictions that do not support the Public Domain." Also "You can redistribute them, ... or even in a product you sell." No engine/game restriction shown. | V https://polyhaven.com/contribute: "We strictly do not allow the use of any generative AI tools to be used in the process of creating your asset." | named studio; authors named per asset | 1: no capture-method label per model (the site says "Your asset must be 100% your own original work") |
| 2 | **ambientCG** | A for materials; **models: unrated** | https://docs.ambientcg.com/license/: "All ambientCG assets are provided under the Creative Commons CC0 1.0 Universal License." https://docs.ambientcg.com/asset-types/ lists "3D Model" | not stated | one unnamed operator, since 2018 | 2 (as texture-sources §2.2). Trees not confirmed: https://ambientcg.com/list?type=3DModel returned only the filter sidebar ("3D Tree Stump", "Pine Needles", "Foliage" among labels, type not shown) |
| 3 | **Quaternius** | **B** (licence contradiction) | https://quaternius.com/license.html, "Quaternius Asset License (QAL) v1.0", "Last updated: 8/28/2026": "You can use these assets, free of charge, in personal, educational, and commercial games"; 2: "No attribution is required."; 3(a) forbids "Resell or redistribute the Assets themselves"; 7: changes "will not apply retroactively". Pack pages https://quaternius.com/packs/ultimatenature.html, `.../stylizedtree.html` still show Licence "CC0". | not stated | pseudonym "Quaternius", Patreon | 2: licence text differs between pages; pseudonymous. QAL is not CC0 (no public domain dedication; a repo containing the assets is a redistribution question) |
| 4 | **Kenney** | **A-/B** (CC0 site-wide, but per-asset data thin) | https://kenney.nl/support: "all game assets on the asset pages are public domain licensed (CC0)." https://kenney.nl/assets/nature-kit: "Creative Commons CC0", v1.0 "Released in 2020", tags nature, tree, rock, foliage | not stated | "Kenney", no legal name on pages read | 2: no per-asset tri/format/author data; AI policy not stated |
| 5 | **Poly Pizza** | **C for our purpose** (aggregator) | https://poly.pizza/docs/tos: "adhere to the terms of the Creative Commons license that applies at the time of download"; per-model label e.g. https://poly.pizza/m/oYtDty0fR6 "Public Domain (CC0)" | none; ToS bars AI training on site content only | handle "dook"; 10,700+ models, user uploads self-warranted | 4: user uploads without provenance, mixed licences, paid Synty/"Poly by Google" items in results, mirrors Quaternius (licence drift vs source #3) |
| 6 | OpenGameArt | **unverified** | https://opengameart.org/ returned HTTP 403 (permanent). Nothing read | - | - | n/a |
| 7 | Sketchfab | **unverified / C** | https://sketchfab.com/licenses is Sketchfab's own paid per-listing licence; no CC0 wording. Agreement says "division of Epic Games", footer says "KitBash SF Operations LLC" | site has "AI Generated" filter, no policy on that page | - | 3: entity mismatch, uploader-licensed, AI mixed in |
| 8 | KayKit, Smithsonian 3D, Free3D | **not checked** | fetch budget; leads only (O) | - | - | n/a |

Brief asked for "up to 4 other" sources: 4 were read (Quaternius, Kenney, Poly Pizza, Sketchfab) plus OpenGameArt (blocked); further leads in row 8 are unverified.

## 3. Assets table

All numbers are what the Poly Haven asset page shows (V, https://polyhaven.com/a/<id>, 2026-11-12); `tris` equals API `polycount`. Page shows **one** download size per asset (no per-format/resolution split), **no wind data on any page**, no per-LOD tri counts. Formats on every PH page: Blend, glTF, USD, FBX, ZIP. Textures 1K/2K/4K/8K (grass_medium_02: 1K-4K). Leaves: an alpha/`twig_alpha`/`leaves_alpha` map is listed -> alpha cutout (O, the page does not say "cutout").

| # | Asset | Kind | Tris (full model) | LODs | Size (all) | Height/width | Author | Note |
|---|---|---|---|---|---|---|---|---|
| 1 | `pine_sapling_small` | conifer sapling | 398 K | not shown (no LOD tag) | 290.06 MB | 2.6 m wide | Rico Cilliers (model), Rob Tuytel (photo) | twig alpha + mask |
| 2 | `fir_sapling` | conifer sapling | 433 K (API listing) | no LOD flag | not read | not read | not read | listing only |
| 3 | `fir_tree_01` | conifer tree | 7.85 M (API listing) | LOD tag, geonodes | not read | not read | not read | listing only |
| 4 | `pine_tree_01` | conifer tree | 17.4 M (API listing) | LOD tag, geonodes | not read | not read | not read | listing only |
| 5 | `tree_small_02` | broadleaf | 5 M | LOD tag, geometry nodes | 263.02 MB | 4.6 m tall | Rico Cilliers | `leaves_alpha`, `branch_*` |
| 6 | `jacaranda_tree` | broadleaf | 312 K (API listing) | LOD tag | not read | not read | not read | listing only |
| 7 | `searsia_lucida` | bush (Karoo) | 842 K | LOD tag | 56.74 MB | 4.8 m wide | James Ray Cock (model), Jenelle van Heerden (photo) | alpha map |
| 8 | `shrub_03` | bush | 16,955 (API listing) | not shown | not read | not read | not read | closest to our budget |
| 9 | `shrub_sorrel_01` | bush | 3,319 (API listing) | not shown | not read | not read | not read | |
| 10 | `fern_02` | fern | 6 K | no LOD tag | 17.07 MB | 2 m wide | Rico Cilliers (model), Rob Tuytel ("Scanning") | the only page with a scan role |
| 11 | `grass_medium_02` | grass tuft | 1 M | no LOD tag, geonodes | 53.42 MB | 3.6 m wide | Rico Cilliers | alpha + disp |
| 12 | Kenney Nature Kit | tree/foliage kit | not shown | not shown | not shown | not shown | "Kenney" | https://kenney.nl/assets/nature-kit "Files 330x" (not clear: files or downloads) |
| 13 | Quaternius Ultimate Nature Pack | 150 models | not shown | not shown | not shown | - | Quaternius | FBX/OBJ/Blend; **untextured** per page (cross mark); licence conflict |
| 14 | Quaternius Stylized Tree Pack | 45 trees | not shown | not shown | not shown | - | Quaternius | FBX/OBJ/Blend; textured; licence conflict |
| 15 | Poly Pizza "Pine Trees" (Quaternius, 2022-09-04) | pine | not shown (page shows unlabelled 3.7k/9.26k/40/6.48k, not interpreted) | not shown | not shown | - | Quaternius | "FBX/GLTF format" |

Rows 3, 4, 6, 8, 9 come from the Poly Haven JSON listing (V for the number, not for the page). Rows 2-11 are not the whole catalogue (shrubs 01-04, wild_rooibos, island_tree_01-03 etc. exist, API listing).

**Gaps (unverified, not guessed):** per-LOD tri counts of every PH model; whether the LOD files are separate meshes; wind data; billboard/impostor; Kenney/Quaternius tri counts. https://docs.polyhaven.com/en/technical-standards/models returned only its title (no text). These need either a file download (user decision D2) or a page that renders.

## 4. Cost on UHD

Baseline M (RG-154 outbox `agent/claude/RG-154:agent_pool/agents/claude/outbox/20261112T010331532781Z_RG-154.md`, Low, i3-1215U/UHD): scene **121,527 tris p50 / 145,187 max, 107 draw calls p50**; vegetation on: 122,308 tris, 108 draws, **max 122 instances, 3 draw calls**; tile row **+0.43 ms P95** (valid), warm P95 about 3.0-3.6 ms. Procedural meshes: tree LOD1 64 tris, LOD2 12, bush 24, house 14; no shadow casters; opaque.

Formula (O): `tris_veg = sum over visible instances n_i x tris_LOD(i)`; time cost is assumed roughly linear in tris+overdraw at a fixed draw count (O, not measured).

| Case | Instances | Tris each | Added tris | vs scene 122 K | Verdict (O) |
|---|---|---|---|---|---|
| RG-154 mix (M) | 122 | about 12-64 | about 0.8 K | +0.6 % | measured +0.43 ms P95 (includes shader/placement) |
| PH `fern_02` 6 K, LOD0, 20 near | 20 | 6,232 | 125 K | +102 % | doubles the scene; near-only 5 would be 31 K (+26 %) |
| PH `shrub_03` 17 K, 10 near | 10 | 16,955 | 170 K | +139 % | too heavy as LOD0 beyond about 3 |
| PH `tree_small_02` 5 M, 1 near | 1 | 5,000,000 | 5 M | +4000 % | unusable without a decimated LOD (tri count unknown) |
| PH `pine_tree_01` 17.4 M | 1 | 17.4 M | 17.4 M | +14,000 % | out of question |
| Hypothetical decimated 2 K-tri tree, 30 near | 30 | 2,000 | 60 K | +49 % | possible only at LOD0 < 30 m; LOD budget must be set by us (E/O) |

- **Draw calls (O):** each distinct mesh+material is one `RenderMeshInstanced` call per LOD; CC0 variety (say 6 species x 3 LODs, 2 materials each) = up to about 36 batches vs 3 now. At about 0.01-0.02 ms per extra call on a CPU-light frame this is small, but the SRP-Batcher/instancing variant trap of RG-154 (INSTANCING_ON stripped when the material is runtime-only) applies again.
- **Alpha-test overdraw (O):** cutout leaves cost `covered_pixels x layers` fragment shader runs; on a shared-memory iGPU, already GPU-bound at High (brief), a close 4 m tree filling 20 % of a 1280x720 frame with 4-6 overlapping leaf layers is about 0.9 M x 5 = 4.6 M extra fragments per frame, versus about 0.9 M for the whole frame. Estimate only, not measured; real cost is a Unity measurement (not in this ticket).
- **Shadow casters:** RG-154 chose `ShadowCastingMode.Off` because the world has 0 casters (M). A cutout leaf material needs the alpha-clip in the shadow pass (an extra texture sample) and every tree becomes a caster draw: tris and overdraw are counted twice (O). With opaque cones nothing needs clipping, so D4 (casters only within 60 m, Medium+) stays cheap; with cutout leaves D4 would need a separate decision.
- **Texture memory (O):** one 2K albedo+normal+alpha set per species at 3 channels x 4 MB = about 12 MB uncompressed per species before mip/compression; PH ships 4K/8K (maps listed at 4K). With about 6 species the needed set is 2K or smaller; page sizes of 17-290 MB per model are for all-resolution zips.

## 5. Manifest schema v2 (fields missing in `nightracer.thirdparty-asset.v1`, for models)

v1 (`thirdparty-assets-pipeline.md` §2) has `kind: "texture-material"` only and per-file `format: png`. For `kind: "model"` add:

| Field | Type | Why (threat) |
|---|---|---|
| `schema` | `"nightracer.thirdparty-asset.v2"` | v1 stays valid for textures; a model must not pass the v1 lint |
| `kind` | `"model-tree"`, `"model-bush"`, `"model-grass"`, `"model-prop"` | selects checks |
| `files[].format` | `glb`, `gltf`, `fbx`, `blend` (one is the *import source*; others are listed but unused) | T10: loader attack surface follows the format |
| `mesh.sha256`, `mesh.bytes` | hash per mesh file | T8, reproducible import |
| `mesh.lods[]` | `{level, file_or_node, tris, verts, max_screen_height_m}` | LOD availability, budget check |
| `mesh.tris_total`, `mesh.tris_lod0` | int | budget gate (§4) |
| `mesh.bounds_m` | `[x,y,z]` size in metres | scale check |
| `mesh.unit`, `mesh.up_axis`, `mesh.handedness` | `m`/`cm`; `Y`/`Z`; `left`/`right` | glTF is Y-up right-handed, Blend Z-up, FBX unit mix (O: import trap) |
| `mesh.pivot` | `base_center` or other | trees must sit on the ground |
| `materials[]` | `{name, textures{role->file}, alpha_mode: opaque/mask/blend, alpha_cutoff, double_sided, shader_hint}` | cutout vs opaque, shadow pass |
| `textures[]` | reuse v1 `files[]` rows (`width`,`height`,`colour_space`, `normal_convention`) | existing QA reused for texture channels |
| `wind` | `{vertex_color_channel/none, source: "asset"/"none"}` | none of the pages read states wind data |
| `billboard` | `{present, file}` | impostor availability |
| `author_per_file[]` | `{path, role(model/photo/scan), name}` | PH lists several roles per asset (V) |
| `provenance_per_file[]` | `{path, source_url, retrieved_on, licence_id, licence_page_sha256}` | kits (Kenney, Quaternius) mix files; v1 has one provenance per asset |
| `declared.ai_generated` | as v1 | the three sources say "not stated" except PH site rule |
| `licence.version_or_date` | string | QAL v1.0 updated 2026-08-28 shows licences move (T8) |
| `import.method` | `editor-import` or `runtime-gltf` | see §6 |
| `qa.mesh` | `tris`, `degenerate`, `non-manifold`, `uv_overlap`, `metadata_scan` | analogue of 4.8 for glTF `extras`/FBX user properties (T9 prompt injection) |

## 6. Comparison and recommendation

| Criterion | Procedural v1 (RG-154, M) | CC0 LOD0 (this doc) |
|---|---|---|
| Look | cones/boxes, flat colour; "forest as row of cones" (M review); no leaves | real foliage, bark; only usable if decimated; film-grade tris (V) |
| Cost on UHD | +0.43 ms P95 at Low (M) | fern/shrub 6-17 K tris OK to about 5-10 near; trees need our own LODs; overdraw + shadows unmeasured (O) |
| Licence work | none (original) | Poly Haven: ADR + schema v2 + per-model manifest; Quaternius: QAL decision; Kenney: user decision (class B) |
| Pipeline work | exists | importer, LOD authoring/decimation (tool or Blender, not installed here), alpha material, wind, collision-free; **editor import** (Unity) keeps formats out of the player; **runtime glTF** would add a parser attack surface (T10) and load time |
| Risk | low | high: LOD tri counts unknown, provenance per kit, licence drift |
| Download | none | 17-290 MB per PH model; all-resolution zips |

**Recommendation (O):** (1) keep D2 procedural for now; (2) if the ride judges trees as "row of cones", the cheapest improvement is richer **procedural** LOD0 (more cone layers, leaf cards) before touching CC0; (3) if CC0 is wanted, start with **Kenney Nature Kit** (CC0 stated site-wide, low-poly style) as a one-kit pilot, with `fern_02` and `shrub_03` from Poly Haven as the only PH candidates in budget; (4) never use PH trees above 1 M tris without a decimated LOD. The decision stays the user's.

## 7. Decisions for the user (collected, none blocking)

| ID | Question | Recommendation |
|---|---|---|
| D1 | Do models enter the project at all, after the RG-154 ride? | decide after the ride; default no |
| D2 | May a worker download **one** candidate to `artifacts/tmp/` on D: to read per-LOD tris, glTF structure and measure it? Candidates: `fern_02` (17.07 MB page figure, https://polyhaven.com/a/fern_02), Kenney Nature Kit (size not shown, https://kenney.nl/assets/nature-kit) | yes for these two, glb only, by hand |
| D3 | Source/licence class for Kenney and Quaternius (both outside allowlist A; QAL not CC0) | Kenney = per-asset B; Quaternius = excluded until the licence is read in full |
| D4 | Schema v2 + ADR extending ADR-037 to models (needed before any model enters) | only after D1/D2 |
| D5 | Import path: editor-import (recommended) vs runtime glTF | editor-import |

## 8. Evidence index

V sources: polyhaven.com/license, /contribute, /a/pine_sapling_small, /a/tree_small_02, /a/searsia_lucida, /a/fern_02, /a/grass_medium_02, api.polyhaven.com/assets?t=models&c=trees and `c=plants` (listing, truncated); docs.ambientcg.com/license and /asset-types; quaternius.com packs + license; kenney.nl/support and /assets/nature-kit; poly.pizza docs/tos and /m/oYtDty0fR6; sketchfab.com/licenses. Not readable: opengameart.org (403), docs.polyhaven.com models standards (title only). M: RG-154 outbox above. All V quotes were returned by `web-fetch` helper agents, each at most 40 words; the orchestrator re-fetches 3 at review (research-rules §8).
