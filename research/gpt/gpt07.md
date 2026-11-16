Potvrzeno. Repozitář `projectzonio/what_if` je veřejně dostupný a podařilo se mi jej otevřít.



Přístup ověřen

[projectzonio/what_if – GitHub](https://github.com/projectzonio/what_if?utm_source=chatgpt.com)



Obsahuje všech devět požadovaných dokumentů `w1.md` až `w9.md`. Nebudu tedy požadovat jejich nahrávání.

Začínám společnou architektonickou analýzou. W07 nebudu považovat za automaticky správný zdroj rozhodnutí; budu konfrontovat jeho návrhy s ostatními osmi dokumenty.

# GPT-07 — Determinism, Replay and Reproducibility

Planetary Engine · C-07 · Architecture Resolution · Research revision 1

Repository analysis started and source documents verified

## A. Executive decision

Recommended architecture: Hierarchical Determinism Contract (HDC).

Planetary Engine must not attempt to make the entire universe bitwise deterministic. It must guarantee deterministic authority, event ordering and recoverable history, while providing stronger reproducibility guarantees selectively for individual simulation domains.

The architecture has three principal guarantee levels:

| Level              | Guarantee                                               | Intended use                                           |
| ------------------ | ------------------------------------------------------- | ------------------------------------------------------ |
| D1 — Authoritative | One accepted mutation order and one authoritative state | Entire persistent world                                |
| D2 — Reproducible  | Replay under a defined numerical tolerance              | Physics, environment, economy, scientific simulation   |
| D3 — Bitwise       | Identical output for identical versioned inputs         | Selected discrete systems and deterministic algorithms |

This extends the three-level proposal already present in W06 §7.4, rather than introducing a competing global synchronization model.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



The most important architectural distinction:

- Deterministic authority does not require deterministic execution on every machine.
- Reproducible history does not require bitwise-identical floating-point calculations.
- A fixed timestep does not guarantee deterministic simulation.
- Distributed regions do not need a global lockstep clock to maintain a coherent world.

The canonical source of truth is the authoritative Rust simulation runtime, supported by immutable committed events, versioned snapshots, explicit input ordering and recorded external dependencies.

## B. Existing architecture

### B.1 Findings from the nine worker documents

The repository contains nine complementary proposals:

| Document | Architectural responsibility          | Relevance to C-07                                                   |
| -------- | ------------------------------------- | ------------------------------------------------------------------- |
| W01      | Planetary geometry and environment    | Procedural generation, coordinate and environmental reproducibility |
| W02      | Universal physics runtime             | Ticks, solver state, rollback                                       |
| W03      | Entity physics                        | Domain-specific numerical behavior                                  |
| W04      | Celestial simulation                  | Astronomical time and adaptive numerical integration                |
| W05      | Authoritative server                  | Ownership, migration, persistence and recovery                      |
| W06      | Gameplay and world mutation           | Immutable events, branching and replay                              |
| W07      | Unity networking                      | Prediction, reconciliation and presentation                         |
| W08      | Scientific/environmental simulation   | Numerical reproducibility and model provenance                      |
| W09      | Performance and distributed execution | Parallel execution, fidelity transitions and scalability            |

All nine files were verified in the public repository. The relevant sections below are drawn from their actual contents, not inferred from their titles.

### B.2 Existing determinism-related proposals

W01 — Procedural generation

W01 §3.4, lines 420–435, proposes canonical boundary sampling and a deterministic terrain function:

\\[ Tile=f(BodyID,TileID,Seed,GeneratorVersion,DataRevision) \\]

It also requires generator updates to use explicit versions instead of silently changing persisted geometry.

This is a good foundation, but it does not yet define the complete reproducibility contract for imported data, generator dependencies or numerical implementation changes.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



W02 — Physics runtime

W02 §3 and §7, particularly lines 259–271 and 619–669, recommend fixed authoritative timesteps, integer tick counters and deterministic command processing.

W02 §10.3, lines 925–967, goes further and defines rollback using checkpoints, command replay and state hashes.

Importantly, it identifies solver state that must be preserved:

- Contact manifolds.
- Warm-start impulses.
- Joint state.
- Sleeping state.
- Broadphase and narrowphase state.
- CCD state.
- Domain-specific state.
- RNG state.
- Reference-frame state.

W02 also explicitly warns that Rapier internal snapshots are not stable across different library builds and configurations. It consequently separates internal rollback snapshots from permanent persistence formats.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



W05 — Authoritative distributed runtime

W05 §2.2, lines 249–269, establishes server authority over physical state.

W05 §9.5, lines 795–816, proposes region migration using ownership epochs and checkpoint hashes.

W05 §6.2, lines 589–614, proposes a hybrid persistence strategy involving an event journal and snapshots.

This provides the basis for replay across process migration, but does not completely specify how pending events, concurrent cross-region interactions and execution history are transferred.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+2



W06 — World mutation and alternate timelines

W06 §5.1 distinguishes commands from committed events.

W06 §5.5 recommends hybrid event persistence rather than recording every continuous physical update as a permanent world mutation.

W06 §7.4 explicitly rejects assuming global deterministic lockstep and proposes D1, D2 and D3.

W06 §7.6 establishes region-specific mutation sequences and ownership.

This is the closest existing proposal to the recommended architecture.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+2



W07 — Unity prediction

W07 §5.6, lines 800–825, defines client prediction, authoritative reconciliation and replay of unacknowledged inputs.

The client is explicitly not the authority over physical state. It can predict selected local actions and correct its internal state using server snapshots.

W07 does not establish that Unity and Rust produce identical physics results.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



W08 — Scientific reproducibility

W08 §10.4, lines 620–633, explicitly distinguishes authoritative physical state from scientific reproducibility.

It warns that parallel floating-point calculations cannot automatically be expected to produce bitwise-identical results across different hardware.

It also identifies important replay inputs: model version, initial state, simulation timestamp, random seed, numerical configuration, external forcing and state transitions.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



W09 — Distributed fidelity

W09 §3.1–3.3, lines 265–313, defines L0–L3 simulation fidelity and requires transitions to preserve canonical state, including deterministic identifiers and seeds.

W09 §11.5 and §12 establish communication and performance boundaries.

The architecture therefore already recognizes that identical entities can be represented by different numerical models at different fidelity levels.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+2



### B.3 Existing architecture status

The following is my assessment, not a claim that the repository has already accepted these decisions.

| Topic                              | Existing status                   |
| ---------------------------------- | --------------------------------- |
| Authoritative server               | Proposed consistently             |
| Integer simulation ticks           | Proposed                          |
| Fixed interactive physics timestep | Proposed                          |
| Deterministic command ordering     | Mentioned, incompletely specified |
| Immutable world mutation events    | Proposed                          |
| Hybrid event journal and snapshots | Proposed                          |
| Physics rollback                   | Proposed                          |
| RNG persistence                    | Partially addressed               |
| Cross-process replay               | Incomplete                        |
| Cross-platform bitwise physics     | Not established                   |
| Astronomical reproducibility       | Incomplete                        |
| Environmental reproducibility      | Partially addressed               |
| Alternate timeline determinism     | Incomplete                        |
| Global event ordering              | Not defined                       |

The main architectural gap is not the absence of fixed timestep or snapshots. It is the absence of a unified contract connecting all of them.

## C. Problem analysis

### C.1 Why fixed timestep is insufficient

A fixed timestep defines when the simulation advances. It does not define whether two executions produce identical results.

Consider:

\\[ S\_{n+1}=F(S_n,I_n,\Delta t) \\]

Even if \\(S_n\\), \\(I_n\\) and \\(\Delta t\\) are identical, the resulting \\(S\_{n+1}\\) can differ because of:

1. Floating-point operation ordering.
2. Different processor instruction sets.
3. SIMD implementations.
4. Parallel task scheduling.
5. Unordered collection iteration.
6. Different collision pair ordering.
7. Solver convergence behavior.
8. Different numerical libraries.
9. Random number generation.
10. External environmental inputs.

A fixed timestep removes one source of nondeterminism: variable integration intervals. It does not eliminate the other sources.

W02 correctly identifies fixed timesteps as the default for interactive physics, but its proposed deterministic command processing must be expanded into a full execution contract.

### C.2 Nine distinct determinism dimensions

These dimensions must not be conflated.

| Dimension             | Definition                                     | Required guarantee                              |
| --------------------- | ---------------------------------------------- | ----------------------------------------------- |
| Event ordering        | Same authoritative order of accepted mutations | Mandatory                                       |
| Simulation replay     | Ability to reconstruct a historical state      | Mandatory within declared replay profile        |
| Physics determinism   | Repeatability of physical calculations         | Domain-dependent                                |
| Cross-process         | Reproduction after process migration           | Mandatory for recovery                          |
| Cross-platform        | Same result on different architectures         | Only where explicitly qualified                 |
| Bitwise               | Identical serialized output bytes              | Selected systems                                |
| Procedural generation | Identical canonical generated output           | Mandatory for persisted procedural assets       |
| Distributed timeline  | Consistent causality between regions           | Mandatory                                       |
| Client prediction     | Agreement through authoritative correction     | Mandatory for correctness, not bitwise equality |

### C.3 Event ordering is not global chronological ordering

A distributed world may contain multiple regions executing concurrently.

For example:

- Region A produces an industrial resource.
- Region B consumes that resource.
- Region C transfers ownership of the factory.
- Region D processes a player's interaction.

These operations can originate independently.

Requiring a single globally synchronized sequence for every event would create a scalability bottleneck and contradict W05/W09.

The recommended solution is:

- A canonical sequence within each authoritative region.
- Explicit causal dependencies between regions.
- Deterministic ordering of events that conflict over the same authoritative state.
- No requirement to totally order independent events.

This is a critical distinction.

A globally unique event identifier is not the same as a globally ordered event sequence.

### C.4 Floating-point differences

Floating-point arithmetic is not generally associative:

\\[ (a+b)+c \neq a+(b+c) \\]

Consequently, parallel execution can change numerical results even if the mathematical problem is identical.

This is especially important for:

- N-body simulations.
- Fluid dynamics.
- Atmospheric models.
- Ocean circulation.
- Large constraint systems.
- Turbulence.
- High-order numerical integration.

A global requirement for bitwise identity would impose substantial restrictions on parallelism, SIMD, hardware selection and solver development.

That restriction is unjustified for most scientific and physical subsystems.

### C.5 Adaptive numerical integration conflicts with universal fixed ticks

W02 proposes fixed interactive timesteps.

W04 includes astronomical models where adaptive integration is appropriate. W08 also addresses multiscale scientific models.

For example, W04 references WHFast, IAS15 and TRACE for different orbital conditions.

Forcing every scientific solver to use the same fixed timestep would compromise numerical efficiency or accuracy.

The distinction must be:

- World time is authoritative.
- Simulation execution frequency is domain-specific.
- Numerical integration intervals are solver-specific.
- Every committed result is associated with a well-defined simulation-time interval.

The world does not need one universal integration step.

### C.6 Replay is not the same as restoring entity positions

A physics snapshot containing position, velocity and rotation is insufficient for exact continuation.

The next result may depend on:

- Previous contact state.
- Solver warm-start information.
- Pending collision events.
- Sleeping and activation state.
- Internal integrator state.
- RNG state.
- Reference-frame state.
- Environment revisions.
- Scheduled operations.

W02 recognizes much of this for rigid-body physics. The requirement must be extended to all simulation domains.

### C.7 The most serious unresolved contradiction

W06 proposes reproducible subsystem replay, W02 proposes physics rollback, W05 proposes checkpoint-based region migration, and W09 permits different fidelity levels.

None independently defines how their replay guarantees compose.

A replay of an entity that transitions from L0 to L2, crosses a region boundary, and later returns to L0 cannot be considered reproducible if the transition loses hidden state or uses a different unrecorded model version.

Resolution: Replay must preserve both the canonical entity state and the identity of the numerical model used during each historical interval.

## D. Cross-system dependencies

### D.1 Dependency matrix

| Worker | What determinism requires                 | What the worker must guarantee                      | Compatibility                 |
| ------ | ----------------------------------------- | --------------------------------------------------- | ----------------------------- |
| W01    | Stable spatial and environmental inputs   | Versioned geometry, generator and data revisions    | Compatible with clarification |
| W02    | Replayable physical integration           | Complete solver checkpoints and ordered execution   | Requires extension            |
| W03    | Domain-specific replay                    | Portable physical state and model versions          | Requires clarification        |
| W04    | Reproducible celestial calculations       | Time scales, ephemeris and integrator provenance    | Requires extension            |
| W05    | Replay across owners                      | Epoch fencing, checkpoint and journal continuity    | Requires extension            |
| W06    | Reproducible world mutations and branches | Causal event history and branch identity            | Requires extension            |
| W07    | Reconciliation with server state          | Tick/input sequence and authoritative correction    | Compatible                    |
| W08    | Scientific reproducibility                | Full numerical configuration and forcing provenance | Requires extension            |
| W09    | Distributed execution                     | Causal ordering and fidelity transition history     | Requires extension            |

### D.2 W01 — Planetary geometry and environment

Dependency: Canonical environmental samples and generated geometry must be reproducible.

Required contract:

- Stable body and tile identifiers.
- Generator version.
- Seed.
- Data revision.
- Coordinate-frame definition.
- Environment model revision.
- Time of evaluation.

A generated terrain tile must not change merely because it was loaded on another server.

However, an environment provider that interpolates weather or atmospheric fields must also identify its interpolation and input-data versions.

W01 §3.4 already provides the foundation. Its generator identity must become part of the general replay metadata.

### D.3 W02 — Universal physics runtime

W02 owns the most important execution-level contract.

It must define:

- Canonical simulation tick.
- Command application order.
- Simulation phase ordering.
- Solver configuration identity.
- Snapshot completeness.
- State hashing.
- Replay compatibility.

The existing lifecycle in W02 §7.1, lines 621–666, is a suitable basis.

One important refinement: parallel physics execution can remain nondeterministic internally if its final authoritative results are validated against the declared D2 tolerance.

D3 physics must use stronger constraints, including deterministic iteration and reduction order.

### D.4 W03 — Entity physics

Different entities use different mathematical models.

A rocket, aircraft, automobile and human character cannot be required to use the same solver.

W03's division of responsibility between environment authority, universal physics and specialized entity physics is compatible with HDC.

However, every domain must expose:

1. Model identifier.
2. Model version.
3. Input dependencies.
4. Numerical configuration.
5. State checkpoint.
6. Replay capability.
7. Declared numerical tolerance.

A vehicle replay must not silently substitute a newer tire model or suspension solver.

### D.5 W04 — Celestial simulation

W04 §3.7, lines 519–526, establishes three different concepts:

- Simulation time.
- Wall-clock time.
- Presentation time.

This distinction is essential.

The celestial subsystem must additionally record:

- Ephemeris source and revision.
- Reference-frame definition.
- Time-scale conversion data.
- Gravitational model.
- Integration method.
- Numerical tolerances.
- Adaptive-step history where required for exact continuation.

Astronomical simulation may be numerically reproducible without being bitwise identical.

### D.6 W05 — Authoritative server

W05 owns region authority and process migration.

HDC requires the existing ownership epoch to be part of every authoritative journal entry and checkpoint.

The source process must not be able to commit events after the new ownership epoch becomes active.

Migration must transfer:

- Canonical state.
- Checkpoint identity.
- Event journal position.
- Pending commands.
- Scheduled events.
- RNG state.
- Active solver state.
- Causal dependencies.
- Model and configuration identities.

W05's proposed migration state machine is a suitable starting point, but the exact recovery behavior at every migration state must be coordinated with the replay contract.

### D.7 W06 — Gameplay, mutation and alternate timelines

W06 is the owner of committed gameplay mutations and historical world branches.

Its distinction between commands and immutable events must be preserved.

A command is not a historical fact. A committed event is.

For alternate timelines, the branch must identify:

- Parent timeline.
- Parent revision.
- Fork simulation time.
- Fork checkpoint.
- Branch-specific event sequence.
- Branch configuration.
- Any altered external inputs.

A branch must never overwrite the canonical historical sequence.

A branch that uses a different physics or environmental model is a new execution profile, not an exact continuation of the original history.

### D.8 W07 — Unity client

W07 must not become a second authoritative simulation.

Unity prediction is an optimization for responsiveness.

The server must remain authoritative even when client and server calculations differ.

The client needs:

- Server tick.
- Input sequence acknowledgement.
- Authoritative entity revision.
- Relevant model/profile identity.
- Snapshot state.

Unity may replay unacknowledged inputs against its local prediction model.

It does not need to reproduce the entire server's numerical trajectory.

### D.9 W08 — Scientific and environmental simulation

W08's reproducibility contract is already directionally correct.

The scientific runtime must distinguish:

- Reproducible initial conditions.
- Reproducible forcing.
- Numerical reproducibility.
- Conservation and physical validity.
- Bitwise identity.

A scientific simulation that differs slightly numerically across hardware can still be valid if it satisfies the declared error bounds and conservation requirements.

### D.10 W09 — Distributed execution

W09 introduces multilevel fidelity and parallel execution.

This means a replay cannot be defined solely by a world tick.

It must also record the model used during each interval.

For example:

| Interval      | Fidelity | Required historical information           |
| ------------- | -------- | ----------------------------------------- |
| Tick 100–500  | L0       | Full physical state and solver checkpoint |
| Tick 501–900  | L1       | Reduced model and transition metadata     |
| Tick 901–1200 | L2       | Statistical state and transition metadata |

Returning to L0 must reconstruct a valid state from the information actually preserved.

If information was lost during L2, the replay contract must identify that loss rather than pretend to recreate the original detailed state.

## E. Alternatives

Three technically credible approaches were evaluated.

### E.1 Alternative A — Global deterministic lockstep

Every simulation node executes identical inputs in an identical order.

Not recommended

Advantages:

- Simple conceptual replay.
- Strong synchronization guarantees.
- Useful for deterministic RTS simulations.
- Straightforward desynchronization detection.

Disadvantages:

- Requires global coordination.
- Parallel execution becomes difficult.
- Adaptive solvers become problematic.
- Different hardware becomes problematic.
- Distributed regions must coordinate their progress.
- A slow subsystem can delay unrelated simulation.
- Poor fit for continuous physical simulation and scientific models.

This conflicts with W05 and W09's distributed ownership architecture.

It is suitable for selected isolated domains, not the entire universe.

### E.2 Alternative B — Server-authoritative state with snapshot correction

Each server owns its simulation and periodically publishes snapshots.

Insufficient alone

Advantages:

- Scales well.
- Supports heterogeneous simulation backends.
- Does not require clients to execute identical physics.
- Straightforward replication.

Disadvantages:

- Historical reconstruction is incomplete without recorded inputs.
- Difficult debugging of historical incidents.
- Weak alternate timeline support.
- Migration recovery may depend on incomplete snapshots.
- No general reproducibility guarantee.

This is compatible with the basic W05/W07 networking model but does not satisfy C-07.

### E.3 Alternative C — Hierarchical Determinism Contract

Recommended

A combination of:

- Authoritative event ordering.
- Region-local execution authority.
- Explicit causal dependencies.
- Versioned numerical models.
- Complete replay inputs.
- Domain-specific checkpoints.
- Numerical equivalence where bitwise identity is unnecessary.
- Selective bitwise determinism.

Advantages:

- Preserves distributed execution.
- Supports specialized solvers.
- Supports scientific simulation.
- Supports branching timelines.
- Supports server migration.
- Supports client prediction.
- Allows future simulation domains.

Disadvantages:

- More metadata and version management.
- Requires explicit replay compatibility rules.
- Requires domain-specific validation.
- More complicated debugging infrastructure.
- Cannot promise identical output for every historical execution across arbitrary software versions.

### E.4 Comparison

| Criterion             | A: Lockstep                                 | B: Snapshots                      | C: HDC                        |
| --------------------- | ------------------------------------------- | --------------------------------- | ----------------------------- |
| Correctness           | High within constrained deterministic model | Authoritative but limited history | High with declared guarantees |
| Scalability           | Limited                                     | High                              | High                          |
| Distributed execution | Difficult                                   | Good                              | Good                          |
| Physics               | Restrictive                                 | Weak replay                       | Domain-specific               |
| Performance           | Synchronization overhead                    | Low overhead                      | Moderate metadata overhead    |
| Complexity            | High                                        | Low                               | High                          |
| Extensibility         | Limited                                     | Moderate                          | High                          |
| Alternate timelines   | Good if deterministic                       | Weak                              | Strong                        |
| Migration             | Requires coordinated lockstep               | Snapshot-dependent                | Checkpoint + journal          |
| Scientific models     | Poor general fit                            | Weak                              | Strong                        |
| Unity integration     | Demands matching execution                  | Straightforward                   | Straightforward               |

Decision: Alternative C is the only option that satisfies the full set of architectural principles without introducing a global simulation bottleneck.

## F. Recommended architecture

### F.1 Architecture overview

Global logical world

World identity · simulation time · timeline identity · canonical entity identity

Region A

Rust owner A

D1 authority

Journal · checkpoint · solver state

Region B

Rust owner B

D1 authority

Journal · checkpoint · solver state

Replay and determinism infrastructure

Input journal

Commands · events · causal dependencies

Checkpoint manager

State · solver · RNG · revisions

Model registry

Versions · numerical profiles

Validation

Hashes · tolerances · invariants

Replay consumers

Recovery · debugging · alternate timelines · scientific analysis · migration validation

The replay infrastructure is a set of contracts and capabilities, not a new global simulation process.

### F.2 Determinism policy per subsystem

This is the recommended normative policy.

| Subsystem                | Guarantee               | Requirement                                   |
| ------------------------ | ----------------------- | --------------------------------------------- |
| World identity           | D3                      | Stable canonical identifiers                  |
| Event journal            | D3                      | Immutable, ordered committed records          |
| Region mutation ordering | D3                      | Stable per-region sequence                    |
| Cross-region causality   | D1                      | Explicit dependencies and conflict resolution |
| Economy transactions     | D3 where practical      | Exact replay of committed transactions        |
| Construction             | D3 for mutation results | Versioned operations and geometry             |
| Procedural terrain       | D3                      | Stable generator inputs and canonical output  |
| Rigid-body physics       | D2                      | Replay within declared tolerances             |
| Character locomotion     | D2                      | Server authority and bounded divergence       |
| Vehicle physics          | D2                      | Solver-profile-specific replay                |
| Aircraft and spacecraft  | D2                      | Numerical model and time provenance           |
| Celestial mechanics      | D2                      | Numerical reproducibility                     |
| Atmospheric simulation   | D2                      | Scientific validation and tolerance           |
| Ocean simulation         | D2                      | Scientific validation and conservation        |
| NPC AI                   | D2 or D3                | Explicit random and decision state            |
| Unity presentation       | Best effort             | Visual interpolation only                     |
| Client prediction        | D1 relative to server   | Authoritative reconciliation                  |
| Telemetry                | Best effort             | No influence on authoritative state           |

D3 does not mean that every operation must use integer arithmetic. It means the subsystem must guarantee identical canonical output under its declared execution profile.

For example, a procedural generator could use floating-point calculations but would have to constrain its supported platform and runtime profile if bitwise output is required.

### F.3 Event ordering contract

The architecture must distinguish four ordering concepts.

1. Region sequence: Monotonically increasing sequence for committed authoritative mutations within a region.
2. Entity revision: Monotonically increasing version of an entity's authoritative state.
3. Simulation tick: Logical time at which an operation is applied.
4. Causal dependency: Reference to another operation that must precede it.

Recommended event envelope:

| Field              | Meaning                        |
| ------------------ | ------------------------------ |
| `world_id`         | Persistent world identity      |
| `timeline_id`      | Historical branch identity     |
| `event_id`         | Globally unique event identity |
| `region_id`        | Authoritative region           |
| `owner_epoch`      | Current ownership generation   |
| `region_sequence`  | Region-local committed order   |
| `simulation_tick`  | Logical application time       |
| `entity_id`        | Affected entity                |
| `entity_revision`  | Resulting entity revision      |
| `causation_id`     | Direct cause                   |
| `correlation_id`   | Related operation chain        |
| `dependency_ids`   | Required causal predecessors   |
| `schema_version`   | Event representation version   |
| `model_profile_id` | Relevant execution profile     |
| `payload`          | Immutable event content        |

These fields extend the W06 §10.3 event envelope and W05's owner-epoch model.

Ordering rules:

- A committed event is immutable.
- An event cannot be applied twice.
- Events within one region follow `region_sequence`.
- A command is applied only by the current authoritative owner.
- Cross-region dependencies must be satisfied before dependent commits.
- Independent events do not require artificial global ordering.
- Wall-clock timestamps cannot override authoritative simulation order.
- Rejected commands do not create successful mutation events.

For conflicting operations against shared state, the owning authority must serialize acceptance.

For example, two factories attempting to consume the final available resource cannot independently commit successful transactions.

The resource owner must decide the accepted order.

### F.4 Distributed event ordering

The recommended model is a causal partial order.

Let \\(A \rightarrow B\\) mean that event B causally depends on event A.

Then:

\\[ A \rightarrow B \implies A \text{ precedes } B \\]

Independent events may be concurrent:

\\[ A \not\rightarrow B \quad\land\quad B \not\rightarrow A \\]

A deterministic ordering is required only when operations conflict or when their causal relationship demands it.

If a diagnostic total ordering is needed, events may be sorted by a canonical tuple:

\\[ (region\\\_id,region\\\_sequence,event\\\_id) \\]

This is a deterministic presentation order, not a claim that all events occurred in one physically synchronized global sequence.

### F.5 Random number generation

A global shared random generator is prohibited as the default architecture.

It would make unrelated systems dependent on execution order.

Instead, random state must be scoped.

Recommended logical identity:

\\[ RNG = (world,timeline,domain,entity,stream) \\]

Each stream must have:

- Algorithm identifier.
- Algorithm version.
- Initial seed or derivation key.
- Current state or counter.
- Stream identity.
- Consumption position where necessary.

Random streams should be independent between unrelated entities and subsystems.

Examples:

- NPC decision stream.
- Weather stochastic process stream.
- Procedural vegetation stream.
- Combat variation stream.
- Economic demand model stream.

For reproducible replay, record the initial stream state and ensure the same stream algorithm is available.

For counter-based generators, record the key and counter position.

For stateful generators, preserve their complete state.

The seed alone is insufficient after a generator has advanced.

### F.6 External inputs and nondeterministic systems

All inputs capable of changing authoritative state must enter through a recorded boundary.

| Input                    | Replay requirement                                 |
| ------------------------ | -------------------------------------------------- |
| Player command           | Exact accepted command and application tick        |
| Administrative command   | Identity, payload and commit result                |
| External API             | Captured response or immutable input dataset       |
| Weather observations     | Dataset and revision                               |
| Scientific forcing       | Exact forcing data and model version               |
| Real-world clock         | Recorded simulation-time conversion                |
| Randomness               | RNG stream state                                   |
| AI model output          | Captured decision or deterministic model execution |
| Physics backend          | Version and numerical configuration                |
| External hardware sensor | Recorded observation and timestamp                 |
| Network arrival          | Accepted input order, not raw arrival timing alone |

A replay must not contact a live external API and silently accept a different response.

This is especially important for scientific datasets and external AI systems.

### F.7 Replay data contract

Replay requires two different types of snapshots.

1\. Runtime checkpoint

Optimized for rapid restoration of an identical compatible runtime.

Contains:

- Canonical world/entity state.
- Physics solver internals.
- Collision state.
- Domain-specific state.
- RNG state.
- Pending commands.
- Scheduled operations.
- Reference frames.
- Environment cache dependencies.
- Model execution state.

2\. Durable checkpoint

Optimized for long-term storage and schema evolution.

Contains:

- Canonical persistent state.
- Entity identities.
- State revisions.
- Timeline identity.
- Region sequence.
- Simulation time.
- Model and configuration provenance.
- External data references.
- Replay journal position.

The durable checkpoint must not depend on undocumented internal memory layouts of a particular physics library.

This follows directly from W02 §10.3, lines 963–967.

### F.8 Replay algorithm contract

A replay operation must:

1. Identify the world and timeline.
2. Identify the requested target tick or revision.
3. Locate the latest compatible checkpoint preceding the target.
4. Validate model and schema compatibility.
5. Restore the checkpoint.
6. Load the required immutable event and input history.
7. Restore RNG streams.
8. Restore scheduled operations and causal dependencies.
9. Replay in authoritative order.
10. Calculate canonical state hashes.
11. Validate numerical tolerances and invariants.
12. Report the result and any divergence.

A replay must never silently switch to a newer solver version.

If the original model is unavailable, the result must be classified as one of:

- Exact replay unavailable.
- Compatible replay using an explicitly approved migration.
- Numerical approximation using a different model.
- Historical state reconstruction from durable data.

These are different outcomes and must not be presented as equivalent.

### F.9 Alternate timelines

Each timeline must have an independent identity and journal.

Recommended conceptual model:

\\[ Timeline_B = Fork(Timeline_A,Checkpoint,Tick) \\]

The branch inherits the parent history up to the fork point.

After that point, it has independent events, RNG state and simulation execution.

A branch can intentionally alter:

- Player actions.
- Environmental conditions.
- Economic parameters.
- Physical parameters.
- Model versions.
- External inputs.

However, every altered parameter must be identifiable.

A branch with different model versions is scientifically comparable only when the difference in model configuration is accounted for.

Merging alternate timelines into the canonical world must be an explicit conflict-resolution operation, not an automatic replacement of historical state.

### F.10 Ownership migration

Migration must preserve replay continuity.

Recommended sequence:

1. Source owner stops accepting new authoritative mutations.
2. Source owner reaches a defined checkpoint boundary.
3. Pending accepted operations are committed or explicitly transferred.
4. Runtime checkpoint is created.
5. Checkpoint hash and journal position are recorded.
6. Ownership epoch is advanced.
7. Target owner restores the checkpoint.
8. Target validates its state against the checkpoint hash.
9. Target resumes execution.
10. Old owner is fenced from further mutations.

The target must not begin from a checkpoint while silently losing committed but unpersisted events.

Cross-region interactions must preserve their causal dependencies during migration.

The exact atomic commit protocol remains a W05 ownership decision.

### F.11 Client prediction

Unity prediction must use the following relationship:

\\[ S\_{client}^{predicted} \neq S\_{server}^{authoritative} \\]

is permitted temporarily.

But after receiving an authoritative update, the client must:

1. Replace its predicted physical state with the authoritative state.
2. Remove acknowledged inputs.
3. Replay remaining inputs where supported.
4. Apply visual smoothing separately.

The predicted state must not be treated as an authoritative historical fact.

This is consistent with W07 §5.6.

### F.12 Multiple simulation frequencies

A global integer simulation-time representation must coexist with domain-specific scheduling.

Recommended:

- Interactive rigid-body physics: fixed authoritative intervals.
- Character and vehicle dynamics: fixed intervals with optional substeps.
- Orbital mechanics: solver-specific intervals, potentially adaptive.
- Global environmental models: independent scientific cadence.
- Regional weather: configurable cadence.
- Economic production: event-driven scheduling.
- Dormant regions: state-based time advancement.

Every result must specify its effective simulation-time interval.

A lower-frequency system must not fabricate historical precision that it did not compute.

## G. Cross-system impact

### G.1 Documents that remain compatible

These architectural directions can be retained:

| Document | Compatible proposals                                                                            |
| -------- | ----------------------------------------------------------------------------------------------- |
| W01      | Hierarchical coordinate systems, canonical terrain sampling and versioned procedural generation |
| W02      | Fixed interactive timestep, integer tick, solver snapshots and rollback                         |
| W03      | Specialized physical solvers sharing universal physical state                                   |
| W04      | Separate simulation, wall-clock and presentation time                                           |
| W05      | Single authoritative owner per region, ownership epochs and hybrid persistence                  |
| W06      | Immutable mutation events, hybrid persistence and separate alternate timelines                  |
| W07      | Server authority, client prediction and reconciliation                                          |
| W08      | Scientific reproducibility with recorded numerical parameters                                   |
| W09      | Distributed simulation, multirate execution and multiple fidelity levels                        |

### G.2 Documents requiring clarification

W01

Add a formal generator reproducibility profile.

Clarify whether generator output is required to be bitwise identical or geometrically equivalent within a declared tolerance.

W02

Define:

- Deterministic execution profiles.
- Stable iteration and reduction order.
- State hash serialization.
- Runtime checkpoint compatibility.
- Solver-version compatibility.
- Rollback behavior after backend upgrades.

W03

Define portable state for each specialized physical domain.

Clarify which solver internals must be persisted and which can be reconstructed.

W04

Define astronomical replay provenance, especially time conversion data, ephemeris revisions and adaptive integration state.

W05

Define the relationship between region sequence, ownership epoch, checkpoint hash and event journal position.

Specify migration recovery after crashes at every transition.

W06

Define timeline identity, branch-local sequence, fork checkpoint and branch replay compatibility.

W07

Define how the client identifies the authoritative tick and input acknowledgement associated with every snapshot.

W08

Define numerical equivalence profiles and scientific validation tolerances.

W09

Define how replay handles fidelity changes and how state loss is reported.

### G.3 Documents requiring architectural changes

No worker requires abandoning its main architecture.

However, the following changes are necessary to establish a complete global contract:

| Document | Required architectural change                      |
| -------- | -------------------------------------------------- |
| W02      | Add a formal determinism and replay interface      |
| W05      | Integrate causal journal continuity into migration |
| W06      | Extend event envelopes and branch metadata         |
| W08      | Formalize reproducibility profiles and provenance  |
| W09      | Add replay-aware fidelity transition contracts     |

W01, W03, W04 and W07 need interface clarification rather than a fundamental redesign.

### G.4 Central integration dependencies

The central architect must coordinate four decisions before these contracts can be finalized:

1. W02 must own simulation execution semantics.
2. W05 must own distributed mutation authority and migration.
3. W06 must own persistent mutation history and alternate timeline semantics.
4. W09 must own distributed scheduling and fidelity transitions.

W07 consumes these contracts; it must not independently redefine them.

W01, W03, W04 and W08 provide domain-specific requirements and validation.

## H. Required architectural decisions

The following ADRs are recommendations for central integration, not decisions already accepted by the repository.

ADR-DET-001

Recommend accept

Hierarchical determinism policy

Adopt D1, D2 and D3 guarantees. Prohibit a global bitwise determinism assumption.

ADR-DET-002

Recommend accept

Canonical event ordering

Region-local monotonic sequences and explicit cross-region causal dependencies. No global total ordering requirement for independent events.

ADR-DET-003

Recommend accept

Fixed authoritative interactive timestep

Fixed timestep for interactive physics, while allowing domain-specific substeps and adaptive scientific integration.

ADR-DET-004

Recommend accept

Domain-scoped RNG

Independent versioned random streams. No shared global RNG whose consumption order depends on parallel scheduling.

ADR-DET-005

Recommend accept

Two-tier checkpoint architecture

Runtime checkpoints for compatible restoration and durable checkpoints for long-term persistence.

ADR-DET-006

Recommend accept

Replay provenance

Every replayable execution must identify input history, model versions, numerical configuration, RNG state and external data revisions.

ADR-DET-007

Recommend accept

Ownership migration continuity

Migration must preserve checkpoint identity, journal position, pending operations, RNG state and causal dependencies.

ADR-DET-008

Recommend accept

Independent timeline histories

Alternate timelines inherit a defined historical prefix and subsequently maintain independent state and event history.

ADR-DET-009

Recommend accept

Numerical equivalence profiles

Every D2 subsystem must declare its numerical comparison method and permitted tolerances.

ADR-DET-010

Recommend accept

Authoritative client reconciliation

Client prediction is non-authoritative. Authoritative server state replaces predicted physical state.

ADR-DET-011

Recommend accept

Fidelity transition provenance

Every transition between L0–L3 records model identity, canonical state conversion and information loss.

ADR-DET-012

Coordinate

Replay compatibility policy

Define which historical runtime versions remain executable, which require migration and how long replay compatibility is guaranteed. This needs agreement from W02, W05, W06 and W09.

### H.1 Decision priority

I recommend accepting ADR-DET-001 through ADR-DET-011 as the architectural direction.

ADR-DET-012 requires a separate retention and compatibility policy.

It would be premature to mandate indefinite execution compatibility with every historical Rust solver or physics backend.

## I. Validation plan

No performance results are claimed here. All following items are proposed validation experiments.

### I.1 Event ordering tests

| Test                 | Procedure                                          | Acceptance criterion                   |
| -------------------- | -------------------------------------------------- | -------------------------------------- |
| Duplicate submission | Submit the same command repeatedly                 | One committed mutation                 |
| Concurrent conflict  | Two owners attempt conflicting mutations           | One valid authoritative resolution     |
| Event ordering       | Replay identical accepted commands                 | Identical region sequence              |
| Causal dependency    | Delay a dependent event                            | It cannot commit before its dependency |
| Independent events   | Execute independent regions in different schedules | No invalid causal divergence           |
| Stale ownership      | Submit command from old epoch                      | Rejected                               |

### I.2 Physics determinism tests

Test identical initial conditions and input sequences using:

- Single-thread execution.
- Multithread execution.
- Different thread counts.
- Different execution schedules.
- Different supported CPU architectures.
- Different supported compiler configurations.

Measure:

1. Bitwise state equality.
2. Numerical state error.
3. Energy drift.
4. Momentum conservation where applicable.
5. Collision outcome consistency.
6. Constraint stability.
7. Divergence over time.

D3 subsystems must produce identical canonical output under their declared supported profile.

D2 subsystems must remain within declared numerical tolerances and satisfy physical invariants.

### I.3 Cross-process migration tests

Test:

- Migration at a normal checkpoint.
- Migration with pending commands.
- Migration during active collisions.
- Migration with active constraints.
- Migration with scheduled operations.
- Migration during a cross-region interaction.
- Source process crash before checkpoint.
- Source process crash after checkpoint.
- Target process crash during restoration.
- Network interruption during ownership transfer.

Acceptance:

- No duplicated committed mutations.
- No lost committed mutations.
- No split-brain authority.
- No duplicate entity ownership.
- No unrecorded RNG reset.
- Valid replay from the last durable checkpoint.

### I.4 Alternate timeline tests

Test:

1. Create a timeline from a historical checkpoint.
2. Replay the parent timeline.
3. Fork a child timeline.
4. Change one external input.
5. Replay the child.
6. Verify divergence occurs only after the fork.
7. Verify the parent remains unchanged.
8. Create a second child with different RNG state.
9. Verify independent event sequences.
10. Test an explicit merge conflict.

Acceptance: Identical inputs and identical model profiles must reproduce the same branch results within the branch's declared determinism level.

### I.5 RNG tests

Test:

- Identical seeds and stream identities.
- Different execution orders.
- Different thread counts.
- Process migration.
- Checkpoint restoration.
- Timeline branching.
- Entity creation and deletion.
- Generator version changes.

Acceptance:

Unrelated entities must not change their random sequences merely because another entity was created or processed earlier.

### I.6 Procedural generation tests

Use W01's tile function.

Generate identical tiles:

- On different processes.
- On repeated runs.
- After process restart.
- After migration.
- With identical source data revisions.
- With different generator versions.

Acceptance:

Same D3 generation profile must produce identical canonical geometry hashes.

A different generator version must produce a distinguishable generation identity.

### I.7 Scientific reproducibility tests

W08's existing scientific validation suite should be extended with:

- Different thread counts.
- Different supported processors.
- Different numerical precision.
- Different solver configurations.
- Replay of historical forcing data.
- Model version changes.
- Long-duration simulation.

Measure:

- State differences.
- Conservation errors.
- Numerical stability.
- Error growth.
- Sensitivity to initial conditions.

Acceptance criteria must be defined separately for each scientific model.

### I.8 Client prediction tests

Test:

- Normal latency.
- High latency.
- Jitter.
- Packet loss.
- Out-of-order snapshots.
- Multiple unacknowledged inputs.
- Server corrections.
- Client reconnection.

Acceptance:

The client converges to authoritative state after correction. Visual smoothing must never become the input state for authoritative physical calculations.

### I.9 Performance validation

Determinism has a measurable cost.

Benchmark at least:

| Measurement                      | Purpose                   |
| -------------------------------- | ------------------------- |
| Tick duration                    | Execution overhead        |
| State hashing                    | Validation cost           |
| Checkpoint creation              | Snapshot cost             |
| Checkpoint restoration           | Recovery speed            |
| Journal size                     | Persistence cost          |
| Replay speed                     | Historical reconstruction |
| Deterministic parallel reduction | Parallelization overhead  |
| RNG throughput                   | Random stream overhead    |
| Migration duration               | Distributed recovery      |
| Memory overhead                  | Replay metadata cost      |

Compare deterministic and non-deterministic execution profiles.

The goal is not to maximize determinism at any price.

It is to quantify the cost of each guarantee and apply it where justified.

## J. Unresolved questions

The following questions cannot responsibly be settled by GPT-07 alone.

### J.1 Numerical backend compatibility

Owner: W02

- Which physics backend will be selected?
- Will the backend provide any supported cross-platform determinism guarantee?
- Which solver internals are portable?
- What is the policy for backend upgrades?
- Is deterministic parallel physics a required future capability?

### J.2 Astronomical replay precision

Owner: W04

- What numerical tolerances are appropriate for each celestial model?
- Which ephemeris datasets must remain available?
- How are historical Earth-orientation parameters retained?
- How is adaptive integration state restored?

### J.3 Physical state completeness

Owner: W03

- What state is required to resume every entity solver?
- Which physical state can be reconstructed?
- Which domain transitions are inherently lossy?
- What is the canonical representation across fidelity levels?

### J.4 Ownership and persistence atomicity

Owner: W05

- Is the region journal synchronously durable before acknowledging every committed mutation?
- What happens when a cross-region operation is interrupted?
- How is the migration checkpoint associated atomically with the journal?
- How are pending messages reconciled after a crash?

### J.5 Timeline semantics

Owner: W06

- Are alternate timelines persistent indefinitely?
- Can branches use different numerical models?
- Can two branches be merged?
- What is the authoritative policy for merging conflicting physical changes?
- Does a historical branch preserve all external datasets or only references to them?

### J.6 Scientific numerical equivalence

Owner: W08

- Which tolerance metrics are suitable for climate, atmosphere and ocean models?
- Which physical invariants are mandatory?
- How should chaotic systems be compared?
- When is statistical equivalence acceptable instead of trajectory equivalence?

### J.7 Distributed ordering

Owner: W05 + W09

- How are cross-region transactions committed?
- What is the maximum allowed causal delay?
- Which interactions require synchronous coordination?
- Can a region advance independently while waiting for another region's causal dependency?
- How are delayed environmental inputs applied?

### J.8 Historical runtime retention

Owner: Central integrator

This is a particularly important question.

If the physics engine changes five years after the original world was created, can the original simulation still be replayed?

Three possible policies exist:

1. Retain every historical executable runtime.
2. Migrate historical checkpoints to newer versions.
3. Preserve historical snapshots and inputs, but permit only approximate reconstruction when the original runtime is unavailable.

I recommend a hybrid policy: retain selected historical execution profiles, provide explicit durable-state migrations, and never mislabel a migrated or approximate replay as exact.

## K. Final assessment

Resolved conditionally

C-07 is architecturally resolvable without requiring global deterministic lockstep or abandoning distributed execution.

The recommended architecture is compatible with the central direction of all nine worker documents.

The key architectural result is that Planetary Engine should treat determinism as a declared capability of each simulation domain, built on top of common authoritative ordering, provenance and replay infrastructure.

The system must guarantee:

1. One authoritative accepted mutation history.
2. Explicit causal ordering across distributed regions.
3. Replayable authoritative state within a declared compatibility profile.
4. Complete recording of external inputs and random state.
5. Reproducible procedural generation for persisted generated content.
6. Explicit numerical tolerances for scientific and continuous physical models.
7. Authoritative correction of client predictions.
8. Preservation of history across ownership migration.
9. Independent and reproducible alternate timelines.

It must not promise:

- Universal bitwise determinism.
- Identical floating-point results across arbitrary hardware.
- Exact reconstruction after an unrecorded external input.
- Lossless replay across incompatible solver versions.
- A globally synchronized event sequence for unrelated regions.

Conditions for final acceptance:

- W02 accepts the execution and checkpoint contracts.
- W05 accepts the migration and causal journal contracts.
- W06 accepts the timeline and historical event contracts.
- W08 accepts numerical reproducibility profiles.
- W09 accepts replay-aware multirate and fidelity transitions.

Until those interfaces are coordinated, the resolution is conditional rather than final.

### Doporučení pro centrálního integrátora

Nejdůležitější je přijmout HDC jako společnou architektonickou vrstvu, nikoliv jako samostatný deterministický engine.

Její rozhraní musí být součástí Rust simulation runtime, persistence, region ownership a timeline systému. Nemá vzniknout další centrální proces, který by synchronizoval celý vesmír.

Výsledek GPT-07: C-07 vyřešeno podmíněně.

Primární podklady: W01–W09, větev main, repository projectzonio/what_if. Architektonická doporučení jsou oddělena od existujících návrhů. Implementace nebyla provedena.

Hlavní výstup pro integraci: ADR-DET-001 až ADR-DET-012.

Doporučuji předat tento dokument integrátorovi jako samostatný návrh GPT-07 a zachovat jeho otevřené závislosti.