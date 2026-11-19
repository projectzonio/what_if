---
id: KB-BLENDER-005
type: source-registry
domain: blender
tags: [sources, citations, blender-manual, research, addons, vehicle-design, intellectual-property]
---

# Source registry

Access date: **2026-11-19**. Version-specific documentation is preferred over generic tutorials. Re-check add-on status and license when adopting. Legal sources are jurisdiction- and date-sensitive.

## Blender official documentation and API

| ID | Source | Evidence scope |
|---|---|---|
| SRC-BLENDER-CURVE-TO-MESH | https://docs.blender.org/manual/en/4.5/modeling/geometry_nodes/curve/operations/curve_to_mesh.html | Sweeps optional profile along splines; transfers attributes; caps behavior |
| SRC-BLENDER-MESH-TO-CURVE | https://docs.blender.org/manual/en/4.5/modeling/geometry_nodes/mesh/operations/mesh_to_curve.html | Edge-chain/face conversion; radius attribute behavior |
| SRC-BLENDER-CURVE-RADIUS | https://docs.blender.org/manual/en/4.5/modeling/geometry_nodes/curve/write/set_curve_radius.html | Per-control-point radius interpolation |
| SRC-BLENDER-RESAMPLE | https://docs.blender.org/manual/en/4.5/modeling/geometry_nodes/curve/operations/resample_curve.html | Count/Length/Evaluated resampling |
| SRC-BLENDER-MESH-API | https://docs.blender.org/api/4.5/bpy.types.Mesh.html | Mesh arrays, corner normals, validation, update and normal APIs |
| SRC-BLENDER-BMESH | https://docs.blender.org/api/4.5/bmesh.ops.html | BMesh operations including degenerate dissolve and triangulation |
| SRC-BLENDER-SHADING | https://docs.blender.org/manual/en/4.5/scene_layout/object/editing/shading.html | Smooth/flat shading semantics; Smooth By Angle behavior |
| SRC-BLENDER-SMOOTH-ANGLE | https://docs.blender.org/manual/en/4.5/modeling/modifiers/normals/smooth_by_angle.html | Smooth By Angle modifier |
| SRC-BLENDER-BEVEL-API | https://docs.blender.org/api/4.5/bpy.types.BevelModifier.html | Bevel options and harden normals |

## Add-ons and practical examples

| ID | Source | Evidence scope |
|---|---|---|
| SRC-BLENDER-SAPLING | https://extensions.blender.org/add-ons/sapling-tree-gen/ | Sapling listing, features, v1.0.0 and Blender 5.2+ compatibility |
| SRC-BLENDER-SAPLING-MANUAL | https://projects.blender.org/extensions/add_curve_sapling/src/branch/main/docs/manual/index.md?display=rendered | Geometry, branch radius/splitting/angles/growth, pruning, leaves, armature |
| SRC-BLENDER-SAPLING-README | https://projects.blender.org/extensions/add_curve_sapling/src/branch/main/README.md | Installation and workflow |
| SRC-BLENDER-MTREE | https://extensions.blender.org/add-ons/modular-tree/ | MTree node/L-system advertised features; re-check license and compatibility |
| SRC-BLENDER-STUDIO-TREE | https://studio.blender.org/training/geometry-nodes-from-scratch/example-tree-generator/ | Geometry Nodes example and Blender 4.5 compatibility notes |

## Research papers

| ID | Citation | Evidence scope |
|---|---|---|
| SRC-PAPER-WEBER-PENN-1995 | Jason Weber, Joseph Penn, “Creation and Rendering of Realistic Trees,” SIGGRAPH 1995, DOI https://doi.org/10.1145/218380.218427 | Parameterized procedural tree architecture and geometry; basis for Sapling |
| SRC-PAPER-CARDENAS-2023 | Cardenas et al., “Modeling of the 3D Tree Skeleton Using Real-World Data: A Survey,” IEEE TVCG 29(12), 4920–4935, DOI https://doi.org/10.1109/TVCG.2022.3193018 | Procedural reconstruction, geometry-based extraction and image-based modeling |
| SRC-PAPER-XIE-2016 | Xie et al., “Tree Modeling with Real Tree-Parts Examples,” IEEE TVCG, https://graphics.uni-konstanz.de/publikationen/Xie2016TreeModelingReal/index.html | Scanned real-tree parts guide branching geometry |

