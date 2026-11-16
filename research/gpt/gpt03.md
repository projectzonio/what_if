Máš pravdu. Ověřil jsem repository přímo: projectzonio/what_if je veřejně přístupné a obsahuje všech devět požadovaných dokumentů w1.md až w9.md.

Předchozí závěr o nutnosti nahrát ZIP byl chybný. Pokračuji přímo z GitHubu. Nejdříve provedu globální kontrolu všech devíti dokumentů, teprve potom vyřeším C-03.
První důležitý nález z repository
V dokumentech už existuje základ pro správné řešení, ale není dotažený do jednoho konzistentního kontraktu.
- W01, 5.1–5.3: výslovně odděluje physics partitions od server regions a physics islands.
- W02, 9.1–9.2: definuje fyzikální ostrovy jako propojené constraint systémy, které nelze libovolně rozdělit mezi nezávislé výpočty.
- W05, 2.6: definuje regionální ownership, epochy a migrační protokol, ale jeho model je primárně postavený kolem regionu a entity.
- W09, 5.1–5.5: požaduje lokální časové osy, boundary interfaces a omezenou konzistenci mezi regiony.
- W06, 7.1–7.3: výslovně odděluje simulation authority, persistence ownership a network visibility.
Tohle je důležité: problém není v chybějícím regionálním systému. Problém je v tom, že regionální vlastnictví zatím neurčuje, kdo smí autoritativně řešit celý propojený fyzikální systém.

Zdroje: 

GitHub+4

GPT-03 — Distributed Regions and Physics Islands
Architecture Resolution · C-03 · Planetary Engine · 16 November 2026

Resolved conditionally
 Architecture proposal — no implementation

A. Executive decision
Physics island authority must be independent of spatial region ownership.
I recommend a Physics Island Authority Model (PIAM).

The architecture separates four independently managed concepts:
Concept	Owns / controls
Spatial region	Spatial allocation and regional workload
Region owner	Region registry, membership and regional execution
Entity authority	Authoritative non-physical entity state and lifecycle, according to component ownership
Physics island authority	Coupled physical state, contact resolution and constraint integration

A physics island is a dynamically connected physical system. It may occupy multiple spatial regions, but it must have exactly one authoritative physics execution owner at any given committed simulation step.
Core architecture
Global spatial world

Independent spatial partitioning


Region A

Process 01
Spatial owner


Region B

Process 02
Spatial owner




Physics Island PI-001
Single physics authority — Process 01
Connected bodies from Region A and Region B

One constraint graph · One authoritative integration · One commit sequence

Diagram illustrates logical ownership, not a proposed deployment topology.

The resulting rules:
1. A physics island may span arbitrarily many spatial regions.
2. A connected island cannot be independently integrated by multiple processes under the default solver model.
3. Region migration does not automatically transfer physics authority.
4. Ghost entities are replicated physical representations, never independent authorities.
5. Contact between independently owned islands requires explicit authority arbitration.
6. Physics authority migration transfers the complete connected physical system, not an arbitrary selection of its bodies.
7. A physics island has its own committed simulation timeline.
This retains the scalability principles of W05 and W09 without sacrificing W02's constraint-solving correctness.
B. Existing architecture
I reviewed all nine worker documents, treating them as one system.
B.1 Relevant existing decisions
Document	Existing architecture
W01	Independent spatial, physics, server, streaming and persistence partitions
W02	Constraint graph and independent physics islands
W03	Specialized physical entities, vehicles, aircraft, docking and mechanical interactions
W04	Celestial mechanics, independent gravitational simulation and authoritative time
W05	Region ownership, process IDs, epochs, leases, ghosts and migration
W06	Separate simulation authority, persistence ownership and network visibility
W07	Rust authoritative physics, Unity prediction and presentation
W08	Environmental fields, atmospheric and hydrodynamic interactions
W09	Distributed regions, independent clocks, boundary interfaces and local execution

B.2 W01: the fundamental distinction already exists
W01, section 5.1, explicitly states that rendering tiles, physics partitions, server regions, streaming regions and persistence partitions must be independent.

Sections 5.2–5.3 distinguish a spatial physics partition from a physics island.

W01 even provides the exact architectural examples:
- Two cars in one spatial partition can belong to separate islands.
- A long train can span several spatial cells while requiring coordinated constraint solving.
- Two aircraft sharing an atmospheric region do not necessarily interact physically.
This is the correct foundation. The missing part is the ownership contract connecting those concepts.

