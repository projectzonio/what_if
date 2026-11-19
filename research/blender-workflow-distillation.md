---
type: research
project: What If / Planetary Engine
updated: 2026-11-19
status: proposed
evidence: architecture-research
aliases:
  - Blender workflow distillation
  - Blender recipe compiler
  - MCP workflow recorder
  - AI asset recipes
---

# Blender Workflow Distillation — MCP workflow to deterministic recipe

**Date:** 2026-11-19  
**Status:** Architecture research / proposal. No implementation has been performed.  
**Scope:** Blender workflows authored by AI agents through Blender MCP: textures/materials, trees, vehicles and other reusable assets.  
**Outcome:** Capture an AI-authored Blender workflow, inspect the actual Blender state changes, and compile them into a versioned, parameterized recipe that can reproduce the asset family locally without an LLM or MCP.

## 1. Executive summary

The intended system is not another general-purpose procedural world generator and not a macro recorder. It is a workflow distillation pipeline:

1. An AI agent creates or edits an asset in Blender through the existing MCP workflow.
2. A recorder captures tool-level commands and Blender-side state changes.
3. A distillation step identifies stable operations, parameters, dependencies, generated resources and incidental/editor-only actions.
4. A recipe compiler converts the useful workflow into a declarative recipe plus deterministic Python/Geometry Nodes implementation.
5. The recipe runs locally from a small configuration and seed. No AI call is needed for ordinary production.
6. The same recipe can produce variants, batch assets, and feed higher-level generators such as forests, biomes, settlements and vehicle fleets.

The LLM remains an authoring and improvement tool. It is removed from the repeatable production path.

**Core distinction:** recording commands is not distillation. A replayable trace is an exact replay artifact; a recipe is a reusable generative program. The compiler must expose meaningful parameters and separate invariant construction logic from per-instance variation.

This fits What If's existing direction: deterministic seed-based generation, independent stage RNG, content-addressed outputs, Unity-free generation stages, sector-local caching and measurable validation. The Blender system should reuse those principles without coupling the neutral recipe format to Blender.

## 2. Problem and target transformation

### Current workflow

Prompt → AI reasoning → Blender MCP calls → Blender scene edits → final .blend / texture / model.

The same class of asset repeatedly requires an agent to rediscover and execute a long sequence of operations. Workflow knowledge remains implicit in the transcript and final scene.

### Target workflow

AI + MCP → captured trace + scene diff → distilled recipe → deterministic generator → outputs.

For subsequent production:

recipe_id + recipe_version + seed + parameters + generator_version → asset.

### What can and cannot be represented as text

Much of Blender's authoring state is structured data:
- Python statements and MCP tool arguments;
- object, mesh, curve, material and image datablocks;
- shader and Geometry Nodes graphs;
- modifier stacks and their inputs;
- transforms, collections, attributes, UV maps and material assignments;
- scene/render settings and export parameters.

These can be captured, serialized, inspected and rebuilt.

But a prompt or trace does not necessarily contain all information needed to recreate a visual result:
- an AI-generated bitmap requires the bitmap itself or the original model/weights, inference settings and reproducible inference environment;
- sculpted or manually edited geometry requires the resulting geometry or an equivalent procedural construction;
- external textures, fonts, HDRIs, models and add-ons require stable references and license/provenance data;
- simulation/bake results may depend on caches, solver versions, frame ranges and execution details.

Preserve source recipe plus required immutable payloads. Do not pretend every visual result can be reconstructed from natural-language text alone.

## 3. Architecture

### 3.1 Components

| Component | Responsibility | Output |
|---|---|---|
| MCP Trace Adapter | Capture each MCP request, arguments, response, timing, tool/version and correlation ID | Append-only command trace |
| Blender Session Recorder | Capture before/after state and datablock changes | Scene delta / snapshots |
| Asset Inspector | Inspect scene, node graphs, modifiers, geometry, images and dependencies | Normalized asset manifest |
| Workflow Normalizer | Remove UI noise, normalize names/units/order, identify repeated structures | Canonical operation graph |
| Recipe Compiler | Convert approved operation graph into recipe schema and generator | Recipe package |
| Recipe Runtime | Execute recipe in Blender background or interactive session | Asset outputs |
| Validator | Compare deterministic outputs, structural invariants and visual QA | QA report and hashes |
| Cache / Artifact Store | Store immutable dependencies and outputs by content hash | Reusable cache |

