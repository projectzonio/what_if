PLANETARY ENGINE — Integrated Architecture Assessment

Repository archive successfully inspected
 Formal baseline not yet ready

Role: Principal Systems Architect — Cross-Document Integration
Repository: projectzonio/what_if
Analysis date: 16 November 2026
Scope: W01–W09, GPT-01–GPT-08, supporting audit and integration documents.
0. Scope and source verification
I inspected the uploaded what_if-main.zip directly.

The archive contains all the primary materials required for the integration task:
Source	Availability	Contents
W01–W09 originals	Present	Nine original architecture proposals
W01–W09 supplements	Present	Nine supplementary research documents
GPT-01–GPT-08	Present	Eight independent resolution proposals
W10 review	Present	Initial cross-system audit
CONFLICTS.md	Present	Conflict register
OPEN_QUESTIONS.md	Present	Unresolved architectural and user decisions
Integration contracts	Present	Interface index and dependency graph
ADR directory	Present	ADR documents, currently templates rather than completed accepted decisions

An important distinction: the archive contains an initial W10 audit and the first round of supplements. It does not establish that the second integration audit has already been completed.

I treat the eight GPT documents as independent architectural proposals, not accepted decisions. Likewise, recommendations in CONFLICTS.md and OPEN_QUESTIONS.md are not automatically binding.

The following is my integrated architectural assessment, rather than a simple aggregation of the eight recommendations.

1. Integrated architecture overview
1.1 Central architectural decision
Planetary Engine should be designed as:

One persistent logical world, with a shared temporal and spatial foundation, multiple specialized simulation domains, independently managed physical execution islands, and distributed regional authority.

The architecture must distinguish five things that the original documents sometimes conflate:
1. Logical identity of the world and its entities.
2. Physical state and its representation.
3. Authority over state transitions.
4. Numerical execution of simulation domains.
5. Presentation and replication of state.
These are related but must not have identical ownership or lifecycle.

The most important architectural principle emerging from the integration is:
One coherent world does not require one global simulation loop, one physics solver, one execution frequency, one ownership partition or one globally synchronized computational process.

This is the foundation upon which all other decisions should be built.
1.2 Integrated logical architecture
World Identity and Timeline

WorldId · BranchId · WorldInstant · causal history

Spatial Runtime

Reference frames · planetary geometry · coordinate transformations


Time Authority

Domain clocks · scheduling · temporal barriers · time scales



Physical and Environmental Providers

Celestial dynamics · gravity · atmosphere · oceans · terrain

Authoritative Simulation Runtime — Rust

Physics islands · entity mechanics · specialized solvers · state transitions

World Mutation

Commands · revisions · dependency projections


Distributed Authority

Regions · ownership · fencing · migration



Persistence and Replication

Journal · snapshots · recovery · Unity state replication

Unity

Presentation


Node.js

Control plane


Python

Scientific tools




This is a logical responsibility diagram, not a deployment diagram. It does not imply that every box must be a separate process or service.
1.3 Ownership boundaries
Domain	Primary authority
World identity and branches	World Runtime
Canonical world time	Time Authority
Reference-frame graph	Spatial Runtime
Planetary geometry	Spatial Runtime
Celestial orientation and orbital models	Celestial Runtime
Gravity providers	Gravity Service
Gravity composition and application	Physics Runtime
Environmental fields	Environment Runtime
Physical integration	Physics Runtime / authorized island solver
Specialized vehicle and character models	Entity Mechanics
Region ownership	Ownership Coordinator
Connected physical systems	Physics Island Authority
World mutation	Mutation System
Historical persistence	Journal and Persistence
Client presentation	Unity
Orchestration	Node.js
Scientific validation	Python

A crucial consequence: the system must not use region ownership as a substitute for physical authority.

Similarly, a physics island must not become a replacement for the world's spatial partitioning system.
2. Decision matrix — GPT-01 through GPT-08
The eight proposals address eight different architectural conflicts. They are complementary in scope, but several of their solutions overlap.

My assessment:
Proposal	Problem	Integrated decision	Status
GPT-01	World time and timelines	One logical timeline, regional committed-time watermarks, causal synchronization	Accept conditionally
GPT-02	Gravity ownership	Specialized gravity providers, one composition contract, one application by Physics	Accept conditionally
GPT-03	Regions vs. physics islands	Independent spatial and physical authority	Accept conditionally
GPT-04	Entity identity and state	Persistent identity separated from versioned physical state and runtime representation	Accept conditionally
GPT-05	World mutation lifecycle	Authoritative journal, asynchronous projections, explicit activation barriers	Accept conditionally
GPT-06	Multirate scheduler	Shared logical time, domain-specific execution frequencies	Accept conditionally
GPT-07	Determinism and replay	Hierarchical determinism guarantees	Accept conditionally
GPT-08	Fidelity transitions	Domain-specific, ownership-safe and conservation-aware transitions	Accept conditionally