Source: 

GitHub+1

B.3 W02: physical coupling
W02, sections 9.1–9.2, defines a graph:
- Nodes represent physical bodies.
- Edges represent contacts and constraints.
- Connected components represent independent constraint islands.
It also states that a vehicle with four wheels and suspension is one coupled constraint system.

Independent islands can execute concurrently, while connected bodies cannot simply be solved independently.

This establishes the physical correctness requirement for distributed execution.

Source: 

GitHub+1

B.4 W05: distributed ownership
W05, section 2.6, defines:
- region_id
- owner_process_id
- owner_epoch
- lease_expiration
- simulation_tick
- region_state_version
It proposes a seven-stage migration procedure and identifies split-brain protection through monotonically increasing epochs.

Section 2.6 also proposes three cross-region interaction mechanisms:
1. Read-only ghost entities.
2. Cross-region commands.
3. Ownership migration.
However, the document does not establish how these mechanisms work when a single physical constraint graph spans multiple owners.

Source: 

GitHub+1

B.5 W09: distributed execution
W09, sections 5.1–5.5, allows regions to represent more than geographical squares.

It proposes independent regional timelines and rejects global synchronization of every physics tick.

This is compatible with PIAM provided that a physics island has its own timeline and synchronization boundary.

Source: 

GitHub+1

B.6 Important distinction: proposals versus decisions
The documents contain recommendations, not an accepted and implemented distributed physics protocol.

In particular:
- Independent physics islands are an existing proposal in W02.
- Region epochs and leases are an existing proposal in W05.
- Independent regional clocks are an existing proposal in W09.
- Independent physics-island ownership is my recommendation.
- Atomic transfer of a complete connected island is a proposed new contract.
No existing benchmark establishes the scalability of distributed island ownership.
C. Problem analysis
C.1 The fundamental contradiction
Consider:
- Region A owned by Rust Process 01.
- Region B owned by Rust Process 02.
- A train occupies both regions.
- Every carriage is connected through physical constraints.
W05 permits the region ownership to be distributed.

W02 requires connected bodies to be solved as one constraint island.

If both processes independently simulate their local portions, each produces a physically authoritative result based on incomplete constraint information.

This can create:
- Constraint drift.
- Incorrect impulses.
- Energy errors.
- Different collision outcomes.
- Divergent simulation timelines.
- Duplicate contact resolution.
Ghost replication does not solve this problem. A read-only ghost can provide collision geometry and state information, but it cannot independently apply the authoritative reaction impulse to its source entity.
C.2 Three distinct ownership graphs
The system needs three different graphs.

Spatial ownership graph

Maps world positions and spatial allocations to regions.

Entity ownership graph

Maps entity identities and mutable components to their authoritative owners.

Physical interaction graph

Maps dynamically connected bodies into physics islands.

These graphs overlap but are not identical.

A building can be in Region A, have its persistence managed elsewhere and participate in a physical island whose authority resides on Process 02.
C.3 The connected-component problem
A physics island is not a permanent spatial object.

It changes as:
- Contacts begin.
- Contacts end.
- Joints are created.
- Joints are destroyed.
- Structures break.
- Vehicles attach to other objects.
- Mechanical systems enter or leave contact.
Therefore, physics-island ownership must support dynamic merging and splitting.

A new contact between two previously independent islands can create a new authority requirement during simulation.

This is the central unresolved issue in the original W05 proposal.
C.4 Why independent regional physics is not sufficient
Two regional processes cannot independently resolve the same coupled contact system and then reconcile their results afterward without risking inconsistent physical outcomes.

The problem is not just network latency.

It is that constraint solving is coupled: the impulse applied to one body affects the solution of the other bodies connected through the constraint graph.

A distributed iterative solver is theoretically possible, but introduces communication rounds, convergence conditions, synchronization and failure complexity.

It should not be the default architecture.
C.5 Hotspot problem
The architecture also needs to recognize that one physics island may become a computational hotspot.

Examples:
- Large trains.
- Collapsing structures.
- Dense vehicle collisions.
- Industrial machinery.
- Complex articulated robots.
Moving surrounding regions to other processes does not necessarily reduce the cost of that island.

The island itself becomes the unit of physical workload accounting.

