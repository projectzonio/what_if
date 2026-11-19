---
id: KB-BLENDER-TREE-002
type: production-workflow
domain: vegetation
tags: [geometry-nodes, procedural, skeleton, curves, sapling, mtree, python, bark, foliage]
entities: [WeberPennModel, LSystem, TreeSkeleton, BranchGraph, GeometryNodes, SaplingTreeGen, ModularTree]
---

# Procedural tree production

## Recommended architecture

Generate trees as hierarchical data, not as a monolithic mesh:

Species Profile → Branch Graph → Centerline Curves → Swept Branch Meshes → Junction Fusion → Bark/UV → Foliage → LODs → Validation/Export

Keep the branch graph as the authoritative authoring representation. Meshes are derived outputs and can be regenerated when branching, radius or LOD parameters change.

## 1. Species profile

Store growth architecture (monopodial leader, sympodial/dichotomous, multi-stem or palm-like), trunk height, root flare, crown dimensions/envelope, branch levels/density, insertion-angle distribution, curvature, taper, foliage type, leaf size/distribution, seasonal state, variation ranges and deterministic seed.

These are controllable modeling parameters, not a claim of full botanical simulation. Weber–Penn is a practical parameterized foundation; L-systems provide rule-based recursive growth. A 2023 survey classifies real-world tree skeleton modeling into procedural reconstruction, geometry-based extraction and image-based modeling.

## 2. Branch graph schema

Each branch node stores:
- branch_id and parent_branch_id;
- level (0 = trunk);
- attach_t (normalized location on parent curve, 0..1);
- deterministic seed;
- centerline samples or spline definition;
- local tangent/preferred growth direction;
- radius profile and curvature controls;
- child branch IDs;
- optional species, age, pruning, damage and deadwood metadata.

Invariants:
- one root trunk for a single-stem tree, or explicitly declared multiple roots;
- every non-root branch has one valid parent;
- no parent cycles;
- attachment parameter lies on parent;
- radius tapers toward tips;
- child centerline begins at parent surface;
- centerlines are continuous and nonzero length.

## 3. Geometry Nodes stages

A. Build or ingest branch curves.
B. Resample by length; add samples near high curvature and attachment regions.
C. Compute branch radius from level, normalized spline factor and species parameters.
D. Set Curve Radius.
E. Sweep radial profile with Curve to Mesh.
F. Add controlled radial irregularity only after stable base geometry.
G. Generate foliage points from branch curves or a crown distribution volume.
H. Instance leaf clusters with deterministic orientation and scale variation.
I. Expose LOD selection and species parameters as node-group inputs.

Resample Curve provides Count, Length and Evaluated modes. Set Curve Radius sets per-point radius interpolated between control points. Curve to Mesh sweeps an optional profile along splines. Mesh to Curve can extract edge chains from a mesh, but does not reconstruct semantic tree skeletons automatically.

Do not expect Curve to Mesh to fuse branch junctions. Many independent branch tubes are a valid intermediate, not necessarily a final close-up tree.

## 4. Junction strategy

| Strategy | Quality | Cost | Use |
|---|---|---|---|
| Intersecting swept tubes | Low close up | Low | distant/background LOD |
| Local implicit/voxel fusion | Good if controlled | Medium/high | close-up trunk and primary forks |
| Unified branching surface | Highest control | High authoring complexity | hero trees |
| Scan/example-based tree parts | Natural complexity | acquisition/cleanup cost | hero and near-field species |

For close-up assets, fuse trunk and primary branches first. Secondary branches may remain separate if their junctions are small and hidden by bark/foliage. Preserve branch identity metadata even when the final surface is fused.

## 5. Add-on evaluation

### Sapling Tree Gen

The current extension listing describes a parametric tree/shrub generator with leaves, blossoms, armature and wind. Version 1.0.0 (2026-11-04) is rewritten for Blender 5.2+, uses a live Geometry Nodes object, has editable bevel/resolution and 29 presets.

Use it for species prototypes, a parametric reference tree, controlled variation and comparison against AI-generated geometry. Do not assume default output passes hero-asset junction quality; inspect at close range.

### Modular Tree (MTree)

The Blender Extensions listing describes a node-based generator with trunk/branch/leaf parameters, multi-level branching, crown shapes and L-system growth. Evaluate maintenance, Blender compatibility, source license and export behavior before adoption. Candidate generator, not approved dependency.

### Blender Studio Geometry Nodes example

Blender Studio publishes a Geometry Nodes tree-generator example. Its compatibility note for Blender 4.5 says Curve to Mesh has a Scale input; connect radius setup to Scale (or use a Radius node). The documented Boolean setup requires Exact mode and Hole Tolerant for reliability. Useful learning asset, not universal production generator.

## 6. Bark and surface detail

Layer detail:
- macro: taper, buttress roots, large scars, knots and branch collars are geometry;
- meso: bark ridges/plates are geometry, displacement or normal maps depending on camera distance;
- micro: roughness, color breakup and fine normal detail belong in materials.

Avoid high-frequency bark displacement on a low-resolution or discontinuous base mesh; it amplifies defects. At junctions, use geometry-aware blending or dedicated junction material instead of stretching one UV island over the fork.

## 7. Foliage

The canopy must not be a uniformly filled green sphere. Build foliage clusters on secondary/tertiary branches with species-specific leaf silhouette, controlled overlap and negative space, nonuniform density, variation in rotation/scale/age, plausible leaf normals and separate near/far representations.

Distant LODs may use clustered cards or impostors. Near-field assets should preserve recognizable silhouettes without excessive overdraw.

## 8. Determinism and provenance

Store generator/version, Blender version, species profile ID/version, seed, full parameter snapshot, geometry processing steps, export format and transform conventions, and source/license provenance for scans or textures. A seed alone is insufficient when generator or node graph version changes.

## 9. Practical build order

1. Build one clean trunk and three primary forks.
2. Validate centerlines, taper and junction topology.
3. Add secondary branches and crown envelope.
4. Add tertiary twigs only to target camera requirement.
5. Add foliage clusters.
6. Add bark materials and UVs.
7. Generate LODs from same branch graph.
8. Run automated mesh and visual validation.
9. Export and inspect in target runtime.

See source registry for Blender documentation, add-ons and research.
