Máš pravdu. Ověřil jsem repozitář přímo: `projectzonio/what_if` je veřejný a všech devět souborů `w1.md` až `w9.md` je dostupných. Předchozí závěr o nutnosti nahrávat ZIP byl chybný. Omlouvám se.

Otevřel jsem všech devět dokumentů. Budu vycházet z jejich skutečného obsahu, nikoliv pouze z konfliktů popsaných v zadání. Zejména budu rozlišovat mezi návrhem jednotlivého workera, skutečně dohodnutým kontraktem a vlastní architektonickou změnou.

# PLANETARY ENGINE

## GPT-01 — World Time and Timeline Architecture Resolution

Architecture research · Cross-worker consolidation · 16 November 2026



Resolved conditionally

Implementation prohibited



Repository: [projectzonio/what_if](https://github.com/projectzonio/what_if?utm_source=chatgpt.com)\
Scope: C-01 — Conflicting world time models\
Role: Principal Systems Architect — Independent Resolution Worker

# A. Executive decision

## A.1 Recommended architecture

Planetary Engine should use one canonical logical world timeline with distributed regional execution clocks and explicit causal synchronization.

These are three different architectural concepts:

1. Canonical World Timeline — defines the temporal position of every persistent world state and event within a particular timeline.
2. Regional Execution Time — defines how far an individual simulation region has actually computed and committed its state.
3. Wall-clock Execution Time — measures real elapsed time and controls scheduling, networking, leases, deadlines and operational timeouts.

The canonical timeline is globally consistent as a coordinate system, but its execution is not globally synchronized.

This distinction resolves the central contradiction between W04's universal time architecture and W09's regional execution model.

The recommended architecture has the following properties:

- One persistent logical world.
- One canonical simulation-time coordinate per timeline.
- No mandatory global barrier at every simulation step.
- Independent regional simulation schedules.
- Explicit causal dependencies between regions.
- Region-local committed-time watermarks.
- Separate numerical integration intervals and execution frequencies.
- Explicit mappings to astronomical time standards.
- Timeline identity independent of region ownership and process identity.
- Branching through immutable checkpoints and versioned event histories.

Neither 30 Hz nor 60 Hz becomes a universal world clock.

These values remain candidate execution profiles for particular simulation domains.

## A.2 Most important architectural distinction

The following statements are simultaneously true:

- Two regions can have different execution progress.
- Both regions belong to the same logical world timeline.
- Both regions can represent states at the same logical world time.
- They do not need to finish computing those states simultaneously.
- Their causally dependent interactions must nevertheless obey defined temporal consistency rules.

This is the foundation of the proposed solution.

# B. Existing architecture

The repository already contains most of the conceptual elements required for this architecture. The problem is that their relationships have not been formalized.

## B.1 Worker-by-worker findings

| Worker | Existing proposal                                                                    | Architectural status |
| ------ | ------------------------------------------------------------------------------------ | -------------------- |
| W01    | Hierarchical reference frames, integer simulation time, astronomical time conversion | Proposed             |
| W02    | Fixed authoritative physics tick, initially 60 Hz                                    | Proposed             |
| W03    | Common simulation clock with different model frequencies                             | Proposed             |
| W04    | Universal simulation timeline, astronomical scales, wall-clock separation            | Proposed             |
| W05    | Region ownership, epochs, local ticks and migration                                  | Proposed             |
| W06    | World revisions, branches, event-driven mutations and regional partitioning          | Proposed             |
| W07    | Server-authoritative timeline, client prediction and interpolation                   | Proposed             |
| W08    | Environmental models at different temporal resolutions                               | Proposed             |
| W09    | 30 Hz initial profile, multirate execution and region-local clocks                   | Proposed             |

None of these proposals should automatically be interpreted as an accepted global architecture decision.

## B.2 W01 — Spatial and astronomical foundation

W01 §2.8, lines 326–334, establishes an important foundation:

- Simulation time is distinct from astronomical time.
- TAI, UTC, UT1, TT and TDB have different purposes.
- UTC should not be used as the continuous internal physics clock.
- Time-dependent coordinate transformations require an explicit evaluation epoch.

W01 §2.7, lines 308–325, also establishes that global coordinates, local simulation coordinates and rendering coordinates must remain separate.

Finding: W01 is compatible with a canonical simulation timeline. Its integer-time proposal needs a more precise representation contract.

Source: [W01](https://github.com/projectzonio/what_if/blob/main/w1.md?utm_source=chatgpt.com).

## B.3 W02 — Physics runtime

W02 §3.1, lines 258–276, recommends:

- Fixed timestep.
- Independent rendering frequency.
- Integer tick counter.
- Explicit rational timestep.
- Initial 60 Hz authoritative physics profile.

W02 explicitly states that the proposed values are starting points, not measured performance results.

Finding: W02 describes a physics execution cadence, not a requirement for global world synchronization.

The 60 Hz recommendation should remain a candidate physics profile.

Source: [W02](https://github.com/projectzonio/what_if/blob/main/w2.md?utm_source=chatgpt.com).

## B.4 W03 — Physical entities and model time

W03 §6.3, lines 1523–1559, explicitly distinguishes simulation time from model time.

Its proposed frequencies include:

- Rigid-body simulation: 60–120 Hz.
- High-speed vehicle substeps: 120–240 Hz.
- Drone flight-control loop: 250–1000 Hz.
- Orbital propagation: adaptive.
- Offline scientific simulation: adaptive.

W03 §6.4, lines 1564–1586, also distinguishes different levels of determinism.

Finding: W03 already rejects the idea that every physical subsystem must execute at one universal frequency.

Its common simulation clock should be interpreted as a shared temporal coordinate, not a shared execution loop.

Source: [W03](https://github.com/projectzonio/what_if/blob/main/w3.md?utm_source=chatgpt.com).

## B.5 W04 — Celestial and universe simulation

W04 §3.7, lines 519–577, provides the strongest foundation for the time model.

It proposes:

- Universal simulation time.
- Separate wall-clock time.
- Separate presentation time.
- Explicit astronomical time scales.
- Integer epoch and fractional time representation.
- Adaptive integration.
- Explicit handling of time discontinuities.

W04 also introduces a dedicated Time Service in §4.1, lines 626–663.

Finding: W04 correctly establishes the shared temporal foundation but does not fully define distributed regional execution or causal consistency.

Source: [W04](https://github.com/projectzonio/what_if/blob/main/w4.md?utm_source=chatgpt.com).

## B.6 W05 — Distributed ownership and migration

W05 §2.6, lines 376–402, proposes:

- Region-local ownership.
- Monotonically increasing ownership epochs.
- Leases.
- State serialization.
- Migration checkpoints.
- Ownership commit.

W05 §9.2, lines 745–756, includes a region ownership contract with `owner_epoch`, `lease_expiration`, `state_version` and `simulation_tick`.

W05 §9.3 also includes a client-provided timestamp in `PlayerCommand`.

Finding: Ownership epochs solve authority fencing. They do not establish temporal continuity by themselves.

A transferred region needs a canonical simulation-time checkpoint and a committed event position, not merely a tick counter.

Source: [W05](https://github.com/projectzonio/what_if/blob/main/w5.md?utm_source=chatgpt.com).

## B.7 W06 — Gameplay and branching

W06 §7.6, lines 1097–1118, requires regional migration to preserve simulation time and active tasks.

W06 §8.1, lines 1149–1167, proposes branches based on:

- Parent world.
- Parent revision.
- Parent simulation time.
- Configuration and subsystem versions.
- Branch-specific mutations and inputs.
- Copy-on-write state.

W06 §16, lines 1694–1710, explicitly identifies universal time and simulation tick as cross-worker contracts.

Finding: W06 requires timeline identity to become a persistent world-level concept.

A branch cannot be identified solely by a timestamp or snapshot ID.

Source: [W06](https://github.com/projectzonio/what_if/blob/main/w6.md?utm_source=chatgpt.com).

## B.8 W07 — Multiplayer and client synchronization

W07 §8, lines 1041–1117, distinguishes:

- Server authoritative timeline.
- Client predicted timeline.
- Client interpolated timeline.
- Wall-clock/network time.
- Simulation time.

It also requires authoritative updates to contain a timeline identifier, simulation tick, simulation time, entity version and event sequence.

Finding: W07 is highly compatible with the proposed architecture.

However, its single server simulation tick must be generalized to a regional execution coordinate and canonical world-time position.

Source: [W07](https://github.com/projectzonio/what_if/blob/main/w7.md?utm_source=chatgpt.com).

## B.9 W08 — Environmental simulation

W08 §8.5, lines 535–566, describes scientific datasets as initial conditions, boundary conditions, historical reconstruction and validation references rather than independent simulation engines.

W08 §9.1, lines 567–574, places environmental authority in Rust.

Finding: Environmental simulation must consume canonical world time while retaining its own update cadence and numerical integration strategy.

Source: [W08](https://github.com/projectzonio/what_if/blob/main/w8.md?utm_source=chatgpt.com).

## B.10 W09 — Runtime and distributed scheduling

W09 §2.2, lines 221–230, proposes 30 Hz as an initial main simulation profile.

W09 §5.4–5.5, lines 470–496, explicitly rejects global synchronization of every physics tick.

It proposes regional timelines and explicit synchronization for critical interactions.

W09 §12, decision PERF-011, identifies regional clocks as a recommended direction.

Finding: W09 correctly identifies the distributed execution problem, but the phrase region-local clocks is insufficiently precise.

Independent clocks must not imply independent definitions of world time.

Source: [W09](https://github.com/projectzonio/what_if/blob/main/w9.md?utm_source=chatgpt.com).

# C. Problem analysis

## C.1 The actual conflict is not 30 Hz versus 60 Hz

The frequency disagreement is relatively minor.

The real problem is that the existing documents use the words:

- World clock.
- Simulation clock.
- Simulation tick.
- Region clock.
- Model time.
- Execution time.

Without a shared definition.

This creates the possibility of several incompatible interpretations.

Incorrect interpretation

Global 60 Hz clock

Region A

# 60 Hz

Must synchronize

Region B

# 60 Hz

Must synchronize

Region C

# 60 Hz

Must synchronize

One slow region can become a world-wide execution bottleneck.

Recommended interpretation

Canonical World Timeline

Shared logical coordinate and event history

Region A

# 60 Hz

Local execution

Region B

# 30 Hz

Local execution

Region C

# Adaptive

Local execution

All regions use the same temporal coordinate. Synchronization is required only where causal dependencies demand it.

## C.2 Four concepts that must not be confused

| Concept              | Meaning                             |
| -------------------- | ----------------------------------- |
| Logical time         | Position in the world's timeline    |
| Execution time       | How far a region has computed       |
| Integration timestep | Numerical interval used by a solver |
| Update frequency     | How often a subsystem executes      |

For example, a vehicle solver may integrate at 240 Hz while its containing region publishes authoritative snapshots at 30 Hz.

Neither frequency defines the world's absolute time.

## C.3 Why independent regional clocks are dangerous

Consider:

- Region A contains a vehicle.
- Region B contains a bridge.
- The vehicle crosses the regional boundary.
- Region B has processed less simulation time than Region A.

If both regions simply use their local ticks, the vehicle may interact with the bridge at an ambiguous temporal position.

Potential consequences:

1. Collision evaluated against a stale bridge state.
2. Vehicle position updated before a causally required event.
3. Duplicate collision processing during ownership transfer.
4. Incorrect replay after process failure.
5. Inconsistent results between online and offline simulation.

The problem cannot be solved by adding a global barrier to every tick.

It requires an explicit causal contract.

## C.4 The relativity problem

There is one additional issue not fully addressed by the current worker documents.

A universal logical timeline must not be confused with a claim that relativity provides a physically privileged universal simultaneity.

In relativistic physics, the ordering of spacelike-separated events is not invariant across all reference frames.

Therefore, Planetary Engine needs to distinguish:

- The engine's canonical simulation coordinate.
- Proper time measured by physical clocks.
- Coordinate time in astronomical reference systems.
- Causal ordering of events.

The canonical world timeline is an architectural simulation parameter. It is not a declaration that every physical observer shares one universal proper time.

This distinction becomes important for W04's astronomical and relativistic models.

# D. Cross-system dependencies

The time architecture is a foundational contract. It cannot be finalized independently of the other workers.

## D.1 Dependency matrix

| Worker | What time architecture requires           | What worker requires from time architecture     | Compatibility                  |
| ------ | ----------------------------------------- | ----------------------------------------------- | ------------------------------ |
| W01    | Reference-frame and epoch definitions     | Precise instant for transformations             | Compatible                     |
| W02    | Physics timestep and tick identity        | Stable time coordinate for integration          | Compatible after clarification |
| W03    | Solver integration contracts              | Shared time across physical models              | Compatible                     |
| W04    | Astronomical time conversion              | Canonical world time and time-scale metadata    | Requires extension             |
| W05    | Region ownership and migration            | Temporal checkpoint and continuity              | Requires extension             |
| W06    | World revision and branch model           | Timeline identity and fork semantics            | Requires extension             |
| W07    | Network snapshot and prediction contracts | Regional timestamp and causal sequence          | Requires extension             |
| W08    | Environment sampling interface            | Consistent time-dependent environmental queries | Compatible after clarification |
| W09    | Scheduler and multirate execution         | Canonical coordinate plus regional progress     | Requires clarification         |

## D.2 W01 — Reference frames

A transformation between two reference frames must be evaluated at a defined instant.

Required contract:

- Canonical simulation instant.
- Astronomical time-scale conversion.
- Reference-frame definition.
- Ephemeris revision.
- Transformation validity interval.

A frame transformation must not depend on whichever region happens to calculate it.

Required change: W01 should explicitly use the canonical world-time service as the input to time-dependent transformations.

## D.3 W02 and W03 — Physics

Physics requires fixed or controlled numerical intervals.

It does not require every region to execute the same number of solver iterations.

The architecture must support:

- Fixed local physics timestep.
- Internal substeps.
- Adaptive integration.
- State sampling at canonical world-time instants.
- Versioned environmental inputs.
- Deterministic event ordering within the declared determinism level.

A solver must receive an explicit integration interval, not infer it from the process's elapsed wall-clock time.

## D.4 W04 — Astronomical simulation

W04's Time Service should become the authoritative owner of:

- Time-scale definitions.
- Time conversion.
- World epoch metadata.
- Astronomical epoch mapping.
- Reference time conventions.
- Time discontinuity operations.

However, it must not become a centralized service that synchronously authorizes every regional simulation step.

It should define the temporal coordinate and provide conversion capabilities. Regional execution should remain distributed.

## D.5 W05 — Ownership and migration

The existing `simulation_tick` field is insufficient as the sole migration coordinate.

A migration must preserve:

1. Timeline identity.
2. Canonical simulation instant.
3. Region committed-time watermark.
4. Last committed mutation sequence.
5. Pending causal dependencies.
6. Active scheduled operations.
7. Ownership epoch.
8. Checkpoint revision.

The receiving owner must continue from the exact committed temporal state.

The ownership epoch and timeline time must remain separate concepts.

## D.6 W06 — Branches and world revisions

W06's copy-on-write approach is appropriate.

A branch must additionally define its own time-control state.

For example, two branches may originate from the same parent checkpoint:

- Branch A continues at normal speed.
- Branch B advances at 100× speed.
- Branch C remains paused.
- Branch D receives a different external intervention.

They share historical ancestry, not future execution state.

A branch must never mutate its parent's timeline.

## D.7 W07 — Multiplayer

The client must receive sufficient information to distinguish:

- Which timeline it observes.
- Which canonical simulation instant a snapshot represents.
- Which region produced the snapshot.
- Which authority epoch was valid.
- Which state revision is represented.
- Which events have already been incorporated.

Client prediction must be tied to an authoritative region and input sequence.

Client wall-clock time must never determine authoritative simulation time.

## D.8 W08 — Environment

Environmental providers must accept an explicit canonical simulation instant.

For example:

- Atmospheric state at a specified world time.
- Ocean state at a specified world time.
- Surface temperature at a specified world time.
- Weather state at a specified world time.

A cached environmental result must identify its temporal validity interval and model/data revision.

A weather model executing hourly cannot pretend to provide independently computed physical states at every 30 Hz physics update.

It may provide an interpolated state with an explicit approximation policy.

## D.9 W09 — Scheduler

W09 should own scheduling policy, not the definition of world time.

The scheduler determines:

- Which subsystem runs.
- When it runs.
- Its computational budget.
- Its requested integration interval.
- Its fidelity level.
- Whether it must wait for a causal dependency.

The time architecture defines the coordinate against which these decisions are made.

This ownership separation is essential.

# E. Alternatives

Three technically credible alternatives were evaluated.

## E.1 Alternative A — Globally synchronized simulation clock

Every region advances in globally coordinated simulation ticks.

For example, every region must commit tick 100 before any region can advance to tick 101.

Not recommended

Advantages

- Simple global ordering.
- Straightforward deterministic replay.
- Easy interpretation of cross-region state.
- Relatively simple debugging.

Disadvantages

- Slowest region can block the entire world.
- Poor scalability across heterogeneous simulation domains.
- Difficult to combine adaptive astronomical integration with high-frequency local physics.
- Network partitions become world-wide simulation problems.
- Offline simulation and accelerated time become unnecessarily expensive.

| Criterion                  | Assessment                             |
| -------------------------- | -------------------------------------- |
| Correctness                | High under strict lockstep assumptions |
| Scalability                | Poor                                   |
| Distributed execution      | Poor                                   |
| Performance                | Vulnerable to global barriers          |
| Complexity                 | Low initially, high operationally      |
| Extensibility              | Limited                                |
| Architecture compatibility | Conflicts with W05/W09                 |

This model is appropriate for tightly coupled deterministic simulations, but not as the universal Planetary Engine execution architecture.

## E.2 Alternative B — Fully independent regional clocks

Every region maintains its own simulation time, with synchronization only when messages arrive.

Not recommended

Advantages

- Excellent local autonomy.
- Minimal global coordination.
- Straightforward regional scheduling.
- Natural support for heterogeneous workloads.

Disadvantages

- No unambiguous common temporal coordinate.
- Difficult reconstruction of global historical state.
- Cross-region interactions require additional reconciliation.
- Timeline branching becomes complicated.
- Astronomical and environmental consistency becomes ambiguous.
- Region migration can accidentally change temporal interpretation.

| Criterion                  | Assessment                             |
| -------------------------- | -------------------------------------- |
| Correctness                | Conditional and difficult to guarantee |
| Scalability                | High                                   |
| Distributed execution      | High                                   |
| Performance                | High local autonomy                    |
| Complexity                 | High                                   |
| Extensibility              | Risky                                  |
| Architecture compatibility | Conflicts with W04/W06                 |

This is the interpretation that must be avoided when adopting W09's regional clocks.

## E.3 Alternative C — Canonical logical timeline with distributed execution

Every region uses the same canonical simulation-time coordinate but advances independently.

Regional execution progress is tracked separately.

Cross-region interactions use explicit causal synchronization.

Recommended

Advantages

- One coherent persistent world.
- No global per-tick synchronization.
- Supports multiple solver frequencies.
- Supports adaptive numerical integration.
- Supports offline simulation and time acceleration.
- Preserves a common historical coordinate.
- Compatible with branching and replay.
- Allows selective synchronization where physical interactions require it.

Disadvantages

- Requires explicit temporal watermarks.
- Requires causal dependency management.
- Cross-region interactions need well-defined synchronization protocols.
- Distributed rollback and failure recovery require careful design.

| Criterion                  | Assessment                          |
| -------------------------- | ----------------------------------- |
| Correctness                | High with explicit causal contracts |
| Scalability                | High                                |
| Distributed execution      | High                                |
| Performance                | No universal tick barrier           |
| Complexity                 | Medium–high                         |
| Extensibility              | High                                |
| Architecture compatibility | Best                                |

## E.4 Decision

Alternative C is recommended.

It preserves the fundamental invariant of one coherent world without imposing a global execution bottleneck.

# F. Recommended architecture

## F.1 Architecture overview

WORLD TIMELINE AUTHORITY

Logical time · Timeline identity · Branch lineage · Time control

Time representation

Canonical instant and epoch

Astronomical service

TAI · TT · TDB · UTC · UT1

DISTRIBUTED REGIONAL EXECUTION

Independent schedulers and committed-time watermarks

Region A

## 60 Hz

Local execution

Region B

## 30 Hz

Local execution

Region C

## Adaptive

Local execution

CAUSALITY AND BOUNDARY COORDINATION

Events · Dependencies · Watermarks · Interaction synchronization

Persistence

Journal · Checkpoints · Replay

Unity clients

Prediction · Interpolation · Presentation

The World Timeline Authority is a logical ownership responsibility, not necessarily one globally centralized runtime process.

Its configuration and durable control records may be managed through Node.js, while Rust simulation nodes consume the resulting contracts.

## F.2 Canonical time representation

I recommend a signed 128-bit integer representation of simulation nanoseconds relative to a defined world epoch.

Conceptually:

\\[ T_w = N\_{\mathrm{ns}} \\]

Where:

- \\(T_w\\) is the canonical world-time coordinate.
- \\(N\_{\mathrm{ns}}\\) is the signed integer number of elapsed nanoseconds.
- Zero identifies the world epoch.

This is an architectural representation recommendation, not an existing repository implementation.

The representation offers:

- No cumulative floating-point drift in the canonical coordinate.
- Deterministic comparison and ordering.
- Sufficient range for planetary and astronomical timescales.
- Sub-microsecond and nanosecond temporal resolution.
- Simple serialization.

The choice of nanoseconds does not require every solver to integrate at nanosecond precision.

Numerical solvers may use rational intervals, floating-point substeps or adaptive integration internally.

Their resulting states must still identify the canonical interval they represent.

### Why not use a floating-point Julian Date directly?

A single floating-point Julian Date is unsuitable as the canonical runtime representation because small time differences become progressively harder to distinguish at large absolute dates.

W04 correctly identifies the value of a two-part Julian Date representation.

The proposed solution is:

- Integer nanoseconds for canonical simulation time.
- Two-part Julian Date for astronomical conversions.
- Explicit time-scale metadata.
- Higher-precision internal representations where scientific models require them.

## F.3 Time scales

The system must distinguish the following:

| Time scale            | Responsibility                                       |
| --------------------- | ---------------------------------------------------- |
| World Simulation Time | Canonical progression of the logical world           |
| TAI                   | Continuous atomic reference                          |
| UTC                   | Civil timestamps and human-readable dates            |
| TT                    | Terrestrial dynamical calculations                   |
| TDB                   | Solar-system ephemerides                             |
| TCB                   | Barycentric coordinate time                          |
| UT1                   | Earth rotation calculations                          |
| Proper Time           | Time measured along an observer's physical worldline |
| Monotonic Wall Time   | Operational durations and deadlines                  |

The critical rule is that a time value must never be interpreted without knowing its scale and epoch.

### World epoch mapping

Every world should define an optional astronomical epoch mapping:

\\[ M_W(T_w) \rightarrow T\_{\mathrm{astro}} \\]

This mapping specifies how canonical world time relates to the chosen astronomical reference.

For an Earth-based world, the mapping may be anchored to an appropriate TAI/TT epoch and associated astronomical conventions.

For a fictional universe, the world epoch may have no mapping to terrestrial civil time.

For relativistic simulation, additional coordinate-frame and proper-time transformations are required.

A single constant offset must not be assumed to convert every astronomical time scale correctly.

## F.4 World time versus astronomical time

A critical distinction:

World time is the simulation's authoritative coordinate. Astronomical time is a scientific interpretation or mapping of that coordinate.

The world can progress without requiring every subsystem to use TDB or UTC.

For example:

- A vehicle uses local simulation time.
- An atmosphere model uses canonical world time.
- An orbital propagator converts world time to TDB.
- A user interface displays UTC.
- A relativistic clock calculates proper time along its trajectory.

This prevents astronomical conventions from contaminating ordinary gameplay and physics scheduling.

## F.5 Regional execution clocks

Every region maintains its own execution metadata.

Recommended conceptual contract:

| Field                  | Meaning                               |
| ---------------------- | ------------------------------------- |
| `timeline_id`          | Timeline being simulated              |
| `region_id`            | Logical region                        |
| `owner_epoch`          | Current ownership fencing token       |
| `execution_tick`       | Region-local execution counter        |
| `last_committed_time`  | Latest canonical time fully committed |
| `target_time`          | Requested execution horizon           |
| `state_revision`       | Region state version                  |
| `event_sequence`       | Last committed event sequence         |
| `scheduler_profile_id` | Active scheduling configuration       |

Two fields deserve special attention.

Execution tick is local to a region and has no universal meaning.

Last committed time identifies the actual canonical temporal position of the committed region state.

For example:

Illustrative distributed execution snapshot

All regions belong to timeline T-001

Region A

120.000 s

Execution counter: 7,200

Region B

119.800 s

Execution counter: 3,594

Region C

115.000 s

Execution counter: Adaptive

Illustrative values, not repository benchmarks or actual runtime measurements.

This is a valid distributed state.

Region A does not need to wait for Region C unless a causal dependency requires it.

## F.6 Causal ordering

A timestamp alone is insufficient to establish causality.

Planetary Engine needs two separate concepts:

1. Temporal position.
2. Causal relationship.

Every committed event should have a canonical simulation timestamp and a stable identity.

Recommended event metadata:

| Field              | Purpose                        |
| ------------------ | ------------------------------ |
| `timeline_id`      | Identifies the history         |
| `event_id`         | Globally unique event identity |
| `simulation_time`  | Canonical temporal position    |
| `source_region_id` | Originating region             |
| `owner_epoch`      | Valid authority epoch          |
| `source_sequence`  | Monotonic source sequence      |
| `causal_parents`   | Explicit prerequisite events   |
| `state_revision`   | Resulting state revision       |
| `schema_version`   | Event contract version         |

The system must support the distinction:

\\[ A \rightarrow B \\]

Meaning event A causally precedes event B.

This is different from:

\\[ t(A)\<t(B) \\]

A timestamp comparison does not establish a causal relationship between independent events.

### Example

- Region A records a vehicle entering an intersection.
- Region B owns a traffic light.
- The vehicle event requires the current traffic-light state.
- Region A must obtain a sufficiently current, causally valid state before committing the dependent interaction.

For independent events in distant regions, no synchronization is necessary.

### Cross-region synchronization modes

Three levels are recommended.

| Mode                          | Use                                                                  |
| ----------------------------- | -------------------------------------------------------------------- |
| Independent execution         | No causal dependency                                                 |
| Bounded temporal consistency  | Environmental sampling and approximate interactions                  |
| Strong causal synchronization | Collision, ownership, financial and other authoritative interactions |

For tightly coupled physical interactions, the system must either:

- Synchronize the involved interaction domains.
- Exchange sufficient future information to safely advance.
- Roll back and replay under a defined correction policy.

It must not silently commit physically inconsistent results.

A useful research reference is the IEEE High Level Architecture time-management model, which W09 already identifies as relevant in §5.5.

## F.7 Time acceleration

Time acceleration must be a control-plane operation over canonical world-time progression.

The relationship between simulation time and wall-clock time can be represented as:

\\[ \frac{dT_w}{dT\_{\mathrm{wall}}}=s(t) \\]

Where \\(s(t)\\) is the configured simulation speed multiplier.

Examples:

| Speed | Meaning                   |
| ----- | ------------------------- |
| 0×    | Paused                    |
| 0.5×  | Half speed                |
| 1×    | Normal progression        |
| 10×   | Ten times faster          |
| 1000× | One thousand times faster |

The speed multiplier is not a numerical integration timestep.

At 1000×, the scheduler may request a large simulation interval, but the physics integrator must still subdivide that interval according to numerical stability and model requirements.

This is consistent with W04 §3.7.

### Important restriction

Time acceleration must never modify:

- Network timeout duration.
- QUIC connection timeout semantics.
- Ownership lease duration.
- Authentication expiration.
- Wall-clock monitoring intervals.
- Real-time operational deadlines.

A paused world must still permit operational control, network management and recovery.

## F.8 Pause and discontinuities

Pause is a change in the world-time progression policy.

It does not stop the infrastructure.

A paused world can still:

- Accept administrative commands.
- Persist checkpoints.
- Replicate existing state.
- Migrate ownership.
- Process network traffic.
- Prepare branch operations.

Explicit discontinuity operations are required for:

- Restore checkpoint.
- Branch creation.
- Forward time jump.
- Historical reconstruction.
- Simulation reset.

Backward time must not be treated as an ordinary clock adjustment.

A dissipative physical simulation cannot generally reconstruct its previous state by simply reversing its integration interval.

Restoration requires a checkpoint and replay or an explicitly reversible model.

## F.9 Timeline identity and branching

A timeline is a distinct persistent simulation history.

Recommended conceptual contract:

| Field                    | Meaning                     |
| ------------------------ | --------------------------- |
| `world_id`               | Persistent world identity   |
| `timeline_id`            | Unique history identity     |
| `parent_timeline_id`     | Parent history, if any      |
| `fork_time`              | Canonical fork instant      |
| `parent_revision`        | Exact parent state revision |
| `branch_revision`        | Current branch revision     |
| `time_control_revision`  | Time-control configuration  |
| `configuration_revision` | Simulation configuration    |

The timeline identity must be included in every authoritative event and state snapshot.

### Branching semantics

Parent timeline T0

## World time 1000 s

Shared checkpoint and revision

T1

Branch A

Normal progression

T2

Branch B

Accelerated

T3

Branch C

Alternative events

Branches share immutable ancestry but maintain independent future state, event history and time controls.

Two timelines may have the same canonical time coordinate and different physical states.

Therefore, timestamp equality does not imply state equality.

## F.10 Ownership migration and temporal continuity

Migration must preserve temporal continuity independently of process identity.

Recommended migration sequence:

1. Prepare the target region.
2. Establish a consistent source checkpoint.
3. Record canonical committed time.
4. Record the last committed event sequence.
5. Transfer active state and pending causal dependencies.
6. Commit the new ownership epoch.
7. Reject old-owner mutations.
8. Resume execution from the transferred temporal position.

The receiving owner must not:

- Reset its clock to zero.
- Derive its time from process uptime.
- Infer world time from a local tick count.
- Advance past unresolved dependencies.
- Accept stale events from the previous owner.

W05's ownership epoch mechanism is appropriate, but the migration contract must include the canonical time and event position.

## F.11 Failure and recovery

The recovery model must distinguish three cases.

Region process failure

Restore the latest valid checkpoint and replay the committed event journal.

Owner migration

Transfer the committed temporal position and authority fencing token.

Timeline restoration

Create a new revision or branch from an earlier checkpoint without silently rewriting already published historical records.

A region's wall-clock downtime must not automatically translate into simulation-time advancement.

Offline catch-up is a deliberate world-level policy.

## F.12 Client temporal guarantees

Unity should receive:

- Timeline ID.
- Canonical simulation time.
- Region ID.
- Region owner epoch.
- State revision.
- Snapshot sequence.
- Relevant causal/event sequence.
- Temporal validity information.

The client can then maintain:

1. Authoritative snapshot timeline.
2. Predicted local timeline.
3. Interpolated presentation timeline.

A client may render a slightly older world state for smooth interpolation.

This does not change the authoritative world time.

The client must also detect when a timeline changes or an authoritative snapshot becomes invalid due to ownership migration.

# G. Cross-system impact

## G.1 Compatibility matrix

| Document | Status                  | Required action                                                |
| -------- | ----------------------- | -------------------------------------------------------------- |
| W01      | Compatible              | Add explicit world-time input contract                         |
| W02      | Clarification           | Retain 60 Hz as a candidate profile, not global clock          |
| W03      | Compatible              | Clarify model-time and integration intervals                   |
| W04      | Architectural extension | Separate time authority from distributed execution             |
| W05      | Architectural change    | Extend migration metadata                                      |
| W06      | Architectural extension | Formalize timeline identity and branch semantics               |
| W07      | Architectural change    | Add regional time and causal metadata                          |
| W08      | Clarification           | Require temporal validity on environment samples               |
| W09      | Architectural change    | Define regional clocks as progress coordinates on one timeline |

## G.2 Required ownership boundaries

| Component                        | Owner                         |
| -------------------------------- | ----------------------------- |
| Canonical world-time definition  | Shared world foundation / W04 |
| Astronomical conversion          | W04                           |
| Reference-frame evaluation epoch | W01                           |
| Physics integration intervals    | W02 / W03                     |
| Regional execution scheduling    | W09                           |
| Region ownership and migration   | W05                           |
| Timeline branching and revisions | W06                           |
| Client time synchronization      | W07                           |
| Environmental temporal sampling  | W08                           |

No worker should independently introduce a second authoritative definition of world time.

## G.3 Required interface changes

The following contracts must be coordinated:

- W04 ↔ W05: canonical time and migration checkpoint.
- W04 ↔ W06: timeline identity and branch epoch.
- W04 ↔ W07: snapshot time semantics.
- W04 ↔ W09: regional progress and scheduling.
- W01 ↔ W04: reference-frame evaluation time.
- W02/W03 ↔ W09: solver intervals and execution cadence.
- W08 ↔ W04: environment sampling time.
- W05 ↔ W06: ownership epoch versus timeline revision.

# H. Required architectural decisions

The central integrator should explicitly accept or reject the following decisions.

| ID           | Decision                                                                       | Recommendation       |
| ------------ | ------------------------------------------------------------------------------ | -------------------- |
| ADR-TIME-001 | One canonical logical timeline per history                                     | Accept               |
| ADR-TIME-002 | No global synchronization every tick                                           | Accept               |
| ADR-TIME-003 | Regional clocks represent execution progress, not independent time definitions | Accept               |
| ADR-TIME-004 | Canonical time uses signed integer nanoseconds                                 | Accept provisionally |
| ADR-TIME-005 | Astronomical scales remain explicitly typed                                    | Accept               |
| ADR-TIME-006 | Separate simulation and monotonic wall-clock time                              | Accept               |
| ADR-TIME-007 | Causal ordering is separate from timestamp ordering                            | Accept               |
| ADR-TIME-008 | Regional committed-time watermarks                                             | Accept               |
| ADR-TIME-009 | Cross-region interaction synchronization is selective                          | Accept               |
| ADR-TIME-010 | Timeline identity is separate from world identity                              | Accept               |
| ADR-TIME-011 | Branches use immutable ancestry and independent future state                   | Accept               |
| ADR-TIME-012 | Time acceleration is a scheduler/world-control policy                          | Accept               |
| ADR-TIME-013 | Region migration preserves canonical time and event position                   | Accept               |
| ADR-TIME-014 | Clients use authoritative timestamps and independent presentation clocks       | Accept               |
| ADR-TIME-015 | 30 Hz or 60 Hz is not a universal world-time frequency                         | Accept               |
| ADR-TIME-016 | Relativistic proper time is distinct from canonical simulation coordinate time | Accept               |

## H.1 Highest-priority decisions

Three decisions must be resolved before the architecture can be frozen:

1. ADR-TIME-001 and ADR-TIME-003 establish the fundamental temporal model.
2. ADR-TIME-007 and ADR-TIME-009 establish distributed causality.
3. ADR-TIME-010 and ADR-TIME-013 establish historical continuity and migration.

If these are not accepted, W05, W06 and W09 cannot establish fully compatible contracts.

# I. Validation plan

The architecture must be validated through temporal invariants, distributed execution tests, numerical tests and recovery scenarios.

These are proposed tests, not claims of existing implementation capability.

## I.1 Canonical time invariants

| Test                       | Required result                                 |
| -------------------------- | ----------------------------------------------- |
| Integer time serialization | Exact round-trip                                |
| Large astronomical epoch   | No loss of canonical integer precision          |
| Time conversion            | Explicit scale and epoch preserved              |
| Time comparison            | Deterministic ordering                          |
| Simulation restart         | Same committed temporal position                |
| Time acceleration          | Correct progression according to control policy |
| Pause                      | No unintended world-time advancement            |
| Branch creation            | Parent history remains unchanged                |

## I.2 Distributed regional execution

Test three regions:

- Region A: 60 Hz.
- Region B: 30 Hz.
- Region C: adaptive integration.

Verify:

1. All use the same canonical timeline.
2. Their execution ticks are independent.
3. Their committed-time watermarks are correctly maintained.
4. No global synchronization is needed when there are no dependencies.
5. Causally dependent interactions cannot commit against invalid temporal state.

## I.3 Causality tests

| Scenario                              | Expected result                        |
| ------------------------------------- | -------------------------------------- |
| Independent events in distant regions | Independent execution                  |
| Event A causes event B                | B cannot commit before A               |
| Delayed cross-region message          | Correct causal reconciliation          |
| Duplicate event                       | Idempotent processing                  |
| Out-of-order event                    | Correct dependency handling            |
| Conflicting ownership epoch           | Old authority rejected                 |
| Region rollback                       | Consistent replay                      |
| Cross-region collision                | No duplicated authoritative resolution |

## I.4 Time acceleration

Test:

- 0×.
- 0.5×.
- 1×.
- 10×.
- 100×.
- 1000×.

Measure:

- World-time advancement.
- Numerical integration error.
- Event ordering.
- Region synchronization overhead.
- Physics stability.
- Environmental model stability.
- Replication correctness.
- Network timeout behavior.
- Persistence consistency.

The most important invariant:

Accelerating simulation time must not accelerate operational wall-clock deadlines.

## I.5 Astronomical validation

Use independently validated astronomical reference data.

Test:

- TAI/TT conversions.
- UTC leap-second boundaries.
- UT1 Earth-rotation calculations.
- TDB ephemeris evaluation.
- TCB/TDB distinction.
- Time-dependent ECEF/ECI transformations.
- Proper-time integration along selected relativistic trajectories.

Compare results against appropriate reference implementations, including NASA SPICE and JPL ephemerides.

## I.6 Branching and replay

Test:

1. Create a world checkpoint.
2. Create two branches.
3. Advance each independently.
4. Modify environmental and physical conditions.
5. Restore one branch.
6. Replay its events.
7. Compare state hashes and event histories.
8. Verify that the parent remains unchanged.

Cross-platform deterministic replay should be treated separately from same-runtime deterministic replay, as W03 correctly observes.

## I.7 Migration and failure

Test:

- Migration during normal execution.
- Migration during accelerated time.
- Migration while paused.
- Process failure during ownership transfer.
- Delayed messages from the previous owner.
- Recovery from an older checkpoint.
- Recovery with pending causal dependencies.

Required invariant:

No migration may silently change the canonical simulation time or create two simultaneously valid authoritative owners.

## I.8 Performance criteria

Measure:

- Regional execution throughput.
- Temporal watermark lag.
- Cross-region synchronization overhead.
- Causal dependency backlog.
- Event journal throughput.
- Checkpoint creation and restoration.
- Scheduler overhead.
- Time conversion overhead.
- Performance under uneven regional workloads.

A particularly important experiment is deliberately making one region much slower than all others.

The remaining regions should continue executing independently wherever causal dependencies permit.

# J. Unresolved questions

The proposed architecture resolves the primary conflict, but several decisions require additional research or coordination.

## J.1 Global time and relativistic simulation

What is the precise canonical coordinate-time convention for a world containing relativistic simulations?

A universal simulation coordinate is practical, but its relationship to relativistic coordinate systems must be formally specified.

In particular:

- Which coordinate-time convention is used for astronomical calculations?
- How are multiple gravitational reference systems reconciled?
- How are proper-time clocks represented?
- What happens when relativistic effects become significant?

Owner: W01 / W04.

## J.2 Cross-region physical interactions

What synchronization strategy should be used for tightly coupled physical interactions?

Candidates include:

- Conservative synchronization.
- Speculative execution with rollback.
- Dedicated interaction ownership.
- Temporally bounded boundary exchange.

A hybrid is likely appropriate, but the exact selection depends on physics and networking requirements.

Owner: W02 / W05 / W09.

## J.3 Time acceleration policy

Should acceleration be:

- Global to an entire timeline?
- Configurable per world?
- Restricted by active multiplayer sessions?
- Adaptively limited by computational capacity?

My recommendation is that time scale is timeline-level state, with explicit restrictions for multiplayer sessions and computational overload.

Per-region independent time dilation should not be allowed to silently change the physical relationship between neighboring regions.

Owner: W06 / W09.

## J.4 Wall-clock synchronization

Which monotonic clock and synchronization strategy should be standardized across Rust nodes?

The architecture should distinguish local monotonic durations from synchronized UTC timestamps.

Exact clock discipline, drift monitoring and failure thresholds require infrastructure research.

Owner: W05 / W09.

## J.5 Temporal event ordering

How should events with identical canonical timestamps be ordered?

A global total-order sequence would introduce unnecessary coordination.

The preferred solution is:

- Causal ordering where dependencies exist.
- Stable local sequence numbers.
- Deterministic tie-breaking where required.
- No invented causal relationship between independent events.

The exact serialization and replay contract still needs coordination with W05 and W06.

## J.6 Time representation precision

Signed 128-bit nanoseconds are recommended for the canonical coordinate, but the scientific integration layer must establish whether nanosecond quantization is sufficient for all targeted domains.

Some specialized scientific calculations may require additional fractional precision.

The canonical representation and numerical solver representation should therefore remain separate.

Owner: W04.

## J.7 Historical data retention

How long should detailed event history be retained?

Planetary-scale simulations could generate enormous event histories.

W06's checkpoint and copy-on-write model is appropriate, but retention, compaction and historical reconstruction policies require separate decisions.

Owner: W06 / W05.

# K. Final assessment

Resolved conditionally

The conflict between W04's universal simulation time and W09's regional clocks is resolved at the architectural level.

The recommended model is:

One canonical logical world timeline, distributed regional execution, independent solver frequencies and explicit causal synchronization.

The architecture does not require:

- A globally synchronized 30 Hz clock.
- A globally synchronized 60 Hz clock.
- One central process advancing every region.
- Independent incompatible regional time definitions.
- Wall-clock time as the source of physical world progression.

It does require:

- Canonical temporal coordinates.
- Timeline identity.
- Regional execution watermarks.
- Explicit event causality.
- Temporal continuity during migration.
- Astronomical time conversion.
- Defined time-control semantics.
- Versioned state and event history.

## K.1 What is actually resolved?

| Problem                             | Assessment                                          |
| ----------------------------------- | --------------------------------------------------- |
| W02 versus W09 tick conflict        | Resolved                                            |
| W04 versus W09 clock conflict       | Resolved                                            |
| Wall-clock versus simulation time   | Resolved                                            |
| Simulation versus presentation time | Resolved                                            |
| Astronomical time representation    | Resolved conditionally                              |
| Regional execution semantics        | Resolved                                            |
| Cross-region causality              | Architecture selected; protocol requires validation |
| Ownership migration                 | Resolved conditionally                              |
| Timeline branching                  | Resolved                                            |
| Time acceleration                   | Resolved conditionally                              |
| Relativistic time semantics         | Requires further specification                      |

## K.2 Required integration order

I recommend that the central architect integrate the decisions in this order:

1. Accept the canonical time model. Without this, all other workers may continue using incompatible time definitions.
2. Establish the regional execution contract. This resolves W05/W09 compatibility.
3. Establish causal event semantics. This resolves cross-region correctness.
4. Integrate timeline identity with W06. This enables branching and historical reconstruction.
5. Update W07 replication contracts. This provides clients with consistent temporal information.
6. Integrate W01/W04 astronomical conversion.
7. Finalize W02/W03/W08 scheduling and solver interfaces.

## K.3 Final architectural recommendation

The most important decision is not whether Planetary Engine runs at 30 Hz or 60 Hz.

It is whether the architecture understands the difference between the time represented by the world and the time consumed by the computers simulating it.

These must be independent.

A planetary environment may advance through thousands of years of simulated history while local physics performs adaptive integration. Multiple regional servers may compute different portions of that history independently. A Unity client may display a delayed interpolated state. A scientific model may reconstruct an earlier period. A new timeline may branch from a common checkpoint.

All of these operations must refer to the same well-defined temporal model without requiring one globally synchronized execution loop.

That is the architecture I recommend for Planetary Engine.

## Source documents

All nine worker documents were examined, with the following sections providing the principal evidence for this resolution:

| Source                                                                                | Relevant sections                                  |
| ------------------------------------------------------------------------------------- | -------------------------------------------------- |
| [W01](https://github.com/projectzonio/what_if/blob/main/w1.md?utm_source=chatgpt.com) | §2.7–2.8, spatial precision and astronomical time  |
| [W02](https://github.com/projectzonio/what_if/blob/main/w2.md?utm_source=chatgpt.com) | §3.1, fixed timestep and 60 Hz proposal            |
| [W03](https://github.com/projectzonio/what_if/blob/main/w3.md?utm_source=chatgpt.com) | §6.3–6.4, model time and determinism               |
| [W04](https://github.com/projectzonio/what_if/blob/main/w4.md?utm_source=chatgpt.com) | §3.7 and §4.1, universal time architecture         |
| [W05](https://github.com/projectzonio/what_if/blob/main/w5.md?utm_source=chatgpt.com) | §2.6 and §9.2–9.3, ownership and migration         |
| [W06](https://github.com/projectzonio/what_if/blob/main/w6.md?utm_source=chatgpt.com) | §7.6, §8.1, regional migration and branches        |
| [W07](https://github.com/projectzonio/what_if/blob/main/w7.md?utm_source=chatgpt.com) | §8, time synchronization                           |
| [W08](https://github.com/projectzonio/what_if/blob/main/w8.md?utm_source=chatgpt.com) | §8.5 and §9, environmental simulation              |
| [W09](https://github.com/projectzonio/what_if/blob/main/w9.md?utm_source=chatgpt.com) | §2.2 and §5.4–5.5, scheduling and distributed time |

Deliverable status: Architecture resolution completed. No repository files modified, no implementation performed, and no benchmark results invented. The remaining conditions are explicitly assigned to cross-worker integration and validation.