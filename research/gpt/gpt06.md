# PLANETARY ENGINE — GPT-06

## Multirate Simulation Scheduler

Architecture resolution: conditionally resolved

Research only — no implementation

Role: Principal Systems Architect — Independent Architecture Resolution Worker

Repository: [projectzonio/what_if](https://github.com/projectzonio/what_if)

Scope: C-06 — Conflicting simulation frequencies and scheduling assumptions.

Primary recommendation: Hierarchical Multirate Simulation Scheduler (HMSS).

# A. Executive decision

Planetary Engine must use one authoritative logical world timeline, but must not use one universal simulation execution frequency.

The recommended architecture is a hierarchical scheduler combining:

1. A shared logical world-time contract.
2. Region-level execution and computational-budget management.
3. Domain-specific scheduling.
4. Fixed-step integration for interactive physical systems.
5. Adaptive integration for astronomical and other numerically sensitive systems.
6. Event-driven execution for discrete gameplay and economic processes.
7. Independent replication and presentation clocks.
8. Offline execution capable of advancing simulation time faster or slower than wall-clock time.

The central distinction is:

> All authoritative simulation domains agree on what time it is in the world. They do not have to calculate the world at the same frequency.

The scheduler must coordinate when computations become necessary, which state versions they consume, and when their results become authoritative.

It must not force all domains into a common synchronous loop.

## Key architectural decisions

| Question                    | Recommendation              |
| --------------------------- | --------------------------- |
| Universal simulation tick?  | No universal execution tick |
| Universal logical time?     | Yes                         |
| Scheduler organization      | Hierarchical                |
| Primary execution partition | Region and physics island   |
| Domain scheduling           | Independent, coordinated    |
| Interactive vehicle physics | Candidate 60 Hz             |
| High-speed physics          | 120–240 Hz substeps         |
| Orbital mechanics           | Adaptive integration        |
| Environment                 | Hierarchical multi-fidelity |
| Gameplay                    | Periodic and event-driven   |
| Replication                 | Independent, priority-based |
| Rendering                   | Independent client clock    |
| Offline simulation          | Variable execution speed    |

All numerical frequencies remain provisional design parameters.

## Important resolution of the 30 Hz versus 60 Hz conflict

The repository contains two competing recommendations:

- W02 proposes a 60 Hz authoritative interactive tick.
- W03 also proposes 60 Hz as the authoritative simulation baseline.
- W09 proposes a 30 Hz initial server simulation profile.

These are not necessarily incompatible physical requirements.

The architectural mistake would be to interpret either frequency as a mandatory execution rate for every subsystem.

I recommend:

- 60 Hz as the initial interactive physics evaluation target.
- 120–240 Hz as a candidate internal vehicle integration range.
- 30 Hz as a candidate coarse region coordination and workload-planning cadence, not a universal world clock.
- Independent rates for all other domains.

This preserves the interactive requirements while retaining W09's computational-budget model.

Sources: W02 §7.2, W03 §6.3, W09 §2.2, W05 §2.3.

# B. Existing architecture

The nine worker documents already contain most of the necessary architectural foundations. The missing element is a unified scheduling contract.

## B.1 Worker responsibilities and relevant proposals

| Worker | Architectural ownership                            | Relevant scheduling implications                    |
| ------ | -------------------------------------------------- | --------------------------------------------------- |
| W01    | World geometry, coordinates, environment interface | Time-dependent environmental queries                |
| W02    | General physics runtime                            | Fixed timestep, physics substeps, lifecycle         |
| W03    | Specialized physical models                        | Vehicle, aircraft, drone and spacecraft integration |
| W04    | Astronomy and orbital mechanics                    | Adaptive integration, analytical propagation        |
| W05    | Distributed simulation and regions                 | Regional ticks, authority and execution budgets     |
| W06    | Gameplay and world mutation                        | Event-driven and periodic gameplay                  |
| W07    | Client prediction and replication                  | Independent network and presentation timelines      |
| W08    | Environmental simulation                           | Hierarchical multi-fidelity environment             |
| W09    | Performance and scalability                        | 30 Hz initial profile, adaptive scheduling          |

This table describes the relevant ownership for C-06, not a replacement of the workers' complete responsibilities.

## B.2 W02 — Physics scheduler

W02 §7.1 defines an authoritative physical lifecycle:

1. Command collection.
2. Tick boundary.
3. Coordinate and environment update.
4. Force evaluation.
5. Velocity prediction.
6. Collision detection.
7. Constraint preparation.
8. Constraint solving.
9. State integration.
10. Sleep and activation management.
11. Event generation.
12. State checkpoint.

W02 §7.2 explicitly acknowledges multirate scheduling.

Its proposed domain scheduling distinguishes interactive rigid bodies, vehicle dynamics, characters, aircraft, atmosphere, orbital dynamics, geology and economy.

Importantly, it allows multiple substeps within one authoritative world interval.

Source: [W02 §7.1–7.2](https://github.com/projectzonio/what_if/blob/main/w2.md), lines 619–674.

## B.3 W03 — Specialized physical models

W03 §6.3 makes an important distinction between simulation time and model time.

Its proposed frequencies include:

| Model                         | Proposed frequency |
| ----------------------------- | ------------------ |
| Authoritative simulation      | 60 Hz              |
| Standard rigid bodies         | 60–120 Hz          |
| High-speed vehicles           | 120–240 Hz         |
| Drone flight controllers      | 250–1000 Hz        |
| Rocket propulsion             | 60–240 Hz          |
| Orbital propagation           | Adaptive           |
| Offline scientific simulation | Adaptive           |

These are research starting points, not measured requirements.

The drone controller example is particularly important: an internal controller can execute more frequently than the world-level physics interval, provided its state is integrated consistently over that interval.

Source: [W03 §6.3](https://github.com/projectzonio/what_if/blob/main/w3.md), lines 1537–1559.

## B.4 W04 — Astronomical simulation

W04 §3.7 explicitly rejects the assumption that all physical systems should execute at the same frequency.

It distinguishes:

- Simulation time.
- Wall-clock time.
- Presentation time.
- Astronomical time standards.

It also establishes that accelerating simulation time must not automatically increase numerical integration steps.

The scheduler requests a simulation interval; the numerical solver determines the appropriate internal integration steps.

Source: [W04 §3.7](https://github.com/projectzonio/what_if/blob/main/w4.md), lines 519–577.

## B.5 W05 — Regional simulation

W05 §2.3 proposes different rates for different entity classes:

- Interactive vehicle: 60 Hz.
- Active NPC: 10–30 Hz.
- Distant NPC: 1–5 Hz.
- Static objects: no regular update.
- Economic processes: 0.1–1 Hz.
- Astronomy: model-dependent.

W05 also separates Tokio networking from dedicated simulation workers.

This is important: asynchronous networking must not determine authoritative physics execution order.

Sources: [W05 §2.3](https://github.com/projectzonio/what_if/blob/main/w5.md), lines 283–310; [W05 §4](https://github.com/projectzonio/what_if/blob/main/w5.md), lines 488–515.

## B.6 W06 — Gameplay scheduling

W06 §12.2 provides a useful initial frequency model:

| Gameplay subsystem    | Candidate frequency |
| --------------------- | ------------------- |
| Character interaction | 30–60 Hz            |
| Tactical combat       | 10–30 Hz            |
| Construction          | 1–10 Hz             |
| Factory production    | Event-driven / 1 Hz |
| Regional economy      | 0.1–1 Hz            |
| Strategic AI          | 0.1–2 Hz            |
| Long-term economy     | 0.01–0.1 Hz         |

W06 explicitly recommends event-driven processing for production completion, resource availability, ownership transfers and market transactions.

Source: [W06 §12.2–12.3](https://github.com/projectzonio/what_if/blob/main/w6.md), lines 1483–1516.

## B.7 W07 — Client synchronization

W07 §8 distinguishes:

- Authoritative server simulation time.
- Client prediction time.
- Client interpolation time.
- Wall-clock/network time.

The client receives server tick, simulation timestamp, world epoch and entity version.

Unity's fixed update must not become the authoritative world clock.

Source: [W07 §8](https://github.com/projectzonio/what_if/blob/main/w7.md), lines 1041–1117.

## B.8 W08 — Environmental simulation

W08 proposes Hierarchical Multi-Fidelity Environmental Simulation.

Its four levels are:

1. Global Environment.
2. Regional Environment.
3. Local Environment.
4. Interaction Environment.

These are different computational representations of one environment, not four independent worlds.

The worker explicitly identifies mismatched temporal resolutions and incorrect interpolation as critical risks.

Sources: [W08 §1](https://github.com/projectzonio/what_if/blob/main/w8.md), lines 170–180; [W08 risk register](https://github.com/projectzonio/what_if/blob/main/w8.md), lines 666–675.

## B.9 W09 — Distributed performance architecture

W09 §2.2 proposes a 30 Hz initial server performance profile.

Its illustrative 33.33 ms budget is:

| Operation               | Proposed budget |
| ----------------------- | --------------- |
| Physics                 | 12 ms           |
| ECS/gameplay            | 5 ms            |
| AI/environment          | 4 ms            |
| Replication preparation | 4 ms            |
| Region coordination     | 2 ms            |
| Reserve                 | 6.33 ms         |

Crucially, W09 explicitly states that these operations need not all complete synchronously in every tick.

W09 also proposes region-local scheduling, adaptive fidelity and specialized physics islands.

Sources: [W09 §2.2](https://github.com/projectzonio/what_if/blob/main/w9.md), lines 221–238; [W09 §13](https://github.com/projectzonio/what_if/blob/main/w9.md), lines 794–835.

# C. Problem analysis

## C.1 The actual architectural conflict

The conflict is not fundamentally 30 Hz versus 60 Hz.

It is the absence of a precise distinction between:

- Logical time.
- Scheduler dispatch.
- Physical integration.
- Domain evaluation.
- Regional coordination.
- State publication.

The documents sometimes use the word tick for different concepts.

That creates ambiguity when combining their proposals.

For example:

A 60 Hz vehicle simulation does not imply that the atmosphere must execute at 60 Hz.

Likewise, a 30 Hz region coordination cycle does not imply that orbital mechanics must calculate a new orbital state every 33.33 ms.

## C.2 Ten different temporal concepts

These concepts must become separate architectural terms.

| Concept                       | Definition                                          | Authority        |
| ----------------------------- | --------------------------------------------------- | ---------------- |
| Logical world time            | Time represented by the simulated universe          | W01/W04 contract |
| Scheduler frequency           | How often scheduling decisions execute              | Scheduler        |
| Physics integration frequency | Number of physical integration intervals            | Physics solver   |
| Physics substeps              | Internal subdivisions of an integration interval    | Physics solver   |
| Environment update frequency  | Frequency of environmental model evolution          | W08              |
| Gameplay update frequency     | Frequency of gameplay decisions and processes       | W06              |
| Astronomical solver frequency | Numerical evaluation frequency                      | W04              |
| Replication frequency         | Frequency of state transmission                     | W05/W07          |
| Rendering frequency           | Frequency of visual presentation                    | Unity            |
| Offline execution frequency   | Computational throughput relative to simulated time | Offline runtime  |

A scheduler may dispatch an operation at 1 Hz, while that operation requests 100 internal integration steps.

Those are not contradictory frequencies.

## C.3 Why a universal fixed timestep fails

Consider an ordinary vehicle and a planet.

The vehicle requires frequent physical integration because of:

- Tire forces.
- Suspension.
- Collision detection.
- Steering.
- Engine torque.
- Rapid changes in contact conditions.

The planet's orbital state evolves on an entirely different characteristic timescale.

Forcing both into a 60 Hz loop creates unnecessary astronomical computation.

Reducing both to 1 Hz would make the interactive vehicle simulation unacceptable.

The same problem occurs with climate, economics and gameplay.

## C.4 Why completely independent schedulers also fail

Independent domain schedulers without temporal contracts introduce:

- Stale environmental forces.
- Inconsistent physical interactions.
- Events applied at incorrect simulation times.
- Non-reproducible cross-domain state.
- Incorrect interpolation.
- Race conditions between regional authorities.

The solution must coordinate dependencies, not simply separate everything.

## C.5 Missing temporal consistency contract

The repository describes timestamped data, snapshots and deterministic event ordering, but does not establish one universal rule for the following:

1. Which simulation instant a subsystem reads.
2. Which revision of another subsystem it may consume.
3. How old that input may be.
4. When its output becomes authoritative.
5. Whether the output may be interpolated.
6. Whether another subsystem must wait for it.

This is the principal architectural gap C-06 must close.

# D. Cross-system dependencies

## D.1 Dependency matrix

| Worker | What C-06 requires                             | What the worker requires from C-06                        | Compatibility                 |
| ------ | ---------------------------------------------- | --------------------------------------------------------- | ----------------------------- |
| W01    | Canonical time and environment query semantics | Time-stamped environment sampling                         | Compatible with clarification |
| W02    | Physics lifecycle and solver interfaces        | Reliable scheduling and integration intervals             | Compatible with clarification |
| W03    | Solver-specific execution requirements         | Consistent world-time integration                         | Compatible                    |
| W04    | Canonical time representation                  | Variable integration horizons and epoch conversions       | Compatible                    |
| W05    | Region ownership and local execution           | Hierarchical scheduling without global barriers           | Compatible with clarification |
| W06    | Gameplay event and state contracts             | Event ordering and dependency revisions                   | Compatible                    |
| W07    | Authoritative timestamps and state versions    | Independent replication schedule                          | Compatible                    |
| W08    | Environmental model hierarchy                  | Time-stamped field versions and interpolation             | Compatible with clarification |
| W09    | Resource budgets and execution topology        | Explicit distinction between region tick and domain rates | Requires clarification        |

## D.2 W01 dependency — Environment provider

W01 defines the environment provider as a query based on position, world time and reference frame.

The scheduler must preserve this abstraction.

A vehicle must be able to request gravity, atmosphere and wind at its actual integration timestamp.

It must not assume that the environment solver executed at the same frequency as the vehicle.

Required contract:

- Environment state revision.
- Valid simulation-time interval.
- Reference frame.
- Spatial validity.
- Interpolation method.
- Accuracy or uncertainty metadata.

W01 must remain the owner of the spatial and environmental query contract.

The scheduler must not create a competing environmental model.

## D.3 W02/W03 dependency — Physical integration

Physics must receive a defined integration interval.

A physics subsystem may perform multiple internal steps within that interval.

The scheduler owns execution timing, but the solver owns numerical integration.

This distinction prevents the scheduler from becoming a universal physics integrator.

## D.4 W04 dependency — Astronomy

The astronomical subsystem requires adaptive integration and analytical propagation.

It must be possible to request a state at a specified simulation instant without requiring every intermediate instant to be processed by a global scheduler.

However, a close encounter or interaction with a spacecraft must produce appropriately accurate physical state at the interaction boundary.

## D.5 W05/W09 dependency — Distributed execution

W05 and W09 propose regional execution and ownership.

C-06 must distinguish:

- Region ownership.
- Physics-island ownership.
- Scheduler worker allocation.
- Logical time.
- Local execution sequence.

A region must not become a mandatory synchronization boundary for every domain.

Two regions can advance their independent work concurrently, provided cross-region interactions follow an explicit consistency contract.

W09 itself recommends that regions need not all execute in real time and that physics islands may be separate from server regions.

## D.6 W06 dependency — Gameplay

Gameplay systems require both event-driven and periodic execution.

The scheduler must support scheduled future events, state-change events and recurring processes.

A factory does not need a 60 Hz update loop.

However, a factory whose operation depends on electricity, temperature or physical machinery must consume the corresponding authoritative state.

## D.7 W07 dependency — Networking

Replication must observe authoritative state.

It cannot dictate how frequently physics executes.

The scheduler must publish timestamped states independently of rendering and network transmission.

Client interpolation must use simulation timestamps, not assume that network packet arrival corresponds to world-time progression.

## D.8 W08 dependency — Environmental fidelity

Environmental models must publish valid time intervals for their outputs.

Higher-fidelity interaction models may need more frequent environmental sampling without forcing the global climate solver to run at that rate.

The transfer between global, regional, local and interaction models must preserve relevant physical quantities and declared error bounds.

## D.9 W09 dependency — Performance

W09's budget model must become a scheduler resource budget rather than a requirement that all domains complete every 30 Hz cycle.

A scheduler must be allowed to distribute work across different deadlines.

This is necessary to avoid a slow environmental solver delaying interactive physics.

# E. Alternatives

Three technically credible alternatives were evaluated.

## E.1 Alternative A — Global fixed-timestep scheduler

Not recommended

All authoritative domains execute on one global fixed timestep, with lower-frequency domains skipping selected ticks.

For example:

- Global tick: 60 Hz.
- Environment: every 60 ticks.
- Economy: every 600 ticks.
- Astronomy: every 3,600 ticks.

Advantages:

- Simple global ordering.
- Straightforward tick-based rollback.
- Relatively simple replication semantics.
- Easy to reason about discrete events.

Disadvantages:

- Unnecessary global coordination.
- Difficult integration of adaptive numerical solvers.
- Potential synchronization bottlenecks.
- Poor fit for offline scientific simulation.
- Global scheduling complexity increases with the number of domains.
- A delayed subsystem can become a bottleneck if the implementation requires synchronous completion.

Most importantly, this architecture would make the global tick an accidental dependency of subsystems that do not need it.

It is therefore incompatible with the full scope of Planetary Engine.

## E.2 Alternative B — Completely independent domain schedulers

Not recommended

Every domain owns its scheduler and executes independently.

Examples:

- Physics: 60 Hz.
- Atmosphere: 1 Hz.
- Orbital mechanics: adaptive.
- Economy: event-driven.
- Replication: 20 Hz.

Advantages:

- Maximum scheduling independence.
- Low coordination overhead.
- Natural support for specialized numerical solvers.
- Good potential for horizontal scaling.

Disadvantages:

- No inherent causal ordering.
- Difficult cross-domain interactions.
- Risk of consuming stale state.
- Difficult rollback and reproducibility.
- Potential inconsistency across distributed regions.
- Difficult management of accelerated simulation time.

This architecture solves the frequency conflict but creates a world-consistency problem.

Independent execution without coordinated temporal contracts is insufficient.

## E.3 Alternative C — Hierarchical Multirate Simulation Scheduler

Recommended

A shared logical timeline feeds a hierarchy of scheduling layers.

Each region manages execution resources and local dependencies.

Each domain owns its scheduling cadence and solver requirements.

Cross-domain communication occurs through versioned, timestamped state and explicitly defined synchronization boundaries.

Advantages:

- Preserves one coherent world.
- Avoids a global execution bottleneck.
- Supports specialized solvers.
- Supports adaptive fidelity.
- Supports distributed regions.
- Supports accelerated and offline execution.
- Allows event-driven gameplay.
- Allows independent replication and rendering.

Disadvantages:

- More complex scheduling architecture.
- Requires precise temporal contracts.
- Requires explicit stale-data policies.
- Requires careful management of cross-region interactions.
- Requires additional validation of numerical coupling.

This is the only alternative that adequately satisfies the combined requirements of W01–W09.

## E.4 Comparative evaluation

| Criterion                  | Global fixed tick                    | Independent schedulers  | Hierarchical multirate       |
| -------------------------- | ------------------------------------ | ----------------------- | ---------------------------- |
| Physical correctness       | Good if synchronized                 | Risky without contracts | Good with temporal contracts |
| Scalability                | Limited by global coordination       | High potential          | High potential               |
| Distributed execution      | Difficult                            | Good                    | Good                         |
| Interactive performance    | Predictable but potentially wasteful | Good                    | Good                         |
| Adaptive integration       | Awkward                              | Natural                 | Natural                      |
| Event-driven execution     | Limited                              | Natural                 | Natural                      |
| Causal consistency         | Relatively simple                    | Difficult               | Explicitly managed           |
| Offline simulation         | Limited                              | Good                    | Excellent                    |
| Extensibility              | Moderate                             | High                    | High                         |
| Architectural complexity   | Low initially                        | High                    | High                         |
| Compatibility with W01–W09 | Partial                              | Partial                 | Best                         |

Decision: Select Alternative C.

# F. Recommended architecture

## F.1 Architectural overview

WORLD TIME AUTHORITY

Canonical logical time · Timeline epoch · Time scaling

GLOBAL SCHEDULING COORDINATOR

Deadlines · Dependency graph · Budget allocation · Event dispatch

REGION A

Regional scheduler

Physics islands

Gameplay

Environment

REGION B

Regional scheduler

Physics islands

Gameplay

Environment

DOMAIN SCHEDULERS

Physics

Environment

Astronomy

Gameplay

Economy

AI

Independent execution cadence and domain-specific numerical methods

TEMPORAL STATE PUBLICATION

Timestamped results · State revisions · Events · Validity intervals

Replication

Network snapshots

Presentation

Unity interpolation

This diagram represents scheduling responsibility, not a mandatory physical deployment topology. The coordinator must not become a centralized process through which every physical operation passes.

## F.2 Ownership boundaries

| Component              | Owns                                            | Does not own                    |
| ---------------------- | ----------------------------------------------- | ------------------------------- |
| World time authority   | Canonical logical time contract                 | Domain execution rates          |
| Global coordinator     | Scheduling policy and cross-domain dependencies | Physical state                  |
| Region scheduler       | Local execution deadlines and budgets           | Global time semantics           |
| Physics scheduler      | Physical execution cadence                      | Environment model               |
| Physics solver         | Numerical integration and substeps              | World scheduling                |
| Environment scheduler  | Environmental model execution                   | Vehicle state                   |
| Astronomical scheduler | Orbital evaluation and propagation              | Interactive vehicle integration |
| Gameplay scheduler     | Gameplay processes and events                   | Physical authority              |
| Replication scheduler  | State transmission                              | Authoritative simulation        |
| Unity                  | Rendering and local prediction                  | Persistent world state          |

## F.3 Temporal contract

This is the most important proposed interface.

Every scheduled computation must have an explicit temporal context.

The following is an architectural contract, not an existing API.

```

interface SimulationJobContract {
    worldId: string;
    timelineEpoch: string;

    domainId: string;
    regionId?: string;
    authorityEpoch?: number;

    interval: {
        start: SimulationInstant;
        end: SimulationInstant;
    };

    executionPolicy: {
        mode:
            | "fixed"
            | "adaptive"
            | "event_driven"
            | "analytical";

        priority: number;
        fidelityLevel: number;
    };

    inputs: {
        sourceDomain: string;
        requiredRevision: number;
        sampleTime: SimulationInstant;
        consistency:
            | "exact"
            | "interpolated"
            | "bounded_stale";
    }[];

    output: {
        effectiveTime: SimulationInstant;
        stateRevision: number;
        validUntil?: SimulationInstant;
    };
}

```

`SimulationInstant` is deliberately an abstract type.

Its concrete representation must be agreed with W01/W04. The scheduler must not invent a competing time representation.

### Required temporal properties

1\. Timeline identity

A timestamp must belong to a particular world and timeline epoch.

A timestamp from an abandoned branch must not be interpreted as belonging to a restored branch.

2\. Interval semantics

Every integration job receives a defined interval:

\\[ [t\_{\mathrm{start}},t\_{\mathrm{end}}] \\]

The solver is responsible for determining its internal integration steps.

3\. Input validity

Every input must identify the state revision and time at which it is valid.

4\. Output validity

Every output must identify the effective simulation time and the revision at which it becomes available.

5\. No implicit time conversion

TAI, TT, TDB, UTC and simulation time must never be silently mixed.

## F.4 Fixed, adaptive and event-driven execution

Three primary execution modes are required.

| Mode         | Purpose                                           | Example            |
| ------------ | ------------------------------------------------- | ------------------ |
| Fixed        | Predictable recurring intervals                   | Vehicle physics    |
| Adaptive     | Variable numerical integration                    | Orbital mechanics  |
| Event-driven | Execution triggered by state changes or deadlines | Factory production |

Analytical propagation should also be supported as a specialized execution method, particularly for celestial objects whose motion can be evaluated without continuous numerical integration.

## F.5 Causal synchronization

Different execution frequencies are acceptable only if causal dependencies remain correct.

I recommend three consistency classes.

Class 1

Strict synchronization

Required for tightly coupled physical interactions.

Example: two colliding vehicles in one physics island.

Both states must be evaluated consistently over the same integration interval.

Class 2

Timestamped interpolation

Used for continuously evolving fields where interpolation is numerically acceptable.

Example: wind and atmospheric density sampled by a vehicle between environmental updates.

Interpolation must use valid temporal endpoints and a declared error bound.

Class 3

Bounded asynchronous consistency

Used for weakly coupled or aggregated systems.

Example: distant economic activity and strategic AI.

Stale state is acceptable only within an explicitly defined validity horizon.

### Causal ordering rules

1. A domain cannot consume a future state that has not been committed or explicitly predicted.
2. An event must carry its effective simulation timestamp.
3. Cross-domain events must have deterministic ordering rules.
4. A physical interaction cannot use an arbitrarily stale force or collision state.
5. Interpolation cannot silently extrapolate beyond its validated horizon.
6. When an interaction crosses an authority boundary, the relevant authorities must establish a consistent interaction interval.

A critical distinction: strict synchronization is required only for actual causal dependencies, not for the entire planet.

## F.6 Hierarchical scheduling

I recommend four scheduling layers.

Layer 1 — World coordination

Responsibilities:

- Consume canonical logical time.
- Apply pause and acceleration policies.
- Manage timeline epochs.
- Publish global scheduling constraints.
- Coordinate global events.

This layer must not synchronously execute all domain calculations.

Layer 2 — Regional scheduling

Responsibilities:

- Allocate computational budgets.
- Activate and deactivate simulation jobs.
- Manage regional deadlines.
- Coordinate local dependencies.
- Coordinate ownership transitions.

Regions can execute concurrently.

Layer 3 — Domain scheduling

Responsibilities:

- Physics cadence.
- Environment cadence.
- Gameplay cadence.
- AI cadence.
- Astronomical cadence.

Each domain chooses an execution policy compatible with its physical and logical requirements.

Layer 4 — Numerical solver execution

Responsibilities:

- Internal integration steps.
- Numerical error control.
- Collision substeps.
- Constraint solving.
- Solver-specific stability.

The solver may subdivide the interval requested by its domain scheduler.

## F.7 State handling and stale inputs

Every published state must include:

- World identity.
- Timeline epoch.
- Simulation timestamp.
- Source revision.
- Validity interval.
- Reference frame where applicable.
- Model version.
- Fidelity level.

The consumer evaluates whether the data is suitable for its operation.

Three possible responses:

1. Accept the state.
2. Interpolate or predict within a validated interval.
3. Delay or reject the dependent operation.

There must be no implicit fallback that silently accepts arbitrarily old data.

## F.8 Failure and recovery

The scheduler must distinguish between execution delay and logical-time failure.

If a subsystem misses its scheduled deadline:

| Situation                               | Required response                                                                        |
| --------------------------------------- | ---------------------------------------------------------------------------------------- |
| Non-critical environment update delayed | Continue with valid previous state within its permitted horizon                          |
| Critical physics job delayed            | Preserve causal state; do not silently skip integration                                  |
| Replication delayed                     | Drop obsolete intermediate snapshots where permitted                                     |
| Gameplay event delayed                  | Preserve event and execute according to its causal timestamp                             |
| Astronomical integration delayed        | Retain last valid state and prevent dependent consumers from using invalid extrapolation |
| Regional worker overloaded              | Reallocate work, reduce permitted fidelity or slow simulation progression                |
| Worker failure                          | Recover from checkpoint and replay authoritative events                                  |
| Authority migration                     | Fence old authority and establish a new authority epoch                                  |

An important rule:

A missed deadline must never silently become a skipped period of logical world time.

For real-time interactive operation, the runtime may have to slow the simulation relative to wall-clock time if critical computation cannot keep up.

For offline operation, it can simply take longer to complete the requested simulated interval.

## F.9 Computational budget allocation

W09's budget model is useful but should be extended.

A regional scheduler should manage:

- Available CPU time.
- Memory consumption.
- Active physics islands.
- Number of high-fidelity entities.
- Environmental solver demand.
- AI workload.
- Cross-region synchronization.
- Replication workload.
- Pending events.
- Deadline pressure.

Scheduling priority should consider:

1. Causal importance.
2. Physical stability.
3. Interaction proximity.
4. Fidelity requirements.
5. Deadline.
6. Computational cost.

A distant decorative object must not receive the same priority as a vehicle approaching a collision.

However, priority management must not allow starvation of low-priority processes.

## F.10 Fidelity and scheduling

Fidelity must influence both the physical model and its execution cadence.

It must not be reduced to frequency alone.

For example:

| Fidelity | Physical representation                    | Scheduling              |
| -------- | ------------------------------------------ | ----------------------- |
| High     | Full specialized solver                    | Frequent fixed/adaptive |
| Medium   | Reduced physical model                     | Lower frequency         |
| Low      | Aggregated physical model                  | Periodic/event-driven   |
| Dormant  | Stored state and analytical reconstruction | Event-driven/lazy       |

The transition between fidelity levels must preserve relevant physical quantities and state continuity.

This is consistent with W08's environmental hierarchy and W09's Active/Reduced/Dormant model.

## F.11 Time acceleration

The scheduler must operate on simulation-time intervals rather than assuming one simulation second equals one wall-clock second.

Let:

\\[ \frac{dt_s}{dt_w}=v(t) \\]

Where:

- \\(t_s\\) = simulation time.
- \\(t_w\\) = wall-clock time.
- \\(v(t)\\) = simulation speed multiplier.

If the simulation advances 1,000 seconds during one wall-clock second, that does not imply that every domain can execute one 1,000-second integration step.

The scheduler requests the larger interval.

The individual domain determines how to process it.

For example:

- A factory can calculate completed production cycles using scheduled events.
- A celestial solver can use adaptive numerical integration.
- A vehicle simulation may require many small integration steps.
- A climate solver may use a coarser numerical interval.

The requested interval and actual numerical integration steps must remain separate concepts.

Source: W04 §3.7, lines 557–577.

## F.12 Example schedules

The following are candidate initial configurations for architectural validation, not performance commitments.

### Example 1 — Vehicle physics

Interactive region

| Operation                         | Candidate frequency                |
| --------------------------------- | ---------------------------------- |
| Authoritative vehicle integration | 60 Hz                              |
| High-speed vehicle substeps       | 120–240 Hz                         |
| Drone control loop                | 250–1000 Hz                        |
| Collision and constraint solving  | Solver-dependent                   |
| Environment sampling              | At required integration timestamps |
| Replication                       | 20–30 Hz for nearby vehicles       |

A vehicle's suspension, tire dynamics, propulsion and aerodynamic forces are integrated according to their respective solver requirements.

The environment provider supplies time-consistent gravity, wind and atmospheric conditions.

The vehicle does not force the environmental solver to execute at 60 or 240 Hz.

### Example 2 — Planetary environment

Environmental hierarchy

The following frequencies are proposed starting parameters, not frequencies established by W08.

| Environmental layer            | Candidate update         |
| ------------------------------ | ------------------------ |
| Global climate                 | 0.1 Hz                   |
| Regional weather               | 1 Hz                     |
| Local atmospheric conditions   | 10 Hz                    |
| Interaction sampling           | On physics demand        |
| Terrain modification           | Event-driven             |
| Long-term geological evolution | Event-driven or adaptive |

The exact rates must depend on the physical model.

For example, local turbulence around an aircraft may need much higher temporal resolution than a global climate field.

The environmental solver should publish versioned field states with validity intervals.

### Example 3 — Astronomical orbital mechanics

Adaptive numerical solver

| Operation                        | Scheduling strategy              |
| -------------------------------- | -------------------------------- |
| Planetary analytical propagation | On-demand                        |
| General orbital propagation      | Adaptive                         |
| Close encounter                  | Increased integration resolution |
| Spacecraft trajectory            | Adaptive                         |
| Planetary rotation               | Evaluated at requested epoch     |
| Celestial state publication      | Independent configurable cadence |
| Scientific reconstruction        | Offline adaptive                 |

A planet's position should not need to be numerically recalculated 60 times every second merely because a player is driving a vehicle on its surface.

The astronomical subsystem must still be able to provide the appropriate state at the exact requested simulation instant.

### Example 4 — Gameplay logic

Gameplay scheduler

| Operation             | Candidate frequency |
| --------------------- | ------------------- |
| Character interaction | 30–60 Hz            |
| Tactical combat       | 10–30 Hz            |
| NPC decision-making   | 0.1–2 Hz            |
| Construction progress | 1–10 Hz             |
| Factory production    | Event-driven / 1 Hz |
| Regional economy      | 0.1–1 Hz            |
| Long-term economy     | 0.01–0.1 Hz         |

A production process should schedule completion events rather than repeatedly polling its entire state.

Physical machinery still requires appropriate physical integration.

### Example 5 — Replication and presentation

Independent presentation timeline

| Object                         | Candidate replication |
| ------------------------------ | --------------------- |
| Local player's predicted state | 30–60 Hz              |
| Nearby vehicles                | 20–30 Hz              |
| Nearby NPCs                    | 10–20 Hz              |
| Distant NPCs                   | 1–5 Hz                |
| Environmental state            | 0.1–2 Hz              |
| Economic aggregates            | Event-driven          |

Unity rendering may run at 60, 120 or 144 FPS independently of server simulation.

The client interpolates authoritative snapshots and uses local prediction where appropriate.

A 144 FPS client must not demand 144 authoritative physics updates.

Sources: W07 §8, W09 §6.3, W05 §4.2.

# G. Cross-system impact

## G.1 Documents remaining compatible

The following proposals are fundamentally compatible with HMSS:

| Document | Existing proposal                                     | Impact   |
| -------- | ----------------------------------------------------- | -------- |
| W01      | Shared world-time and environment queries             | Preserve |
| W03      | Specialized physical solvers and internal frequencies | Preserve |
| W04      | Adaptive orbital mechanics and analytical propagation | Preserve |
| W06      | Event-driven and periodic gameplay                    | Preserve |
| W07      | Independent client prediction and interpolation       | Preserve |
| W08      | Hierarchical multi-fidelity environment               | Preserve |

None requires a fundamental architectural redesign.

## G.2 Documents requiring clarification

W02 — Physics runtime

Clarify the meaning of authoritative tick.

It should distinguish:

- Interactive simulation interval.
- Physics integration interval.
- Physics substeps.
- Region scheduling cadence.

W02's lifecycle remains valid, but should not be interpreted as a mandatory global sequence executed by every domain.

W05 — Distributed runtime

Clarify the difference between:

- Region simulation tick.
- Region execution sequence.
- Logical world time.
- Physics-island integration interval.

W05's proposed region-level 60 Hz budget must not become a global synchronization requirement.

W09 — Performance architecture

Clarify PERF-004.

The proposed 30 Hz profile should be treated as a provisional regional coordination/workload-planning profile, not as a universal simulation frequency.

PERF-011, Region-local clocks, also needs clarification.

A region may have its own scheduling cursor and local sequence counter, but it must not develop an independently advancing logical world timeline.

## G.3 Required architectural changes

I recommend the following changes to the master architecture.

| Document | Required change                                                                   | Priority |
| -------- | --------------------------------------------------------------------------------- | -------- |
| W01      | Publish canonical time and environment temporal contracts                         | Critical |
| W02      | Define distinction between authoritative interval and solver timestep             | Critical |
| W04      | Define interface between adaptive solvers and shared logical time                 | Critical |
| W05      | Formalize hierarchical regional scheduling and cross-region synchronization       | Critical |
| W08      | Define temporal validity and interpolation contracts for environmental fields     | High     |
| W09      | Replace ambiguous universal tick assumptions with explicit scheduling layers      | Critical |
| W07      | Ensure replication supports independent publication cadence and simulation epochs | High     |
| W06      | Define event execution timestamps and causal dependencies                         | High     |
| W03      | Publish solver-specific integration and temporal coupling requirements            | High     |

## G.4 Decisions requiring coordination

The following cannot be finalized by GPT-06 alone.

| Decision                                   | Owner       | Required coordination |
| ------------------------------------------ | ----------- | --------------------- |
| Canonical SimulationInstant representation | W01/W04     | GPT-06                |
| Physical interaction boundaries            | W02/W03/W05 | GPT-06                |
| Region time and authority semantics        | W05/W09     | GPT-06                |
| Environment validity intervals             | W01/W08     | GPT-06                |
| Entity fidelity transition contract        | W06/W09     | GPT-06                |
| Replication timestamps and interpolation   | W07/W05     | GPT-06                |

The most important unresolved dependency is the canonical logical time representation.

GPT-06 must consume it, not replace it.

# H. Required architectural decisions

The following ADRs are proposed for central architecture integration.

## ADR-SCHED-001 — No universal execution frequency

Recommended

Decision: Establish one logical world timeline, with independent execution frequencies for simulation domains.

Reason: Different physical and computational systems have fundamentally different temporal requirements.

Consequences: All domains must implement temporal contracts.

## ADR-SCHED-002 — Hierarchical scheduler

Decision: Use global coordination, regional scheduling, domain scheduling and solver-level integration.

Reason: This separates global consistency from local execution.

Consequences: Requires explicit scheduling ownership and dependency management.

## ADR-SCHED-003 — Region-local clocks are scheduling cursors

Decision: Regions may maintain local execution counters and scheduling cursors, but all authoritative state must map to the canonical logical world timeline.

Reason: Independent logical clocks would create inconsistent world time.

Consequences: W09 PERF-011 requires clarification.

## ADR-SCHED-004 — Independent domain execution modes

Decision: Support fixed, adaptive, analytical and event-driven execution.

Reason: One execution strategy cannot satisfy all simulation domains.

Consequences: Domain interfaces must expose execution requirements.

## ADR-SCHED-005 — Explicit temporal consistency classes

Decision: Introduce strict synchronization, timestamped interpolation and bounded asynchronous consistency.

Reason: Different dependencies have different causal requirements.

Consequences: All cross-domain inputs must declare consistency requirements.

## ADR-SCHED-006 — Solver ownership of numerical integration

Decision: The scheduler defines requested intervals; the numerical solver determines internal integration steps.

Reason: Prevents the global scheduler from becoming a universal numerical integrator.

Consequences: W02, W03 and W04 must expose solver-compatible interfaces.

## ADR-SCHED-007 — Independent replication and presentation

Decision: Replication and rendering frequencies must not dictate authoritative simulation frequency.

Reason: Network and rendering workloads have different requirements.

Consequences: W07 must preserve timestamped snapshots and prediction semantics.

## ADR-SCHED-008 — Adaptive fidelity must be temporally consistent

Decision: Fidelity changes must preserve relevant physical state and causal continuity.

Reason: Lower simulation frequency cannot justify arbitrary changes in physical behavior.

Consequences: Requires validated transition rules and error bounds.

## ADR-SCHED-009 — Explicit overload behavior

Decision: Missed deadlines must produce explicit scheduler responses rather than silent logical-time advancement.

Reason: Prevents hidden physical inconsistencies.

Consequences: Runtime must support workload reduction, delayed non-critical jobs and simulation slowdown.

## ADR-SCHED-010 — Offline execution is a first-class mode

Decision: Offline simulation must support execution independent of wall-clock pacing.

Reason: Scientific computation, historical reconstruction and accelerated what-if scenarios cannot depend on real-time rendering cadence.

Consequences: Simulation domains must accept arbitrary requested intervals subject to their numerical stability constraints.

## ADR-SCHED-011 — 30 Hz and 60 Hz are profiles, not global laws

Decision: Preserve both recommendations as candidate workload profiles with different meanings.

- 60 Hz: initial interactive physics target.
- 30 Hz: candidate regional coordination and performance-planning cadence.

Reason: Neither should become a universal execution restriction.

Consequences: W09 PERF-004 and W02 PHY-05 must be coordinated.

# I. Validation plan

No performance results are claimed.

The following tests are required before freezing the scheduler architecture.

## I.1 Temporal consistency tests

| Test                                   | Expected invariant                             |
| -------------------------------------- | ---------------------------------------------- |
| Two domains execute at different rates | Both reference the same logical time           |
| Environment updates at lower frequency | Valid interpolation is possible                |
| Delayed environmental update           | No invalid future state is consumed            |
| Event arrives late                     | Event retains its original effective timestamp |
| Timeline restored                      | Previous epoch cannot overwrite new timeline   |
| Region migrates                        | Logical state continuity is preserved          |

## I.2 Multirate physical simulation tests

Construct a shared world containing:

- One vehicle.
- One aircraft.
- One drone.
- One spacecraft.
- One rotating planet.
- A changing atmosphere.

Execute the scenario under several frequency configurations.

Measure:

- Position divergence.
- Velocity divergence.
- Energy conservation where applicable.
- Collision consistency.
- Environmental interpolation error.
- Numerical integration error.
- Cross-domain timing discrepancies.

The same scenario should be executed with different scheduling configurations.

The resulting state differences must remain within explicitly declared model tolerances.

## I.3 Distributed execution tests

Test:

1. Two regions executing concurrently.
2. An entity crossing a region boundary.
3. A physics island spanning a regional boundary.
4. Region authority migration.
5. Delayed inter-region communication.
6. Worker overload.
7. Worker restart.
8. Recovery from checkpoint.

Critical invariant:

A regional execution delay must not silently alter the logical time or authoritative state of another region.

## I.4 Accelerated-time tests

Run the same scenario at:

- 1×.
- 10×.
- 100×.
- 1,000×.

Measure the difference between:

- Fixed-step physical integration.
- Adaptive numerical integration.
- Analytical propagation.
- Event-driven execution.

Expected result:

Increasing simulation speed must not cause numerical integration intervals to exceed the declared stability requirements.

## I.5 Fidelity-transition tests

Test transitions:

- Active → Reduced.
- Reduced → Active.
- Active → Dormant.
- Dormant → Active.

Verify:

- Momentum continuity.
- Position continuity.
- Relevant energy consistency.
- Entity identity.
- Event history.
- Physical interaction validity.

The acceptable error must be defined per model.

## I.6 Scheduler performance tests

Measure:

| Metric                            | Purpose               |
| --------------------------------- | --------------------- |
| p50/p95/p99 execution latency     | Scheduling efficiency |
| Deadline miss rate                | Real-time reliability |
| Queue depth                       | Backlog detection     |
| CPU time per domain               | Workload distribution |
| Cross-region synchronization time | Distributed overhead  |
| State publication delay           | Temporal consistency  |
| Stale input rejection rate        | Contract quality      |
| Fidelity transitions per second   | Adaptive workload     |
| Memory per active job             | Scalability           |
| Simulation-time advancement rate  | Offline throughput    |

Use progressively larger synthetic scenarios.

Do not interpret the proposed W09 workload budget as a benchmark result.

## I.7 Acceptance criteria

The architecture should not be approved for implementation until these invariants hold:

1. No domain owns an independent authoritative world timeline.
2. No global barrier is required for unrelated regions.
3. All cross-domain state has explicit temporal validity.
4. Fast physical interactions cannot consume arbitrarily stale inputs.
5. Adaptive solvers can process intervals independently of scheduler cadence.
6. Replication cannot modify authoritative physical state.
7. Offline simulation can progress without rendering or networking.
8. Fidelity transitions have measurable error bounds.
9. Scheduler overload has explicit behavior.
10. Recovery preserves timeline and authority identity.

# J. Unresolved questions

These questions must remain visible in the master architecture.

## J.1 Canonical time representation

Owner: W01/W04.

Will SimulationInstant use a two-part integer representation, fixed-point SI time, or another canonical representation?

The scheduler cannot settle this independently.

## J.2 Global versus regional time progression

Owner: W04/W05/W09.

Can different regions advance at different logical-time positions while remaining part of one persistent world?

My recommendation is:

- A shared logical timeline.
- Regional execution cursors.
- No requirement for identical wall-clock progress.
- Explicit causal synchronization at interaction boundaries.

However, the exact semantics of temporarily divergent regional simulation progress require agreement with W04 and W05.

## J.3 Cross-region physics

Owner: W02/W03/W05.

How are physics islands handled when their interactions cross region ownership boundaries?

Possible approaches:

- Shared authoritative physics worker.
- Temporary interaction ownership.
- Coordinated distributed integration.

This requires a separate distributed-physics investigation.

## J.4 Environmental interpolation

Owner: W01/W08.

Which interpolation methods are permitted for:

- Gravity.
- Atmospheric density.
- Wind.
- Ocean currents.
- Surface temperature.
- Local turbulence.

The answer must be model-specific.

## J.5 Determinism

Owner: W02/W03/W05/W09.

Which domains require bitwise determinism?

The architecture should require reproducibility, but bitwise-identical results across hardware should not be assumed universally.

## J.6 Event ordering

Owner: W05/W06.

How are simultaneous events ordered across multiple regions?

A timestamp alone is insufficient when two events occur at the same logical instant.

A deterministic ordering mechanism and authority ownership rule are required.

## J.7 Offline simulation and distributed execution

Owner: W04/W05/W09.

Can a large offline simulation be distributed across independent computational nodes while preserving reproducibility and cross-domain causality?

This deserves dedicated research.

## J.8 Global computational budgets

Owner: W05/W09.

How should global workload allocation interact with regional CPU limits?

A central coordinator should allocate policy and resources without becoming a per-operation bottleneck.

## J.9 Time reversal

Owner: W04/W06.

Which domains support reverse integration?

Conservative orbital mechanics and dissipative atmospheric or economic processes have different reversibility properties.

Restoring an earlier world state must not be confused with mathematically reversing every subsystem.

# K. Final assessment

C-06 — Multirate Simulation Scheduler

Resolved conditionally

# Conditionally resolved

The recommended scheduling architecture is sufficiently defined for integration into the Planetary Engine master architecture, but several foundational contracts must be agreed before implementation.

## K.1 What has been resolved

The following architectural conclusions are sufficiently established:

1. One shared logical world timeline is required.
2. A universal execution frequency is not required.
3. Scheduling must be hierarchical.
4. Regions must not become mandatory global synchronization boundaries.
5. Physics solvers own numerical integration and substeps.
6. Environmental systems can execute at lower frequencies while supporting higher-frequency physical queries.
7. Astronomical mechanics require adaptive and analytical execution.
8. Gameplay must support event-driven execution.
9. Replication and rendering must remain independent of authoritative simulation frequency.
10. Offline execution must support accelerated simulation without compromising numerical stability.

## K.2 What remains conditional

Three dependencies prevent unconditional resolution.

1\. Canonical time contract

W01 and W04 must finalize the representation and semantics of SimulationInstant.

2\. Distributed execution contract

W05 and W09 must finalize regional execution cursors, ownership and cross-region synchronization.

3\. Physical coupling contract

W02 and W03 must define how physics integration intervals interact with environmental sampling and distributed physics islands.

These are dependencies, not reasons to redesign the scheduler.

## K.3 Recommendation to the central architecture integrator

Accept the hierarchical multirate architecture as the scheduling baseline.

Do not accept a universal 30 Hz or 60 Hz simulation loop.

Preserve 60 Hz as an initial interactive physics target and 30 Hz as a candidate regional coordination profile.

Require all workers to use the shared temporal contracts.

The most important architectural principle is:

> Planetary Engine must synchronize causality, not synchronize every computation.

A planet, an aircraft, a vehicle, an economy and a galaxy can evolve at radically different computational frequencies while remaining part of one persistent physical universe.

That is the foundation required for Planetary Engine to scale from interactive simulation to planetary and astronomical what-if computation.

## Evidence register

The following repository documents were inspected for cross-system dependencies and scheduling implications.

| Document                                                                              | Relevant evidence           |
| ------------------------------------------------------------------------------------- | --------------------------- |
| [W01](https://github.com/projectzonio/what_if/blob/main/w1.md) | §2.8, lines 326–334; §4.1   |
| [W02](https://github.com/projectzonio/what_if/blob/main/w2.md) | §7.1–7.2, lines 619–674     |
| [W03](https://github.com/projectzonio/what_if/blob/main/w3.md) | §6.3, lines 1537–1559       |
| [W04](https://github.com/projectzonio/what_if/blob/main/w4.md) | §3.7, lines 519–577         |
| [W05](https://github.com/projectzonio/what_if/blob/main/w5.md) | §2.3, lines 283–310; §4     |
| [W06](https://github.com/projectzonio/what_if/blob/main/w6.md) | §12.2–12.3, lines 1483–1516 |
| [W07](https://github.com/projectzonio/what_if/blob/main/w7.md) | §8, lines 1041–1117         |
| [W08](https://github.com/projectzonio/what_if/blob/main/w8.md) | §1, lines 170–180; §10      |
| [W09](https://github.com/projectzonio/what_if/blob/main/w9.md) | §2.2, lines 221–238; §13–14 |

Research boundary: No implementation, production code, or repository modifications were performed. Numerical frequencies and performance budgets remain proposals pending benchmarking.

Handoff to central architecture integrator

Recommended integration order:

1. Accept ADR-SCHED-001 through ADR-SCHED-011 provisionally.
2. Coordinate SimulationInstant with W01/W04.
3. Coordinate region scheduling with W05/W09.
4. Coordinate physics coupling with W02/W03.
5. Update the master architecture's temporal contracts.
6. Validate the complete architecture before implementation.

Final architectural position: HMSS should become the common scheduling architecture for Planetary Engine, not another independent subsystem competing with the existing worker proposals.
