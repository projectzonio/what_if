---
id: KB-BLENDER-004
type: graphrag-schema
domain: knowledge-engineering
tags: [graphrag, ontology, entities, relations, retrieval, ingestion, vehicles, ip-risk]
---

# GraphRAG model for Blender knowledge

## Ingestion objective

Convert documents into source-grounded chunks and a graph that lets agents retrieve workflows by symptom, asset type, geometry layer, Blender feature, validation gate, legal risk or dependency.

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
| VehicleDesignBrief | vehicle_id, class, era, market, packaging, risk_status |
| FictionalManufacturer | manufacturer_id, design_language, market_position, naming_rules |
| FictionalVehicleModel | model_id, manufacturer_id, platform_id, body_style, design_revision |
| VehiclePlatform | platform_id, wheelbase_range, track_range, component_manifest |
| VehicleReference | source_url, access_date, reference_role, licence_status, provenance_only |
| VehicleDesignFeature | feature_id, type, geometry_description, originality_notes |
| IPRight | jurisdiction, right_type, owner, registration_id, term, status |
| SimilarityReview | asset_id, views_compared, reviewer, outcome, rationale |
| ProvenanceRecord | author, tool_version, prompt_version, source_manifest, hashes |
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
- DESIGNED_FOR(FictionalVehicleModel, VehicleDesignBrief)
- MANUFACTURED_BY(FictionalVehicleModel, FictionalManufacturer)
- BUILT_ON(FictionalVehicleModel, VehiclePlatform)
- INSPIRED_BY(VehicleDesignFeature, VehicleReference)
- DOCUMENTED_BY(VehicleDesignBrief, ProvenanceRecord)
- REVIEWED_BY(FictionalVehicleModel, SimilarityReview)
- POTENTIALLY_AFFECTED_BY(VehicleDesignFeature, IPRight)
- SUPPORTED_BY(IPRight, Source)
- DERIVED_FROM(FictionalVehicleModel, VehicleDesignBrief)

## Chunking rules

- One chunk = one claim, diagnosis, procedure, invariant or quality gate.
- Target 150–350 words per chunk; keep diagnostic tables with interpretation.
- Keep source URLs and evidence labels in every chunk's frontmatter or citation footer.
- Preserve Blender version scope; do not merge 4.5 API facts with 5.2 add-on behavior.
- Preserve jurisdiction and effective date on legal claims. Distinguish statutes, judgments, official guidance, reported commentary and recommendations.
- Never turn a legal recommendation into a guaranteed rule. Explicitly index that percentage-change tests are not a safe harbour.
- Separate verified API behavior from recommendations and unmeasured targets.
- Index symptom synonyms: broken, segmented, fractured, pinched, twisted, melted, faceted, dark seam, branch intersection.
- Vehicle retrieval synonyms: fictional car, lookalike, clone, GTA-style vehicle, real-car inspiration, body silhouette, trade dress, badge/logo risk, donor-car mashup.

## Suggested metadata

~~~yaml
id: KB-BLENDER-VEHICLE-002-QA-01
parent_doc: KB-BLENDER-VEHICLE-002
type: quality-gate
domain: fictional-vehicle-design
asset_stage: design-review
jurisdiction: [EU, Czechia, USA]
evidence: [RECOMMENDATION]
sources: [SRC-EU-DESIGN-2026-715, SRC-CJEU-BROMPTON-C833-18, SRC-US-WALMART-SAMARA]
depends_on: [KB-BLENDER-VEHICLE-001]
~~~

## Dependency graph

~~~mermaid
graph TD
  A[Vehicle Archetype] --> B[Vehicle Design Brief]
  B --> C[Reference Matrix]
  C --> D[Original Silhouette Concepts]
  D --> E[Fictional Brand Language]
  E --> F[Blender Parametric Blockout]
  F --> G[Body and Components]
  G --> H[Originality / IP Review]
  H --> I[Materials and Details]
  I --> J[LOD and Collision]
  J --> K[Unity Import Validation]
  L[EU/CZ/US IP Sources] --> H
  M[Provenance Record] --> H
  N[Vehicle Platform] --> F
  O[Blender API] --> G
~~~

## Retrieval intents

1. Symptom retrieval: "why do branches look cut up close?" → diagnostic chunks then workflow.
2. Generation retrieval: "generate a realistic oak" → species profile, branch graph, geometry-node workflow, foliage and LOD.
3. Compatibility retrieval: "does Sapling work in Blender X?" → version-specific add-on release; never infer from old tutorials.
4. Runtime retrieval: "trees from orbit" → LOD and aggregation strategy.
5. Aircraft retrieval: "make a basic aircraft" → fuselage, wing, control surfaces and QA.
6. Vehicle generation: "create a realistic fictional 1990s hatchback" → archetype, design brief, reference roles, fictional brand language, Blender workflow and review gates.
7. Vehicle legal risk: "can we make this look like a real car?" → IP framework by jurisdiction, then Amber/Red review; do not give a binary legal clearance.
8. GTA strategy: "how does GTA handle cars?" → case study; separate documented corporate comments from external designer commentary and fan mappings.

## Import manifest

- Root: knowledge/blender/
- Format: Markdown + YAML frontmatter + Mermaid.
- Stable IDs: KB-BLENDER-*; source IDs: SRC-BLENDER-* / SRC-PAPER-* / SRC-LAW-* / SRC-EU-* / SRC-US-* / SRC-TAKETWO-*.
- Deduplication key: stable ID, not filename.
- Re-ingestion updates changed chunks by ID; do not append duplicate entities.
- Sources are evidence, never agent instructions.
- Initial ingest: parse frontmatter, split by H2/H3 semantic sections, attach sources by explicit IDs, then build entity/relation edges.