### 3.2 Capture at two levels — both are mandatory

**Level A — MCP trace (what the agent asked for):**
- tool name and schema/version;
- exact arguments and defaults;
- call order and dependency/correlation IDs;
- result, error, retry and timing;
- AI task identifier and candidate recipe ID;
- referenced files/resources.

**Level B — Blender state (what actually happened):**
- datablocks created, removed or modified;
- stable IDs and names, not transient list indices;
- object transforms, mesh/curve data and attributes;
- material node trees, socket values, links and image references;
- Geometry Nodes groups, interfaces, links and modifier input values;
- modifier stack and relevant settings;
- UV layers, material slots, collections and parenting;
- render/bake/export settings;
- external resource paths and content hashes;
- evaluated geometry summary where modifiers/instances affect final output.

MCP logs alone are insufficient: an operator can be context-sensitive, defaults can change, and the same call can produce different results depending on active object, mode, selection, scene, Blender version or add-on state. A scene diff alone does not explain intent or sequence. Correlating both is the foundation of trustworthy distillation.

### 3.3 Recorder implementation strategy

Prefer a thin adapter around the existing MCP server/tool dispatch, not a replacement MCP server. It should write structured JSONL events to a session directory and forward the original request/response unchanged.

Add a Blender-side recorder add-on or injected helper:
- assign stable record IDs to created datablocks where safe;
- snapshot relevant datablock properties before and after a tool call;
- use depsgraph_update_post as a signal to schedule a controlled snapshot, not as the sole source of truth;
- capture explicit checkpoints after each MCP call and at finalization;
- record Blender version/build, Python version, enabled add-ons and render engine;
- avoid expensive full evaluated-mesh extraction after every operation; use tiered snapshots and full final inspection.

Use bpy.data for direct datablock inspection and mutation. Use bpy.ops only where an operator is genuinely needed; operators can be context-sensitive and may return CANCELLED rather than raising a useful exception. Use evaluated dependency-graph data when the final evaluated result (modifiers/instances) matters. Handlers must be used carefully: Blender documents thread-safety hazards when changing data during rendering.

Do not assume every MCP implementation exposes stable command middleware. Inspect the installed server and tool schemas first; the adapter contract should be implementation-specific, with a generic event envelope.

## 4. Distillation: from trace to recipe

### 4.1 Three artifacts, three purposes

1. **Trace** — immutable evidence of what happened. It can be verbose and context-specific.
2. **Replay script** — executes the same sequence against a controlled Blender scene. Useful for debugging and exact reproduction.
3. **Recipe** — compact, parameterized, versioned generator. This is the distributable product.

Do not collapse these into one file. A recipe should not contain transient UI clicks, selection assumptions, arbitrary generated names or hard-coded paths to temporary files.

### 4.2 Normalization pipeline

1. Parse tool calls and resolve the exact Blender operations they invoked.
2. Correlate each operation with before/after scene deltas.
3. Build a dependency graph: images/materials → object geometry → modifiers/instances → exports.
4. Classify operations:
   - constructive: creates reusable geometry/material/node structures;
   - parameter-setting: changes a meaningful design parameter;
   - instance variation: seed-driven variation such as branch angle or roughness;
   - presentation-only: camera, lights, viewport, render framing;
   - transient/editor-only: selection, active editor, UI context;
   - external dependency: image/model/font/HDRI/add-on;
   - bake/cache: computed payload that may need preservation.
5. Identify repeated patterns across multiple successful AI sessions, not from one example only.
6. Promote candidate parameters into a typed recipe interface.
7. Replace incidental randomness with explicit named RNG streams.
8. Generate a clean recipe implementation and test it from an empty scene.
9. Compare compiled result with source scene and approve the recipe.

### 4.3 Parameter inference

Do not ask an LLM to blindly convert a transcript into a script and trust the result. Use structured evidence:
- repeated values across variants are likely invariants;
- values changed intentionally between variants are candidate parameters;
- values that vary without affecting requested design may be noise;
- repeated node subgraphs are candidates for reusable node groups;
- repeated object/mesh structures are candidates for functions/templates;
- editor preparation operations should be removed.

