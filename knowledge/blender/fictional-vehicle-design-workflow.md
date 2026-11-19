---
id: KB-BLENDER-VEHICLE-002
type: blender-production-workflow
domain: fictional-vehicle-design
tags: [blender, hard-surface, cars, procedural-modeling, fictional-brands, geometry-nodes, asset-pipeline]
entities: [VehicleDesignBrief, VehicleArchetype, VehicleFamily, VehiclePlatform, VehicleBody, VehicleBrandLanguage, VehicleQA]
depends_on: [KB-BLENDER-VEHICLE-001]
---

# Blender workflow: original fictional vehicles

## Production objective

Build a reusable vehicle catalogue for Planetary Engine with the visual credibility of real automotive design, without reproducing licensed production cars. Blender is the geometry and asset-authoring environment; Unity/Planetary Engine consumes exported visual assets. Vehicle physics, handling, drivetrain simulation, dimensions and authoritative state remain separate runtime systems.

## Design principle: category first, identity second

A vehicle brief begins with a functional archetype, not a named real-world model.

Examples:
- late-1990s compact front-engine hatchback for a budget urban market;
- 1970s rear-wheel-drive touring coupe;
- contemporary electric executive fastback;
- 1980s rally homologation sedan;
- mid-engine track-focused exotic;
- heavy-duty long-wheelbase utility pickup.

The archetype establishes constraints and context. It does not dictate the exact appearance.

## Stage 1 — Vehicle Design Brief

Create a structured brief before opening Blender:

| Field | Meaning |
|---|---|
| vehicle_id | Stable fictional asset identifier |
| manufacturer_id | Fictional brand |
| model_name | Fictional model name |
| class / era | Segment and design period |
| market_position | Economy, family, premium, sport, utility, luxury |
| body_style | Hatchback, sedan, coupe, wagon, SUV, pickup, van, etc. |
| powertrain_layout | Front/rear/mid engine, EV skateboard, 4x4 packaging |
| drivetrain | FWD/RWD/AWD/4WD; runtime data, not visual identity alone |
| proportions | Wheelbase, track, overall length/width/height, overhangs |
| design_language | Brand-specific shapes, surfaces, graphic motifs |
| reference_roles | What each reference teaches (packaging, era, material, stance) |
| originality_decisions | Which major features are deliberately new |
| risk_status | Green / Amber / Red internal review |

Do not place real car model names in the public asset name or manufacturer field. If reference names are retained internally, isolate them in a provenance-only field.

## Stage 2 — Reference research without tracing

Use references to understand automotive grammar:
- packaging and proportions for a vehicle class;
- historical trends and construction conventions;
- aerodynamic requirements and manufacturing constraints;
- broad design language of an era;
- surface treatment, material and assembly logic.

A reference matrix should have multiple independent entries with distinct roles. Avoid a one-to-one donor model. Do not trace outlines or copy a reference image into a mesh as a direct blueprint.

Preferred sequence:
1. Write the archetype in words.
2. Identify functional constraints.
3. Study several examples from the class/era.
4. Record abstract observations (e.g. "short rear overhang", not "copy model X rear").
5. Close the reference board and create original silhouette thumbnails.
6. Choose a new proportion package and a new front/side/rear identity.
7. Record why the chosen design differs at the overall-impression level.

The use of multiple references improves design breadth, but does not by itself establish legal safety.

## Stage 3 — Fictional manufacturer design language

Build a coherent manufacturer identity before generating dozens of models.

Define:
- brand positioning and fictional origin;
- proportion tendencies (cab-forward, long-hood, upright, wedge, etc.);
- surface vocabulary (soft radii, crisp creases, faceted volumes);
- graphic language for lamps and glazing;
- grille/air-intake philosophy;
- wheel design family;
- interior architecture and material palette;
- badge geometry, wordmark and typography;
- naming conventions and model hierarchy.

Brand signatures must be original and should not deliberately echo a real logo, badge silhouette, grille motif or distinctive lamp signature.

A fictional brand should have variation between models. Do not repeat one face on every vehicle; use a family resemblance rather than identical front/rear graphics.

## Stage 4 — Parametric blockout in Blender

Start with a clean proportion cage:
- ground plane and wheel contact points;
- wheelbase and track;
- tire outer diameter;
- front/rear overhangs;
- roof height and greenhouse;
- hood, cabin and rear volume;
- wheel-arch envelope.

Create a non-destructive blockout using curves, mesh sections or Geometry Nodes. Keep parameters in a single data structure or node-group interface. Generate left/right symmetry only where appropriate; asymmetry can be intentional.

Review the vehicle at:
- side orthographic;
- front orthographic;
- rear orthographic;
- top orthographic;
- front three-quarter;
- rear three-quarter.