None should be declared fully accepted before the shared contracts and dependencies below are resolved.
2.1 GPT-01 — World Time and Timeline
Recommendation: One canonical logical world timeline, separate regional execution progress and wall-clock time.

This is correct and compatible with W01, W04, W07 and W09.

Three separate temporal concepts must exist:
- WorldInstant: position on the logical world timeline.
- Regional watermark: furthest committed simulation instant for a region.
- WallClock: real elapsed time used for operational deadlines and distributed coordination.
A fourth concept is required: BranchId, identifying a particular historical or What-If timeline.
Required integration correction
GPT-03 proposes a physics island with its own committed simulation timeline.

This must not mean an independent world timeline.

A physics island has its own execution progress and step sequence, but its timestamps must belong to the same logical timeline as the world containing it.

Otherwise, interactions between physics islands cannot be interpreted consistently.

Decision: Accept the temporal separation, reject independent island time coordinates.

Sources: GPT-01 §A–F; W01 §2–3; W09 §5; W06 §8.
2.2 GPT-02 — Gravity and Physical Authority
Recommendation: Specialized gravity providers with a shared evaluation and composition contract.

The division of responsibility should be:
Responsibility	Owner
Planetary gravity models	W01 domain
Astronomical gravity models	W04 domain
Gravity composition	Gravity Service
Non-inertial frame corrections	Spatial Runtime supplies frame kinematics; Physics applies corrections
Force evaluation	Physics Runtime
Integration	Authoritative physical solver

An important correction from CONFLICTS.md C-07:

Coriolis, centrifugal and Euler accelerations must not become independent environmental gravity providers.

They are consequences of reference-frame kinematics.

This avoids double application when an object is evaluated in a rotating frame.

Gravity and other forces must also remain separate concepts. Atmospheric drag, radiation pressure and rocket exhaust cannot be silently mixed into the gravity API.

Decision: Accept GPT-02's provider architecture, with the W01 frame-kinematics correction.

Sources: GPT-02 §A–F; W01 §2.5; W04 §3.3; W03 §4.3; CONFLICTS.md C-07 and NC-11–NC-15.
2.3 GPT-03 — Distributed Regions and Physics Islands
Recommendation: Physics Island Authority Model (PIAM).

This is one of the most important proposals in the entire repository.

The spatial partition and physical constraint graph are fundamentally different structures.

Example:
- Region A belongs to process 1.
- Region B belongs to process 2.
- A train occupies both regions.
- Its connected rigid-body system must be solved consistently.
Neither region should independently integrate its portion of the train.

The solution is an independent physics island authority.
Integrated ownership model
Concept	Meaning
Region	Spatial and workload partition
Region owner	Regional execution and coordination
Entity authority	Authoritative entity lifecycle and non-physical state
Physics island authority	Connected physical state and constraint integration
Process	Execution location, not logical identity

The island may span regions, but it has one authoritative integration owner for a given committed step.
Important correction
GPT-03's proposal for an island-specific committed timeline must be interpreted as an island-local execution sequence on the shared world timeline.

It must not introduce independent clocks.

Also, contact between independently owned islands requires a distinct boundary protocol. A single-owner rule for connected constraints does not automatically solve collisions between separate islands.

Decision: Accept PIAM conditionally. Require a formally specified island-to-island contact protocol and island migration protocol.

Sources: GPT-03 §A–F; W02 §9.1–9.2; W05 §2.6; W09 §5; CONFLICTS.md C-01, C-05, C-06.
2.4 GPT-04 — Entity Identity and Physical State
Recommendation: Canonical Entity Contract.

An entity must remain the same logical object when:
- Its physical solver changes.
- It moves between regions.
- Its simulation fidelity changes.
- Its reference frame changes.
- Its runtime process changes.
- Its Unity representation is recreated.
- Its world history branches.
This requires separation of:
1. Persistent identity.
2. Versioned authoritative state.
3. Simulation capabilities.
4. Runtime representation.
5. Ownership and authority metadata.
Integration issue: state ownership
The canonical entity contract cannot imply that one universal component owns every entity property.

For example:
- Physics owns physical integration state.
- Mutation owns structural revision.
- Gameplay owns its domain-specific state.
- Ownership Coordinator controls fencing.
- Entity identity remains stable across all of them.
I recommend a versioned aggregate state contract with explicitly owned substate.

The state contract must identify the version and temporal validity of every independently authoritative portion.

A single global state_version would create unnecessary coupling.

Decision: Accept GPT-04's identity/state separation. Require explicit component-level authority and version semantics.

Sources: GPT-04 §A–F; W02 §3; W03 §2–3; W05 §3; W06 §10; W07 §11.
2.5 GPT-05 — World Mutation and Consistency Lifecycle
Recommendation: Dependency-Driven Mutation Lifecycle.