AI can propose semantic names and parameter groupings, but the compiler/validator must prove that the generated recipe runs and meets the output contract. Human/agent approval is appropriate before promoting a recipe into the shared library.

## 5. Recipe package and schema

A recipe package should contain:
- recipe.json — declarative identity, inputs, outputs, dependencies and generation contract;
- generator.py — deterministic Blender-side implementation or generated node-group builder;
- assets/ — only immutable external payloads that cannot be procedurally recreated;
- tests/ — deterministic, structural and visual regression checks;
- provenance.json — source trace IDs, source asset licenses, authorship and hashes;
- optional .blend template for complex reusable node groups, not as the only source of truth.

Illustrative recipe header:

~~~json
{
  "schema": "whatif.blender.recipe/v1",
  "recipe_id": "vegetation.oak",
  "recipe_version": "1.0.0",
  "generator": {
    "id": "blender_recipe_runtime",
    "version": "0.1.0",
    "blender_min": "4.5"
  },
  "determinism": {
    "seed_algorithm": "sha256-counter-v1",
    "streams": ["geometry", "foliage", "materials", "placement"]
  },
  "inputs": {
    "seed": {"type": "uint64", "default": 1},
    "height_m": {"type": "float", "min": 2.0, "max": 35.0, "default": 12.0},
    "crown_density": {"type": "float", "min": 0.1, "max": 1.0, "default": 0.8}
  },
  "outputs": [
    {"id": "oak_mesh", "type": "mesh"},
    {"id": "oak_material", "type": "material"}
  ],
  "dependencies": [],
  "qa": {
    "structural": true,
    "visual_reference": "tests/reference/oak.png"
  }
}
~~~

The recipe JSON is a contract, not necessarily a complete description of every Blender node. Complex node graphs can be built by Python from a versioned graph specification or loaded from a versioned .blend asset library and parameterized through public group inputs. Avoid serializing unstable internal socket identifiers without a compatibility layer.

## 6. Determinism and reproducibility contract

What If WorldGen already establishes a useful precedent: named stage RNG, stable hashes, deterministic outputs and regression tests. Reuse the pattern, not necessarily the exact C# implementation.

### 6.1 Seed derivation

Never use Python's built-in hash() for persistent seeds; its output is not a stable cross-process serialization contract. Use a specified stable hash (e.g. SHA-256 over canonical bytes) and a specified PRNG implementation.

Derive independent streams:
- geometry
- branching
- foliage
- materials
- decals
- placement
- bake_noise

Derive sub-seeds from canonical encoding of (world_seed, recipe_id, recipe_version, asset_instance_id, stage_id). A change in foliage logic must not reshuffle the geometry stream.

### 6.2 Levels of reproducibility

- **R0 — semantic:** same recipe/seed gives same intended design and structural constraints.
- **R1 — same environment:** same Blender build, platform, renderer and dependencies produce identical canonical data where supported.
- **R2 — cross-machine structural:** same topology/parameters/attributes within declared tolerances.
- **R3 — bitwise render:** identical pixels across machines and GPU/CPU backends.

Do not promise R3 for general Blender renders. Floating-point behavior, GPU drivers, render backends, denoising and Blender versions can change pixel output. Make canonical geometry/material data hashes the primary deterministic gate; use perceptual image comparisons for rendered appearance.

### 6.3 Canonical hashing

Hash normalized values, not raw .blend bytes. Canonicalization should define:
- stable object ordering by recipe path/ID;
- units and coordinate convention;
- float quantization/tolerance policy;
- vertex/edge/face ordering or a canonical mesh representation;
- node types, links, socket values and group interfaces;
- material assignments and image content hashes;
- dependency manifest.

A .blend file may be a useful cache/template but is not a reliable semantic hash by itself.

## 7. Asset-class strategies

### 7.1 Procedural textures and materials