This is why physics authority cannot simply be inferred from region ownership.
D. Cross-system dependencies
The solution affects all nine workers, although not all require architectural changes.
D.1 Dependency matrix
Worker	What PIAM requires	What PIAM provides
W01	Canonical spatial identifiers and coordinate transforms	Physics ownership independent of spatial partitioning
W02	Stable physical state, constraint graph and solver lifecycle	One authoritative solver owner per connected island
W03	Entity-specific physical constraints and attachment semantics	Stable authority for vehicles, trains, aircraft and docking
W04	Explicit time and reference-frame semantics	Defined boundary between rigid-body and celestial authority
W05	Region leases, epochs and migration coordination	Separate island ownership and island epochs
W06	Component-level authority and mutation rules	Separation of physical authority from legal/economic ownership
W07	Authoritative state replication	Clear prediction and reconciliation source
W08	Versioned environmental samples	Consistent environment inputs across island members
W09	Regional timelines and boundary synchronization	Island-level synchronization without global lockstep

D.2 Detailed dependencies
W01 — Spatial foundation

W01 sections 5.1–5.5 are compatible.

Required clarification: a physics island's geographic extent must not determine its identity or ownership.

The spatial index answers where bodies are. The physics graph answers which bodies must be solved together.

W02 — Physics runtime

This is the primary dependency.

W02 must define:
- Island identity and generation.
- Constraint graph updates.
- Island merge/split semantics.
- Serializable solver state.
- Deterministic commit ordering.
- Whether warm-start impulses and accumulated solver state are transferable.
The last item matters: transferring positions and velocities alone may not preserve the next solver step.

W03 — Entity physics

W03 section 13.11 recommends that docking geometry and capture conditions belong to W03, while resulting mechanical constraints are resolved by W02.

That is compatible.

However, docking two independently owned spacecraft must trigger physical authority arbitration before the resulting constraint is authoritatively committed.

W04 — Celestial mechanics

W04's astronomical authority must remain independent of rigid-body island ownership.

A planetary gravitational field can influence multiple islands without merging them.

A physical docking or collision between bodies, however, may require a common rigid-body authority.

The gravitational solver must not become a competing writer of the same rigid-body state.

W05 — Distributed server

W05 needs the largest architectural clarification.

The existing RegionOwnership and RegionMigration contracts must not be reused as the only physics authority mechanism.

A separate PhysicsIslandOwnership record is required.

W06 — Gameplay and multiplayer

W06 sections 7.1–7.3 explicitly separate simulation authority, persistence ownership and network visibility.

PIAM extends this separation to physical component authority.

For example, a corporation can own a train economically, a region process can manage its spatial membership, and a third process can own its physical simulation.

W07 — Unity client

No architectural conflict.

Unity remains non-authoritative.

The client receives snapshots from the current island authority, even if the island spans multiple regions.

A migration must not result in two competing authoritative snapshot streams.

W08 — Environment

Environmental fields should remain independently calculated.

An island authority obtains versioned environment samples for its integration interval.

A weather-region boundary must not automatically become a physics-island boundary.

W09 — Distributed execution

W09 must clarify that regional clocks and island clocks are different concepts.

An island spanning multiple regions has one physical integration timeline, even when those regions have independent execution schedules.

Source references: 

GitHub+5

E. Alternatives
Three technically credible architectures were evaluated.
Alternative 1 — Region-owned physics
Not recommended
Each region owns all physical entities inside its boundaries and solves its own physics.

Cross-region contacts are exchanged through messages and ghost entities.

Advantages
- Simple ownership model.
- Straightforward region migration.
- Easy regional workload allocation.
- Minimal additional ownership metadata.
Disadvantages
- Constraint systems crossing region boundaries become distributed.
- Collision reactions require cross-process coordination.
- A train can have multiple competing solver owners.
- High-frequency communication can dominate physical execution.
- Region boundaries become physical discontinuities.
This is suitable for loosely coupled simulations, but not as the universal rigid-body architecture.
Alternative 2 — Distributed physics island solver
Research candidate
One logical physics island is distributed across multiple processes.

Each process solves a subset of bodies and constraints, exchanging boundary information during solver iterations.

Advantages
- Large physical systems can theoretically use multiple machines.
- Potentially useful for exceptionally large structures.
- Spatial workload can be distributed even within a connected system.
Disadvantages
- Requires distributed constraint solving.
- Iterative synchronization introduces latency.
- Convergence and numerical stability become network-dependent.
- Failure recovery must account for partially completed solver iterations.
- Deterministic replay becomes substantially more difficult.
- Network partitions can interrupt the solver itself.
This could eventually be valuable for specialized high-fidelity simulations, but it is not a reasonable default for interactive physics.
Alternative 3 — Independent island authority
Recommended
Every physics island has one authoritative solver process, independently of the spatial regions it occupies.

