---
id: KB-BLENDER-TREE-001
type: troubleshooting
domain: vegetation
tags: [tree, branches, trunk, mesh, topology, normals, ai-generation, close-up]
entities: [TreeSkeleton, BranchJunction, SweepMesh, CrossSection, SurfaceNormal, UVMap]
---

# Tree defect diagnosis: broken or deformed trunks and branches

## Symptom

AI-generated trees look plausible from a distance, but close inspection reveals trunks or branches that appear segmented, fractured, pinched, twisted, flattened, discontinuous or melted.

This is not one defect class. The cause can be the branch skeleton, curve-to-mesh sweep, topology, normals, UV/material projection or image-to-3D reconstruction. Diagnose in that order.

## 1. Branch network is not a coherent skeleton

**Signs:** abrupt branch endings; intersections that do not merge; tubes merely penetrate one another; trunk direction changes at isolated points; no plausible taper; repeated identical forks.

**Cause:** image-generative systems may optimize projected appearance rather than a connected botanical structure. A plausible silhouette does not prove that the underlying geometry is coherent.

**Test:** hide leaves and bark material. Display branch centerlines as curves or thin tubes. Trace trunk from root flare to leader and every primary branch back to its parent. Check explicit parent attachment and continuous centerlines.

**Fix:** create a hierarchical branch graph first. Store parent ID, branch level, attachment parameter along parent, local direction, length, curvature, radius profile and deterministic seed. Generate surface geometry from this graph instead of asking a mesh generator to invent all intersections at once.

## 2. Curve sampling is sparse or irregular

**Signs:** straight segments in curved branches, abrupt kinks, angular cross-sections, sudden radius jumps.

**Cause:** a poly curve with too few control points cannot describe curvature. Arbitrarily dense samples do not repair a defective centerline.

**Test:** show centerline and control points. Compare the same branch at progressively finer resampling.

**Fix:** resample by approximate arc length, then add samples where curvature or taper changes quickly. Use smooth spline interpolation for organic growth. Blender's Resample Curve node creates poly splines with approximately uniform spacing in Count/Length modes; Evaluated mode uses existing curve resolution. Resampling is not a botanical growth algorithm.

## 3. Curve-to-mesh sweep twists or pinches

**Signs:** branch spirals unexpectedly; circular section becomes elliptical; bark seam rotates; sharp corners near bends; missing caps.

**Cause:** sweeping a profile along a 3D curve requires a stable local frame. Rapid tangent changes, poor curve normals/tilt, insufficient samples and inconsistent profile orientation can create twist.

**Test:** use a neutral material and a high-contrast longitudinal stripe on the profile. Inspect the stripe through bends. Temporarily use a circular profile. If the stripe twists while the centerline is smooth, inspect curve tilt/orientation and sampling.

**Fix:** define a stable orientation policy per spline; control tilt intentionally; add samples around tight bends; use circular or near-circular sections for most branches. Curve to Mesh sweeps an optional profile along splines and transfers attributes. It does not create biologically correct branch junctions automatically.

## 4. Branch junctions are intersecting tubes, not fused wood

**Signs:** seams, dark creases, internal faces, star-shaped pinches or disconnected cylinders at a fork.

**Cause:** independently swept branches intersect but do not become one continuous manifold trunk. Smooth shading may conceal seams at a distance, but close lighting reveals them.

**Fix options:**
- Production default: fuse a local junction volume using controlled implicit/voxel remeshing, then retopologize or decimate carefully.
- Higher-control option: generate trunk and primary branch as a unified surface from a branching implicit field or generalized-cylinder method.
- Background option: allow intersecting tubes only when the junction is never visible at the target camera distance.

Do not blindly apply high-resolution voxel remesh to the whole tree. It can erase thin branches and bark relief. Fuse primary junctions locally using a resolution derived from the smallest branch radius that must survive.

## 5. Radius/taper profile is discontinuous

