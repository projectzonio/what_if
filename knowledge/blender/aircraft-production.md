---
id: KB-BLENDER-AIRCRAFT-001
type: production-workflow
domain: aircraft
tags: [aircraft, hard-surface, topology, wings, fuselage, blender, export]
entities: [AircraftAsset, Fuselage, Wing, ControlSurface, LandingGear, Cockpit]
---

# Basic aircraft production in Blender

## Scope

Create visually coherent aircraft assets for the Planetary Engine presentation layer. This is geometry and asset production, not flight dynamics, aerodynamic coefficients or authoritative physics.

## Construction order

1. Establish reference dimensions and coordinate convention.
2. Block out fuselage centerline and major cross-sections.
3. Build wings from planform curves/surfaces with controlled thickness.
4. Add horizontal/vertical stabilizers and control surfaces.
5. Add engine nacelles/intakes/exhausts as separate components where appropriate.
6. Add cockpit canopy, landing gear and external details.
7. Add bevels, shading and materials.
8. Create simplified collision/LOD representations.
9. Validate scale, origin, naming and export.

## Fuselage

Use a longitudinal sequence of cross-sections rather than stretching a primitive into a tube. Cross-sections control width, height and curvature from nose to tail. Maintain smooth transitions and avoid abrupt section changes. A ring-based mesh or curve-driven surface provides controllable topology; terminate nose and tail cleanly and avoid overlapping internal faces.

## Wings

Define span, half-span, root/tip chord, leading-edge sweep, dihedral/anhedral, airfoil-like cross-section, thickness distribution, wingtip shape and aileron/flap boundaries.

A wing should not be a paper-thin plane. Give it a closed, controlled volume. Keep upper/lower surfaces smooth and preserve a deliberate trailing edge. Avoid random beveling of aerodynamic surfaces; use restrained bevels on hard-surface panel boundaries.

## Components and animation

Model ailerons, flaps, elevators and rudder as distinct components or clearly separated mesh regions if animation is required. Put pivots at plausible hinge axes and preserve component names/metadata. Keep landing gear, doors, propellers and engine fans separate where animation or LOD replacement is required.

## Topology and shading

- Keep large fuselage/wing surfaces free of accidental poles and pinched loops.
- Add support loops or bevel modifiers only where they control silhouette/highlight.
- Use smooth shading on curved surfaces; preserve hard edges at panel boundaries and mechanical parts.
- Shade Smooth affects normal interpolation, not actual outline.
- Smooth By Angle separates smooth surfaces from sharp boundaries based on neighboring face angles.
- Apply scale before bevel-dependent operations where required; inspect bevel width in object space.
- Check face orientation, non-manifold edges, duplicate geometry and normals before export.

## AI-generated aircraft review

AI may produce plausible concept geometry with hidden intersections, non-manifold surfaces, arbitrary thickness, disconnected components and inconsistent symmetry. Evaluate centerline/bilateral symmetry (unless intentional), closed fuselage volume, wing-root continuity, consistent wing thickness/taper, tailplane attachment, separate cockpit glazing, actual engine openings where camera-visible, no floating control surfaces/gear, and object transforms/component origins.

## Automation

Use Geometry Nodes or Python for repeated structures: ribs, panel lines, rivets, window arrays, engine blades, gear struts and LOD generation. Keep primary airframe as explicit controllable geometry. Parameterize dimensions and store source profiles so variants regenerate deterministically.

## Export checklist

- Units and real-world dimensions recorded.
- Forward/up axes explicitly mapped to Unity import convention.
- Origin/pivot documented.
- Component names stable.
- Materials and texture paths packed/resolved.
- Collision proxy separate.
- LODs and animation pivots tested in target runtime.
- Exported file reopened in clean Blender session and imported into Unity.

Numeric aircraft dimensions, triangle budgets and physics parameters must come from aircraft class/runtime contract; do not invent them in an asset-generation prompt.