Regions continue operating independently.

Neighboring regions maintain replicated physical information and coordinate interactions through explicit protocols.

Advantages
- Preserves conventional coupled constraint solving.
- No global physics lockstep.
- Supports islands spanning arbitrary numbers of regions.
- Allows island-level workload balancing.
- Allows region migration without automatically transferring physical authority.
- Fits Rust's authoritative simulation role.
- Compatible with W02's modular physics backend strategy.
Disadvantages
- Requires an additional ownership registry.
- Island merging can require authority transfer.
- Large islands can become individual hotspots.
- Migration must transfer complete solver state.
- Long-lived cross-region islands need explicit lifecycle management.
E.4 Comparison
Criterion	Region solver	Distributed island	Independent island owner
Physical correctness	Conditional	Complex	Strong
Scalability	Good spatially	Potentially high	Good, with hotspots
Distributed execution	Simple	Difficult	Good
Runtime performance	Good locally	Network-sensitive	Good locally
Complexity	Low initially	Very high	Medium
Extensibility	Limited	High	High
W01 compatibility	Partial	Compatible with changes	Direct
W02 compatibility	Weak	Requires new solver	Direct
W05 compatibility	Direct	Requires redesign	Requires extension
W09 compatibility	Partial	Requires synchronization model	Direct with island clocks

Decision: Alternative 3.

The distributed island solver remains a future optional capability, not a requirement for the foundational architecture.
F. Recommended architecture
F.1 Ownership hierarchy
The architecture should introduce an explicit hierarchy of identities and responsibilities.
Object	Identity	Authority
World	world_id	World registry
Spatial region	region_id	Region owner
Physical entity	entity_id	Entity/component authority
Physics island	physics_island_id	Island owner
Simulation process	process_id	Process identity
Authority generation	authority_epoch	Fencing mechanism

The physics island is a first-class distributed simulation object.

It is not a child of a region.

It references the regions containing its members.
F.2 Proposed physics island ownership contract
The following is an architectural schema, not an existing implementation.

PhysicsIslandOwnership {
    world_id: UUID,
    physics_island_id: UUID,

    owner_process_id: UUID,
    authority_epoch: uint64,
    lease_expiration: timestamp,

    island_generation: uint64,
    state_revision: uint64,

    simulation_tick: uint64,
    committed_time: SimulationTime,

    member_entity_ids: UUID[],
    participating_region_ids: UUID[],

    constraint_graph_revision: uint64,
    solver_state_revision: uint64,

    lifecycle_state: enum
}



Suggested lifecycle:
- ACTIVE
- MERGING
- SPLITTING
- MIGRATING
- FROZEN
- RECOVERING
- INACTIVE
The ownership registry must be authoritative for the current owner and epoch.

The solver state itself remains authoritative in the Rust simulation runtime.
F.3 Authority responsibilities
Region owner

Responsible for:
- Spatial membership.
- Regional indexing.
- Local entity discovery.
- Region-level workload metrics.
- Spatial boundary notifications.
- Regional replication subscriptions.
Physics island owner

Responsible for:
- Physical state integration.
- Constraint graph.
- Contact generation and resolution.
- Solver warm-start state.
- Physical impulses.
- Physical state revisions.
- Island simulation timeline.
- Island migration and recovery.
Entity/component authority

Responsible for:
- Entity lifecycle.
- Non-physical mutable components.
- Domain-specific state.
- Routing authorized state changes to the relevant simulation subsystem.
One entity may participate in several logical domains, but two processes must never authoritatively write the same physical state revision.
F.4 Can an island span multiple regions?
Yes
This must be explicitly supported.

Example:
- Region A: train locomotives and first carriages.
- Region B: middle carriages.
- Region C: final carriages.
All train bodies belong to one physical constraint graph.

One process owns the complete physics island.

The other regional processes maintain spatial information and replicated state.

This prevents region boundaries from affecting train mechanics.
F.5 Can two processes simulate different parts of one island?
Not under the default solver model
Parallel execution is still possible inside the owning process:
- Broadphase generation.
- Independent narrowphase collision tests.
- Independent force evaluation.
- Parallelizable solver jobs where the solver guarantees correctness.
But the island's coupled constraint solution and authoritative state commit must remain under one logical authority.