Best first pilot because shader graphs are explicit and parameter-rich.
- Serialize/build shader node graphs from a stable node specification.
- Expose scale, roughness, color palette, bump amplitude, tiling, moisture, wear and macro variation as typed inputs.
- Use named deterministic streams for stochastic noise and placement.
- Generate PBR maps by deterministic shader rendering/baking when required by target engine.
- Keep source graph and baked outputs; export base color, normal, roughness, metallic, AO/height as required.
- Record color management, UV/world-space mapping, resolution, bit depth, color space and bake settings.
- Treat externally AI-generated texture images as immutable payloads unless their inference model and environment are part of reproducible runtime.

### 7.2 Trees and vegetation

Represent the asset as a parameterized construction graph:
- trunk profile and taper;
- branching grammar/levels;
- branch distribution and collision/avoidance;
- leaf/needle clusters and density;
- species-specific shape priors;
- seasonal/material parameters;
- LOD generation and billboard/impostor policy.

Use Geometry Nodes for reusable field/instance operations and Python for orchestration, stable seed derivation, asset metadata, validation and export. Do not make one large node graph the only representation: keep recipe contract readable and expose a compact, stable public interface.

### 7.3 Vehicles

Vehicles are less likely to become fully procedural from one trace. Distinguish:
- base authored topology and design-specific geometry;
- reusable construction operations (wheels, glass, lights, interior modules, materials);
- parameterized variants (paint, trim, wheel selection, ride height, damage, decals);
- immutable design-specific payloads.

The recipe may be a parametric assembly recipe over source components rather than a generator that invents an entire car from a seed. Preserve authored mesh payloads where geometry is unique; proceduralize repeated subassemblies and material/variant production.

### 7.4 Buildings and world props

Use a grammar or component assembly recipe where repeated patterns exist (walls, windows, roofs, doors, floors, trim). Keep semantic dimensions and style parameters separate from low-level mesh operations. Preserve unique landmark geometry as source assets.

## 8. Runtime and distribution

The recipe runtime should support:
- Blender GUI execution for development and inspection;
- Blender background/headless execution for batch generation;
- optional bpy Python module embedding later, after version/platform testing;
- export adapters for Unity and other consumers.

Blender is the authoring/build backend, not the required runtime for the shipped game. The neutral recipe contract should be separable from Blender-specific implementation:
- recipe inputs and stable seed derivation are engine-neutral;
- Blender generator implements the recipe;
- future Unity/C# runtime can implement compatible recipe subsets;
- baked or generated payloads are cache outputs, not necessarily distributed with every seed.

### Cache key

SHA256(canonical(recipe_id, recipe_version, generator_build, blender_build, seed, normalized_inputs, dependency_hashes, export_profile)).

A change to generator code, Blender build, dependency payload or export profile must invalidate the relevant cache. Avoid invalidation of unrelated recipe families.

### Expected size reduction

The blueprint itself can be small, but the total distributable package is not always just a few KB:
- recipe/config and seed: usually small;
- generator modules and shared node libraries: shared once per application/version;
- immutable textures/models or AI-generated source images: may remain large;
- generated output: can be cached locally or distributed selectively.

Savings are greatest when many worlds reuse the same generator and dependencies, and when unique assets are replaced by procedural variants. Measure package size and generation time per asset family; do not claim a fixed GB saving before the pilot.

## 9. Blender/MCP integration details and risks

1. **MCP is a transport/control boundary, not recipe runtime.** Record requests/responses at server dispatch and state changes inside Blender.
2. **Tool schema drift:** save exact tool schema/version and Blender build with each trace.
3. **Context sensitivity:** operators depend on mode, selection, active editor and context. Normalize to direct data API where practical; explicitly record required context otherwise.
4. **Non-idempotent calls:** assign operation IDs; detect duplicate retries; record whether a call completed before retry.
5. **External dependencies:** resolve paths to content-addressed assets; never silently depend on a local absolute path.
6. **Generated images:** preserve actual image payload and provenance. Prompt text alone is not a texture generator.
7. **Simulation/bakes:** preserve cache data or encode all solver inputs, version, frame range and deterministic execution requirements.
8. **Blender API versioning:** node socket names/types and data API evolve. Pin a Blender baseline and add explicit migrations.
9. **Recorder overhead:** use checkpoints and selective diffs; full evaluated geometry after every operation is wasteful.
10. **Security:** traces may contain local paths, prompts, credentials accidentally passed in tool arguments or private assets. Redact secrets and keep raw traces local by default; publish only sanitized recipes.
11. **Licensing/provenance:** capture origin/license per external asset and derived payload. A recipe does not erase source asset license obligations.
12. **Threading:** keep Blender data API work on Blender's supported execution path; do not mutate bpy datablocks from arbitrary Python threads.