Do not add detailed lamps, badges, vents or panel lines until the silhouette works.

## Stage 5 — Body construction

Recommended modeling order:
1. Primary body volume and major cross-sections.
2. Hood, roof, cabin/greenhouse and rear deck.
3. Fender volumes and wheel arches.
4. Front/rear fascia and lamp volumes.
5. Bumpers, sills, mirrors and glazing.
6. Doors, shut lines, handles and trim.
7. Underbody and wheel wells.
8. Wheels, tires, brakes and suspension-visible parts.
9. Interior and cockpit.
10. Small details, decals and materials.

Use controlled quad topology for deforming or close-up surfaces. Maintain panel thickness and plausible seams. Use separate objects for parts that need independent materials, animation, damage, replacement or LOD control. Do not make every detail a separate object if it harms runtime batching.

## Stage 6 — Parametric family and modular platform

Separate platform geometry from model-specific appearance:
- common wheel/tire library by class;
- configurable wheelbase/track and suspension envelope;
- modular front/rear crash structures;
- configurable roof/cabin volume;
- shared interior primitives;
- variant-specific body panels;
- brand-specific lamps, grille and trim;
- optional performance packages.

A platform can generate sedan, wagon, coupe or utility variants, but do not force every derivative into the same body silhouette. Model variants must remain credible and visually differentiated.

Use a manifest such as:

~~~yaml
vehicle_id: VHC-ORION-SEDAN-001
manufacturer_id: BRAND-ORION
model_name: Meridian
class: executive_sedan
era: contemporary
platform_id: PLATFORM-ORION-M
design_revision: 3
blender_version: 4.5
generator_version: vehicle_builder_0.1
seed: 48291
reference_manifest: references/meridian-reference-log.yaml
risk_status: review_required
components:
  - body_shell
  - glass
  - wheels
  - interior
  - lamps
  - collision_proxy
  - lod1
  - lod2
~~~

The example names are fictional placeholders.

## Stage 7 — Blender automation

Use Python for repeatable asset setup:
- create collections and named components;
- apply controlled transforms;
- generate wheel positions from wheelbase/track parameters;
- create cameras for standardized QA views;
- export turntable renders;
- validate object names, dimensions, materials and mesh health;
- export FBX/glTF according to the project's Unity import contract.

Use Geometry Nodes for repeated or parameterized geometry such as tire tread, grille structures, vents, fasteners, interior trim and variant components. Keep the primary body silhouette explicit and artist-reviewable. Procedural generation should improve consistency, not obscure provenance.

## Stage 8 — Originality and visual QA

Run a review before detailed polish:
- compare silhouettes from front, side, rear and three-quarter views;
- inspect the roofline, glasshouse, wheelbase, overhangs and fender volumes;
- inspect the complete front/rear graphic identity;
- check whether one real model remains immediately identifiable;
- inspect wheel design, mirrors, handles, vents and other signature details;
- remove real badges, model names and copied typography;
- retain comparison images and review notes.

A perceptual similarity model or image embedding can be used to flag candidates for human review. It is not a legal test and must not be used to declare an asset safe. A similarity percentage is not a statutory threshold.

## Stage 9 — LOD, damage and runtime

- LOD0: complete body, interior where visible, wheels and detailed lamps.
- LOD1: simplified body/interior and reduced small details.
- LOD2: simplified glazing, wheels and underbody.
- LOD3: distant silhouette or impostor as appropriate.
- Collision: independent simplified proxies.
- Damage: separate deformable/detachable parts where supported by runtime.

Keep visual LOD independent from physics and collision authority. Ensure all variants share consistent dimensions, wheel contact points, pivots and naming conventions.

## AI prompting pattern

Prompt for a design brief and original design constraints, not "make a [real make/model] but change it".

Example:

> Design an original late-1990s European compact rally hatchback for a fictional manufacturer. Use plausible period engineering, short wheelbase, practical cabin packaging and rally-ready proportions. Develop a distinctive original roofline, front and rear identity, lamp graphics, wheel arches and body surfacing. Do not reproduce any specific production vehicle, badge, grille, lamp signature or recognizable body silhouette. Provide front, side, rear and three-quarter concepts, then a dimensioned proportion sheet. The result must read as a believable production car, not a direct lookalike.

The negative prompt is a design instruction, not a legal guarantee. Review generated output independently.

## Acceptance gates

- Geometry and topology QA passes.
- Dimensions, wheelbase, track and contact points match the vehicle manifest.
- All components have stable IDs and names.
- No unlicensed logos, marks, textures or copied geometry.
- Originality review completed and stored.
- Export reopens correctly and imports into Unity.
- LOD and collision proxies validated in runtime.
