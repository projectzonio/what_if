---
id: KB-BLENDER-DESTRUCTION-007
type: asset-and-runtime-architecture
domain: persistent-world-destruction
status: research-baseline
tags: [destruction, crysis, cryengine, trees, buildings, fracture, boolean, physics, persistence, blender, unity]
sources: [SRC-CRYENGINE-PHYSICAL-BREAKAGE, SRC-CRYENGINE-BOOLEAN-DESTRUCTIBLES, SRC-CRYENGINE-BREAKABLE-TREES, SRC-CRYENGINE-BREAKABLE-OBJECTS, SRC-CRYENGINE-PHYSICS]
---

# Destructible world assets — trees and buildings

## 1. Objective

Planetary Engine should support environmental damage that changes the persistent world: trees can be shot, cut, blasted, toppled and left as stumps/logs; buildings can lose windows, facades, walls, roofs and structural sections; debris can settle, be cleared or remain as a saved state.

Blender is responsible for authored geometry, fracture variants, cut surfaces, collision proxies, material IDs, pivots and damage metadata. The runtime destruction system owns impact evaluation, structural state, physics, debris budgets, replication, persistence and streaming. Do not attempt to implement persistent destruction solely as a Blender modifier or one-off animation.

## 2. CryEngine / Crysis findings

### 2.1 Crysis vegetation

[VERIFIED] Tiago Sousa's GPU Gems 3 chapter says Crysis vegetation combined procedural breaking and physics interaction with shading, GPU procedural animation and distant sprite generation. It documents global/local wind and detailed bending. This confirms that vegetation interaction was treated as part of the renderer/physics design, not only as static art.

### 2.2 Boolean breakable trees

[VERIFIED] Current CRYENGINE documentation describes Boolean-breakable trees as dynamic procedural breaking at the impact point. A Boolean cutter mesh is subtracted from the trunk's render and physics mesh; tree parts are separated and foliage patches can be assigned to the resulting broken pieces. The cut is not restricted to a pre-authored fixed split location.

The documentation also exposes the practical cost: Boolean destructibles need simple source and cutter geometry, and repeated simultaneous operations can cause noticeable performance degradation. CryEngine recommends low-poly breakable assets, sparing use, limiting the number of Boolean operations and controlling which surface types can trigger them.

### 2.3 General physical breakage systems

[VERIFIED] CRYENGINE documents four breakability systems:
1. breakable structural joints between physical parts;
2. Boolean subtraction for dynamic holes/cuts;
3. procedural triangulation for suitable flat objects;
4. cloth slicing.

It separately documents deformable objects. With breakable joints, impulses from bullets, collisions and explosions propagate through the connected parts. Joints whose thresholds are exceeded break; connectivity is recomputed and disconnected parts become new rigid bodies. Events report part creation/removal/reveal and broken joints.

[VERIFIED] CRYENGINE's breakable-object documentation distinguishes 2D destructible assets for thin/flat structures from 3D Boolean destructibles. It describes pre-fractured or tetrahedralized geometry and runtime carving, with newly separated pieces becoming physical objects. This is an engine-specific pipeline and should not be copied without profiling.

### 2.4 Evidence boundary

The current CRYENGINE 5 documentation demonstrates the engine family's documented capabilities and asset workflows. It is not proof that every exact feature or implementation was identical in the original 2007 Crysis build. The user's Crysis 1 gameplay observation is consistent with CryEngine's long-standing breakable-vegetation/destruction direction, but this knowledge base distinguishes the original game from later public documentation.

## 3. Destruction model taxonomy

Use the cheapest method that preserves the required gameplay outcome.

| Method | Description | Best use | Cost/risk |
|---|---|---|---|
| State swap | Intact asset replaced by damaged/broken variant | distant props, low-value vegetation | cheap, limited interaction |
| Authored fracture | Pre-cut pieces separated by joints | walls, roofs, large branches, tree sections | predictable; asset authoring cost |
| Procedural Boolean | Runtime cutter subtracts volume | trunk cuts, bullet holes, local blast holes | topology/physics update cost |
| Deformation | Mesh/vertex displacement without separation | dents, bent thin structures, foliage | topology remains connected |
| Structural graph | Parts linked by support/joint constraints | buildings, bridges, towers | needs stability/support solver |
| Hybrid | State swap + authored fracture + selective Boolean | recommended general solution | requires orchestration |

Do not use full dynamic fracture for every object. Reserve expensive runtime geometry changes for objects the player can interact with or that materially affect gameplay.

## 4. Tree asset workflow in Blender

### 4.1 Authoring structure

