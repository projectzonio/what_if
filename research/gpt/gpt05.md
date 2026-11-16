Máš pravdu. Repozitář je veřejný a právě jsem ověřil přímý přístup:

Veřejný repozitář potvrzen
 projectzonio/what_if
GitHub skutečně zobrazuje všech devět požadovaných dokumentů w1.md až w9.md. Předchozí požadavek na nahrání ZIPu tedy nebyl oprávněný. Pokračuji přímo nad repozitářem.
První zjištění z architektury
Nejdůležitější výchozí skutečnost je ve W06, sekce 10.2:
- Mutace dostává world_revision.
- Události jsou immutable.
- derived_updates samostatně eviduje čekající aktualizace.
- Commit výslovně neznamená dokončení všech závislých subsystémů.
To je správný základ, ale zatím jde spíše o evidenci pending stavů než o kompletní konzistenční protokol. W06 navíc v sekci 4.4 správně upozorňuje, že změna terénu ovlivňuje kolize, navigaci, hydrologii i klientskou replikaci. 

GitHub+1

Další důležitá hranice: W05 přiděluje každému aktivnímu regionu právě jednoho autoritativního vlastníka a používá owner_epoch jako fencing token. W09 současně rozlišuje strong, bounded a eventual consistency. To znamená, že řešení C-05 nesmí zavést jedinou globální synchronizační bariéru pro celý svět. 

GitHub+1

GPT-05 — WORLD MUTATION AND CONSISTENCY LIFECYCLE
PLANETARY ENGINE · C-05 · Architecture Resolution · 16 November 2026

Repository analysis completed
 Resolved conditionally

Repository: projectzonio/what_if

Scope: W01–W09, with primary responsibility for resolving the W06 mutation lifecycle and its contracts with the rest of the architecture.

Implementation: None. This document specifies architecture only.

A. Executive decision
Recommended architecture: Dependency-Driven Mutation Lifecycle
Planetary Engine should use an authoritative mutation journal combined with dependency-aware asynchronous projection processing and explicit activation barriers.

A world mutation has three fundamentally different aspects:
1. Authoritative commitment — the requested change becomes an accepted, immutable fact in the canonical world history.
2. Derived-state convergence — dependent subsystems update their own representations of that fact.
3. Operational activation — a dependent representation becomes eligible for use by systems that require it.
These aspects must not be represented by one universal completion status.

I recommend the following conceptual lifecycle:
1. COMMAND

Requested · Validated · Conflict checked


2. AUTHORITATIVE COMMIT

Canonical state + immutable event + revision


3. DEPENDENCY GRAPH

Determine affected projections and causal dependencies

Physics

Collision · Geometry · Bodies


Environment

Terrain · Water · Atmosphere


Navigation

Graphs · Traversability


Replication

Snapshots · Deltas · AOI




4. INDEPENDENT PROJECTION ACKNOWLEDGEMENTS

Each subsystem reports its processed revision and readiness


The central rule:

A mutation is globally committed once, but its consequences become available independently according to explicit dependency contracts.

A physics-critical interaction must not consume an outdated collision representation while assuming it represents the latest authoritative world state.

However, a remote economic statistic must not delay local physics merely because both depend on the same mutation.

The architecture should therefore distinguish:
- Authoritative truth.
- Derived representation.
- Representation freshness.
- Operational readiness.
- Durability and recovery.
This resolves C-05 without introducing global synchronization.

B. Existing architecture
B1. What W06 already establishes
W06 contains most of the necessary foundational elements.
Existing proposal	Location	Assessment
Command/event separation	W06 §5.1	Correct
Optimistic concurrency	W06 §5.7	Correct
Atomic mutation transactions	W06 §5.4	Correct, with cross-region qualification
Immutable mutation history	W06 §5.5	Correct
Hybrid snapshots and event history	W06 §5.5	Correct
Dependency revisions	W06 §11.2	Correct direction
Pending derived updates	W06 §10.2	Incomplete lifecycle
Explicit subsystem dependencies	W06 §11	Correct

W06 §5.1 establishes that commands express requests while events record completed authoritative changes.

W06 §5.4 distinguishes local transactions from distributed transactions and recommends explicit prepare/commit coordination when multiple regions genuinely require atomicity.

W06 §5.5 recommends hybrid persistence rather than indiscriminate full event sourcing.

These are compatible with the proposed resolution. 

GitHub+1

B2. Existing mutation result
W06 §10.2 proposes:
{
  "command_id": "uuid",
  "status": "committed",
  "world_revision": 121,
  "events": [
    {
      "event_id": "uuid",
      "event_type": "BuildingPlaced",
      "entity_id": "uuid",
      "revision": 121
    }
  ],
  "affected_regions": [
    "region-42"
  ],
  "derived_updates": {
    "collision": "pending",
    "navigation": "pending",
    "replication": "pending"
  }
}


This already recognizes that commitment and derived completion are different.

W06 §11.2 goes further and introduces a dependency revision vector:
World Revision:       121
Terrain Revision:     44
Collision Revision:  43
Navigation Revision: 42
Economy Revision:     89
Replication Revision:120


The example correctly illustrates that different representations can be at different revisions.

However, neither example fully specifies:
- How projection dependencies are discovered.
- Whether a subsystem may skip irrelevant events.
- How processing order is guaranteed.
- How a failed projection recovers.
- How stale data is prevented from being used in critical interactions.
- How multiple regions establish causal consistency.
- What exactly a replication-ready state guarantees.
These are the missing parts of C-05. 

GitHub+1

B3. Other workers' existing assumptions
W02 — Physics

W02 §7.1 defines an authoritative simulation tick with a command collection phase and deterministic command application at the tick boundary.

This provides a natural activation point for physics-affecting mutations.

W02 also separates collision detection from ownership of physical body state. The mutation lifecycle must preserve that separation. 

GitHub+1

W05 — Distributed regions

W05 §2.6 defines single-region authority and monotonic owner_epoch fencing.

Cross-region commands cannot simply assume that all region owners share one synchronized state. 

GitHub+1

W07 — Replication

W07 §7.1–7.5 proposes component-level replication, event-driven static entities, snapshot baselines and independently scheduled updates.

It also establishes that Unity maintains authoritative, predicted and interpolated representations separately. 

GitHub+2

W08 — Environment

W08 §1 and §9.3 describe hierarchical environmental simulation with different spatial resolutions and frequencies.