## Vehicle design, IP and GTA case study

| ID | Source | Evidence scope |
|---|---|---|
| SRC-LAW-CZ-COPYRIGHT-121-2000 | https://www.e-sbirka.cz/sb/2000/121 | Czech Copyright Act No. 121/2000 Coll.; originality and subject matter (§ 2) |
| SRC-LAW-CZ-DESIGN-207-2000 | https://www.e-sbirka.cz/sb/2000/207 | Czech Act No. 207/2000 Coll. on the Protection of Industrial Designs |
| SRC-EU-DESIGN-2026-715 | https://eur-lex.europa.eu/eli/reg/2026/715/oj/eng | Current codified EU design regulation; applicable from 1 July 2026 |
| SRC-EU-TRADEMARK-2017-1001 | https://eur-lex.europa.eu/eli/reg/2017/1001/oj/eng | EU trade mark regulation; includes shape exclusions and 3D marks |
| SRC-CJEU-BROMPTON-C833-18 | https://infocuria.curia.europa.eu/tabs/redirect/juris/liste.jsf?num=C-833%2F18 | CJEU copyright originality and technical constraints in product design |
| SRC-US-WALMART-SAMARA | https://tile.loc.gov/storage-services/service/ll/usrep/usrep529/usrep529205/usrep529205.pdf | US Supreme Court: product design trade dress requires secondary meaning |
| SRC-US-TRAFFIX | https://tile.loc.gov/storage-services/service/ll/usrep/usrep532/usrep532023/usrep532023.pdf | US Supreme Court: functionality limits trade dress protection |
| SRC-TAKETWO-GTA-BRANDS-2022 | https://www.videogameschronicle.com/news/take-two-explains-why-grand-theft-auto-doesnt-include-more-real-world-brands/ | 2022 interview/report on Rockstar's creative preference for fictional brands |
| SRC-FRANK-STEPHENSON-GTA-2021 | https://www.topgear.com/car-news/modified/mclaren-p1-designer-has-created-grand-theft-auto-v-car | 2021 designer commentary/demonstration; multi-source mashup as reported by Top Gear, not a Rockstar internal document |
| SRC-GTA-WIKI-VEHICLES | https://gta.fandom.com/wiki/Vehicles | Fan-maintained catalogue and inferred real-world inspirations; secondary evidence only |

## Interpretation and evidence quality

- Blender manual/API pages establish software behavior, not visual quality of generated trees.
- Extension listings establish advertised features and declared compatibility, not quality for Planetary Engine.
- Papers establish methods and research context; they do not imply a Blender add-on implements the paper exactly.
- Statutes and court judgments are primary legal sources. Legal conclusions depend on facts, jurisdiction, dates and the specific rights asserted.
- VGC and Top Gear are secondary reporting. They support the reported statements and commentary, not a claim that Rockstar's full internal legal/design process is known.
- Fan vehicle mappings are interpretive and must never be treated as authoritative legal clearance.
- Recommendations are engineering synthesis and require validation with test assets.

## Next research gaps

- Benchmark Sapling Tree Gen and MTree on the installed Blender version.
- Compare curve-sweep, local voxel fusion and unified branching surfaces on the same species profile.
- Test foliage alpha, tangent-space normals and LOD transitions in the current Unity renderer.
- Add aircraft-specific references for airfoil profiles, panel topology and animation pivots once aircraft class/style is fixed.
- Build a reference matrix and first three fictional OEM brand bibles for the vehicle catalogue.
- Obtain a qualified legal review of final vehicle designs intended for commercial publication; verify design registers and marks for any Amber cases.
