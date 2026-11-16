# Unity import profile

## Scope

Unity is the presentation and interaction client, not the authoritative simulation runtime. Asset import settings must not introduce authority or gameplay semantics into the content catalog.

## Supported interchange

| Content | Preferred | Accepted alternatives | Notes |
| --- | --- | --- | --- |
| Static/moving meshes | FBX | glTF/GLB, OBJ | Pin glTF importer version; OBJ is limited for complex materials/rigs. |
| PBR textures | PNG, TIFF, EXR | JPEG for lossy photographic color maps | Keep source maps and record color-space semantics. |
| Environment maps | EXR, HDR | — | Linear/HDR workflow. |
| Material data | source maps + manifest | glTF metallic-roughness | Generate Unity materials per target render pipeline. |

## Texture profile

- Default target for general-purpose surface assets: 2048 × 2048.
- Keep native resolution and source files; generate platform-specific derivatives during build/import.
- Base Color uses sRGB; data maps (normal, roughness, metallic, AO, height) use linear sampling.
- Use normal-map import type for tangent-space normal maps and verify handedness/convention.
- Use mipmaps for world surfaces and most 3D objects; choose anisotropic filtering for oblique terrain/road views.
- Prefer BC/ASTC platform compression according to target hardware; do not hard-code one compression profile into source metadata.
- Record channel packing (e.g. ORM) explicitly.

## Materials

The catalog stores source material facts, not a single Unity Material as the canonical representation. Build URP/HDRP/Built-in materials from source maps through project-owned import tooling. Preserve roughness/metallic semantics and document any conversion to Unity smoothness.

## Models

- Set scale and axis conversion explicitly; use a project-wide unit convention.
- Verify pivots, normals, tangents, UVs, material slots and animation/rig data.
- LODs should be generated/tested for planetary viewing distances. Keep source and generated LODs separate.
- Collision geometry is a separate derived asset and should be intentionally simplified.
- For glTF/GLB, use a pinned, project-approved importer such as glTFast only after compatibility and license checks of the importer itself.

## Environment and water

HDRIs are lighting/environment inputs, not a substitute for a sky/atmosphere model. Planetary atmosphere, clouds, ocean depth, wave motion, foam and reflections are runtime shader/simulation concerns. Catalog water normal/foam/shoreline maps as inputs; do not treat a flat water texture as a complete water system.

## Required import test

A new asset is not marked `unity.tested=true` until imported in a representative project test scene and checked under the stated render pipeline. Record Unity version, importer/package version and any conversion settings in the test evidence.