A terrain mutation may therefore affect several environmental representations at different times. 

GitHub+1

W09 — Distributed execution

W09 §5.4 explicitly rejects global synchronization of every physics tick and proposes strong, bounded and eventual consistency according to the type of interaction.

This is a critical constraint for C-05. 

GitHub


C. Problem analysis
C1. The meaning of committed is underspecified
There are at least four different meanings that could be incorrectly associated with Committed:
1. The command has passed validation.
2. The canonical state has changed.
3. The mutation has been recorded durably.
4. All dependent systems can already use the new state.
These are not equivalent.

I recommend the following precise definition:

Committed means that an authoritative mutation has been accepted by its responsible authority, its canonical state transition has been applied atomically, and its immutable event has been recorded in the recoverable authoritative history.

For persistent mutations, the successful acknowledgement must correspond to a recoverable commit record.

This does not require every physics tick to synchronously write its complete state to a database.

W05 and W09 already distinguish high-frequency in-memory simulation from journal and snapshot persistence. However, their durability acknowledgement and recovery-point guarantees remain unspecified. 

GitHub+1

C2. A revision number is not a consistency guarantee
Consider:
World revision:       121
Collision revision:    43
Navigation revision:   42
Environment revision:  44


This tells us that derived representations are behind.

It does not tell us:
- Whether revision 121 affects collision.
- Whether collision revision 43 includes all relevant earlier changes.
- Whether navigation revision 42 is safe for a particular vehicle.
- Whether environment revision 44 includes the latest terrain changes.
- Whether a client has received the structural change.
- Whether the affected data is already active.
A scalar revision is useful for ordering but insufficient for describing the complete consistency state.

The solution is a dependency-aware projection watermark combined with a scoped causal revision vector.
C3. Asynchronous processing creates ordering hazards
Example:
Mutation A: Destroy wall
Mutation B: Build bridge


Both affect terrain and collision.

If mutation B completes before mutation A in a derived subsystem, the resulting representation may not correspond to the authoritative state.

Therefore:
- Events must be ordered within their authoritative partition.
- Projections must preserve the order of relevant causal inputs.
- Independent partitions should not require global ordering.
- Projection results must identify the exact input revision or causal frontier they represent.
A subsystem may process independent events concurrently, but it cannot publish a result that claims to include a later dependent event while omitting an earlier prerequisite.
C4. Commit and physical activation must be separate
Consider a destroyed wall.

The canonical world state says the wall no longer exists.

However, the collision representation may still contain its geometry.

If the physics runtime assumes that canonical state and collision state are already synchronized, it may produce incorrect results.

The solution is an explicit activation boundary.

Physics continues operating on a coherent, identified collision snapshot until the new collision representation is prepared and activated at an authoritative physics tick boundary.

This preserves W02's deterministic command application model.
C5. Cross-region mutations need stronger coordination
A bridge can occupy multiple simulation regions.

A road can connect two separately owned regions.

A terrain deformation can affect neighboring water simulation cells.

A building can reserve land in one region while changing infrastructure in another.

A simple collection of independently committed regional commands is insufficient when the operation requires atomicity.

W06 §5.4 already recognizes this issue but does not define the complete relationship between distributed transaction commitment and projection activation.

The proposed solution is explicit multi-region transaction coordination, with a distinction between:
- Atomic authoritative commitment.
- Regional projection convergence.
- Operational activation.
C6. Replication readiness is not the same as global consistency
A client may receive a building placement event before the collision and navigation projections are updated.

That can be acceptable.

The client can render the authoritative building while still treating collision-dependent interactions as pending or unavailable.

But a client must not infer that receiving a replication event means every other subsystem has converged.

W07's component-level replication model supports this distinction.
C7. The biggest missing architectural element
The existing documents describe authoritative state, derived systems and regional ownership.

What is missing is a formal mutation dependency and activation protocol connecting them.

This is the actual core of C-05.

D. Cross-system dependencies
The following analysis considers all nine workers, not only the directly assigned subsystem.
Worker	Dependency	Required coordination
W01	Planetary/environmental state	Terrain and environmental revision provenance
W02	Physical integration	Physics activation at authoritative tick boundaries
W03	Specialized physical entities	Updated physical capabilities and interaction semantics
W04	Celestial simulation	Reference frames, time and physical parameter changes
W05	Region ownership	Multi-region transactions and fencing
W06	Mutation authority	Journal, command validation and dependency tracking
W07	Client replication	Snapshot and event consistency metadata
W08	Environmental simulation	Asynchronous, multi-fidelity projection updates
W09	Distributed execution	Causal ordering, recovery and no global barrier

D1. W01 — Planetary environment
W01 defines explicit reference frames and an environment response containing WorldRevision, provider versions, data revisions and validity information.

This is important because an environmental result must identify the world state on which it was calculated.

Requirement from C-05:
- Every affected environmental result must carry its input revision.
- A stale environmental sample must be identifiable.
- Terrain and material mutations must invalidate dependent environmental data.
Requirement from W01:
- C-05 must preserve coordinate-frame identity.
- Environmental updates must not introduce ambiguous coordinate or unit semantics.
Compatibility: compatible, with a required revision-provenance extension. 

GitHub+1

D2. W02 — Universal physics runtime
W02 owns physical integration, contacts, collision and physical state.

Its simulation lifecycle already provides a deterministic command application phase.

Requirement from C-05:
- A physics-affecting mutation must be applied at a defined authoritative tick boundary.
- Collision updates must not expose partially constructed spatial structures.
- Physical state must remain owned by the physics runtime.
Requirement from W02:
- Mutations must arrive as queued commands or immutable inputs.
- Other subsystems must not directly alter physical state.
- The physics runtime must identify which world mutation frontier it has activated.
Compatibility: compatible, but W02 needs an explicit external mutation activation contract. 

GitHub+1

D3. W03 — Specialized physical entities
W03 defines vehicle, character, aircraft, drone, rocket and spacecraft behavior.

A road, bridge, terrain deformation or building may alter the constraints under which these entities operate.

For example, a bridge becoming available affects vehicle traversability, but not necessarily the underlying vehicle physics model.

Required coordination:
- Physical entities must query current authoritative collision and environmental representations.
- Specialized solvers must not maintain independent, conflicting copies of the world's physical geometry.
- Changes to specialized entity configuration must be versioned separately from world geometry.
Compatibility: compatible, with shared revision and activation metadata. 

GitHub

