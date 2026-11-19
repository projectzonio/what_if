---
id: KB-BLENDER-000
type: knowledge-base-index
domain: blender
project: planetary-engine
status: research-baseline
created: 2026-11-19
tags: [blender, asset-generation, vegetation, aircraft, graphrag]
---

# Blender Knowledge Base — Planetary Engine

Research baseline for AI-assisted Blender asset creation, procedural geometry, validation and delivery. This is not a claim that generated assets are production-ready.

## Scope

1. Vegetation: close-up quality of trunks, branches, junctions, bark and foliage; diagnosing AI-generated mesh defects.
2. Procedural modeling: Geometry Nodes, curves, mesh construction and branch skeletons.
3. Optimization: LOD, impostors, instancing and planetary-scale representation.
4. Aircraft: hard-surface modeling, components and export.
5. Automation: Blender Python API, repeatable generation and quality gates.

## Start here

- [Tree defect diagnosis](tree-defect-diagnosis.md) — why a tree looks good far away but broken close up.
- [Procedural tree production](procedural-tree-production.md) — recommended geometry-first workflow.
- [Vegetation LOD and runtime](vegetation-lod-runtime.md) — multiple scales and runtime representation.
- [Aircraft production](aircraft-production.md) — basic aircraft geometry and QA.
- [GraphRAG model](graphrag-model.md) — entities, relations, dependencies and ingestion.
- [Source registry](sources.md) — primary documentation and research.

## Core production rule

Do not solve geometric defects with shading or textures. Validate branch skeleton, junctions, cross-sections, mesh continuity and silhouette first. Only then invest in bark, leaves and LODs.

A tree must pass separate tests at silhouette distance, gameplay distance and close inspection.

## Evidence labels

- [VERIFIED] Directly supported by Blender documentation/API or a cited paper.
- [RESEARCH] Method described in published graphics research.
- [RECOMMENDATION] Engineering recommendation; validate against actual assets and runtime.
- [UNMEASURED] Numeric target or performance claim not yet benchmarked.

## Version policy

The Sapling Tree Gen extension listing was updated for Blender 5.2+ on 2026-11-04. Geometry Nodes and Python API links in this baseline primarily use Blender 4.5 documentation. Before implementation, check the installed Blender version and matching API. Do not copy node names or properties blindly across versions.

Blender is an authoring and asset-baking tool, not simulation authority. Exported assets are visual representations consumed by Unity; physical dimensions, collision proxies, identity and runtime ownership remain defined by Planetary Engine contracts.