One destructible tree asset should contain:
- intact render mesh;
- trunk and major branch fracture sections;
- stump and exposed cut-surface material;
- optional Boolean-ready low-poly trunk collision mesh;
- foliage clusters bound to branch groups;
- branch attachment hierarchy;
- simplified collision capsules/convex hulls;
- break thresholds and material strength metadata;
- intact, leaning, fallen and stump states where useful.

### 4.2 Branch graph and break points

Use a graph:
- node = trunk/branch segment;
- edge = parent-child connection;
- attributes = length, radius, taper, wood class, health, stiffness, break impulse, foliage group, fracture plane.

Model the tree's hierarchy in Blender as named objects/collections or custom attributes. Every render part must map to a stable runtime segment ID. Use explicit fracture planes for major branch/trunk sections. For small twigs, use visual break/debris effects rather than separate rigid bodies.

### 4.3 Blender production steps

1. Generate or import the intact procedural tree.
2. Validate trunk and branch junction topology using the tree QA pipeline.
3. Mark structural segments and attachment points.
4. Create cut surfaces with closed caps and interior wood material IDs.
5. Generate simplified collision proxies per structural section.
6. Bind foliage clusters to segment IDs (not arbitrary vertex proximity at runtime).
7. Create fracture variants for high-value cuts and fallen states.
8. Create a low-poly Boolean proxy for dynamic trunk cuts if runtime tests justify it.
9. Export intact geometry, parts, proxies, pivots, metadata and LOD variants.
10. Validate that a cut/fall transition does not duplicate foliage, leave floating branches or lose the stump.

### 4.4 Runtime tree states

Suggested state machine:
- INTACT
- DAMAGED
- LEANING / PARTIALLY_BROKEN
- FALLING
- FALLEN
- STUMP
- REMOVED (only if world rules permit)

Use deterministic transitions. A bullet may damage a branch without felling the trunk; a sufficiently large impulse or cut may break a structural connection. Explosions apply an impulse field and damage to nearby segments, but the tree response depends on species, health, trunk diameter, moisture/decay class and impact location.

Avoid full per-leaf physics. GPU wind and local deformation handle foliage; physics is limited to trunk and major branches. When a major branch separates, its foliage cluster follows that branch as a rigid visual group.

## 5. Building asset workflow in Blender

### 5.1 Modular structural graph

Model buildings as components with explicit structural roles:
- foundation / ground anchors;
- columns and load-bearing walls;
- beams and floor slabs;
- roof trusses and roof panels;
- facade and cladding;
- doors/windows/glazing;
- interior partitions;
- stairs and secondary elements;
- utility systems where gameplay requires them.

Each component receives a stable ID, material class, mass estimate, collision proxy, structural role and attachment/support relations. A visual wall panel is not automatically load-bearing. The structural graph must be authored separately from render topology.

### 5.2 Fracture authoring

Use Blender to author:
- pre-fractured wall/slab/beam variants for common blast and impact cases;
- interior fracture surfaces with correct material IDs;
- debris classes (dust, small fragments, medium chunks, large structural parts);
- simplified collision meshes;
- pivot and attachment locations;
- intact/damaged/destroyed states for distant LOD;
- optional procedural fracture seeds for offline generation.

Avoid creating hundreds of independently simulated shards. Large fragments affect gameplay; small fragments can be short-lived particles, GPU debris or pooled cosmetic objects.

### 5.3 Damage and collapse states

A building should not be represented by one global health bar. Model damage by component and structural dependency:
- window/glazing broken;
- facade panel removed;
- wall breached;
- support column damaged;
- beam joint failed;
- floor/roof section collapsed;
- local compartment exposed;
- global collapse if support connectivity becomes invalid.

The runtime may use a simplified structural solver, threshold-based joint graph or pre-authored collapse recipes. Full finite-element analysis is not required for every gameplay building. Use a hybrid: physical support graph for major elements, authored collapse transitions for predictable outcomes, and debris physics only for selected parts.

## 6. Runtime destruction contract (engine-independent)

### 6.1 Inputs

A DamageEvent should contain:
- event_id and stable world object ID;
- source type (bullet, projectile, melee, explosion, collision, fire, weather);
- world-space impact point and direction;
- impulse/energy estimate and damage channels;
- radius/falloff for area events;
- instigator/authority ID;
- deterministic seed;
- simulation tick/time;
- material and damage-type context.

### 6.2 Outputs

A DamageResult should contain:
- new persistent object state/revision;
- changed component/segment IDs;
- detached rigid bodies and their initial transforms/velocities;
- created holes/cut geometry or selected fracture recipe;
- visual effects request;
- debris budget consumption;
- replication delta;
- save-state delta.

### 6.3 Processing stages