D4. W04 — Celestial simulation
W04 operates on astronomical state and physical models at different scales.

A mutation of local terrain does not require the astronomical subsystem to rebuild its state.

However, changes to planetary mass distribution, orbital parameters or other celestial properties may affect multiple downstream systems.

Required coordination:
- Celestial changes must declare their affected physical and environmental domains.
- Global astronomical updates must not be treated as ordinary local terrain mutations.
- Simulation-time semantics must remain explicit.
Compatibility: compatible, provided mutation dependency scope is not restricted to geographic regions. 

GitHub+1

D5. W05 — Distributed regions
W05 is a critical dependency.

Every active simulation region has one authoritative owner. owner_epoch prevents stale processes from continuing to modify state after ownership transfer.

Required coordination:
- Every authoritative mutation must carry the relevant owner epoch.
- Cross-region operations must coordinate their participants.
- Ownership migration must preserve journal sequence and projection checkpoints.
- Old owners must not publish valid updates after fencing.
Compatibility: compatible, but W05 must adopt the mutation transaction and projection checkpoint contracts defined here. 

GitHub+1

D6. W07 — Replication
W07 requires world epoch, revision and entity-state version metadata.

Required coordination:
- Replication must identify the committed mutation sequence represented by each snapshot.
- Structural events must not be confused with physics activation.
- Clients must know whether received data is authoritative, predicted or interpolated.
- Static objects must support event-driven updates.
Compatibility: compatible, with additional consistency metadata. 

GitHub+1

D7. W08 — Environmental simulation
W08 explicitly supports independent global, regional, local and interaction environmental levels.

Required coordination:
- Mutations must invalidate only affected environmental representations.
- Environmental projections must expose input revisions and simulation time.
- Different environmental frequencies must remain independent.
Compatibility: compatible and strongly aligned with dependency-driven processing. 

GitHub+1

D8. W09 — Distributed execution
W09 explicitly rejects a globally synchronized physics tick.

It also distinguishes strong, bounded and eventual consistency.

Required coordination:
- Mutation processing must preserve local causal order.
- Cross-region critical interactions must use bounded or strong coordination.
- Independent regions must continue executing.
- Recovery must preserve committed mutations and fencing.
Compatibility: compatible. W09 provides the distributed execution foundation for this proposal. 

GitHub+1


E. Alternatives
E1. Alternative A — Universal mutation completion barrier
Every mutation waits until all dependent systems report completion.

Example:
BuildingPlaced
    ↓
Physics complete
    ↓
Collision complete
    ↓
Environment complete
    ↓
Navigation complete
    ↓
Replication complete
    ↓
Mutation Complete


Advantages:
- Simple status interpretation.
- Easy to explain.
- Straightforward completion acknowledgement.
Disadvantages:
- Slowest dependent subsystem determines completion.
- Environmental calculations can block unrelated physical activity.
- Replication latency becomes coupled to gameplay.
- Regional failures can prevent otherwise valid operations.
- Difficult to scale to planetary simulations.
Verdict: Reject.

This contradicts W08's multi-rate execution model and W09's explicit rejection of global synchronization.
E2. Alternative B — Pure eventual consistency
Commit mutations immediately and allow all dependent systems to update independently whenever convenient.

Advantages:
- High availability.
- Minimal coordination.
- Good scalability.
- Simple asynchronous architecture.
Disadvantages:
- No formal guarantee that physical interactions use compatible data.
- Stale collision geometry can produce incorrect results.
- Navigation can route through destroyed infrastructure.
- Environmental queries may use incompatible terrain states.
- Cross-region transactions can expose partially applied changes.
Verdict: Reject as the universal model.

Eventual consistency is appropriate for analytics, distant aggregates and other non-critical derived information, but not for every physical interaction.
E3. Alternative C — Dependency-driven consistency with activation barriers
Every mutation declares its affected domains.

Each domain processes the mutation independently, but must preserve causal order.

Critical consumers can require explicit projection readiness before performing particular operations.

Advantages:
- No global completion barrier.
- Explicit consistency guarantees.
- Supports multi-rate simulation.
- Supports regional ownership.
- Allows independent failure and recovery.
- Supports new simulation domains without changing the entire mutation protocol.
Disadvantages:
- More complex dependency management.
- Requires revision provenance.
- Requires explicit stale-data policies.
- Requires careful cross-region transaction design.
Recommended
This is the only alternative that satisfies the global architectural constraints without introducing unnecessary synchronization.
E4. Comparison
Criterion	A: Global barrier	B: Eventual only	C: Dependency-driven
Correctness	High if implemented correctly	Conditional	High with explicit contracts
Scalability	Poorer	High	High
Distributed execution	Bottleneck risk	Good	Good
Physics safety	High	Insufficient	High
Environmental simulation	Excessive coupling	Possible stale state	Independent convergence
Replication	Delayed	Uncoordinated	Explicit frontier
Complexity	Medium	Low	High
Extensibility	Limited	Good	Excellent
W05 compatibility	Weak	Partial	Strong
W09 compatibility	Poor	Partial	Strong


F. Recommended architecture
F1. Fundamental ownership model
The architecture must maintain one authoritative source of truth for each mutable domain.
Subsystem	Owns
World Mutation System	Command validation, commit decisions, mutation journal
Regional authority	Canonical state for its authoritative region
W02 Physics	Physical state, integration, collision activation
W01/W08 Environment	Environmental representations
Navigation	Navigation graph and traversability projections
W07 Replication	Client-visible authoritative state delivery
W05	Region ownership, migration and fencing
W09	Distributed scheduling and execution coordination

The mutation system does not own every derived representation.

It owns the authoritative mutation history and the contracts governing its consequences.
F2. Proposed lifecycle
1. Requested
   
   An actor submits a mutation command.
   
   The command contains:
   - Command ID.
   - Actor and authorization context.
   - Target entity or region.
   - Expected revisions.
   - Expected owner epochs.
   - Simulation-time target.
   - Payload and schema version.
   - Idempotency key.
2. Validated
   
   Rust validates structural correctness, authorization, world constraints, resource requirements and transaction boundaries.
   
   Stale revisions or ownership epochs are rejected or explicitly reconciled.
3. Prepared
   
   All necessary authoritative transaction participants prepare their changes.
   
   For ordinary single-region operations this can be a local transaction.
   
   Multi-region operations use explicit coordination where atomicity is required.