This is architecturally preferable to treating a world mutation as a single operation that instantly updates every subsystem.

Three stages must remain distinct:
1. Authoritative commitment.
2. Derived-state convergence.
3. Operational activation.
Consider construction of a bridge.

The mutation may be committed, but:
- Collision geometry may still be generating.
- Navigation data may still be updating.
- Hydrology may still need recalculation.
- Unity may still have an older visual representation.
The system therefore needs explicit dependency and readiness information.
Critical cross-system correction
Mutation must not directly own every derived physical representation.

The recommended ownership is:
- Mutation records the structural fact and revisions.
- Physics constructs authoritative collision and physical representations.
- Environment updates its affected projections.
- Navigation updates traversability.
- Replication publishes suitable versions.
- Persistence records the authoritative history and checkpoints.
This preserves subsystem authority.

A mutation commit is not equivalent to all dependent systems being ready.

Decision: Accept GPT-05. Require dependency-specific activation barriers rather than one universal global completion barrier.

Sources: GPT-05 §A–F; W06 §5 and §10; W02 §5; W08 §5; W07 §7.
2.6 GPT-06 — Multirate Simulation Scheduler
Recommendation: Hierarchical Multirate Simulation Scheduler.

This resolves a major contradiction in the originals.

The architecture cannot simultaneously require:
- One global fixed 60 Hz execution frequency.
- Adaptive orbital integration.
- Low-frequency environmental simulation.
- Event-driven gameplay.
- Independent replication rates.
The correct solution is one logical world time and multiple execution frequencies.
Domain	Appropriate scheduling model
Interactive rigid bodies	Fixed-step integration
Vehicles	Fixed-step with optional substeps
Character movement	Fixed-step
Orbital mechanics	Adaptive integration
Global climate	Long-period or adaptive
Regional weather	Multirate
Local fluid dynamics	Domain-specific
Economy	Event-driven
Gameplay	Event-driven and periodic
Replication	Independent scheduling
Rendering	Client presentation clock

Critical integration rule
The scheduler coordinates temporal dependencies and execution eligibility.

It does not own the numerical algorithms of the individual domains.

For example, an orbital solver may request an adaptive step, but its maximum allowed step and required synchronization constraints must be declared by its domain.

The scheduler must enforce those constraints.

Decision: Accept GPT-06. Reject a universal execution tick.

Sources: GPT-06 §A–F; W02 §3; W03 §6.3; W04 §3.4; W08 §6; W09 §2.
2.7 GPT-07 — Determinism, Replay and Reproducibility
Recommendation: Hierarchical Determinism Contract.

The proposed three guarantees are useful:
Level	Guarantee	Application
D1	Authoritative ordering and state	Entire persistent world
D2	Numerical reproducibility within tolerance	Physics and scientific domains
D3	Bitwise-identical replay	Selected discrete systems

The important insight is that deterministic authority does not require identical floating-point results on every processor.

However, this proposal must be coordinated with GPT-05.

A mutation journal cannot be treated as a complete record of every numerical operation in a continuously evolving physical simulation.

The integration architecture should use:
- Immutable authoritative events.
- Versioned checkpoints.
- Recorded external inputs.
- Domain-specific replay records.
- Explicit solver and model versions.
- Defined numerical tolerances.
Major unresolved issue
Recovery and replay across multiple regions must restore a consistent causal cut.

Simply restoring individual region snapshots can produce a world that never existed.

For example, one region may restore a snapshot from before a cross-region event while another restores a snapshot from after it.

That is not a valid recovery state.

Decision: Accept GPT-07 conditionally. Recovery consistency must become a mandatory shared contract with GPT-01, GPT-03 and GPT-05.

Sources: GPT-07 §A–F; W05 §3–4; W06 §8; W09 §5; CONFLICTS.md NC-37 and NC-46.
2.8 GPT-08 — Simulation Fidelity Transitions
Recommendation: Simulation Fidelity Transition Framework.

This is the correct way to reconcile the different fidelity models in W02, W03, W04, W08 and W09.

Three operations must not be confused:
- Representation change.
- Simulation fidelity transition.
- Actual physical state change.
For example, a vehicle changing from full tire dynamics to simplified vehicle dynamics is a fidelity transition.

A vehicle colliding with a wall is a physical state change.

The framework must not own the vehicle's physics or decide the physical outcome of the collision.

It coordinates model transitions and verifies the associated contracts.
Integrated fidelity model
I recommend retaining:

FidelityLevel = (SimTier, ModelVariant)

With distinct concepts for:
- Spatial LOD.
- Environmental LOD.
- Physical simulation tier.
- Numerical model variant.
- Domain/integrator.
Critical missing requirement
A fidelity transition must be a versioned, temporally valid state transformation.