The distinction is between parallel computation and distributed authority.

Multiple worker threads do not imply multiple authoritative owners.
F.6 Ghost entity contract
Ghost entities are read-only replicas.

They should contain:
- Entity identity.
- Physical state revision.
- Source island ID.
- Source authority process.
- Authority epoch.
- Simulation timestamp.
- Position and orientation.
- Linear and angular velocity.
- Collision representation.
- Relevant physical properties.
- Validity horizon.
A ghost can support:
- Broadphase queries.
- Local collision candidate generation.
- Prediction.
- Visibility.
- Spatial indexing.
A ghost cannot:
- Commit an authoritative impulse.
- Modify source entity velocity.
- Advance source physical state.
- Change source constraints.
- Claim island ownership.
When a ghost becomes stale, the receiver must not silently treat its old state as current authoritative information.
F.7 Boundary-crossing protocol
The architecture needs two separate protocols.

Spatial crossing

When an entity crosses a region boundary:
1. Detect its predicted trajectory approaching the boundary.
2. Notify the destination region.
3. Update spatial membership.
4. Transfer regional subscriptions and relevant spatial indexing.
5. Retain physics authority unless an explicit authority transfer is required.
This is not a physics migration.

Physics authority transfer

When an island must move to another process:
1. Prepare destination runtime.
2. Freeze the island at a committed simulation boundary.
3. Serialize all member entities.
4. Serialize constraints and solver state.
5. Transfer environmental and temporal dependencies.
6. Verify the transferred state.
7. Atomically increment island authority epoch.
8. Activate the new owner.
9. Redirect authoritative messages.
10. Retire the previous owner.
The new owner must not begin authoritative integration before the ownership commit.

The previous owner must not commit another step after losing authority.
F.8 Island merging
This is the most important additional protocol.

Consider two independently simulated cars approaching a collision.

Initially:
- Island A belongs to Process 01.
- Island B belongs to Process 02.
They are independent and can execute separately.

When physical contact creates a coupled system:
1. Detect the candidate cross-island contact.
2. Identify both current island authorities.
3. Create an interaction arbitration request.
4. Select a single resulting island authority.
5. Transfer the complete state of the losing island to the selected process.
6. Commit a new island identity/generation and authority epoch.
7. Resolve the contact under the resulting single authority.
8. Replicate the merged state.
The authority selection should consider:
- Current workload.
- Network topology.
- Island size.
- Existing ownership stability.
- Expected contact duration.
- Future trajectory.
A deterministic tie-breaker, such as the canonical island ID, must be available.

The authority registry must serialize concurrent merge requests so two processes cannot independently create competing merged islands.

Important: Collision detection can be performed speculatively by multiple processes. Only one process may commit the physical contact response.
F.9 Island splitting
When a constraint or contact is removed, the graph may separate into independent components.

The existing island owner should initially retain authority over all resulting components.

After graph validation, the runtime may:
- Keep all components on one process.
- Transfer selected components to other processes.
- Rebalance ownership based on workload.
Splitting must occur at a committed simulation boundary.

It must preserve:
- Body state.
- Constraint state.
- Contact state.
- Velocity.
- Angular momentum.
- Relevant solver history.
The process must not create two authorities before the split is committed.
F.10 Time and synchronization
W09 proposes independent regional clocks.

PIAM requires an additional clock:

physics_island_time.

A physical island must advance as one coherent simulation system.

This means:
- All member bodies use the same committed island time.
- Constraint solving operates over one defined integration interval.
- Cross-region messages carry explicit simulation timestamps.
- Stale messages cannot overwrite newer committed state.
- Region clocks do not dictate island integration order.
For an island interacting with regions operating at different frequencies, the island owner needs a conservative synchronization frontier.

The island cannot consume an input that has not yet been committed by the relevant authority.

The architecture should not introduce global lockstep.

If two previously independent islands have incompatible simulation time scales, their merge requires an explicit time-policy decision before the first coupled authoritative step.
F.11 Failure and recovery
Island owner process failure
1. Detect lease expiration or confirmed process failure.
2. Fence the previous owner.
3. Obtain the latest durable island checkpoint.
4. Replay relevant journal entries.
5. Validate island generation and constraint graph.
6. Acquire a new authority epoch.
7. Restore the island.
8. Resume simulation.
Region owner failure