4. Committed
   
   The authoritative transaction is applied atomically.
   
   The immutable event and recoverable commit record are established.
   
   The world revision advances in the relevant authoritative partition.
   
   This is the point at which the mutation becomes an authoritative historical fact.
5. Published
   
   A durable event/outbox becomes available to dependent subsystems.
   
   Delivery may be asynchronous and repeated.
   
   Consumers identify events by stable IDs and process them idempotently.
6. Projected
   
   Each affected subsystem updates its own representation.
   
   Processing can be parallel where dependencies allow.
   
   Every projection reports its processed input frontier and output version.
7. Activated
   
   A prepared projection is atomically switched into operational use at the appropriate subsystem boundary.
   
   For W02, this is an authoritative physics tick boundary.
   
   Other subsystems define their own activation boundaries.
8. Converged
   
   All projections required by a particular consistency query or operation have reached the requested causal frontier.
   
   This does not imply that every projection in the entire world is current.
F3. Commit semantics
A successful mutation acknowledgement must guarantee:
1. The command was accepted by the authoritative owner.
2. The canonical state transition was atomic.
3. The event was assigned an immutable identity.
4. The relevant regional revision advanced.
5. The commit is recoverable according to the defined persistence guarantee.
6. Required projection work has been recorded for downstream processing.
It must not guarantee:
- That collision geometry has already been rebuilt.
- That navigation has already been recalculated.
- That environmental models have converged.
- That every client has received the event.
- That every region in the universe has processed it.
This distinction is essential.
F4. Proposed mutation event contract
The following is an architectural contract, not a finalized wire format.
{
  "event_id": "uuid",
  "transaction_id": "uuid",
  "world_id": "uuid",
  "timeline_id": "uuid",

  "region_id": "region-42",
  "owner_epoch": 16,

  "region_revision": 121,
  "simulation_tick": 250000,

  "event_type": "BuildingPlaced",
  "schema_version": 1,

  "causation_id": "uuid",
  "correlation_id": "uuid",

  "affected_entities": [],
  "affected_regions": [],

  "dependency_scope": [
    "physics.collision",
    "navigation",
    "environment.hydrology",
    "replication.static_geometry"
  ],

  "payload": {}
}


Important design decisions:
- region_revision is monotonic within its authoritative revision domain.
- owner_epoch fences stale owners.
- timeline_id distinguishes alternate simulation branches.
- causation_id preserves event causality.
- dependency_scope identifies the required projections.
- affected_regions identifies cross-region consequences.
The event schema must support extension without requiring all consumers to understand every mutation type.
F5. Dependency graph
Every mutation must resolve into a directed acyclic dependency graph for its required derived work.

