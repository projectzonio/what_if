# Planetary Engine Asset Library

Status: foundation / acquisition not yet populated.  
Policy: public-game distribution, strict CC0 1.0 only.  
Owner: Planetary Engine project.  
Canonical registry: `registry/assets.json`.

## Purpose

A provenance-first asset catalog for the Unity presentation client and world-generation pipeline. It covers the complete planetary scope: terrain and surface materials, water, atmosphere, vegetation, rocks, buildings, civil infrastructure, ground vehicles, rail, maritime vessels, aircraft, spacecraft, orbital infrastructure, and environmental effects.

The library is a content supply layer. It does not define simulation authority, entity identity, physics, world coordinates, or runtime ownership. Unity consumes imported representations; authoritative simulation remains outside Unity.

## Repository layout

- `registry/assets.json`: accepted asset records; empty until individually verified.
- `registry/sources.json`: source/publisher records and their evidence scope.
- `schemas/asset.schema.json`: JSON Schema for an accepted asset.
- `policy/LICENSING.md`: non-negotiable licensing rules.
- `pipeline/ACQUISITION.md`: discovery, verification, intake, validation and release.
- `unity/IMPORT_PROFILE.md`: Unity-facing format, texture and material profile.
- `orchestration/ORCHESTRATOR_HANDOFF.md`: operational instructions for the project orchestrator.
- `orchestration/AGENT_WORKFLOW.md`: bounded worker workflow and acceptance gates.
- `candidates/candidates.json`: leads only; never treated as approved assets.

Large third-party binaries should normally remain in a separately managed asset store or controlled download cache. Git tracks metadata, license evidence, checksums, import profiles and reproducible manifests—not bulk asset archives.

## Current state

No individual asset is approved by this foundation commit. Publisher-level CC0 declarations are discovery leads, not proof that a particular asset, embedded component, texture, scan, font, sound, or derivative is covered. Populate `registry/assets.json` only after the asset-level evidence gate in `policy/LICENSING.md` passes.

## First-party source leads

- Poly Haven: https://polyhaven.com/license — publisher states CC0 for its published assets; preserve the exact asset page and download variant in each record.
- ambientCG: https://docs.ambientcg.com/license/ — publisher license documentation; preserve exact asset page and variant.
- Kenney: https://kenney.nl/support — verify the current license and the specific pack/item before acceptance.
- OpenGameArt: https://opengameart.org/ — per-item license verification is mandatory; category membership is not evidence.
- Smithsonian 3D: https://3d.si.edu/ — rights are object-specific; only accept an object when its own metadata explicitly establishes CC0/public-domain rights compatible with this project's strict policy.

These are discovery sources, not an approved-asset list.

## Quick validation

Use a JSON Schema validator against `schemas/asset.schema.json`, then run the checks defined in `pipeline/ACQUISITION.md`. A structurally valid record is not automatically legally approved: the license evidence gate is a separate mandatory review.