It must identify:
- Source model.
- Target model.
- Source state version.
- Effective world instant.
- State mapping.
- Conserved quantities.
- Explicitly discarded or approximated information.
- Error bounds.
- Ownership authority.
- Successful activation.
The transition must not create two authoritative representations.

Decision: Accept GPT-08 conditionally. Require conservation and recovery contracts to be coordinated with Physics, Environment and Persistence.

Sources: GPT-08 §A–F; W02 §3; W03 §6; W04 §3.4; W08 §6; W09 §3.

3. Shared contract specifications
This is the most important part of the integration.

The eight proposals cannot be merged merely by adopting their terminology. They need common semantics.
3.1 Canonical time
Recommended contract:
Field	Meaning
WorldId	Persistent logical world
BranchId	Timeline branch
WorldInstant	Canonical logical world time
WorldDuration	Duration in canonical time units
RegionWatermark	Region's committed temporal progress
IslandStep	Physical island execution sequence
WallClock	Operational real time
DomainTick	Domain-specific execution sequence

Recommendation: Use W01's proposed i128 canonical representation, subject to validation of the quantum and epoch.

The proposed J2000 TT epoch and \(q=1/(1.44 \times 10^9)\) seconds are architectural proposals, not yet accepted baseline decisions.

The wire protocol should support compact relative time encoding without changing canonical semantics.
3.2 Spatial and physical state
The canonical spatial representation should use:
- Reference frame.
- Anchor.
- Cell coordinates.
- Local coordinates.
- Explicit units.
- Explicit frame transformation semantics.
Recommended structure:

SpatialState = (FrameId, AnchorId, Cell[i64;3], Local[f64;3])

The proposed cell size is \(2^{15}\) metres.

ECEF should be treated as a derived coordinate representation, not the sole canonical state representation.

Physical state must additionally contain:
- Position and orientation.
- Linear and angular velocity.
- Mass and inertia.
- Relevant force and impulse state.
- Temporal validity.
- Physical model identity.
- Ownership metadata.
- Version information.
An important distinction: entity identity must survive even when the entity temporarily has no active physical representation.
3.3 Gravity and environmental contracts
The architecture needs separate contracts for:

GravitySample

EnvironmentSample

FrameTransform

PhysicalForceContribution

WaterSample

The gravity provider returns gravitational contributions.

The reference-frame system supplies frame kinematics.

Physics composes and applies the resulting accelerations exactly once.

Environment provides sampled conditions, not ownership of all forces acting on entities.

For example, Environment supplies wind velocity and atmospheric properties. Entity Mechanics calculates aerodynamic forces. Physics applies those forces.

This division is essential for extensibility.
3.4 Distributed authority
The integration must distinguish three operations:
Operation	Meaning
RegionHandoff	Region changes executing process
EntityTransfer	Entity or complete physical island changes region ownership
DomainHandoff	Entity changes simulation domain without necessarily changing region

These must not be represented by one generic migration operation.

The ownership system needs fencing tokens, incarnation identifiers and explicit transfer generations.

The physical island transfer must include the complete solver state, including constraint manifolds and warm-start information where required.
3.5 Mutation and revision semantics
The repository currently contains conflicting revision models.

The integration should use:
- owner_epoch: ownership fencing.
- entity_generation: entity transfer generation.
- state_version: physical state revision.
- structure_revision: structural mutation revision.
- RevisionVector: per-region and per-subsystem revision context.
A scalar global world_revision should not be used as the universal version of all world state.

This is particularly important because multiple regions can commit independently.
3.6 Persistence and recovery
Persistence needs two complementary mechanisms:
1. Authoritative journal for commands, mutations, transfers, checkpoints and other significant events.
2. Domain-specific snapshots and replay for continuous simulation state.
The architecture must not attempt to journal every floating-point operation globally.

Recovery must restore a consistent causal cut, including:
- Region ownership epochs.
- Region snapshots.
- Physics island state.
- Cross-region messages.
- Event sequence positions.
- Domain execution progress.
- Branch identity.
The recovery procedure must not move a region's committed watermark backwards while leaving neighboring regions in a causally later state.
3.7 Unity replication
Unity remains a presentation client.

It receives versioned authoritative snapshots and relevant events.

It may predict selected interactions, but it cannot become the authority over persistent world state.

The canonical replicated state should use world time, reference-frame identity, revision metadata and ownership-safe versioning.

The client must also be able to reconcile prediction across floating-origin changes.

4. Contradiction register
The existing audit/CONFLICTS.md is a valuable starting point. It identifies conflicts C-01 through C-09 between supplements and NC-01 through NC-52 between the original documents.

I would retain these identifiers rather than create a second competing register.

The following are the conflicts that matter most to the integrated architecture.

Critical
 C-01 — Ownership epochs
Conflict: W02 associates ownership with physics islands; W05 associates fencing with regions; W06 needs both identities.

