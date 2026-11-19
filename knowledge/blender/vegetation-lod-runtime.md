---
id: KB-BLENDER-TREE-003
type: runtime-guidance
domain: vegetation
tags: [lod, impostor, instancing, planetary-scale, unity, performance]
entities: [VegetationLOD, BillboardImpostor, InstanceBatch, TreeSkeleton]
---

# Vegetation LOD and runtime strategy

## Objective

A hero tree near camera and a forest visible from aircraft/orbit cannot use the same geometry budget. Blender authors coherent source assets and bakes controlled representations; runtime chooses representations by projected size and performance.

## Representation ladder

| Level | Representation | Preserve |
|---|---|---|
| LOD0 | Full trunk, fused primary forks, secondary branches, detailed foliage | close silhouette, junctions, bark and leaf shape |
| LOD1 | Simplified branch graph, fewer twigs, clustered foliage | recognizable species silhouette |
| LOD2 | Trunk + major limbs + foliage clusters/cards | crown outline and color mass |
| LOD3 | Crossed cards or view-dependent impostor | projected crown silhouette and seasonal color |
| LOD4 | Aggregate canopy/biome representation | broad vegetation coverage, not individual identity |

These are suggested roles, not measured triangle budgets or fixed distance thresholds.

## Transition policy

- Use projected screen size and/or camera distance with hysteresis, not one brittle threshold.
- Cross-fade only if runtime supports it and overdraw is acceptable.
- Keep origin and height consistent across LODs.
- Preserve wind phase/direction coherently if animated.
- Keep collision representation independent from visual LOD.
- Compare crown outline from multiple azimuth/elevation angles.

## Instancing and planetary scale

Use shared meshes/materials and per-instance transforms/variation data where possible. Do not duplicate a unique full mesh for every tree. Store species, seed, age, health and seasonal state as instance metadata if runtime contract supports it.

At continental/orbital scales, individual trees should collapse into biome/canopy coverage or terrain appearance. Do not stream millions of individual tree meshes when camera cannot resolve them. Visual aggregation must not silently alter authoritative environment state.

## Blender authoring/export gates

- LOD0 and LOD1 derive from same species profile and branch graph.
- LOD2/3 preserve measured crown silhouette.
- Apply/record scale and rotation per import profile.
- Use consistent origin at ground/root contact.
- Keep collision proxy separate.
- Use shared material slots and atlased foliage where appropriate.
- Check alpha clipping/overdraw in target renderer.
- Validate normal/tangent maps after export.
- Inspect transitions in actual Unity client, not only Blender viewport.

No numeric polygon budgets or distance thresholds are prescribed. Establish them through benchmarks on target hardware and renderer.
