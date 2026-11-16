Máš pravdu. Repository jsem nyní přímo otevřel a ověřil. Je veřejné (`projectzonio/what_if`) a obsahuje všech devět požadovaných dokumentů `w1.md` až `w9.md`.

Nebudu tedy požadovat ZIP ani další podklady. Budu vycházet přímo z aktuálního obsahu repository, nikoliv pouze z popisu GPT-04.

# GPT-04 — Entity Identity and Physical State Contract

Repository inspected

Architecture resolution

Implementation prohibited

Project: Planetary Engine\
Repository: [projectzonio/what_if](https://github.com/projectzonio/what_if)\
Assigned problem: C-04 — Inconsistent entity state representations\
Role: Principal Systems Architect — Independent Architecture Resolution Worker\
Scope: W01–W09

## A. Executive decision

Recommended architecture

Adopt a Canonical Entity Contract built around persistent identity, versioned capabilities and temporally qualified physical state.

The canonical entity must not be a universal rigid-body structure, a Unity GameObject, an ECS archetype or a network snapshot.

It must be a persistent logical object whose identity remains stable independently of:

- Its current physical representation.
- Its simulation fidelity.
- Its reference frame.
- Its simulation region.
- Its current authoritative server.
- Its physical solver.
- Its presentation client.
- Its persistence representation.
- Its world branch.

The recommended architecture has three distinct layers:

1\. Entity Identity Layer

# Entity ID

Persistent identity, lifecycle, lineage and world membership

2\. Canonical State Layer

# Versioned State + Capabilities

Spatial, temporal, physical, simulation and authority metadata

3\. Runtime Consumers

Rust ECS, physics, astronomy, environment, economy, AI

3\. External Consumers

Persistence, networking, Python and Unity

The central architectural rule:

An entity is an identity. Its state is versioned. Its capabilities define what it can do. Its simulation representation determines how accurately it is currently calculated.

These are separate concepts and must remain separate.

The proposal preserves the existing W01–W09 architectural direction while resolving their overlapping and incomplete contracts.

## B. Existing architecture

### B1. Repository findings

The nine worker documents already contain most of the necessary building blocks, but they do not yet define one complete canonical entity contract.

| Worker | Existing architectural contribution                                  |
| ------ | -------------------------------------------------------------------- |
| W01    | Hierarchical coordinates, reference frames and spatial state         |
| W02    | ECS, physics ownership, solver state and rollback                    |
| W03    | Physical capabilities and specialized entity models                  |
| W04    | Celestial identity, astronomical properties and epochs               |
| W05    | Server-side entity registry, snapshots and migration                 |
| W06    | Persistent identity, capability composition, authority and branching |
| W07    | Replication schemas and presentation separation                      |
| W08    | Environmental state, scientific reproducibility and temporal inputs  |
| W09    | Simulation fidelity, distributed ownership and migration             |

### B2. Important existing proposals

W01 — Spatial representation

W01 section 2.7 proposes hierarchical coordinates with frame identifiers, anchors, integer cell coordinates and local offsets. Sections 2.4–2.5 correctly distinguish transformations between inertial and rotating reference frames.

Its section 6.3 also proposes a WorldState containing entity identity, simulation instant, position, orientation, linear velocity, angular velocity and world revision.

This is a strong foundation, but it does not fully define identity, lifecycle, capabilities or authority.

W02 — Physics and ECS

W02 section 8.1 explicitly recommends data-oriented ECS:

- Entities are identifiers.
- Components hold physical state.
- Systems process collections of components.
- Domain modules operate on entity groups.

W02 section 7 also defines a multi-rate simulation pipeline.

This is compatible with the proposed architecture, provided ECS identifiers are not confused with persistent entity identities.

W03 — Physical composition

W03 section 3.1 proposes capability-based composition.

Its central physical entity model includes position, velocity, orientation, mass and inertia, with optional dynamics, interaction, actuation and mass-property capabilities.

W03 section 2.4 explicitly requires one physical identity across surface-to-orbit transitions.

This is an important existing decision that should be preserved.

W04 — Astronomical entities

W04 section 3.1 recognizes that planets, stars, satellites, asteroids and galaxies have different physical representations.

Its section 5.1 proposes a celestial entity contract containing entity ID, type, parent ID, physical model, gravity model, mass, reference frame, state epoch and physical state.

The important distinction is that celestial hierarchy is not equivalent to physical interaction hierarchy.

W05 — Server state

W05 section 9.4 proposes an EntitySnapshot with:

- world_id
- region_id
- entity_id
- owner_epoch
- simulation_tick
- state_version
- position
- rotation
- linear_velocity
- angular_velocity
- component_mask
- component_data

This is a useful transport-oriented snapshot proposal, but it is insufficient as the canonical entity model.

For example, it does not independently describe the coordinate frame, temporal scale, entity lifecycle or component schema versions.

W06 — Persistence and authority

W06 sections 2.2, 7.2 and 8.1 establish three particularly important principles:

- Capability composition rather than rigid inheritance.
- Explicit authority epochs.
- Branches based on shared parent state and copy-on-write differences.

These should become part of the common entity architecture.

W07 — Replication

W07 section 7.4 recommends specialized replication schemas built on common primitives.

Its section 20 proposes three separate client representations:

1. Authoritative state.
2. Presentation state.
3. Rendering state.

This is compatible with the canonical entity model and should be adopted.

W08 — Environment

W08 section 10.4 distinguishes authoritative physical state from scientific reproducibility.

It requires recording model versions, initial conditions, timestamps, random seeds and numerical configuration.

Environmental state therefore needs its own temporal and provenance semantics, rather than being embedded indiscriminately into every entity.

W09 — Distributed execution

W09 section 3.1 defines L0–L3 simulation representations and explicitly states that these are different computational representations of the same logical entity.

Its section 5.1 defines region ownership and migration with ownership epochs and fencing.

### B3. Existing architectural status

The following are existing proposals, not yet universally frozen contracts:

- Persistent entity identity.
- Canonical coordinate representation.
- State versioning.
- Ownership epochs.
- Capability serialization.
- Temporal validity.
- Branch identity.
- Simulation fidelity transitions.

The repository has the ingredients, but does not yet specify how these concepts fit into one interoperable contract.

## C. Problem analysis

### C1. Fundamental architectural contradictions

I identify eight problems that need resolution.

C-01 — Persistent identity versus runtime identity

W02 uses ECS entity identifiers. W04 uses stable astronomical entity identifiers. W05 uses UUIDs. W06 uses persistent entity identity.

These identifiers serve different purposes.

An ECS identifier may be recycled after an entity is destroyed. A persistent entity identifier must never silently identify another logical object.

Resolution: introduce an immutable persistent EntityID. ECS handles are temporary runtime references mapped to EntityID.

C-02 — Physical state versus spatial representation

W01 proposes hierarchical spatial coordinates. W03 proposes physical position and velocity. W04 proposes astronomical position and velocity. W05 uses generic Vector3 fields.

A Vector3 alone cannot describe a universal position.

The same applies to velocity: a velocity relative to a rotating planet is not interchangeable with inertial velocity.

Resolution: every spatial and kinematic quantity must carry explicit reference-frame semantics.

C-03 — Entity type versus capability composition

W04 categorizes celestial objects. W03 describes physical models. W06 proposes capability-based composition.

These approaches are compatible only if entity classification is not used as a rigid schema definition.

A planet can be:

- A gravitational source.
- A rotating rigid body.
- A terrain provider.
- An environmental provider.
- A target for construction or resource extraction.

It must not need a separate identity for every role.

Resolution: entity type is descriptive metadata. Capabilities define available behavior and state.

C-04 — Physical state versus simulation fidelity

W09 L0–L3 describes different representations of the same entity.

A transition from full physics to statistical simulation must not replace the entity or discard its persistent identity.

However, fidelity transitions can change the physical information available.

Resolution: fidelity is runtime execution metadata. Capability state and fidelity-transition rules determine which information can be retained, approximated or reconstructed.

C-05 — State revision versus ownership epoch

W05 and W06 use state versions and ownership epochs.

These are not interchangeable.

- State revision identifies a version of entity data.
- Ownership epoch identifies the generation of authoritative ownership.

A migration may change ownership without changing the logical identity.

A physical simulation tick may change the state revision without changing ownership.

Resolution: separate all version domains.

C-06 — Physical state versus presentation state

W07 correctly identifies three representations.

However, the canonical contract must explicitly prevent Unity's Transform, camera-relative position or interpolation state from becoming authoritative.

Resolution: rendering and presentation state are derived client-side representations.

C-07 — Snapshot versus complete restorable state

W05's EntitySnapshot contains useful common physical fields, but W02 explicitly requires solver and collision state to be preserved for rollback.

W03 additionally identifies internal states such as suspension, engine speed, fuel and control integrators.

Resolution: distinguish a replication snapshot from a complete simulation checkpoint.

C-08 — Global world revision versus entity revision

W06 proposes world and subsystem revision vectors.

W05 uses state_version.

A single global revision counter for every entity would create unnecessary coordination and serialization overhead.

Resolution: use separate revision scopes, with explicit dependency references where cross-system consistency requires them.

### C2. The missing conceptual distinction

The architecture currently risks conflating four different questions:

| Question                       | Correct architectural concept |
| ------------------------------ | ----------------------------- |
| What object is this?           | Entity identity               |
| What is its current condition? | Entity state                  |
| How is it calculated?          | Simulation representation     |
| Who may change it?             | Authority                     |

These must not be encoded into one entity type or one universal state structure.

### C3. Architectural risks if unresolved

1. Duplicate identities during region migration.
2. Loss of physical continuity during reference-frame changes.
3. Inability to replay a simulation from a complete checkpoint.
4. Incompatible snapshots between Rust and Unity.
5. Capability incompatibility during schema evolution.
6. Incorrect astronomical transformations.
7. Lost state during fidelity transitions.
8. Branches that accidentally share mutable state.
9. Conflicting ownership and entity revisions.
10. Excessive synchronization caused by global version counters.

## D. Cross-system dependencies

### D1. Dependency matrix

| Worker | What GPT-04 requires                                | What GPT-04 provides                                  |
| ------ | --------------------------------------------------- | ----------------------------------------------------- |
| W01    | Coordinate hierarchy and transformation definitions | Canonical spatial-state requirements                  |
| W02    | Physics state and solver serialization              | Stable identity and physical-state envelope           |
| W03    | Capability and physical-model definitions           | Persistent entity and component identity              |
| W04    | Astronomical epochs and frame semantics             | General entity model for celestial objects            |
| W05    | Persistence and migration mechanisms                | Versioned canonical serialization contract            |
| W06    | World revisions, mutations and branching            | Entity revision and identity rules                    |
| W07    | Replication and client-state separation             | Common identity and state metadata                    |
| W08    | Environmental time and provenance                   | Consistent entity/environment temporal references     |
| W09    | Distributed ownership and fidelity transitions      | Stable identity across migration and fidelity changes |

### D2. Detailed dependencies

W01 — Universal World Architecture

Requires:

- Stable frame identifiers.
- Hierarchical coordinates.
- Six-dimensional state transformations.
- Explicit units and precision.
- Time-qualified transformations.

GPT-04 requires W01 to define the spatial representation independently of entity identity.

Compatibility: high.

Required clarification: the coordinate encoding must not become part of EntityID or entity type.

W01 section 6.2 already provides a suitable conceptual foundation.

W02 — Universal Physics

Requires:

- Position and orientation.
- Linear and angular velocity.
- Mass and inertia where applicable.
- Complete solver state.
- Restorable component state.

GPT-04 requires W02 to distinguish persistent entity state from temporary ECS and solver data.

Compatibility: high.

Required change: the physics runtime must not assume that every entity has a rigid-body component.

W03 — Entity Physics Systems

Requires persistent identity across different physical regimes.

GPT-04 requires W03 to define versioned capability schemas and the minimum physical-state interface.

Compatibility: high.

Required clarification: specialized models must own their internal state while consuming the shared physical-state contract.

W04 — Celestial and Universe Simulation

Requires:

- Stable astronomical identity.
- Hierarchical object relationships.
- Epochs.
- Reference frames.
- Gravitational models.
- Variable simulation fidelity.

GPT-04 requires W04 to define how celestial entities participate in the common identity model.

Compatibility: high.

Required change: `parent_id` must describe an explicit relationship, not implicitly define the coordinate frame or gravitational parent.

W05 — Server Architecture

Requires complete transferable state and stable entity identifiers.

GPT-04 requires W05 to distinguish:

- Entity state revision.
- Region revision.
- Ownership epoch.
- Snapshot sequence.
- Checkpoint identity.

Compatibility: high.

Required change: expand the current EntitySnapshot metadata and distinguish snapshot serialization from complete checkpoint serialization.

W06 — World Mutation

Requires entity identity, expected revisions, authority validation and capability composition.

GPT-04 requires W06 to use entity revision independently from world revision and ownership epoch.

Compatibility: high.

Required clarification: entity deletion, restoration, branching and merging must preserve identity and lineage.

W07 — Networking and Unity

Requires common identity, timestamps, versioning and coordinate conventions.

GPT-04 requires W07 to treat replication as a projection of canonical state.

Compatibility: high.

Required change: replication schemas must preserve the canonical frame and temporal metadata while remaining specialized by entity capability.

W08 — Environmental Simulation

Requires positions, epochs, environmental revisions and model provenance.

GPT-04 requires environmental samples to be temporally qualified independently of entity state revisions.

Compatibility: high.

Required clarification: environmental snapshots are not entity components unless an entity explicitly owns an environmental state capability.

W09 — Distributed Runtime

Requires complete state migration, fidelity transitions and authoritative ownership.

GPT-04 requires W09 to preserve EntityID and canonical state semantics across all migration operations.

Compatibility: high.

Required clarification: region ownership and simulation fidelity must remain independent.

## E. Alternatives

Three architectures were evaluated.

### E1. Alternative A — Universal entity structure

Every entity uses one large structure containing:

- Position.
- Orientation.
- Velocity.
- Angular velocity.
- Mass.
- Inertia.
- Collision data.
- Physical model.
- Simulation level.
- Ownership.
- Replication metadata.
- Domain-specific properties.

Not recommended

Advantages:

- Simple initial serialization.
- Easy to understand.
- Straightforward common snapshot.

Disadvantages:

- Every entity carries fields it does not need.
- Celestial, environmental and economic objects become awkward.
- Schema evolution becomes difficult.
- Optional fields proliferate.
- Specialized solvers remain difficult to isolate.
- Large-scale ECS storage becomes less efficient.

This approach conflicts with W02, W03, W06 and W09.

### E2. Alternative B — Pure ECS entity architecture

Every entity is simply a runtime identifier with independently attached components.

Insufficient as universal contract

Advantages:

- Excellent composition flexibility.
- Data-oriented execution.
- Efficient component storage.
- Natural support for specialized systems.
- Compatible with Rust.

Disadvantages:

- Runtime handles are not necessarily persistent identities.
- ECS alone does not define historical state.
- Does not define branch semantics.
- Does not define distributed ownership.
- Does not define external serialization.
- Does not establish temporal validity.

W02 and W06 already identify the limitations of treating ECS as the complete architecture.

### E3. Alternative C — Persistent identity with canonical state envelope and capability composition

Recommended

An entity has:

1. Stable persistent identity.
2. Lifecycle metadata.
3. Versioned capability collection.
4. Optional canonical physical state.
5. Explicit spatial and temporal semantics.
6. Simulation metadata.
7. Authority metadata.
8. Revision metadata.

Runtime systems map this persistent model into their own efficient ECS and solver representations.

Advantages:

- Persistent identity independent of runtime.
- Compatible with all nine workers.
- No universal rigid-body requirement.
- Supports astronomical and planetary entities.
- Supports branching and replay.
- Supports distributed ownership.
- Allows specialized replication.
- Supports future simulation domains.

Disadvantages:

- More complex serialization contract.
- Requires explicit capability schema governance.
- Requires a defined mapping between persistent and runtime representations.
- Requires careful temporal and revision semantics.

### E4. Comparison

| Criterion             | A: Universal struct | B: Pure ECS                  | C: Canonical envelope        |
| --------------------- | ------------------- | ---------------------------- | ---------------------------- |
| Correctness           | Medium              | Medium                       | High                         |
| Scalability           | Low–medium          | High                         | High                         |
| Distributed execution | Medium              | Medium                       | High                         |
| Performance           | Low–medium          | High                         | High with runtime projection |
| Complexity            | Low initially       | Medium                       | Higher initially             |
| Extensibility         | Low                 | High                         | High                         |
| Persistence           | Medium              | Low without extra contracts  | High                         |
| Branching             | Weak                | Weak without extra contracts | Strong                       |
| Unity independence    | Medium              | High                         | High                         |
| W01–W09 compatibility | Low                 | Medium                       | High                         |

Decision: adopt Alternative C.

ECS remains an implementation strategy inside Rust. It does not become the external identity or persistence contract.

## F. Recommended architecture

### F1. Canonical entity model

The canonical entity consists of ten logically independent areas.

## Canonical Entity

A. Identity

EntityID · EntityType · WorldID · Lineage

B. Lifecycle

Creation · Activation · Suspension · Destruction · Tombstone

C. Spatial State

Position · Orientation · ReferenceFrame · SpatialEncoding

D. Physical State

Velocity · AngularVelocity · MassProperties · Dynamics

E. Temporal State

SimulationInstant · Epoch · ValidityInterval

F. Simulation State

Fidelity · Solver · NumericalModel · ExecutionSchedule

G. Authority

Region · Owner · OwnershipEpoch

H. Capabilities

Versioned Components · Domain State · Dependencies

I. Revision

EntityRevision · ComponentRevision · BranchRevision

J. Provenance

ModelVersion · Source · InputHistory · Uncertainty

These are logical groups, not a mandatory contiguous memory structure.

### F2. Field classification

| Field               | Classification                                  | Ownership                  |
| ------------------- | ----------------------------------------------- | -------------------------- |
| EntityID            | Mandatory                                       | Identity service           |
| EntityType          | Mandatory                                       | Entity registry            |
| WorldID             | Mandatory                                       | World registry             |
| Lifecycle           | Mandatory                                       | World runtime              |
| EntityRevision      | Mandatory                                       | Authoritative state owner  |
| Capability manifest | Mandatory                                       | Entity registry            |
| Position            | Conditional                                     | Spatial/physical domain    |
| Orientation         | Conditional                                     | Spatial/physical domain    |
| ReferenceFrameID    | Mandatory when spatial state exists             | Frame service              |
| Linear velocity     | Conditional                                     | Physical capability        |
| Angular velocity    | Conditional                                     | Physical capability        |
| Mass                | Conditional                                     | Mass-properties capability |
| Inertia tensor      | Conditional                                     | Mass-properties capability |
| Simulation instant  | Mandatory for time-dependent state              | Time service               |
| Ownership epoch     | Conditional for actively authoritative entities | Ownership service          |
| Simulation fidelity | Mandatory for simulated entities                | Fidelity manager           |
| Component revisions | Conditional                                     | Capability owners          |
| Replication state   | External projection                             | Replication system         |
| Rendering transform | Derived                                         | Unity                      |
| Acceleration        | Derived or domain-specific                      | Solver/domain              |
| Collision caches    | Runtime-specific                                | Physics solver             |
| AI internal state   | Capability-specific                             | AI domain                  |

A nonphysical entity is valid without position, velocity, mass or orientation.

A static building may have spatial state but no dynamic rigid-body state.

A celestial object may have a gravitational state without being represented by a conventional collision body.

### F3. Identity contract

The recommended persistent identity is an opaque 128-bit EntityID.

Conceptual format:

```
EntityID = 128-bit globally unique identifier
```

The following rules are mandatory:

1. EntityID is immutable.
2. EntityID is never reused.
3. EntityID does not encode coordinates.
4. EntityID does not encode region ownership.
5. EntityID does not encode simulation fidelity.
6. EntityID does not change during reference-frame transitions.
7. EntityID does not change during migration.
8. EntityID does not change when a capability is added or removed.

A separate ECS handle may be allocated and recycled.

A separate human-readable identifier may be used for astronomical objects, editor tools or external integrations.

Examples:

```
EntityID:       128-bit opaque identity
External name:  Earth
ECS handle:     runtime-specific
Region:         region-001
```

The external name is not the identity.

World and branch identity

An EntityID identifies the logical object, while WorldID and BranchID identify the state context in which it exists.

A branch created from another world initially references the same entity identity.

An entity created only in that branch receives a new EntityID.

If an entity is intentionally cloned, the clone receives a new EntityID and retains an explicit source/lineage relationship.

This allows the same logical entity to be represented in multiple historical or experimental world branches without duplicating its identity.

### F4. Lifecycle contract

The lifecycle is independent of simulation fidelity.

Recommended lifecycle states:

| State        | Meaning                                                 |
| ------------ | ------------------------------------------------------- |
| CREATED      | Identity allocated                                      |
| INITIALIZING | Required capabilities being initialized                 |
| ACTIVE       | Entity participates in its applicable simulation        |
| DORMANT      | Persistent state retained without continuous simulation |
| SUSPENDED    | Temporarily unavailable or administratively disabled    |
| DESTROYED    | Logical destruction committed                           |
| TOMBSTONED   | Identity retained for historical references             |

These are logical states, not mandatory ECS states.

Important rules:

- Destruction is a committed world mutation.
- Destroyed identities are not recycled.
- Historical snapshots remain able to reference destroyed entities.
- Reactivation preserves identity.
- A physically detached component or vehicle stage can become a new entity through an explicit creation event.

W03 section 6.3 already describes rocket staging as a physical event that creates a new entity. This is consistent with the lifecycle model.

### F5. Spatial representation

The canonical spatial state must not be defined as a bare Vector3.

Every spatial state must include:

- ReferenceFrameID.
- Coordinate representation.
- Coordinate values.
- Units.
- Validity epoch.

Recommended logical representation:

```
SpatialState
├── reference_frame_id
├── coordinate_encoding
├── position
│   ├── cell_or_anchor
│   └── local_offset_m
└── orientation
    ├── quaternion
    └── expressed_relative_to
```

Supported coordinate representations:

| Representation             | Intended use                               |
| -------------------------- | ------------------------------------------ |
| Hierarchical cell + offset | Global world positioning                   |
| Cartesian double precision | Local physics and astronomical computation |
| ECEF/body-fixed            | Planetary surface and terrestrial systems  |
| ECI/body-centered inertial | Orbital mechanics                          |
| Barycentric                | Astronomical systems                       |
| Geodetic                   | Geographic interfaces                      |
| ENU/NED                    | Local surface and aviation systems         |
| Body-local                 | Mechanical and articulated models          |

These are representations of the same spatial reality, not separate world identities.

The authoritative spatial representation may change when a subsystem needs a different local representation, but the underlying physical position must remain consistent.

Position and orientation must be independent fields.

A reference frame may be rotating, translating or dynamically defined.

Therefore, orientation cannot be interpreted without knowing the frame in which it is expressed.

### F6. Velocity and six-dimensional state

This is a critical contract.

Linear velocity must be associated with:

- A reference frame.
- A coordinate convention.
- A time epoch.

Angular velocity must also have an explicit frame and expression convention.

For a transformation between a rotating frame and an inertial frame:

\\[ \mathbf r_I=R(t)\mathbf r_F+\mathbf r_O \\]

\\[ \mathbf v_I= R(t)\mathbf v_F+ \dot R(t)\mathbf r_F+ \mathbf v_O \\]

Simply rotating the velocity vector is insufficient.

W01 and W03 already identify this requirement.

The canonical physical-state contract must therefore use a complete six-dimensional state transformation rather than independently transforming position and velocity.

Reference-frame changes are not entity lifecycle events.

For example:

- Car on Earth.
- Aircraft in atmosphere.
- Spacecraft in orbit.
- Spacecraft approaching another planet.

The EntityID remains unchanged.

Only its spatial representation and potentially its active numerical integration regime change.

A frame transition must preserve physical state at the same simulation instant within the numerical tolerance of the transformation.

### F7. Temporal contract

The architecture needs to distinguish four different temporal concepts:

1. World simulation time.
2. Physical state epoch.
3. Numerical solver time and substeps.
4. External astronomical time scales.

Recommended internal model:

- Integer SI-based simulation duration relative to a defined world epoch.
- Explicit time-scale metadata for astronomical conversions.
- Separate simulation tick identifiers.
- Explicit validity intervals for state and environmental samples.

W01 recommends avoiding UTC as the internal continuously advancing physics clock. W04 owns astronomical time and epoch semantics.

I recommend that the common runtime use an integer fixed-point simulation timeline, while the astronomical time service provides conversions to TAI, TT, TDB, UT1 and UTC where appropriate.

A tick is not a universal physical time unit.

For example:

- Vehicle physics may execute at 60 Hz.
- Atmospheric simulation may update less frequently.
- Orbital propagation may use adaptive integration.
- Economy may be event-driven.

All results must nevertheless identify the simulation instant at which they are valid.

A state at time \\(t_1\\) cannot be silently interpreted as a state at \\(t_2\\).

### F8. Physical state

The universal runtime defines common physical semantics, not mandatory physical fields for every entity.

The following capabilities are recommended.

| Capability               | Fields                                 |
| ------------------------ | -------------------------------------- |
| Spatial                  | Position, orientation                  |
| Kinematic                | Linear and angular velocity            |
| RigidBody                | Mass, inertia, center of mass          |
| Collision                | Shape, collision model, filters        |
| ArticulatedBody          | Joints, links, constraints             |
| GravitySource            | Gravitational parameters               |
| VariableMass             | Fuel, payload, mass distribution       |
| Propulsion               | Thrust and actuator state              |
| Locomotion               | Movement and articulation state        |
| EnvironmentalInteraction | Buoyancy, atmosphere, surface response |

These capabilities are composable and versioned.

Not every entity needs every capability.

Mass, inertia and collision geometry must not be mandatory fields of the universal entity envelope.

This preserves compatibility with W03 and W04.

### F9. Capability composition

The canonical model uses:

Entity = Identity + Lifecycle + Capability Manifest + Versioned Capability State

Each capability must define:

- Stable capability identifier.
- Schema version.
- Owning subsystem.
- State representation.
- Serialization contract.
- Initialization requirements.
- Dependencies.
- Migration requirements.
- Optional fidelity-specific representations.

Example:

```
Entity: Spacecraft

Capabilities:
  Spatial
  Kinematic
  RigidBody
  VariableMass
  Propulsion
  AttitudeControl
  OrbitalDynamics
  Collision
  Presentation
```

The same identity can support a different active capability set later.

Capability removal must not silently erase persistent historical state.

A capability's state may be retained, migrated, archived or explicitly invalidated according to its lifecycle contract.

No dependency on Bevy ECS, Flecs, Rapier or another runtime framework is allowed in the public entity contract.

### F10. Simulation fidelity

Fidelity is a property of the current simulation representation, not of the entity's identity.

Adopt W09's four conceptual levels:

| Level | Representation                  |
| ----- | ------------------------------- |
| L0    | Full interactive simulation     |
| L1    | Reduced physical simulation     |
| L2    | Abstract/statistical simulation |
| L3    | Dormant persistent state        |

However, add one important rule:

A fidelity transition must be an explicit state transition with defined information-preservation guarantees.

For example, a vehicle changing from L0 to L2 may transition from individual tire dynamics to a traffic-flow representation.

The vehicle identity persists, but some detailed mechanical state may no longer be actively integrated.

When returning to L0, the system must either restore retained state or initialize a new high-fidelity state using a documented reconstruction model.

The reconstruction must not pretend that unavailable historical information was preserved.

The fidelity manager owns the transition policy. Specialized domain modules own the conversion logic.

This is consistent with W09 section 3.2, which assigns fidelity transitions to domain-specific functions.

### F11. Authority and ownership

Separate:

- Entity identity.
- Physical simulation ownership.
- Region ownership.
- Domain component ownership.
- Persistence ownership.

The canonical entity has one authoritative simulation owner for each mutable physical state at a given authoritative simulation instant.

Proposed authority metadata:

```
Authority
├── region_id
├── owner_node_id
├── ownership_epoch
└── authority_status
```

Ownership epoch is a fencing token.

Every authoritative mutation must validate the current epoch.

When migration completes:

\\[ E\_{\text{new}}=E\_{\text{old}}+1 \\]

Messages carrying the old epoch are rejected.

Importantly, an ownership transfer does not increment EntityID.

It also does not automatically imply a physical state change.

For entities whose state is divided between specialized domains, each mutable capability must have an explicit authoritative owner and coordination contract. A single EntityID must not imply that one process owns every aspect of the entire object.

### F12. Revision and version architecture

This area needs particularly precise separation.

I recommend five revision domains.

| Version            | Scope               | Purpose                                  |
| ------------------ | ------------------- | ---------------------------------------- |
| EntityRevision     | Entity              | Committed logical state changes          |
| CapabilityRevision | Capability instance | Component-specific state changes         |
| OwnershipEpoch     | Authority           | Fencing and ownership generations        |
| WorldRevision      | World/branch        | Committed world mutations                |
| SnapshotSequence   | Replication stream  | Transport ordering and baseline tracking |

Additionally, schema versions and simulation ticks must be separate metadata.

EntityRevision

Monotonically increases for committed entity-state changes within a branch.

CapabilityRevision

Allows independently evolving capabilities without requiring unrelated components to update.

OwnershipEpoch

Changes on authority transfer. It is not a state revision.

WorldRevision

Tracks committed world mutations, not every physical integration step.

SnapshotSequence

Tracks network delivery and baseline progression, not canonical physical state.

A physical simulation tick may produce a new EntityRevision even when no gameplay-level world mutation occurs.

The exact batching policy for component revisions should be defined by the runtime, but it must never permit stale state to overwrite newer authoritative state.

### F13. Replication and presentation separation

Adopt W07's three representations.

1\. Authoritative state

Rust-owned canonical state.

EntityRevision, simulation instant, reference frame, authority epoch and capability state.

2\. Presentation state

Client-side interpolation, prediction, reconciliation and visual correction.

3\. Rendering state

Unity Transform, mesh, animation, camera-relative coordinates, visual LOD and effects.

The network protocol must not become the canonical persistence schema.

The replication layer may use:

- Specialized entity packets.
- Quantized coordinates.
- Delta compression.
- Reduced capability subsets.
- Interpolated snapshots.

The canonical state must remain reconstructable independently of those transport optimizations.

### F14. Persistence, migration and replay

A complete simulation checkpoint must preserve more than the visible physical state.

It must preserve:

- Entity identity.
- Lifecycle.
- Canonical spatial and temporal state.
- All persistent capabilities.
- Capability schema versions.
- Relevant solver state.
- Random-generator state where applicable.
- Simulation model versions.
- Ownership metadata.
- Entity and capability revisions.
- Branch context.
- Required dependency references.

This is distinct from a network snapshot.

Migration

Migration must transfer the complete state required to continue authoritative execution.

Recommended sequence:

1. Source identifies migration.
2. Target prepares the required capability and solver environment.
3. Source produces a consistent checkpoint.
4. Target validates identity, revisions and checkpoint integrity.
5. Ownership epoch advances.
6. Target becomes authoritative.
7. Source is fenced.
8. Replication subscriptions are updated.

Migration must not create a new logical entity.

Branching

A branch retains the same EntityID for inherited entities.

It has a separate BranchID and independent revision history.

Use copy-on-write or immutable checkpoint references as proposed by W06.

A branch merge must validate its mutations against the target branch's current state rather than blindly applying historical events.

### F15. Proposed canonical interfaces

These are logical architecture interfaces, not implemented APIs.

| Interface             | Responsibility                                           |
| --------------------- | -------------------------------------------------------- |
| EntityRegistry        | Resolve and register persistent identities               |
| EntityLifecycle       | Manage entity creation, activation and destruction       |
| CapabilityRegistry    | Resolve capability definitions and versions              |
| CanonicalStateAccess  | Retrieve authoritative entity state                      |
| ReferenceFrameService | Resolve spatial transformations                          |
| TimeService           | Resolve simulation instants and astronomical time scales |
| EntityAuthority       | Validate ownership and fencing epochs                    |
| EntityRevisionService | Validate and advance entity revisions                    |
| StateSerializer       | Serialize canonical persistent state                     |
| StateCheckpoint       | Capture complete restorable runtime state                |
| FidelityTransition    | Change simulation representation                         |
| ReplicationProjection | Build client-specific state projections                  |
| EntityLineage         | Resolve branch and historical relationships              |

No interface should require Unity types, ECS handles or solver-specific data structures.

## G. Cross-system impact

### G1. Documents that remain compatible

Compatible

- W01: Hierarchical coordinates and explicit reference frames remain valid.
- W02: ECS and specialized physics systems remain valid.
- W03: Capability composition and persistent physical identity remain valid.
- W04: Celestial entity representations remain valid.
- W07: Specialized replication and three client-state representations remain valid.
- W08: Environmental provenance and scientific reproducibility remain valid.
- W09: Fidelity levels, regional ownership and migration remain valid.

No fundamental redesign is necessary.

### G2. Documents requiring clarification

| Document | Required clarification                                           |
| -------- | ---------------------------------------------------------------- |
| W01      | Canonical spatial state versus coordinate encoding               |
| W02      | Persistent EntityID versus ECS runtime handle                    |
| W03      | Capability ownership and serialization                           |
| W04      | Astronomical identity, time scales and physical model provenance |
| W05      | Snapshot versus checkpoint and revision domains                  |
| W06      | Entity revision versus world revision and ownership epoch        |
| W07      | Canonical state versus replication projection                    |
| W08      | Environmental temporal validity versus entity temporal validity  |
| W09      | Entity identity during fidelity transitions and migration        |

### G3. Documents requiring architectural changes

I recommend targeted changes rather than rewriting worker documents.

W05 — Required

The existing EntitySnapshot must be classified explicitly as a replication or state-transfer representation, not the complete universal entity schema.

Introduce a distinction between:

- ReplicationSnapshot.
- CanonicalEntityState.
- SimulationCheckpoint.

W06 — Required

Define EntityRevision independently of WorldRevision and OwnershipEpoch.

Clarify branch identity and entity lineage.

W09 — Required

Clarify that fidelity transitions do not change EntityID.

Define how capability state is preserved or reconstructed during L0–L3 transitions.

W02 — Required

Clarify that the ECS entity identifier is a runtime handle, not the persistent EntityID.

### G4. Decisions requiring coordination

| Decision                                 | Owners          |
| ---------------------------------------- | --------------- |
| Global frame registry                    | W01 + W02 + W04 |
| Six-dimensional transformation semantics | W01 + W02 + W03 |
| Astronomical epoch and time conversion   | W01 + W04       |
| Physical component schema                | W02 + W03       |
| Entity persistence format                | W02 + W05 + W06 |
| Ownership and fencing                    | W05 + W06 + W09 |
| Fidelity transition                      | W03 + W04 + W09 |
| Branch and replay semantics              | W02 + W05 + W06 |
| Replication projection                   | W05 + W07       |
| Environmental temporal consistency       | W01 + W08       |

These interfaces should be coordinated before architecture freeze.

## H. Required architectural decisions

The following ADRs are proposed for central integration.

| ID             | Decision                   | Recommendation                                                  |
| -------------- | -------------------------- | --------------------------------------------------------------- |
| ADR-ENTITY-001 | Persistent entity identity | Immutable 128-bit EntityID                                      |
| ADR-ENTITY-002 | Runtime identity           | Separate ECS handles from EntityID                              |
| ADR-ENTITY-003 | Entity composition         | Versioned capability composition                                |
| ADR-ENTITY-004 | Universal entity schema    | Minimal canonical envelope                                      |
| ADR-ENTITY-005 | Physical state             | Optional domain-owned capabilities                              |
| ADR-ENTITY-006 | Spatial state              | Explicit frame-qualified coordinates                            |
| ADR-ENTITY-007 | Kinematic state            | Six-dimensional frame-aware transformations                     |
| ADR-ENTITY-008 | Temporal semantics         | Integer SI-based simulation timeline                            |
| ADR-ENTITY-009 | Lifecycle                  | Explicit persistent lifecycle state machine                     |
| ADR-ENTITY-010 | Fidelity                   | Runtime representation independent of identity                  |
| ADR-ENTITY-011 | Ownership                  | Versioned authority with fencing epochs                         |
| ADR-ENTITY-012 | Revisioning                | Separate entity, component, world and authority versions        |
| ADR-ENTITY-013 | Replication                | Specialized projections of canonical state                      |
| ADR-ENTITY-014 | Persistence                | Separate canonical state, replication snapshots and checkpoints |
| ADR-ENTITY-015 | Branching                  | Stable inherited identity with independent branch history       |
| ADR-ENTITY-016 | Capability evolution       | Explicit schema versioning and migration contracts              |
| ADR-ENTITY-017 | Provenance                 | Versioned simulation models and input provenance                |

### H1. Priority

P0 — Architecture blockers

Critical

ADR-ENTITY-001 through ADR-ENTITY-008, ADR-ENTITY-011 and ADR-ENTITY-014.

P1 — State consistency

High

ADR-ENTITY-009, ADR-ENTITY-010, ADR-ENTITY-012, ADR-ENTITY-013 and ADR-ENTITY-015.

P2 — Evolution and provenance

Normal

ADR-ENTITY-016 and ADR-ENTITY-017.

The integrator should accept or reject these decisions individually.

Accepting the overall architecture without resolving the P0 dependencies would leave important contracts ambiguous.

## I. Validation plan

The architecture must be validated through invariants and cross-domain tests, not just serialization tests.

No benchmark results are claimed here.

### I1. Identity invariants

| Test                        | Expected result                    |
| --------------------------- | ---------------------------------- |
| Destroy and restore entity  | Same EntityID                      |
| ECS handle recycling        | No identity collision              |
| Region migration            | Same EntityID                      |
| Surface-to-orbit transition | Same EntityID                      |
| L0-to-L3 transition         | Same EntityID                      |
| Branch creation             | Inherited entities retain identity |
| Branch-only entity creation | New EntityID                       |
| Entity cloning              | New EntityID with lineage          |

### I2. Coordinate and physical-state invariants

Test 1 — Rotating planet

Create a stationary object on a rotating planet.

Verify:

- Zero velocity in the body-fixed frame.
- Correct nonzero inertial velocity.
- Correct conversion in both directions.
- No artificial energy or momentum discontinuity caused by a frame change.

Test 2 — Surface-to-orbit

Move one vehicle through:

- Planet-fixed frame.
- Local tangent frame.
- Body-centered inertial frame.

Verify continuity of position, velocity and orientation.

Test 3 — Astronomical scales

Test:

- Local mechanical precision.
- Planetary positioning.
- Orbital distances.
- Barycentric positioning.

Verify that coordinate conversion does not depend on Unity's floating origin.

Test 4 — Reference-frame ambiguity

Attempt to load a state without ReferenceFrameID.

Expected: reject as invalid.

### I3. Revision and authority invariants

1. An old ownership epoch cannot commit mutations.
2. A stale entity revision cannot overwrite newer state.
3. A component revision cannot silently overwrite a newer component version.
4. A world revision does not need to increase for every physical integration substep.
5. A migration cannot produce two authoritative owners.
6. Duplicate commands remain idempotent.

### I4. Persistence and replay

Test:

- Entity creation.
- Capability addition.
- Capability removal.
- Physical state modification.
- Entity destruction.
- Checkpoint.
- Restore.
- Replay.
- Branch creation.
- Branch mutation.
- Branch merge.
- Crash during migration.

Expected result: the restored state matches the state represented by the accepted checkpoint and subsequent event history, within the defined determinism and numerical tolerances.

### I5. Capability evolution

Test an entity with:

- Spatial.
- Kinematic.
- RigidBody.
- Propulsion.
- VariableMass.

Then introduce a new capability version.

Verify:

- Existing components remain readable.
- Unknown optional capabilities can be preserved by compatible pass-through storage.
- Unsupported mandatory capabilities are rejected explicitly.
- Migration does not silently destroy state.

### I6. Fidelity transitions

Test a vehicle across L0, L1, L2 and L3.

Measure:

- Position discontinuity.
- Velocity discontinuity.
- Mass conservation.
- Momentum consistency where applicable.
- State reconstruction error.
- Energy deviation.
- Recovery time.
- Lost or approximated information.

Different domains must define their own acceptable tolerances.

### I7. Performance validation

The canonical contract should not force a complete entity serialization for every tick.

Measure:

| Metric                       | Purpose                    |
| ---------------------------- | -------------------------- |
| Bytes per active entity      | Memory efficiency          |
| Entity lookup latency        | Identity resolution        |
| Capability lookup latency    | Composition overhead       |
| Serialization throughput     | Persistence and networking |
| Checkpoint creation time     | Recovery performance       |
| Checkpoint restoration time  | Recovery performance       |
| Migration payload size       | Distributed execution      |
| Revision validation overhead | Consistency cost           |
| Replication projection cost  | Network scalability        |
| Fidelity conversion cost     | Multi-level simulation     |

Test with different entity compositions, not merely homogeneous rigid bodies.

### I8. Cross-worker integration tests

Mandatory end-to-end scenarios

1. One vehicle travels from a planetary surface into orbit without changing identity.
2. A spacecraft migrates between two simulation nodes during orbital propagation.
3. A building receives construction, physical and economic capabilities.
4. A celestial body switches between analytical and numerical simulation.
5. A distant vehicle transitions from individual simulation to aggregated traffic flow.
6. A client changes its floating origin while observing an unchanged authoritative entity.
7. A historical branch modifies a persistent entity without affecting the canonical branch.
8. A failed simulation node is recovered using a checkpoint and event history.

These scenarios jointly exercise W01–W09.

## J. Unresolved questions

The architecture can establish the common contract, but several decisions require coordination or additional research.

### J1. Entity identifier generation

Should the final implementation use:

- UUIDv7.
- UUIDv4.
- A custom 128-bit identifier.

My recommendation is to keep the contract opaque and 128-bit, leaving the generation algorithm to a separate ADR.

A deterministic identity-generation strategy may be useful for reproducible world generation, but must not create collisions across branches or independent worlds.

### J2. Entity relationships

W04 uses astronomical parent relationships. W03 uses physical composition. W06 uses ownership and capability relationships.

These should be different relationship types.

The architecture needs a typed relationship graph supporting:

- Spatial attachment.
- Physical articulation.
- Containment.
- Ownership.
- Gravitational relationships.
- Lineage.
- Logical dependencies.

A single `parent_id` is insufficient to express all of these relationships.

### J3. State ownership inside complex entities

A spacecraft may have propulsion, navigation, power, thermal and orbital subsystems.

Should one region owner control all mutable capabilities, or can some capabilities have separate authorities?

My recommendation is one authoritative region owner for the entity's integrated physical state, with explicit ownership contracts for independently distributed domain data.

This requires further evaluation for highly coupled distributed simulations.

### J4. Physical state at low fidelity

When an entity transitions to L2, which physical quantities remain continuously maintained?

For example:

- Position.
- Velocity.
- Mass.
- Fuel.
- Damage.
- Temperature.
- Structural integrity.

This must be defined by individual capability transition contracts, not by a universal L2 schema.

### J5. Branch identity and external integrations

How should external systems refer to an entity when the same EntityID exists in multiple branches?

My recommendation:

```
EntityReference =
  WorldID + BranchID + EntityID
```

This provides an unambiguous state-context reference without changing persistent identity.

### J6. Temporal representation

The exact internal integer resolution and maximum simulation duration must be selected.

Nanoseconds are a reasonable initial candidate, but numerical integration may use finer internal time representations.

The important architectural requirement is to avoid floating-point timestamps as the authoritative identity of simulation instants.

### J7. Revision semantics

The exact policy for incrementing EntityRevision during high-frequency physical integration remains open.

Two viable policies:

1. Increment on every committed authoritative tick.
2. Increment on every externally visible committed state version, with internal substep versions maintained separately.

I recommend the second only if checkpoint and rollback semantics can preserve the intermediate solver state unambiguously.

### J8. Complete checkpoint ownership

W02 owns solver-specific restoration requirements. W05 owns server persistence architecture.

They must jointly define which data is part of the canonical entity checkpoint and which data belongs to a region-level simulation checkpoint.

### J9. Capability compatibility

The long-term system needs a formal compatibility policy for:

- Old capability schemas.
- Unknown optional capabilities.
- Removed capabilities.
- Cross-version migration.
- Historical replay after model upgrades.

This should become a dedicated architecture decision before persistence implementation.

## K. Final assessment

Architecture status

Resolved conditionally

The canonical entity architecture is sufficiently defined to serve as the common design direction for Planetary Engine.

It is not yet ready for unconditional architecture freeze because several contracts depend on decisions owned by W01, W02, W04, W05, W06 and W09.

### K1. What has been resolved

1. Persistent entity identity is separated from runtime identity.
2. Entity lifecycle is separated from simulation fidelity.
3. Physical state is separated from spatial encoding.
4. Reference-frame transformations are explicitly defined.
5. Astronomical and planetary entities share one identity architecture.
6. Specialized physical capabilities replace universal rigid-body assumptions.
7. Ownership epochs are separated from state revisions.
8. Replication and rendering are separated from authoritative state.
9. Branching and migration preserve entity identity.
10. Complete simulation checkpoints are distinguished from network snapshots.

### K2. What remains conditional

The following dependencies must be accepted by their respective owners:

- W01/W02: authoritative spatial and transformation contracts.
- W02/W03: common physical-state semantics.
- W04: astronomical epoch and reference-frame integration.
- W05: persistence and checkpoint schemas.
- W06: revision and branch semantics.
- W09: distributed ownership and fidelity transitions.

### K3. Final recommendation to the architecture integrator

Accept Alternative C as the global entity architecture.

Do not create another independent entity model in W02, W03, W04, W05 or W09.

Instead, establish one canonical contract and let each subsystem define its specialized capabilities and runtime representations.

The most important rule to preserve throughout the project is:

> One persistent logical entity can have multiple physical representations, multiple computational fidelity levels, multiple coordinate representations and multiple presentation representations — without becoming multiple entities.

This is the foundation that allows a car, human, spacecraft, planet, factory and future simulation objects to coexist in one persistent world.

## Final handoff

GPT-04 deliverable

Architecture proposal complete

- Sections A–K completed.
- Three architecture alternatives evaluated.
- Seventeen proposed ADRs defined.
- Dependencies across W01–W09 identified.
- Cross-system validation scenarios specified.
- No implementation or repository modifications proposed.

Recommended next action: central integrator reviews ADR-ENTITY-001 through ADR-ENTITY-017 and coordinates the P0 decisions with the other workers before architecture freeze.

Principal architectural conclusion: Planetary Engine should have one persistent entity identity system, one canonical state contract and many specialized simulation domains. The common contract establishes coherence; it must never force all domains to use the same physical model, solver, data layout or simulation frequency.
