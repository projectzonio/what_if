---
id: KB-BLENDER-000
type: knowledge-base-index
domain: blender
project: planetary-engine
status: research-baseline
created: 2026-11-19
tags: [blender, asset-generation, vegetation, aircraft, fictional-vehicles, graphrag]
---

# Blender Knowledge Base — Planetary Engine

Research baseline for AI-assisted Blender asset creation, procedural geometry, validation and delivery. This is not a claim that generated assets are production-ready.

## Scope

1. Vegetation: close-up quality of trunks, branches, junctions, bark and foliage; diagnosing AI-generated mesh defects.
2. Procedural modeling: Geometry Nodes, curves, mesh construction and branch skeletons.
3. Optimization: LOD, impostors, instancing and planetary-scale representation.
4. Aircraft: hard-surface modeling, components and export.
5. Fictional vehicles: original car design, IP risk, GTA case study, Blender production workflow and provenance.
6. Automation: Blender Python API, repeatable generation and quality gates.

## Start here

- [Tree defect diagnosis](tree-defect-diagnosis.md) — why a tree looks good far away but broken close up.
- [Procedural tree production](procedural-tree-production.md) — recommended geometry-first workflow.
- [Vegetation LOD and runtime](vegetation-lod-runtime.md) — multiple scales and runtime representation.
- [Aircraft production](aircraft-production.md) — basic aircraft geometry and QA.
- [Vehicle IP risk framework](vehicle-ip-risk-framework.md) — EU/Czech/US rights, risk policy and review checklist.
- [Fictional vehicle design workflow](fictional-vehicle-design-workflow.md) — structured brief, Blender modeling, fictional OEMs, QA and export.
- [GTA fictional vehicle case study](gta-fictional-vehicles-case-study.md) — industry context and what can/cannot be inferred.
- [GraphRAG model](graphrag-model.md) — entities, relations, dependencies and ingestion.
- [Source registry](sources.md) — primary documentation and research.

## Core production rules

- Do not solve geometric defects with shading or textures. Validate branch skeleton, junctions, cross-sections, mesh continuity and silhouette first.
- A tree must pass separate tests at silhouette distance, gameplay distance and close inspection.
- Vehicle design starts from an archetype and original fictional identity, not a named real-world model.
- A fictional badge or changed model name does not make a copied vehicle shape safe. No percentage-change safe harbour exists.
- Store design provenance and complete an originality review before accepting a vehicle asset.

## Evidence labels

- [VERIFIED] Directly supported by official documentation, law, court decision or a cited primary source.
- [RESEARCH] Method described in published graphics research.
- [REPORTED] Secondary reporting or expert commentary; not a primary production record.
- [RECOMMENDATION] Engineering recommendation; validate against actual assets and runtime.
- [UNMEASURED] Numeric target or performance claim not yet benchmarked.

## Version and legal policy

The Sapling Tree Gen extension listing was updated for Blender 5.2+ on 2026-11-04. Geometry Nodes and Python API links in this baseline primarily use Blender 4.5 documentation. Before implementation, check the installed Blender version and matching API.

Legal notes are research guidance, not legal advice. Current EU design references use Regulation (EU) 2026/715 (applicable from 1 July 2026), alongside relevant EU trade mark law, Czech statutes and case law. Review actual territories, dates and rights before commercial publication.

Blender is an authoring and asset-baking tool, not simulation authority. Exported assets are visual representations consumed by Unity; physical dimensions, collision proxies, identity and runtime ownership remain defined by Planetary Engine contracts.


## Grass and destruction research

- [Grass rendering and vegetation](grass-rendering-and-vegetation.md) — Crysis/CryEngine grass techniques, GPU instancing, overdraw, wind, LOD and benchmark plan.
- [Destructible world assets](destructible-world-assets.md) — Blender fracture authoring and persistent runtime destruction of trees and buildings.

Production principle: optimize grass as a whole pipeline (coverage, overdraw, batching, shading, wind, shadows and power), not by polygon count alone. Persist meaningful destruction as sparse world-state deltas over immutable base assets; cosmetic debris is transient.