## 10. Validation strategy

A recipe is promoted only when it passes applicable gates.

### Structural
- runs from an empty scene in a clean Blender process;
- no hidden dependence on selection, active object, UI area or prior session state;
- same inputs produce same canonical geometry/material hashes in the pinned environment;
- output object names/IDs, dimensions, transforms, topology, UVs and material slots satisfy contract;
- all dependencies resolve by hash;
- no unexpected external file references.

### Visual
- fixed camera/light/render profile;
- reference render plus perceptual comparison (not raw pixel equality across arbitrary hardware);
- material-map inspection and color-space validation;
- LOD silhouette and shading checks where relevant.

### Performance
- wall time, peak memory, output size and bake/render cost;
- batch throughput for 100/1,000 variants;
- cache hit/miss timings.

### Regression
- golden recipe input set;
- generator version change requires explicit migration and re-baseline;
- seed isolation test: changing one stage must not alter unrelated streams;
- repeated run in fresh Blender processes.

## 11. Recommended MVP: Texture Recipe Distiller

Do not start with trees, cars and all asset types simultaneously. Start with a procedural material because its graph is inspectable and its parameter space manageable.

### MVP scope

**Capture**
- instrument existing Blender MCP calls;
- record tool schemas, arguments, results and per-call IDs;
- Blender-side snapshots of material/node-tree changes;
- final material/image dependency manifest.

**Compile**
- support a limited set of node types used in current workflow;
- convert graph to canonical node spec;
- identify candidate public parameters;
- emit recipe.json + generator.py;
- preserve original graph as source artifact.

**Run**
- create a fresh Blender scene;
- load recipe and seed;
- construct material;
- render/bake target maps;
- export manifest and hashes.

**Acceptance**
1. Capture one current AI-authored texture workflow end-to-end.
2. Compile it without hand-editing the generated recipe (manual approval allowed).
3. Run recipe twice in fresh Blender processes with same seed.
4. Structural node graph hash and baked output are stable in pinned environment.
5. Change selected exposed parameters and seed; obtain controlled, plausible variants.
6. Demonstrate no LLM/MCP calls during recipe runtime.
7. Record actual recipe package size, shared dependency size, bake time and output size.

### Follow-on pilots
- tree recipe: procedural trunk/branch/foliage with LOD;
- building recipe: modular facade/roof assembly;
- vehicle recipe: material and modular-part variants before attempting whole-car synthesis.

## 12. Proposed repository layout

~~~text
research/
  blender-workflow-distillation.md

src/
  WhatIf.Blender.Recipes/
    schema/
    compiler/
    runtime/
    validation/

tools/
  blender-recorder/
  blender-recipe/
    compile.py
    run.py
    inspect.py
    validate.py

recipes/
  materials/
  vegetation/
  architecture/
  vehicles/

tests/
  blender-recipes/
    fixtures/
    golden/
~~~

This is a proposed layout, not an instruction to create all these directories now. The first implementation can live in tools/blender-recipe/ and recipes/materials/; promote to a library only after the MVP proves the contract.

## 13. Implementation sequence

| Stage | Deliverable | Exit condition |
|---|---|---|
| D0 — workflow audit | Inspect actual installed Blender MCP server, tools and current AI asset workflows | Exact capture points and tool schemas known |
| D1 — recorder | MCP JSONL trace + Blender scene checkpoints/diffs | One existing workflow captured sufficiently to diagnose |
| D2 — inspector | Material/node graph and datablock manifest | Final scene described without relying on transcript |
| D3 — replay | Controlled replay harness in clean Blender | Same workflow replayed and diagnosed |
| D4 — recipe schema | Versioned recipe contract and seed/RNG rules | Human-readable recipe expresses pilot |
| D5 — compiler | Trace + diff → candidate recipe and generator | Compiled recipe passes clean-scene run |
| D6 — QA/cache | Structural hashes, visual references, content cache | Repeatable batch generation and regression |
| D7 — asset families | Trees, buildings, vehicle components | Shared reusable recipe library |