1. Resolve world object and authoritative revision.
2. Query broad-phase candidates in the blast/impact volume.
3. Evaluate material response and damage thresholds.
4. Propagate impulse/damage through structural graph.
5. Break joints or choose authored fracture recipe.
6. Apply Boolean cut only for eligible low-complexity assets.
7. Spawn limited rigid bodies and cosmetic debris.
8. Update navigation, cover, collision and interaction state.
9. Persist a compact delta against the immutable base asset.
10. Replicate state changes and reconcile clients.

### 6.4 Persistent world representation

Do not save every debris particle. Persist meaningful world changes:
- asset ID and revision;
- state/variant ID;
- broken segment/component bitset or sparse IDs;
- Boolean cut parameters or generated mesh reference if retained;
- transform/velocity only for debris that remains relevant;
- structural graph changes;
- timestamp/tick and deterministic seed;
- provenance of the damage event.

Use an immutable base asset plus sparse per-world-instance deltas. When a sector unloads, discard cosmetic debris and retain the persistent damage state. On reload, reconstruct the damaged state deterministically. Apply a debris lifetime/importance policy.

### 6.5 Planetary streaming

- Damage is owned by the persistent world cell/object identity, not by a transient Unity GameObject.
- Unloaded cells retain compact state deltas.
- On cell load, reconstruct intact base + deltas before enabling collision/gameplay.
- Avoid replaying every historical explosion; store resulting state or a bounded event log.
- LOD representations must derive from the same damage state.
- For multiplayer, one authority commits the state transition; clients receive versioned deltas. Do not assume floating-point rigid-body simulations will be bit-identical across machines.
- Large blast propagation across cells should be resolved by a bounded query/region event, not a scan of the entire planet.

## 7. Performance budgets and degradation

Use tiers:
- Tier A gameplay-critical: structural graph + major rigid bodies + optional Boolean.
- Tier B interactive: authored fracture + limited rigid bodies.
- Tier C ambient: state swap / shader damage / short-lived debris.
- Tier D distant: visual state transition only; no rigid-body simulation.

Cap:
- concurrent dynamic fragments per cell;
- active Boolean operations per frame;
- structural graph depth/parts per asset;
- lifetime and distance of small debris;
- number of persistent cut meshes;
- replication bytes per damage event.

When over budget, preserve the gameplay result but simplify visuals: collapse to authored state, merge debris, or discard low-priority fragments. Do not silently undo persistent damage.

## 8. Blender QA and export manifest

Every destructible asset should validate:
- stable segment/component IDs;
- watertight fracture caps where needed;
- correct interior material IDs;
- non-self-intersecting collision proxies;
- plausible mass/centre-of-mass metadata;
- valid attachment graph and no orphan components;
- correct pivots and local axes;
- LODs preserve the damage silhouette;
- export contains intact base and all referenced fracture assets;
- deterministic asset manifest and hashes.

Suggested manifest:

~~~yaml
asset_id: TREE-OAK-001
asset_type: destructible_tree
revision: 1
authoring_tool: Blender
segments:
  - id: trunk_lower
    parent: null
    role: trunk
    break_threshold: species_profile
    render_object: trunk_lower
    collision_proxy: trunk_lower_collision
  - id: trunk_upper
    parent: trunk_lower
    role: trunk
    render_object: trunk_upper
    collision_proxy: trunk_upper_collision
foliage_groups:
  - id: canopy_upper
    attached_to: trunk_upper
fracture:
  authored_variants: [fallen, stump]
  boolean_proxy: trunk_boolean_lowpoly
runtime_policy:
  destruction_tier: A
  persist_state_delta: true
~~~

Threshold values are resolved by the runtime/species profile; this manifest intentionally does not invent physical constants.

## 9. Validation scenarios

### Tree
- small-calibre hit damages bark/branch but does not always fell tree;
- repeated hits can sever a branch;
- trunk cut produces stump and detached upper tree;
- blast topples nearby trees with distance/direction falloff;
- foliage follows detached segments;
- unloaded/reloaded cell restores same stump/fallen state.

### Building
- window breaks without collapsing building;
- wall breach creates traversable opening;
- blast removes facade and damages nearby supports;
- support failure causes local floor/roof collapse;
- large fragments can block a doorway or provide cover;
- distant building switches to matching damaged silhouette;
- cell reload preserves damage and collision.

## 10. Evidence labels

- [VERIFIED] Current CRYENGINE docs describe Boolean breakable trees, breakable joints, Boolean destructibles and asset authoring requirements.
- [VERIFIED] GPU Gems 3 describes Crysis vegetation procedural breaking/physics interaction and GPU animation.
- [RECOMMENDATION] The PE DamageEvent contract, persistence delta, tiering and building structural graph are proposed architecture, not CryEngine implementation claims.
- [UNMEASURED] PE fracture budgets, part limits, solver costs and persistence sizes require benchmark scenes and target hardware.