Example: building a bridge.
#chatgpt-mermaid-_r_ctm_{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;font-size:14px;fill:rgb(26, 28, 31);}@keyframes edge-animation-frame{from{stroke-dashoffset:0;}}@keyframes dash{to{stroke-dashoffset:0;}}#chatgpt-mermaid-_r_ctm_ .edge-animation-slow{stroke-dasharray:9,5!important;stroke-dashoffset:900;animation:dash 50s linear infinite;stroke-linecap:round;}#chatgpt-mermaid-_r_ctm_ .edge-animation-fast{stroke-dasharray:9,5!important;stroke-dashoffset:900;animation:dash 20s linear infinite;stroke-linecap:round;}#chatgpt-mermaid-_r_ctm_ .error-icon{fill:rgba(255, 255, 255, 0.96);}#chatgpt-mermaid-_r_ctm_ .error-text{fill:rgb(26, 28, 31);stroke:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ .edge-thickness-normal{stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .edge-thickness-thick{stroke-width:3.5px;}#chatgpt-mermaid-_r_ctm_ .edge-pattern-solid{stroke-dasharray:0;}#chatgpt-mermaid-_r_ctm_ .edge-thickness-invisible{stroke-width:0;fill:none;}#chatgpt-mermaid-_r_ctm_ .edge-pattern-dashed{stroke-dasharray:3;}#chatgpt-mermaid-_r_ctm_ .edge-pattern-dotted{stroke-dasharray:2;}#chatgpt-mermaid-_r_ctm_ .marker{fill:rgba(26, 28, 31, 0.494);stroke:rgba(26, 28, 31, 0.494);}#chatgpt-mermaid-_r_ctm_ .marker.cross{stroke:rgba(26, 28, 31, 0.494);}#chatgpt-mermaid-_r_ctm_ svg{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;font-size:14px;}#chatgpt-mermaid-_r_ctm_ p{margin:0;}#chatgpt-mermaid-_r_ctm_ .label{font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;color:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ .cluster-label text{fill:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ .cluster-label span{color:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ .cluster-label span p{background-color:transparent;}#chatgpt-mermaid-_r_ctm_ .label text,#chatgpt-mermaid-_r_ctm_ span{fill:rgb(26, 28, 31);color:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ .node rect,#chatgpt-mermaid-_r_ctm_ .node circle,#chatgpt-mermaid-_r_ctm_ .node ellipse,#chatgpt-mermaid-_r_ctm_ .node polygon,#chatgpt-mermaid-_r_ctm_ .node path{fill:rgb(224, 237, 254);stroke:rgb(83, 154, 248);stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .rough-node .label text,#chatgpt-mermaid-_r_ctm_ .node .label text,#chatgpt-mermaid-_r_ctm_ .image-shape .label,#chatgpt-mermaid-_r_ctm_ .icon-shape .label{text-anchor:middle;}#chatgpt-mermaid-_r_ctm_ .node .katex path{fill:#000;stroke:#000;stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .rough-node .label,#chatgpt-mermaid-_r_ctm_ .node .label,#chatgpt-mermaid-_r_ctm_ .image-shape .label,#chatgpt-mermaid-_r_ctm_ .icon-shape .label{text-align:center;}#chatgpt-mermaid-_r_ctm_ .node.clickable{cursor:pointer;}#chatgpt-mermaid-_r_ctm_ .root .anchor path{fill:rgba(26, 28, 31, 0.494)!important;stroke-width:0;stroke:rgba(26, 28, 31, 0.494);}#chatgpt-mermaid-_r_ctm_ .arrowheadPath{fill:rgba(26, 28, 31, 0.494);}#chatgpt-mermaid-_r_ctm_ .edgePath .path{stroke:rgba(26, 28, 31, 0.494);stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .flowchart-link{stroke:rgba(26, 28, 31, 0.494);fill:none;}#chatgpt-mermaid-_r_ctm_ .edgeLabel{background-color:rgb(255, 255, 255);text-align:center;}#chatgpt-mermaid-_r_ctm_ .edgeLabel p{background-color:rgb(255, 255, 255);}#chatgpt-mermaid-_r_ctm_ .edgeLabel rect{opacity:0.5;background-color:rgb(255, 255, 255);fill:rgb(255, 255, 255);}#chatgpt-mermaid-_r_ctm_ .labelBkg{background-color:rgba(255, 255, 255, 0.5);}#chatgpt-mermaid-_r_ctm_ .cluster rect{fill:rgba(255, 255, 255, 0.96);stroke:rgba(26, 28, 31, 0.08);stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .cluster text{fill:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ .cluster span{color:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ div.mermaidTooltip{position:absolute;text-align:center;max-width:200px;padding:2px;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;font-size:12px;background:rgba(255, 255, 255, 0.96);border:1px solid rgba(26, 28, 31, 0.08);border-radius:2px;pointer-events:none;z-index:100;}#chatgpt-mermaid-_r_ctm_ .flowchartTitleText{text-anchor:middle;font-size:18px;fill:rgb(26, 28, 31);}#chatgpt-mermaid-_r_ctm_ rect.text{fill:none;stroke-width:0;}#chatgpt-mermaid-_r_ctm_ .icon-shape,#chatgpt-mermaid-_r_ctm_ .image-shape{background-color:rgb(255, 255, 255);text-align:center;}#chatgpt-mermaid-_r_ctm_ .icon-shape p,#chatgpt-mermaid-_r_ctm_ .image-shape p{background-color:rgb(255, 255, 255);padding:2px;}#chatgpt-mermaid-_r_ctm_ .icon-shape .label rect,#chatgpt-mermaid-_r_ctm_ .image-shape .label rect{opacity:0.5;background-color:rgb(255, 255, 255);fill:rgb(255, 255, 255);}#chatgpt-mermaid-_r_ctm_ .label-icon{display:inline-block;height:1em;overflow:visible;vertical-align:-0.125em;}#chatgpt-mermaid-_r_ctm_ .node .label-icon path{fill:currentColor;stroke:revert;stroke-width:revert;}#chatgpt-mermaid-_r_ctm_ .node .neo-node{stroke:rgb(83, 154, 248);}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node rect,#chatgpt-mermaid-_r_ctm_ [data-look="neo"].cluster rect,#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node polygon{stroke:url(#chatgpt-mermaid-_r_ctm_-gradient);filter:drop-shadow( 1px 2px 2px rgba(185,185,185,1));}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].swimlane.cluster rect{filter:none;}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node path{stroke:url(#chatgpt-mermaid-_r_ctm_-gradient);stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node .outer-path{filter:drop-shadow( 1px 2px 2px rgba(185,185,185,1));}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node .neo-line path{stroke:rgb(83, 154, 248);filter:none;}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node circle{stroke:url(#chatgpt-mermaid-_r_ctm_-gradient);filter:drop-shadow( 1px 2px 2px rgba(185,185,185,1));}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].node circle .state-start{fill:#000000;}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].icon-shape .icon{fill:url(#chatgpt-mermaid-_r_ctm_-gradient);filter:drop-shadow( 1px 2px 2px rgba(185,185,185,1));}#chatgpt-mermaid-_r_ctm_ [data-look="neo"].icon-shape .icon-neo path{stroke:url(#chatgpt-mermaid-_r_ctm_-gradient);filter:drop-shadow( 1px 2px 2px rgba(185,185,185,1));}#chatgpt-mermaid-_r_ctm_ .node text{font-size:14px;font-weight:600;letter-spacing:normal;fill:rgb(0, 79, 153);}#chatgpt-mermaid-_r_ctm_ .edgeLabels text{font-size:13px;font-weight:600;letter-spacing:-0.08px;fill:rgb(0, 79, 153);}#chatgpt-mermaid-_r_ctm_ .node tspan[font-weight="normal"],#chatgpt-mermaid-_r_ctm_ .edgeLabels tspan[font-weight="normal"]{font-weight:600;}#chatgpt-mermaid-_r_ctm_ .edgeLabel .label rect{opacity:1;rx:13px;ry:13px;fill:rgb(245, 250, 255);stroke:rgb(206, 219, 229);stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .node rect,#chatgpt-mermaid-_r_ctm_ .node circle,#chatgpt-mermaid-_r_ctm_ .node ellipse,#chatgpt-mermaid-_r_ctm_ .node polygon,#chatgpt-mermaid-_r_ctm_ .node path{fill:rgb(229, 243, 255);stroke:rgba(0, 0, 0, 0.1);stroke-width:1px;}#chatgpt-mermaid-_r_ctm_ .node rect{rx:16px;ry:16px;}#chatgpt-mermaid-_r_ctm_ .mermaid-decision .label-container{fill:rgb(245, 250, 255);stroke:rgb(206, 219, 229);stroke-dasharray:2,2;}#chatgpt-mermaid-_r_ctm_ .edgePaths .flowchart-link{stroke:rgba(26, 28, 31, 0.494);stroke-width:1px;stroke-linecap:round;stroke-linejoin:round;}#chatgpt-mermaid-_r_ctm_ .marker{fill:rgba(26, 28, 31, 0.494);stroke:rgba(26, 28, 31, 0.494);}#chatgpt-mermaid-_r_ctm_ :root{--mermaid-font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;}BuildingPlaced committedCollision geometryEnvironmental impactStatic replicationPhysics activationHydrology projectionVehicle interactionsWater simulationNavigation updateRoute planningClient presentation





The graph is mutation-specific.

A simple ownership change does not require rebuilding collision geometry.

A terrain deformation may require collision, navigation, hydrology and vegetation projections.

A change to a vehicle's cosmetic appearance may require only replication.

No subsystem should be notified merely because some unrelated world revision advanced.
F6. Revision and causal model
I recommend three related concepts.

1. Authoritative revision

A monotonic sequence within the responsible authoritative revision domain.

2. Projection watermark

The exact authoritative input frontier represented by a derived subsystem.

3. Causal frontier

The set of authoritative revision positions that must be included to establish a particular consistent view.

For example:
World: main
Timeline: 1

Region A:
  Owner epoch: 16
  Authoritative revision: 121
  Physics applied: 121
  Environment applied: 118
  Navigation applied: 120
  Replication published: 121