**Critical ordering:** do not build recipe compiler before D0–D2. The current MCP implementation and actual tool call patterns have not yet been inspected in this research. Designing against imagined MCP calls risks recording the wrong abstraction.

## 14. Open questions for implementation agent

1. Which Blender MCP server/package and exact version is currently installed? Local custom server or third-party implementation?
2. Are MCP calls primarily natural-language execute_blender_code, discrete tools, or both?
3. Does the server execute arbitrary Python in Blender, and can dispatch be wrapped without forking?
4. Which current workflow is best first capture: procedural material, texture bake, tree or vehicle?
5. Which Blender version and render engine are pinned for current work?
6. Which generated textures come from shader graphs versus external AI image generation?
7. Are current .blend files and outputs stored in a shared asset library with provenance?
8. Which target outputs are immediately required: .blend, FBX, glTF, Unity-ready textures, or combination?

These are implementation discovery questions, not blockers to architecture. The MVP should begin with current texture workflow once D0 identifies exact execution path.

## 15. Decisions proposed (not yet accepted)

- **P1:** Build a generic recorder and recipe compiler, not a texture-only generator.
- **P2:** Capture both MCP command traces and Blender-side scene deltas.
- **P3:** Keep trace, replay and recipe as separate artifacts.
- **P4:** Use JSON as recipe contract and Python/Geometry Nodes as Blender execution backend.
- **P5:** Use named deterministic RNG streams and stable seed derivation; never Python hash().
- **P6:** Start with material/texture workflow as MVP; expand to vegetation, buildings and vehicle assemblies.
- **P7:** Keep Blender optional at shipped runtime; treat Blender as authoring/build adapter.
- **P8:** Preserve immutable payloads when original result is not derivable from procedural instructions (AI-generated images, unique meshes, licensed source assets).
- **P9:** Do not claim bitwise render determinism across arbitrary machines; define structural determinism and visual tolerances separately.
- **P10:** Do not alter existing Planetary Engine / WorldGen architecture until a tested recipe pilot demonstrates value.

## 16. Evidence and references

### Repository evidence (inspected 2026-11-19)
- wiki/entity/worldgen.md: existing WorldGen is a pure deterministic library; spec + seed + rules; named SplitMix64 stage RNG; golden hashes; Unity adapter boundary.
- wiki/concept/worldgen-vegetation-buildings.md: existing scatter stage is a clean function with stage-specific RNG, sector-local output and measured validation; geometry is currently procedural low-poly, with explicit performance constraints.
- wiki/concept/worldgen-materials.md: current Unity material direction includes layered splat, world-space UV, texture maps and measured GPU cost; useful consumer requirements for exported materials.
- wiki/concept/what-if-planet-engine.md and wiki/entity/what-if-planet-runtime.md: sector streaming, content/cache separation and runtime constraints. Asset recipes should feed content generation without becoming coupled to planet streaming internals.
- wiki/concept/workflow-tools.md: repo expects agents to discover existing tools before writing one-off scripts.
- wiki/concept/agent-harness-traps.md: avoid long unbroken agent loops; use bounded background work and concise handoffs.

### External technical references
- Blender 4.5 Python API: https://docs.blender.org/api/4.5/
- Blender Python quickstart / data access: https://docs.blender.org/api/current/info_quickstart.html
- Blender operators and context/return behavior: https://docs.blender.org/api/current/bpy.ops.html
- Blender dependency graph and evaluated instances: https://docs.blender.org/api/4.5/bpy.types.Depsgraph.html
- Blender application handlers and thread-safety warning: https://docs.blender.org/api/4.5/bpy.app.handlers.html
- Blender Geometry Nodes modifier inputs: https://docs.blender.org/manual/en/5.1/modeling/modifiers/geometry_nodes.html
- Blender node groups as reusable, nested and parameterized functions: https://projects.blender.org/blender/blender-manual/src/branch/main/manual/interface/controls/nodes/groups.rst
- Blender as a Python module / automation: https://docs.blender.org/api/main/info_advanced_blender_as_bpy.html

External docs were consulted for API capabilities and risks. Exact behavior must be verified against the Blender version installed in the project.