Resolution: Region fencing epoch, island-level transfer unit and entity transfer generation must be separate fields.

Remaining issue: Exact token validation and atomic transfer semantics are not yet specified.

Critical
 C-02 — Canonical time representation
Conflict: W01 proposes i128; W07 proposes i64; W09 requires integer and divisible time representation.

Resolution: i128 canonical time, compact relative wire representation.

Remaining issue: Epoch, quantum, overflow behavior and astronomical conversion validation.

Critical
 C-04 — Revision semantics
Conflict: W06 introduces regional revision vectors, while W02 and W08 use scalar world revisions.

Resolution: Per-region revisions and explicit subsystem revisions.

Remaining issue: A formal revision dependency and compatibility protocol is missing.

Critical
 C-05 — Migration semantics
Conflict: W02, W03, W05 and W09 use migration/handoff to describe different operations.

Resolution: Three separate protocols: RegionHandoff, EntityTransfer, DomainHandoff.

Remaining issue: Atomic cross-region transfer of a physical island and its dependent state.

Critical
 C-06 — Cross-region collision detection
Conflict: W02 proposes local halo-based collision handling. W09 proposes swept boundary queries.

Resolution: Combine local halo processing with explicit cross-region swept collision handling.

Remaining issue: Exact temporal arbitration, simultaneous impacts and late-arriving collision information.

Critical
 C-07 — Non-inertial acceleration ownership
Conflict: W04's original force model risks mixing environmental acceleration with frame-dependent corrections.

Resolution: Spatial Runtime supplies frame kinematics; Physics applies corrections.

Remaining issue: Precise sign conventions and transformation validation across rotating and translating frames.

Critical
 C-09 — Distributed recovery
Conflict: W05 recovery and W09 message ordering do not yet define a complete consistent recovery procedure.

Resolution: Hybrid checkpoint and replay recovery.

Remaining issue: Consistent causal-cut construction, cross-region message recovery and watermark monotonicity.
4.1 Additional contradictions introduced by integration
These are not adequately resolved merely by accepting the existing recommendations.
ID	New integration issue	Severity	Required action
INT-01	Island execution time versus global timeline	Critical	Define common temporal semantics
INT-02	Physics authority versus region authority	Critical	Formalize cross-region island ownership
INT-03	Mutation commit versus derived-state activation	Critical	Define subsystem readiness and activation rules
INT-04	Determinism versus adaptive numerical integration	High	Define replay guarantees per domain
INT-05	Fidelity transition versus state conservation	High	Define domain-specific mapping contracts
INT-06	Entity state versus independently owned components	High	Formalize component-level authority
INT-07	Recovery versus What-If branching	High	Define branch-safe recovery
INT-08	Cross-island collision versus ownership	Critical	Define interaction arbitration
INT-09	Environment sampling versus domain execution	High	Define snapshot temporal validity
INT-10	Global time warp versus domain limits	High	Define scheduler enforcement
INT-11	Physical island migration versus solver internals	High	Define backend-independent migration state
INT-12	Mutation and environmental conservation	High	Define mass/energy accounting across projections

Particularly important: INT-03
The mutation lifecycle is potentially the most consequential integration issue after ownership.

Consider a terrain modification that destroys a bridge.

The authoritative mutation is committed.

But what happens if the old collision geometry remains active for another simulation step?

A vehicle may continue driving across a bridge that no longer exists.

Conversely, if the collision geometry is removed before the mutation becomes effective, the bridge disappears too early.

Therefore the architecture needs an explicit relationship between:
- Structural revision.
- Physical collision activation.
- Effective world instant.
- Region watermark.
- Replication version.
This cannot be solved simply by having every subsystem eventually receive a mutation event.

The system needs a defined consistency boundary for dependent physical representations.

5. Missing architecture
This section identifies areas that none of the eight proposals adequately closes.

These are not minor implementation details. Some are prerequisites for a coherent architecture.
5.1 Interaction authority between physics islands
GPT-03 solves the authority of connected physics islands.

It does not completely solve what happens when two independently authoritative islands collide.

Example:
- A train is owned by physics process A.
- A truck is owned by physics process B.
- The truck collides with the train.
- Both processes have independently advanced their states.
Which process owns the impact?

Who determines the collision instant?

Who commits the impulse?

What happens if the two processes disagree about the contact manifold?

A formal cross-island interaction protocol is needed.

My recommendation is an explicit interaction authority mechanism with deterministic arbitration and a temporally qualified impact record.

This must not require a global physics authority.
5.2 Atomicity across physical and structural state
A structural mutation can affect:
- Collision geometry.
- Rigid-body constraints.
- Navigation.
- Hydrology.
- Environmental fields.
- Replication.
- Persistence.
The architecture does not yet define a complete atomicity model.

The solution should not require every dependent subsystem to update simultaneously.