Region B:
  Owner epoch: 8
  Authoritative revision: 87
  Physics applied: 87
  Environment applied: 87
  Navigation applied: 85
  Replication published: 87


This is an illustrative representation.

It is not a proposal for a single global counter.

The projection watermark must also identify the relevant event sequence or causal frontier. A numerical maximum alone is insufficient when processing has gaps.
Proposed consistency token
{
  "world_id": "uuid",
  "timeline_id": "main",
  "regions": [
    {
      "region_id": "region-A",
      "owner_epoch": 16,
      "authoritative_revision": 121,
      "projections": {
        "physics": {
          "applied_revision": 121,
          "active_revision": 121
        },
        "navigation": {
          "applied_revision": 120,
          "active_revision": 120
        },
        "environment": {
          "applied_revision": 118,
          "active_revision": 118
        },
        "replication": {
          "published_revision": 121
        }
      }
    }
  ]
}


applied_revision and active_revision must remain separate.

A projection can be calculated and prepared without yet being operational.
F7. Physics visibility and collision activation
This is the most important operational contract.

A mutation affecting physical geometry must follow:
1. Authoritative commit.
2. Publication of the physical mutation event.
3. Physics runtime receives the mutation.
4. Collision structures and physical properties are prepared.
5. Physics validates the new representation.
6. The new representation is activated atomically at a physics tick boundary.
7. Physics publishes its active revision.
The previous representation remains coherent until the activation boundary.

No physics step may use a partially rebuilt collision structure.

For mutations that cannot tolerate a temporary stale representation, the command must request a strict activation policy.

That policy may defer the mutation's physical effective time until preparation succeeds.

For ordinary non-critical changes, physics may continue using the last coherent snapshot while reporting its revision as stale.

A consumer must never silently interpret stale physical state as current canonical geometry.
F8. Environment and navigation
Environment and navigation are separate projections.

They must not be forced into the physics tick.

Examples:
Mutation	Required projections
Place decorative object	Replication
Build road	Navigation, collision, replication
Destroy wall	Collision, navigation, replication
Excavate terrain	Collision, environment, navigation, replication
Build dam	Collision, hydrology, navigation, replication
Change atmospheric parameter	Environment, affected physical force inputs

Environmental systems must retain their own simulation time and data resolution.

Navigation must advertise its graph revision and its corresponding authoritative input frontier.

A route planner must either use a sufficiently current graph or explicitly report that its result is based on older infrastructure data.
F9. Replication readiness
Replication is not a global completion barrier.

It has two separate responsibilities:
- Publish committed authoritative changes.
- Provide enough metadata for clients to understand the consistency of the delivered state.
A client may receive a committed building placement before navigation has updated.

It must not infer that the navigation graph is already current.

For snapshots, the replication contract must identify the exact world revision and relevant region epochs represented.

For deltas, it must preserve baseline identity, sequence and recovery behavior.

This extends W07 without changing its fundamental snapshot/delta approach.
F10. Failure and recovery
Every projection consumer must support:
- Idempotent processing.
- Duplicate event detection.
- Ordered processing of causally dependent events.
- Checkpointing.
- Recovery from the last valid checkpoint.
- Replay of subsequent relevant events.
- Revision verification.
- Explicit degraded or unavailable status.
A projection failure must not reverse a committed mutation.

Instead:
1. The projection enters a failed or degraded state.
2. Its last valid active revision remains identifiable.
3. Dependent operations apply their stale-data policy.
4. The projection recovers from a checkpoint and event replay.
5. The rebuilt representation is validated.
6. Activation occurs at its normal safe boundary.
If the projection cannot recover, it remains unavailable rather than advertising false readiness.
F11. Multi-region transaction protocol
For a mutation affecting multiple authoritative regions, use explicit coordination.

Recommended lifecycle:
Coordinator
    |
    +-- Prepare Region A
    |
    +-- Prepare Region B
    |
    +-- Validate all participants
    |
    +-- Durable transaction decision
    |
    +-- Commit Region A
    |
    +-- Commit Region B
    |
    +-- Publish regional events
    |
    +-- Independent projection processing


The authoritative commit decision must be unique and recoverable.

Participants must be idempotent.

Ownership epochs must be checked before every authoritative state change.

For operations where strict atomicity is unnecessary, a compensating workflow may be preferable.

This follows W06 §5.4 rather than introducing unrestricted global transactions.

Important distinction: multi-region atomic commitment does not require all regional projections to activate simultaneously. Each region may activate its prepared representation at its own valid boundary, provided the transaction's declared visibility policy is respected.

G. Cross-system impact
G1. Compatibility assessment
Document	Status	Required action
W01	Compatible	Add explicit environmental projection watermarks
W02	Clarification	Define mutation queue and physics activation boundary
W03	Compatible	Consume versioned physical/environmental interfaces
W04	Clarification	Define scope of celestial mutations
W05	Change	Coordinate regional commit, revision and ownership epochs
W06	Change	Replace simple pending-status model with dependency lifecycle
W07	Change	Add authoritative frontier and projection readiness metadata
W08	Clarification	Define environmental invalidation and projection acknowledgements
W09	Change	Define causal frontiers, recovery and consistency guarantees

G2. Required changes by document
W01

Add:
- Input revision provenance.
- Projection validity.
- Explicit environmental activation status.
Do not change the underlying environmental hierarchy.

W02

Add:
- Mutation ingestion contract.
- Physics-visible revision.
- Collision representation activation.
- Stale geometry policy.
- Physics tick activation semantics.
Do not transfer physical state ownership to W06.

W03

Add:
- Versioned dependencies on physical and environmental representations.
- Defined behavior when a required projection is unavailable.
W04

Clarify how changes to celestial parameters invalidate planetary and downstream representations.

W05

Add:
- Cross-region mutation transaction identity.
- Participant commit acknowledgement.
- Per-region revision continuity.
- Projection checkpoint transfer during ownership migration.
- Recovery of in-progress distributed transactions.
The existing ownership model and fencing mechanism remain intact.

W06

This document needs the most substantial conceptual update.

The existing derived_updates field should become a structured projection status model.

The mutation system must also distinguish:
- Authoritative commitment.
- Event publication.
- Projection processing.
- Projection activation.
- Convergence for a requested consistency level.
W07

Add:
- Snapshot authoritative frontier.
- Region owner epoch.
- Timeline identity.
- Structural mutation sequence.
- Optional projection readiness information.
The client must never become an authority over persistent physical state.