The physics island can continue if its own authoritative runtime and required dependencies remain available.

Network partition

A process that cannot establish valid authority must stop committing authoritative changes once its lease or fencing authorization is invalid.

It may continue non-authoritative prediction, but cannot claim physical authority.

Crash during merge or split

The ownership transaction must recover either the old committed topology or the new committed topology.

There must never be a recoverable state in which both owners can independently commit.
F.12 Interface boundaries
The following contracts should be exposed between subsystems.
Interface	Direction	Purpose
RegisterPhysicsIsland	Physics → Authority registry	Register island
AssignIslandAuthority	Registry → Rust runtime	Assign authority
PublishIslandState	Island owner → Replication	Publish snapshots
RequestIslandMerge	Physics → Authority registry	Request coupled ownership
CommitIslandMerge	Registry → Physics	Commit merged authority
RequestIslandSplit	Physics → Authority registry	Request component separation
BeginIslandMigration	Control plane → Island owner	Prepare transfer
CommitIslandMigration	Authority registry → Rust	Commit new epoch
RestoreIsland	Persistence → Rust	Restore checkpoint
UpdateRegionMembership	Physics → Spatial runtime	Update regional membership

These are proposed logical interface names, not existing APIs.
G. Cross-system impact
G.1 Compatibility assessment
Document	Status	Required action
W01	Compatible	Clarify independent island authority
W02	Clarification	Define island lifecycle, serialization and ownership
W03	Clarification	Define physical attachment and docking authority transitions
W04	Compatible	Preserve separation of celestial and rigid-body authority
W05	Change required	Introduce island ownership independent of region ownership
W06	Clarification	Distinguish entity/component authority from island authority
W07	Compatible	Replicate from current island authority
W08	Clarification	Version environment inputs and timestamps
W09	Change required	Define island clocks and cross-region synchronization

G.2 Required changes
W01

Add a distinction between physics partition ownership and physics island ownership.

The existing spatial partition architecture remains unchanged.

W02

Add:
- Island ownership contract.
- Island generation.
- Merge and split lifecycle.
- Solver-state transfer.
- Authority fencing.
- Island-specific simulation timeline.
W03

Define how specialized systems create and destroy physical constraints.

A train coupling, spacecraft docking or mechanical attachment must be represented in the physical interaction graph.

W05

This is the primary architectural modification.

The current region ownership contract cannot be the sole source of physical authority.

Region ownership and island ownership must have independent leases and epochs.

W06

Its entity authority example currently identifies a region and server.

This should be extended to allow the physical component's authority to reference a physics island independently.

W09

The regional clock model must be extended.

Region time remains valid for regional execution, but connected physics requires an island-level committed timeline.
G.3 Decisions requiring coordination
Decision	Primary owner	Required cooperation
Island identity and graph lifecycle	W02	W03
Island authority registry	W05	W02, W09
Physical state serialization	W02	W05
Entity/component authority	W06	W02, W05
Time synchronization	W09	W02, W04
Environmental sample consistency	W08	W02
Client reconciliation	W07	W05

The central integrator should resolve these dependencies before implementation.
H. Required architectural decisions
The following ADRs are proposed for acceptance.
Recommended
ADR-DISTPHYS-001 — Independent physics island ownership

Physics island ownership must be independent of spatial region ownership.

One physics island has one authoritative solver owner at a committed simulation step.


ADR-DISTPHYS-002 — Complete island authority

A process must not independently commit different parts of one connected island using separate authoritative solvers.


ADR-DISTPHYS-003 — Independent authority epochs

Physics island authority uses its own monotonically increasing fencing epoch, independent of region ownership epochs.


ADR-DISTPHYS-004 — Ghost entity semantics

Ghost entities are read-only replicated state. They cannot commit physical mutations.


ADR-DISTPHYS-005 — Atomic island migration

Physical authority migration transfers complete island state, including constraints, solver state and temporal metadata.


ADR-DISTPHYS-006 — Island merge and split

Changes in physical connectivity require explicit island topology transitions and authority arbitration.


ADR-DISTPHYS-007 — Island simulation time

A physics island has one committed physical timeline independent of regional execution clocks.


ADR-DISTPHYS-008 — No default distributed constraint solver

Distributed iterative constraint solving is not required for the foundational architecture. It may be introduced as a specialized future capability.