Instead, the system needs:
1. An authoritative structural commit.
2. A dependency graph.
3. Explicit activation requirements.
4. A rule governing which revisions are safe for physical interaction.
5. Recovery from partially completed derived updates.
This should be developed jointly by W05, W06, W02 and W08.
5.3 Physical interaction across fidelity boundaries
GPT-08 provides a general transition framework, but there is a more difficult question:

What happens when a high-fidelity object interacts with a low-fidelity object?

Example:
- A fully simulated vehicle collides with a simplified vehicle.
- A detailed aircraft interacts with a coarse atmospheric model.
- A physical spacecraft transitions from orbital propagation into a local rigid-body docking simulation.
The system needs cross-model coupling contracts.

These must define:
- State exchange.
- Error bounds.
- Conservation requirements.
- Temporal interpolation.
- Authority over interaction results.
- Transition failure behavior.
A generic fidelity transition API is insufficient.
5.4 World identity and branch semantics
GPT-01 and GPT-07 introduce branch-aware timelines, but the complete identity model is not settled.

Questions include:
- Can a branch inherit ownership metadata?
- Does a branch inherit all checkpoints?
- Are entity IDs shared between branches?
- Can a branch be merged back?
- How are external inputs represented?
- How are branch-specific mutations distinguished?
- Can a branch be promoted to the primary timeline?
This is important because What-If simulation is not just replaying the same world at a different time.

It is a separate history with shared ancestry.
5.5 Distributed transaction semantics
W05 and W06 provide foundations, but the architecture still needs to distinguish:
- Local authoritative transactions.
- Cross-region operations.
- External transactions.
- Economic transactions.
- World mutations.
- Physical state changes.
The economic ownership question OQ-07 is particularly important.

A transaction that modifies inventory and simultaneously consumes physical resources cannot be treated as two unrelated commits.

A cross-domain transaction or saga protocol must define what happens when only one side succeeds.
5.6 Entity lifecycle and ownership during creation
Rocket staging, construction, destruction and spawning create or remove entities.

The architecture needs one consistent lifecycle contract for:
- Identity allocation.
- Ownership assignment.
- Initial state.
- Branch membership.
- Parent-child relationships.
- Persistence.
- Replication.
- Destruction and tombstones.
The proposed deterministic child IDs are useful, but global uniqueness and branch semantics still need to be specified.
5.7 External scientific data provenance
W01, W04 and W08 depend on scientific datasets and models.

The architecture needs a model and data provenance contract.

Every scientifically relevant result should be traceable to:
- Model version.
- Input dataset version.
- Reference frame.
- Time standard.
- Numerical method.
- Parameter set.
- Data licensing and provenance.
Without this, scientific replay and validation cannot be reliable.
5.8 Failure isolation and overload behavior
The architecture discusses scaling, but less thoroughly defines overload semantics.

For example:
- What happens when a physics island exceeds its computational budget?
- What happens when environmental calculations cannot finish before a dependent physics step?
- What happens when a region loses its coordinator?
- Can a region continue in a read-only state?
- When is a simulation allowed to reduce fidelity?
- Which failures stop simulation versus merely degrade presentation?
This must be a first-class contract, not an implementation-specific decision.

6. Required changes to W01–W09
The objective is not to rewrite the nine original documents.

Each document should preserve its domain expertise while referencing the integrated contracts.
Document	Required changes	Priority
W01	Canonical time, coordinate and frame ownership; gravity provider boundary; revision semantics	Critical
W02	Physics island authority, cross-island interaction, migration, temporal execution and state contract	Critical
W03	Domain handoff versus entity transfer; force ownership; fidelity mapping; component authority	High
W04	Gravity separation, celestial state ownership, reference-frame orientation and time mapping	Critical
W05	Region fencing, island transfer, causal recovery, distributed transaction semantics	Critical
W06	Mutation activation barriers, regional revisions, economic transaction ownership, branching	Critical
W07	Canonical state replication, temporal metadata, prediction and floating-origin history	High
W08	Environmental revision semantics, conservation, temporal sampling and mutation coupling	Critical
W09	Shared timeline, regional watermarks, cross-region collision, GVT and recovery	Critical

6.1 Specific section updates
These are the sections I would require the integrator to update.
Document	Sections
W01	§2.3–2.8, §3, §5
W02	§3, §5, §9, §10, §11
W03	§2–4, §6, §8–9
W04	§3.3–3.4, §3.7, §5
W05	§2.4–2.6, §3–4, §6
W06	§4–5, §7–8, §10–11
W07	§4–8, §11, §13
W08	§3–6, §8–9
W09	§2–5, §8–11

The original documents must not be silently overwritten.

Each change should explicitly state:
- Original proposal.
- Accepted integration decision.
- Rejected or modified assumptions.
- New shared contract.
- Dependencies on other documents.
This preserves the research history and makes later architectural reviews possible.
7. Integrated Architecture Decision Register
I recommend introducing a single integrated decision register.