W08

Add:
- Mutation invalidation contracts.
- Environmental input frontier.
- Projection completion and validity.
- Recovery of derived environmental representations.
W09

Add:
- Causal ordering rules.
- Projection checkpoint semantics.
- Cross-region transaction recovery.
- Defined consistency guarantees for critical interactions.
G3. Decisions that must be coordinated
The following interfaces cannot be independently finalized by GPT-05:
Coordination	Participants	Reason
Physics activation contract	GPT-05 + W02	Determines when mutations affect physical reality
Region revision contract	GPT-05 + W05	Determines authoritative ordering
Replication frontier	GPT-05 + W07	Determines what clients can safely infer
Environmental revision provenance	GPT-05 + W01 + W08	Determines environmental validity
Persistence durability	GPT-05 + W05 + W09	Determines exact commit guarantees
Distributed transaction recovery	GPT-05 + W05 + W09	Prevents partial or duplicated commitment

These are integration decisions, not reasons to delay the entire architectural design.

H. Required architectural decisions
The central integrator should accept or reject the following decisions explicitly.
ADR-MUT-001 — Authoritative mutation commitment
Recommend ACCEPT
A mutation is committed only after its authoritative state transition and recoverable event record have been established atomically.

Derived subsystem completion is not part of commit semantics.
ADR-MUT-002 — Dependency-driven consistency
Recommend ACCEPT
Replace universal mutation completion barriers with mutation-specific dependency graphs.

Every derived subsystem processes only the mutations relevant to its responsibilities.
ADR-MUT-003 — Separate applied and active revisions
Recommend ACCEPT
A projection must distinguish:
- Input revision processed.
- Representation prepared.
- Representation currently active.
This is especially important for physical simulation.
ADR-MUT-004 — Scoped causal revision model
Recommend ACCEPT
Use regional authoritative revisions and projection watermarks, combined with causal frontiers where dependencies cross regions.

Do not require a single globally incremented revision for all world activity.
ADR-MUT-005 — Physics activation boundary
Recommend ACCEPT
Physical mutations become active at an authoritative physics tick boundary after the new representation has been prepared and validated.

W02 retains complete ownership of physical integration.
ADR-MUT-006 — Asynchronous environmental and navigation updates
Recommend ACCEPT
Environmental and navigation projections operate independently according to their own simulation frequencies and dependency requirements.

They must expose their input frontier and readiness.
ADR-MUT-007 — Replication independence
Recommend ACCEPT
Replication may publish committed mutations before every derived subsystem converges.

Clients receive explicit authoritative revision and timeline metadata.
ADR-MUT-008 — Cross-region atomicity
Recommend ACCEPT
Use explicit multi-region prepare/commit coordination where a mutation requires atomic changes across authoritative owners.

Do not use global transactions as the default.
ADR-MUT-009 — Projection failure isolation
Recommend ACCEPT
A failed projection must not invalidate an already committed mutation.

It must report its last valid state, recover independently and reactivate only after validation.
ADR-MUT-010 — Durable commit and simulation persistence separation
Conditional acceptance
Persistent world mutations require recoverable commit records.

Continuous high-frequency physical state must retain its independent checkpoint and recovery model.

The precise durability and recovery-point objectives must be coordinated with W05 and W09.
ADR-MUT-011 — Consistency requirements by operation
Recommend ACCEPT
Every consumer must declare the minimum consistency it requires.

Examples:
- Vehicle collision: active physical representation.
- Route planning: sufficiently current navigation representation.
- Water-flow calculation: valid environmental input frontier.
- Client presentation: authoritative committed state.
- Economic transaction: strong transactional consistency.
No universal completion status should be imposed on all consumers.

I. Validation plan
The following tests validate the architecture rather than a particular implementation.

No benchmark results are claimed.
I1. Mutation lifecycle tests
Test	Expected result
Valid building placement	One authoritative commit
Invalid placement	No committed mutation event
Duplicate command	No duplicate state transition
Stale expected revision	Conflict or explicit reconciliation
Crash during commit	Recovery yields either committed or uncommitted outcome, never partial state
Projection unavailable	Mutation remains committed
Projection replay	Same resulting representation
Duplicate event delivery	No duplicate application

I2. Physics consistency tests
Test A — Wall destruction
1. Commit destruction.
2. Keep the previous collision snapshot active.
3. Prepare the new collision representation.
4. Activate at the next eligible physics boundary.
5. Verify that the physics runtime never consumes a partially rebuilt representation.
Expected invariant:

ActiveCollisionRevision always identifies a complete, validated representation.

Test B — Bridge construction

Verify:
- Building entity exists after authoritative commit.
- Collision representation becomes active at the defined tick.
- Vehicle interactions use the correct collision revision.
- Navigation can independently catch up.
- Client replication does not falsely imply navigation readiness.
Test C — Rapid repeated mutations

Apply multiple terrain changes before the environment projection finishes processing the previous change.

Expected result:
- Causal ordering is preserved.
- Projection can coalesce rebuild work where mathematically valid.
- Published revisions never claim missing dependencies.
I3. Distributed-region tests
Test:
1. Mutation affecting one region.
2. Mutation affecting two regions.
3. Participant failure before prepare.
4. Participant failure after prepare.
5. Coordinator failure during transaction decision.
6. Ownership migration during pending projection work.
7. Old owner attempts to publish after epoch change.
8. Duplicate cross-region messages.
9. Network partition during commit.
Required invariants:
- No two valid authoritative owners.
- No partially committed atomic transaction.
- No stale owner can modify state.
- No lost committed mutation.
- No duplicate authoritative application.
I4. Dependency graph tests
Test a mutation that affects:
- Collision.
- Navigation.
- Hydrology.
- Replication.
Then test an unrelated mutation that affects only replication.

Expected result:

The unrelated mutation must not trigger unnecessary environmental or physical rebuilds.

Also test dependency graph evolution when a new subsystem is introduced.

