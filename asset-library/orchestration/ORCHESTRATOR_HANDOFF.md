# Orchestrator handoff — Asset Library

## Mission

Build and maintain a production-grade, legally auditable CC0-only asset library for Planetary Engine. Deliver a usable catalog and repeatable intake pipeline, not a folder full of unverified downloads.

## Project boundary

- This work is a content-library lane. It does not alter the planetary simulation architecture, authoritative Rust runtime, Node control plane, world coordinate contracts, entity identity, physics or WI-001..WI-012 scope.
- Unity consumes assets as a client. Asset IDs are stable content references; asset records do not define simulation entities.
- Do not change canonical task-pool state unless the project owner explicitly creates/approves a ticket for this lane.
- Do not launch workers. Prepare briefs and let the user start them under the repository's existing pool policy.

## Read order

1. Root `AGENTS.md`.
2. This handoff and `asset-library/README.md`.
3. `asset-library/policy/LICENSING.md`.
4. `asset-library/schemas/asset.schema.json`.
5. `asset-library/pipeline/ACQUISITION.md` and `unity/IMPORT_PROFILE.md`.
6. Existing architecture and orchestration rules only where integration is needed.

## Invariants

1. CC0 1.0 only. No CC-BY, QAL, NC, unknown or inferred licenses.
2. Asset-level proof is mandatory. A source-wide license statement is not enough without the exact item and variant.
3. Candidate leads never enter the accepted registry or release builds.
4. Never fabricate counts, formats, dimensions, download URLs, hashes, tests or license claims.
5. Preserve source files and provenance. Derivatives must point to their parent asset and reproducible conversion settings.
6. No large binary archives in Git unless the project owner explicitly approves the storage design.
7. Unity remains a client. Do not encode authoritative simulation state in imported asset metadata.
8. No silent replacement of a binary or license record; version and audit changes.

## Work decomposition

Recommended bounded work packages, to be scheduled by the project owner:
- AL-01 Source verification: inspect candidate publishers and exact item-level license evidence; produce source audit.
- AL-02 PBR surfaces: terrain, rock, asphalt, concrete, metal, wood, water inputs; target 2K where available.
- AL-03 Biomes and vegetation: trees, shrubs, grass, crops, ground cover, rocks; include seasonal/biome tags.
- AL-04 Built environment: residential, commercial, industrial, roads, bridges, rail, ports, utilities.
- AL-05 Transport: cars, trucks, buses, motorcycles, trains, ships, aircraft; prioritize coherent quality and formats.
- AL-06 Space domain: rockets, launch vehicles, shuttles, satellites, stations, probes and celestial props.
- AL-07 Pipeline tooling: schema validation, archive checks, SHA-256, duplicate detection, import report.
- AL-08 Unity integration: sample scenes, render-pipeline material generation, LOD/import tests.
- AL-09 Audit and release: license recheck, manifest freeze, build inclusion/exclusion report.

The work packages above are planning labels, not canonical pool tickets and not authorization to queue or launch.

## Definition of done

- Every accepted record validates against the schema.
- Every accepted record has exact item page, license evidence, scope note, verification timestamp, reviewer and per-file SHA-256.
- No non-CC0 or unknown-license content appears in the accepted registry.
- Candidate and rejected records are physically/logically separated from approved records.
- Every model has format, units/axis/pivot/UV/material and LOD/collider inspection status.
- Every texture has exact dimensions, map semantics, color space, normal convention and tileability status where relevant.
- Unity-tested assets identify Unity version, render pipeline and importer version.
- Duplicate detection is checksum-based; near-duplicate detection is advisory and never deletes source material automatically.
- The orchestrator can reproduce intake and validation from committed instructions without undocumented manual steps.

## Session closeout

Report:
- exact changed paths and commit/PR;
- accepted asset count (only records in approved registry);
- candidate and rejected counts separately;
- license evidence coverage and unresolved rights questions;
- formats/resolutions/category coverage;
- validation commands and exit codes;
- Unity test evidence and known limitations;
- next bounded task, without launching it.