The following IDs are proposed for the central architecture baseline. They do not replace the original GPT decision identifiers.
ID	Decision	Recommendation	Status
PE-001	Canonical world timeline	One logical timeline per branch	Conditional
PE-002	Time representation	i128 WorldInstant	Conditional
PE-003	Regional temporal progress	Independent committed watermarks	Conditional
PE-004	Spatial coordinates	Hierarchical frame/cell/local representation	Conditional
PE-005	Gravity architecture	Specialized providers, unified composition	Conditional
PE-006	Physical authority	Independent physics island authority	Conditional
PE-007	Entity identity	Persistent identity independent of representation	Conditional
PE-008	Entity state	Versioned, component-owned state	Conditional
PE-009	Simulation scheduler	Hierarchical multirate scheduler	Conditional
PE-010	Determinism	Three-level determinism contract	Conditional
PE-011	Mutation lifecycle	Journal plus dependency-driven activation	Conditional
PE-012	Distributed migration	Separate region, entity and domain transfers	Conditional
PE-013	Recovery	Consistent causal-cut recovery	Needs research
PE-014	Fidelity	Domain-specific conservation-aware transitions	Conditional
PE-015	Replication	Authoritative snapshots with temporal and revision metadata	Conditional
PE-016	Environment coupling	Versioned temporal samples	Conditional
PE-017	Cross-island interaction	Explicit interaction authority protocol	Needs research
PE-018	Cross-domain transactions	Explicit transaction/saga semantics	Needs research
PE-019	Branching	Branch-aware identity, history and replay	Needs research
PE-020	Overload behavior	Explicit degradation and failure policy	Needs research

7.1 Decisions requiring explicit user approval
The following should not be silently decided by an architecture integrator because they affect the intended product and simulation scope.
ID	Question	Existing recommendation
USER-001	Real and fictional celestial worlds?	Support both
USER-002	Global multiplayer time scale?	One global scale
USER-003	Maximum multiplayer time warp?	Establish target after network testing
USER-004	Economic transaction ownership?	Region journal, or external transaction service with saga
USER-005	Cross-region construction?	Initially restrict it
USER-006	Meteorological fidelity?	Select explicit target
USER-007	Can entities modify atmospheric conditions?	One-way coupling initially
USER-008	Detailed gravity coverage?	Earth, optional Moon/Mars, simplified other bodies
USER-009	Acceptable recovery data loss?	Define separately for transactions and continuous simulation
USER-010	Client platforms?	Decide WebGL/mobile support requirements

These correspond primarily to OQ-01 through OQ-16.

8. Validation plan
Architecture acceptance must be based on measurable invariants, not on whether all documents use consistent terminology.

I recommend organizing validation into eight groups.
V1 — World time
- All regions use the same canonical time representation.
- Different regions can advance independently.
- Causally dependent events cannot be committed in an invalid order.
- Island execution progress cannot create an independent timeline.
- Time warp does not invalidate wall-clock leases.
V2 — Spatial and physical consistency
- Transformations between reference frames preserve physical state within defined tolerances.
- Rotating-frame corrections are applied exactly once.
- Cell-boundary transitions do not introduce position discontinuities.
- Gravity contributions are neither duplicated nor silently omitted.
V3 — Physics authority
- A connected physical island has exactly one authoritative integrator.
- Region migration does not create duplicate authority.
- Island migration preserves required solver state.
- Cross-island collision resolution produces one authoritative impact result.
- Collision outcomes remain consistent under different process placements.
V4 — Mutation lifecycle
- Every committed mutation has an immutable identity and revision.
- Derived updates cannot silently overwrite newer revisions.
- Physical activation occurs only at an allowed effective instant.
- Failed projections can be recovered independently.
- No subsystem uses obsolete collision geometry after its activation barrier.
V5 — Determinism and recovery
- Event ordering is stable.
- Replays reproduce the specified guarantees.
- Recovery produces a causally consistent world.
- No stale ownership epoch can commit state.
- Branch replay does not modify its parent timeline.
V6 — Fidelity
- State transitions preserve required conserved quantities.
- Numerical errors remain within declared bounds.
- A transition cannot create two authorities.
- Domain-specific models can transition independently.
- Failed transitions retain a valid authoritative representation.
V7 — Environment
- Water and mass conservation meet declared tolerances.
- Environmental samples have explicit temporal validity.
- Regional and local environmental models remain consistent at boundaries.
- Terrain mutations invalidate the appropriate projections.
V8 — Client
- Unity cannot commit authoritative world state.
- Prediction and reconciliation respect server versions.
- Floating-origin shifts do not corrupt network history.
- Different client rendering rates do not alter simulation results.
Benchmark gates
The following measurements are required before accepting performance assumptions:
Area	Required measurement
Physics	Island size, solver throughput, contact complexity
Distribution	Boundary synchronization latency
Ownership	Lease renewal and migration overhead
Scheduler	Scheduling overhead under heterogeneous rates
Environment	Sampling cost and projection latency
Persistence	Journal commit and recovery throughput
Fidelity	Transition cost and numerical error
Networking	Bandwidth, snapshot frequency and reconciliation cost