Adding a new derived system must not require redesigning the entire mutation protocol.
I5. Recovery tests
Simulate:
- Consumer process crash.
- Lost acknowledgement.
- Duplicate event.
- Out-of-order event delivery.
- Corrupted projection checkpoint.
- Missing journal segment.
- Recovery after region migration.
- Rebuild from older checkpoint.
Measure:
- Time to restore projection readiness.
- Projection lag.
- Lost or duplicated events.
- Revision divergence.
- Recovery correctness.
I6. Measurable acceptance criteria
The architecture should be accepted only if these invariants hold:
Invariant	Acceptance criterion
Authoritative commitment	Exactly one outcome per command ID
Atomicity	No partial local or required cross-region commit
Ownership	One valid owner per active region
Causality	No projection publishes a frontier with missing prerequisites
Physics	No partially activated collision representation
Environment	Every result identifies its input provenance
Navigation	Every graph identifies its source frontier
Replication	Client can identify authoritative state revision
Recovery	Committed mutations survive recovery
Isolation	Unrelated projections do not block one another

I7. Performance validation
The most important measurements are:
- Mutation commit latency p50/p95/p99.
- Event publication latency.
- Projection processing lag.
- Projection activation latency.
- Projection backlog.
- Number of pending dependencies per mutation.
- Cross-region transaction duration.
- Recovery duration.
- Storage write amplification.
- Physics tick delay attributable to mutation activation.
The critical performance test is not maximum event throughput.

It is whether a high volume of structural mutations can be processed without destabilizing unrelated real-time simulation.

J. Unresolved questions
These questions must remain open until the relevant architecture owners make coordinated decisions.
J1. Persistence durability
W05 and W09 recommend asynchronous persistence.

However, they do not fully define when an authoritative mutation acknowledgement becomes durable.

Questions:
- Is local journal persistence sufficient?
- Is replicated journal persistence required?
- What is the acceptable recovery-point objective?
- Which mutation classes require stronger durability guarantees?
Recommendation: establish durability classes before implementing persistent world transactions.
J2. Global versus regional revision identity
W06 uses world_revision, while W05 defines regional state_version and W09 recommends independent regional timelines.

A single global revision may create unnecessary serialization.

Recommendation: use regional authoritative sequences and a world-level consistency token composed from relevant regional frontiers.

The final naming and compatibility rules require W05/W09 agreement.
J3. Multi-region transaction protocol
W06 suggests prepare/commit and compensating transactions but does not finalize the protocol.

Questions:
- Is a coordinator-based commit decision sufficient?
- How is coordinator failure recovered?
- What happens when an ownership epoch changes during preparation?
- Which operations genuinely require atomic cross-region visibility?
Recommendation: specify one recoverable transaction decision protocol, not unrestricted distributed transactions for every mutation.
J4. Projection activation policy
W02 must decide whether all physical changes activate at the next eligible tick or whether some classes require explicit scheduled activation.

Potential categories:
- Static geometry.
- Dynamic collision objects.
- Terrain deformation.
- Destruction.
- Material changes.
- Structural failures.
Recommendation: define mutation-specific activation policies.
J5. Navigation ownership
Navigation is discussed as a required dependent system in W06 but does not have a clearly established owner among W01–W09.

It needs an explicit architectural owner.

This worker does not assign navigation to physics or environmental simulation merely because they provide its inputs.
J6. Environmental causality
W08 uses several simulation frequencies.

A terrain mutation can invalidate hydrology without immediately requiring a complete climate simulation recalculation.

The exact dependency between environmental scales requires scientific validation.
J7. Historical branches
W06 supports alternate world branches.

A branch must maintain its own authoritative revision history and projection identity.

It remains unresolved whether projections can share immutable parent checkpoints or must be reconstructed independently after a branch mutation.

Recommendation: preserve copy-on-write compatibility and make branch identity mandatory in all projection metadata.
J8. Cross-region physical interactions
W05 and W09 both recognize the problem of interactions across regional boundaries.

A multi-region mutation may involve physics islands owned by different processes.

The exact boundary synchronization protocol is outside the scope of C-05 and requires a dedicated physics/distributed-execution decision.
J9. Stale-data policy
Each consumer must define what it does when a required projection is behind.

Possible policies:
- Continue using the last valid representation.
- Reject the operation.
- Defer execution.
- Use a lower-fidelity approximation.
- Temporarily restrict interactions.
There cannot be one universal policy for all physical and gameplay domains.

K. Final assessment
RESOLVED CONDITIONALLY
C-05 is architecturally resolved at the conceptual and contract level.

The recommended architecture is:

Authoritative Mutation Journal + Dependency-Driven Projection Processing + Explicit Activation Boundaries + Scoped Causal Consistency.

It is compatible with the principal architectural direction established by W01–W09.

Most importantly, it does not require:
- A globally synchronized simulation tick.
- A universal mutation completion barrier.
- One physical representation per gameplay subsystem.
- Synchronous completion of every environmental calculation.
- Unity to become an authority over world state.
- A single global database transaction for the entire planet.
It preserves the central architectural objective: one persistent logical world with multiple independently executing but causally connected simulation domains.
What remains conditional
The final protocol depends on coordinated decisions from:
1. W02 — physical activation semantics.
2. W05 — distributed transaction and ownership semantics.
3. W07 — replication consistency metadata.
4. W08 — environmental projection provenance.
5. W09 — durability and distributed recovery.
These dependencies do not invalidate the recommended solution. They determine its final interface details.
Final recommendation to the central integrator
Accept the eleven ADR proposals in section H as the C-05 architectural baseline.

Do not allow individual workers to independently invent their own revision systems, event ordering rules or mutation completion states.

The mutation lifecycle should become a shared architectural contract used by every simulation domain.

Repository evidence and traceability
All nine worker documents were opened and reviewed. The following are the principal sections used in this resolution.
Document	Relevant sections
W01	§6.2–6.5 — coordinates, state and environment response
W02	§5 — collision; §7.1 — authoritative tick; §10 — rollback
W03	§8.5–9.3 — contact and integration dependencies
W04	§6 — performance and multiscale celestial simulation
W05	§2.6 — ownership; §6 — persistence; §9 — contracts
W06	§4.4; §5; §10; §11
W07	§7 — replication; §8 — time; §13 — integration
W08	§1; §8; §9 — environmental hierarchy and execution
W09	§5 — distributed execution; §7 — recovery; §14 — decisions

Evidence classification:
- Existing architecture: identified from repository documents.
- Existing proposals: attributed to their respective workers, not treated as accepted decisions.
- GPT-05 recommendations: explicitly introduced as proposed ADRs.
- Unresolved dependencies: listed separately rather than silently assumed to be solved.

End of GPT-05 — World Mutation and Consistency Lifecycle.

Architecture research only. No implementation or repository modifications.