ADR-DISTPHYS-009 — Island-based load balancing

Physics workload must be measured at island level, not exclusively at region level.


H.1 Priority
Priority	ADRs	Reason
P0	001, 002, 003, 005	Correctness and authority
P1	004, 006, 007	Cross-boundary operation
P2	008, 009	Future scalability and optimization

I. Validation plan
The architecture should be validated through isolated simulations before production implementation.

No performance results are assumed.
I.1 Mandatory correctness tests
Test	Setup	Expected invariant
Island spanning regions	Train across three regions	Exactly one physics authority
Vehicle crossing	Vehicle moves A → B	No physical state loss
Cross-region structure	Connected building components in A and B	One coherent constraint solution
Independent contact	Two objects owned by different processes collide	One committed contact response
Island merge	Two independent islands collide	One resulting authority
Island split	Joint between components breaks	Correct independent components
Authority migration	Transfer island during active operation	No dual authority
Process failure	Owner crashes during migration	Recoverable committed topology
Network partition	Old owner becomes isolated	Stale owner cannot commit
Time divergence	Regions use different execution frequencies	Island remains temporally coherent

I.2 Detailed example tests
Test 1 — Vehicle crossing a region

Initial state:
- Region A: Process 01.
- Region B: Process 02.
- Vehicle island: Process 01.
Expected:
- Vehicle crosses the spatial boundary.
- Region B receives spatial membership and replication updates.
- Physical authority remains on Process 01.
- No ownership migration is required solely because of position.
Test 2 — Train spanning multiple regions

Initial state:
- Train has 50 connected physical bodies.
- Bodies occupy three regions.
- One island authority owns the complete train.
Expected:
- All constraints resolve under one authoritative solver.
- Regional processes can independently handle their spatial workloads.
- The train does not split into regional physics authorities.
Test 3 — Connected rigid-body structure

Initial state:
- A mechanical structure spans Regions A and B.
- A constraint connects components on opposite sides.
Expected:
- Both sides receive one consistent physical solution.
- Constraint state is not independently committed.
- A region ownership migration does not destroy the connection.
Test 4 — Independently owned interacting objects

Initial state:
- Object A is owned by Process 01.
- Object B is owned by Process 02.
- They are approaching collision.
Expected:
- Both processes can predict and detect the candidate contact.
- The authority registry arbitrates the resulting coupled island.
- One process becomes the committed solver authority.
- Both objects receive one consistent physical response.
I.3 Authority invariants
These should become non-negotiable system invariants:
1. Exactly one committed physics authority per island generation.
2. No authoritative state mutation from a stale epoch.
3. Every committed physical state has a monotonic revision.
4. Every island member belongs to exactly one active island generation.
5. No connected constraint may be silently omitted from the authoritative island graph.
6. A merge or split must preserve physical state.
7. Every cross-process physical mutation has a traceable authority and timestamp.
8. A region migration cannot implicitly invalidate island ownership.
9. Recovery cannot reactivate an obsolete owner.
10. The same committed input history must be reproducible within the defined determinism contract.
I.4 Performance benchmarks
Measure:
- Physics step p50/p95/p99.
- Island size distribution.
- Maximum island body count.
- Constraint count per island.
- Cross-region island frequency.
- Merge frequency.
- Split frequency.
- Island migration duration.
- Solver time per island.
- Ghost update bandwidth.
- Boundary message rate.
- Authority arbitration latency.
- CPU utilization per process.
- Maximum sustained island workload.
Recommended benchmark scenarios:
Scenario	Purpose
100 independent vehicles	Parallel independent islands
1,000 independent rigid bodies	Broadphase and scheduling
100-body train across 3 regions	Long-lived cross-region island
1,000-body connected structure	Island hotspot
100 simultaneous cross-region contacts	Merge arbitration
Repeated merge/split cycles	Topology stability
Process crash during migration	Recovery correctness
Different regional tick frequencies	Time synchronization

These are proposed test sizes, not performance targets or measured capabilities.
J. Unresolved questions
The architecture can be defined, but several decisions require coordination or further research.
J.1 Physics backend limitations
W02 evaluates Rapier, Avian and Parry but does not establish the final production solver.

Questions:
- Can the selected backend serialize complete solver state?
- Can warm-start data survive migration?
- Can constraint graph changes be applied at controlled simulation boundaries?
- What determinism guarantees are available?
The ownership architecture should not depend on a particular backend's internal representation.
J.2 Extremely large connected systems
A 1,000-body train or 10,000-body industrial structure can become a single-process hotspot.