All numerical values in the existing documents must remain classified as proposed targets until measured.

9. Architecture readiness assessment
Not ready for formal baseline
My assessment of the integrated architecture:
Area	Readiness
World identity and logical time	Good foundation
Spatial representation	Good foundation
Gravity ownership	Resolved conditionally
Physics architecture	Resolved conditionally
Entity representation	Resolved conditionally
Distributed ownership	Resolved conditionally
Mutation architecture	Resolved conditionally
Simulation scheduling	Resolved conditionally
Determinism	Requires recovery contract
Fidelity	Requires cross-model coupling
Environmental simulation	Requires validation
Unity authority boundary	Good foundation
Branching and What-If	Incomplete
Cross-domain transactions	Incomplete
Failure and recovery	Incomplete

9.1 What is already architecturally strong
The project has several unusually good architectural foundations:
1. It separates the logical world from its rendering representation.
2. It does not assume that one physics solver can handle every domain.
3. It recognizes the distinction between region ownership and physical constraint connectivity.
4. It separates world time from execution frequency.
5. It does not require universal bitwise determinism.
6. It recognizes that world mutations affect multiple dependent subsystems.
7. It supports offline scientific simulation as well as interactive execution.
8. It treats Unity as a client rather than an authority.
These are worth preserving.
9.2 What prevents baseline acceptance
There are four major blockers.

Blocker 1 — Cross-island interaction authority

The architecture still needs to define the authoritative handling of contacts between independently owned physical systems.

Blocker 2 — Causal recovery

The architecture does not yet specify how all regions, physics islands and cross-region messages restore one consistent historical state.

Blocker 3 — Mutation activation

The relationship between authoritative structural changes and their dependent physical representations needs a precise temporal contract.

Blocker 4 — Cross-domain state transitions

Fidelity changes, physical interactions, economic operations and timeline branching require additional shared semantics.

These are architectural issues, not merely missing implementation details.

10. Final recommendation
Resolved conditionally
The eight proposals should be accepted as the foundation of the integrated architecture, but not yet promoted to a formal architecture baseline.

I would not restart the research or commission another eight independent architecture proposals.

The existing research is sufficiently mature to move into a focused integration-resolution phase.

My recommended sequence is:
1. Freeze the common foundations
   
   Accept the canonical time, spatial representation, entity identity and authority boundaries as provisional shared contracts.
2. Resolve distributed physical authority
   
   Complete physics island ownership, cross-island interactions, migration and collision arbitration.
3. Resolve temporal consistency
   
   Define regional watermarks, causal ordering, synchronization barriers and recovery.
4. Resolve world mutation consistency
   
   Define effective instants, revision vectors, activation barriers and recovery of derived projections.
5. Resolve fidelity and domain coupling
   
   Define conservation, cross-model interactions and temporal validity.
6. Update W01–W09 and complete ADRs
   
   Preserve original proposals while incorporating accepted shared contracts.
7. Establish the formal architecture baseline
   
   Only after critical contradictions have explicit decisions and validation criteria.
10.1 One particularly important strategic recommendation
I would resist the temptation to implement the architecture in the order suggested by individual worker documents without first settling the cross-system contracts.

The most dangerous future mistake would be to build:
- Physics islands around one ownership model.
- Persistence around another.
- Mutation around a third.
- Time synchronization around a fourth.
All four could work independently and still produce a world that cannot be reliably recovered or evolved.

The contracts must exist before their implementation, even if Stage 1 runs everything in one Rust process.

That is the architectural property that will allow Planetary Engine to grow from a single-process scientific simulation into a distributed persistent universe without replacing its foundations.

11. Final verdict
Conditionally resolved
Integrated architecture is conceptually coherent, but not yet formally consistent enough for baseline acceptance.
- Eight proposals: accepted as architectural foundations, conditionally.
- Nine original workers: retained as domain sources.
- Critical contradictions: identified.
- Shared contracts: proposed.
- Missing architecture: identified.
- Validation requirements: defined.
- Formal ADR acceptance: pending.
- Implementation: not performed.

My strongest recommendation: The next architectural worker should not research another isolated subsystem. It should resolve the four integration blockers: cross-island authority, causal recovery, mutation activation and cross-domain state transitions.

Those four problems determine whether Planetary Engine can actually behave as one persistent physical universe rather than a collection of individually sophisticated simulation systems.

The integration work should now converge toward one Master Architecture Baseline, not produce another parallel collection of competing designs.