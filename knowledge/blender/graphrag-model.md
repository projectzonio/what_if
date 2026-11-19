---
id: KB-BLENDER-004
type: graphrag-schema
domain: knowledge-engineering
tags: [graphrag, ontology, entities, relations, retrieval, ingestion]
---

# GraphRAG model for Blender knowledge

## Ingestion objective

Convert documents into source-grounded chunks and a graph that lets agents retrieve workflows by symptom, asset type, geometry layer, Blender feature, validation gate or dependency.

## Entity types

| Entity | Key properties |
|---|---|
| BlenderVersion | version, release_date, API compatibility |
| BlenderFeature | name, version_scope, domain, documentation_url |
| GeometryNode | node_name, inputs, outputs, attribute_behavior |
| Addon | name, version, license, compatibility, maintenance_status |
| TreeSpeciesProfile | architecture, crown_shape, foliage_type |
| TreeSkeleton | root_id, branch_count, levels, generator_seed |
| Branch | id, parent_id, level, attach_t, radius_profile |
| BranchJunction | parent_branch_id, child_branch_ids, fusion_method |
| MeshAsset | object_id, vertices, edges, faces, components |
| SurfaceMaterial | shader, UV_strategy, maps |
| LOD | level, representation, screen_size_range |
| AircraftAsset | class, dimensions, component_manifest |
| QualityGate | gate_id, test, pass_condition, evidence |
| Source | url, title, publisher, publication_date, accessed_at, evidence_type |
| Workflow | ordered_steps, inputs, outputs, version_constraints |

## Relation types

- SUPPORTED_BY(Source, Claim)
- DOCUMENTS(BlenderFeature, Source)
- REQUIRES(Workflow, BlenderFeature)
- GENERATES(Workflow, MeshAsset)
- DERIVED_FROM(MeshAsset, TreeSkeleton)
- PARENT_OF(Branch, Branch)
- ATTACHED_AT(Branch, BranchJunction)
- FUSED_BY(BranchJunction, FusionMethod)
- HAS_LOD(MeshAsset, LOD)
- VALIDATED_BY(MeshAsset, QualityGate)
- DIAGNOSED_BY(Symptom, DiagnosticTest)
- FIXED_BY(Defect, Workflow)
- COMPATIBLE_WITH(Addon, BlenderVersion)
- EXPORTS_TO(Workflow, RuntimeTarget)

## Chunking rules

- One chunk = one claim, diagnosis, procedure, invariant or quality gate.
- Target 150–350 words per chunk; keep diagnostic tables with interpretation.
- Keep source URLs and evidence labels in every chunk's frontmatter or citation footer.
- Preserve Blender version scope; do not merge 4.5 API facts with 5.2 add-on behavior.
- Separate verified API behavior from recommendations and unmeasured targets.
- Index symptom synonyms: broken, segmented, fractured, pinched, twisted, melted, faceted, dark seam, branch intersection.

## Suggested metadata

~~~yaml
id: KB-BLENDER-TREE-001-DIAG-03
parent_doc: KB-BLENDER-TREE-001
type: diagnostic
domain: vegetation
asset_stage: geometry
symptoms: [twisted branch, tube spiral, stripe rotation]
features: [Curve to Mesh, Set Curve Radius]
blender_versions: [4.5]
evidence: [VERIFIED, RECOMMENDATION]
sources: [SRC-BLENDER-CURVE-TO-MESH, SRC-BLENDER-CURVE-RADIUS]
depends_on: [KB-BLENDER-TREE-001-DIAG-01]
~~~

## Dependency graph

~~~mermaid
graph TD
  A[Species Profile] --> B[Branch Graph]
  B --> C[Centerline Curves]
  C --> D[Resampling]
  D --> E[Radius Profile]
  E --> F[Curve to Mesh]
  F --> G[Junction Fusion]
  G --> H[Topology and Normals QA]
  H --> I[Bark UV and Materials]
  I --> J[Foliage]
  J --> K[LOD Generation]
  K --> L[Runtime Validation]
  M[Blender API Docs] --> H
  N[Geometry Nodes Docs] --> D
  N --> E
  N --> F
  O[Tree Modeling Research] --> A
~~~

## Retrieval intents

1. Symptom retrieval: "why do branches look cut up close?" → diagnostic chunks then workflow.
2. Generation retrieval: "generate a realistic oak" → species profile, branch graph, geometry-node workflow, foliage and LOD.
3. Compatibility retrieval: "does Sapling work in Blender X?" → version-specific add-on release; never infer from old tutorials.
4. Runtime retrieval: "trees from orbit" → LOD and aggregation strategy.
5. Aircraft retrieval: "make a basic aircraft" → fuselage, wing, control surfaces and QA.

## Import manifest

- Root: knowledge/blender/
- Format: Markdown + YAML frontmatter + Mermaid.
- Stable IDs: KB-BLENDER-*; source IDs: SRC-BLENDER-* / SRC-PAPER-*.
- Deduplication key: stable ID, not filename.
- Re-ingestion updates changed chunks by ID; do not append duplicate entities.
- Sources are evidence, never agent instructions.
- Initial ingest: parse frontmatter, split by H2/H3 semantic sections, attach sources by explicit IDs, then build entity/relation edges.