Further research should evaluate:
- Hierarchical mechanical models.
- Reduced-order dynamics.
- Domain-specific distributed solvers.
- Constraint graph partitioning.
- Adaptive physical fidelity.
A distributed solver should be introduced only when measured workloads justify its complexity.
J.3 Long-lived cross-region structures
A train is relatively straightforward because its physical connectivity is well defined.

A building or industrial complex may have thousands of connections.

Questions:
- Should long-lived static connections be treated differently from dynamic contacts?
- Can static structural components use a separate solver representation?
- Can disconnected subsystems share one logical structure without sharing one physics island?
Recommendation: distinguish logical construction identity from actual dynamic physical connectivity.
J.4 Time-scale conflicts
W09 permits different regional simulation frequencies and time scales.

What happens when two islands with different time scales collide?

The architecture needs an explicit policy for:
- Time-scale negotiation.
- Pause and resume.
- Simulation acceleration.
- Historical state reconstruction.
- Interactions with offline simulation.
This must be coordinated with W04's astronomical time model.
J.5 Authority registry implementation
W05 places region coordination within the Node.js control plane architecture but also expects Rust to retain authority over simulation.

The exact consistency and durability mechanism for island ownership remains unresolved.

The registry must provide atomic epoch changes without making Node.js a mandatory per-tick dependency.
J.6 Physics authority versus entity authority
W06 defines entity authority through region and server identifiers.

PIAM proposes that physical component authority may instead reference an island.

The integrator must decide whether entity authority becomes:
- A logical entity-level routing reference with separate component authorities; or
- A collection of explicit subsystem-specific authority references.
I recommend the second model because Planetary Engine will eventually contain independently simulated physical, environmental, economic and gameplay components.
K. Final assessment
Resolved conditionally
The core architectural problem is resolved.

The selected model establishes a coherent relationship between spatial regions, entity ownership, physics islands and distributed execution.

Most importantly, it prevents an architectural mistake that would become extremely expensive to correct later: treating geographical ownership as equivalent to physical simulation authority.

The solution preserves:
- W01's independent spatial partitioning.
- W02's connected constraint islands.
- W05's region ownership and epoch fencing.
- W06's separation of simulation, persistence and visibility.
- W09's asynchronous regional execution.
However, final acceptance depends on three coordinated changes:
1. W02 must define the transferable physical island state and lifecycle.
2. W05 must introduce independent island authority and migration.
3. W09 must define island-level temporal synchronization.
W06 must also clarify component-level authority.

My recommendation to the central architecture integrator: accept ADR-DISTPHYS-001 through ADR-DISTPHYS-009 as the proposed direction, with the three dependencies above treated as mandatory integration work.

Do not implement region-based physics ownership as the final model.

Do not implement distributed constraint solving as a prerequisite for the initial runtime.

The correct foundation is one logical persistent world, independently partitioned spatial execution, and authoritative physical islands that can cross spatial and process boundaries without breaking physical causality.

Repository evidence and traceability
All references below point to the reviewed repository documents.
Document	Relevant sections
W01 — World architecture	5.1–5.5, lines 600–687
W02 — Physics	9.1–9.4, lines 792–855
W03 — Entity physics	13.11–14, lines 2349–2392
W04 — Celestial simulation	7.4–8, lines 769–789
W05 — Distributed server	2.6, 3.1–3.2, 6, 9.2–10
W06 — Gameplay and multiplayer	5.3–5.4, 6.6, 7.1–7.3
W07 — Unity client	4.1–4.2, lines 604–646
W08 — Environment	3.3–4.3, lines 237–287
W09 — Distributed execution	5.1–5.5, lines 398–496

Evidence limitation: Line references are based on the GitHub versions retrieved during this review. They identify the relevant source sections, not implementation evidence.

GPT-03 completion record
Assigned problem

C-03

Documents reviewed

W01–W09

Recommended ADRs

9

Implementation

None

Repository modifications

None


Architecture resolution completed conditionally.

Independent architecture proposal for central integration. Not yet an accepted project ADR.

One particularly important recommendation for the orchestrator: do not let the nine workers independently redefine ownership. The central architecture should establish one common authority model and require every subsystem to reference it. Otherwise, the same entity could acquire nine slightly different interpretations of ownership as the project evolves.