**Signs:** sudden thickness changes; thin branch joins thick trunk with blunt cylinder; branches look like pipes.

**Fix:** define radius as a smooth function along each branch. Use monotonic taper from base to tip and a smooth transition near the parent junction. Constrain child radii by parent radius and branch order. Avoid zero radius until the final endpoint because near-zero values can collapse faces.

Blender's Set Curve Radius node sets per-control-point radius and interpolates between points. Curve to Mesh uses curve radius/scale to control profile size; verify the actual node graph Scale input because versions/tutorial files can differ.

## 6. Normals and shading make valid geometry look broken

**Signs:** black wedges, abrupt lighting discontinuities, faceted patches or dark seams that move with light.

**Test:** neutral gray material; flat shading; face-orientation visualization; inspect winding and non-manifold edges. Recalculate normals only after topology is understood.

**Important:** Shade Smooth interpolates normals; it does not change polygon silhouette or repair disconnected geometry. Shade Auto Smooth adds a Smooth By Angle modifier that marks sharpness based on angles between neighboring faces. Shading is not a topology repair.

**Fix:** remove duplicate/degenerate faces; repair non-manifold junctions; correct winding; smooth curved bark surfaces while retaining deliberate sharp boundaries. Re-check after modifiers and export.

## 7. UVs and bark texture create false seams

**Signs:** wireframe is continuous but bark appears cut, stretched, mirrored or discontinuous.

**Test:** replace bark material with a uniform shader. If defect disappears, inspect UV islands, texture coordinates, tangent basis and material assignment.

**Fix:** unwrap each branch along its length with consistent texel density; hide seams on less visible sides; use object/world-space procedural bark for prototypes; bake to UVs for stable export. At forks, avoid stretching one trunk UV island across the junction—use local projection, vertex blending or dedicated junction material.

## Diagnostic table

| Observation | Likely layer | First action |
|---|---|---|
| Broken even as centerline | Skeleton/source | Rebuild branch graph and curves |
| Centerline smooth, tube angular | Sampling/profile | Resample and increase profile resolution |
| Stripe twists along tube | Sweep frame/tilt | Inspect curve orientation |
| Dark seam only at forks | Junction topology | Fuse junction or redesign attachment |
| Defect changes with light | Normals/shading | Inspect face orientation and normals |
| Defect vanishes with neutral material | UV/shader | Inspect UVs and tangent space |
| Close-up good, distance bad | Silhouette/LOD | Review crown structure and LOD |

## Automated quality gates

A Blender Python validator should report, not silently repair:
- vertex, edge, face and triangle counts;
- loose vertices/edges;
- zero-area faces and near-zero edges;
- non-manifold boundary edges and wire edges;
- connected component count for each expected trunk/branch object;
- inverted or inconsistent face winding where detectable;
- missing UV layer/material slots;
- dimensions, applied scale and origin;
- branch graph nodes with invalid parents, cycles, dangling IDs or disconnected components.

Mesh.validate() can correct/remove invalid mesh geometry and reports whether it changed the mesh. It is a final sanitation check, not a substitute for topology design. Use BMesh for flexible inspection and operations.

## Minimal reproduction protocol

1. Duplicate asset and preserve original.
2. Hide leaves, small twigs and bark textures.
3. Show branch skeleton.
4. Inspect trunk and primary forks close up.
5. Use neutral material and flat shading; then smooth shading.
6. Inspect topology and normals.
7. Re-enable bark and inspect UVs.
8. Compare against a controlled procedural baseline.

## Evidence

[VERIFIED] Blender documents that smooth shading changes normal interpolation, not geometry; Curve to Mesh sweeps a profile along curves; Set Curve Radius interpolates per-control-point radius.

[RECOMMENDATION] Skeleton-first generation, explicit junction fusion and staged diagnosis are production recommendations synthesized from geometry tools and tree-modeling literature. Validate on representative assets.
