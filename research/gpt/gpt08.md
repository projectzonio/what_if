Repozitář `projectzonio/what_if` je skutečně veřejně přístupný. Ověřil jsem přístup a otevřel všech devět dokumentů `w1.md` až `w9.md`.

[Otevřít Planetary Engine – GitHub](https://github.com/projectzonio/what_if?utm_source=chatgpt.com)

Začínám tedy vlastní architektonickou analýzu GPT-08 nad společným návrhem, nikoliv pouze nad W08.

První důležitý poznatek z W01: prostorový LOD a fyzikální LOD musí být nezávislé. Dokument to výslovně řeší v části 3.3, řádky 394–411. To je důležitý základ, ale samo o sobě to ještě neřeší přechod mezi dvěma fyzikálními modely, zachování stavu ani distribuovanou synchronizaci.

# PLANETARY ENGINE

## GPT-08 — Simulation Fidelity Transitions

Architecture Resolution · C-08 · Principal Systems Architect

Architecture resolved conditionally

Research only

W01–W09 reviewed

Repository: [projectzonio/what_if](https://github.com/projectzonio/what_if?utm_source=chatgpt.com)\
Assigned problem: C-08 — Inconsistent fidelity and model transitions\
Scope: Unified entity and environmental simulation fidelity architecture.

# A. Executive decision

I recommend introducing a common Simulation Fidelity Transition Framework (SFTF) into the authoritative Rust simulation runtime.

The framework must coordinate transitions between computational models without requiring all simulation domains to use the same numerical algorithms or internal representations.

The central architectural decision is:

> Simulation fidelity is a multidimensional computational property of a world representation, not a single global LOD value. Transitions between representations are explicit, versioned, domain-aware state transformations governed by shared temporal, ownership and conservation contracts.

The recommended architecture has five fundamental properties.

1. Independent fidelity dimensions. Spatial, physical, temporal and environmental fidelity are independently configurable, subject to compatibility constraints.
2. Canonical state continuity. Every model must expose a domain-defined mapping to and from a shared authoritative state contract.
3. Domain-specific transformations. A vehicle, atmosphere, ocean and planetary orbit do not use the same state conversion algorithm.
4. Conservation-aware transitions. Relevant physical quantities must be preserved or their exchange explicitly accounted for.
5. Ownership-safe execution. A transition cannot create two authoritative simulations of the same state or bypass region ownership.

The framework is not a global simulation manager that calculates everything. It is a coordination and validation layer over specialized simulation domains.

## Fundamental distinction

Three operations must not be confused:

| Operation                 | Meaning                                                                                 |
| ------------------------- | --------------------------------------------------------------------------------------- |
| Representation change     | Same physical model, different spatial or visual representation                         |
| Fidelity transition       | Change in computational model or approximation                                          |
| Physical state transition | Actual change in the world, such as collision, combustion, destruction or mass transfer |

Only the second operation is the primary responsibility of C-08.

The first belongs primarily to spatial and presentation systems. The third belongs to the physical or environmental domain that owns the relevant phenomenon.

# B. Existing architecture

## B1. Repository-wide findings

All nine worker documents were reviewed. The following is a synthesis of their architectural positions, not a claim that every proposal has already been accepted as a central decision.

| Worker | Architectural responsibility                   | Relevance to C-08                                              |
| ------ | ---------------------------------------------- | -------------------------------------------------------------- |
| W01    | Universal spatial and environmental foundation | Spatial LOD, coordinate transformations, environment providers |
| W02    | Universal physics integration                  | Numerical integration, timesteps, physical state               |
| W03    | Specialized physical entities                  | Vehicle, aircraft, spacecraft and character fidelity           |
| W04    | Astronomy and orbital simulation               | Analytical-to-numerical solver transitions                     |
| W05    | Distributed simulation and ownership           | Region migration and authoritative ownership                   |
| W06    | Gameplay, world history and orchestration      | Importance, branching, event history                           |
| W07    | Unity presentation and networking              | Separation of rendering and simulation fidelity                |
| W08    | Scientific and environmental simulation        | Hierarchical environmental models and conservation             |
| W09    | Performance and scalability                    | Entity fidelity tiers, scheduler and transition management     |

## B2. W01 — Spatial fidelity

W01, section 3.3, lines 394–411:

The document explicitly separates terrain rendering LOD from physics LOD.

A distant terrain tile can have low visual resolution while retaining a different collision representation.

This is correct and must be preserved.

W01 also proposes replaceable environmental providers, allowing different physical fidelity levels without changing the entities consuming environmental information.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: Spatial representation and computational physical fidelity must not be coupled through a single LOD variable.

## B3. W02 — Universal physics

W02, section 3.1, lines 258–268, recommends fixed timesteps for authoritative interactive physics and separates rendering frequency from physics frequency.

It also discusses adaptive integration, particularly for numerical difficulty and orbital calculations.

This establishes an important distinction:

- Simulation frequency is not rendering frequency.
- Numerical integration step size is not necessarily the same as authoritative world tick frequency.
- Changing the solver must not silently change the meaning of simulation time.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: Fidelity transitions must be compatible with multiple numerical integration strategies.

## B4. W03 — Specialized physical models

W03, section 10.4, lines 1959–1980, proposes five simulation tiers:

- Tier 0: static or analytical approximation.
- Tier 1: simplified physical behavior.
- Tier 2: standard entity dynamics.
- Tier 3: high-fidelity subsystem dynamics.
- Tier 4: scientific or engineering simulation.

It explicitly requires preserving physical state and preventing unexplained impulses during transitions.

W03 also identifies an unresolved decision: common external fidelity tiers versus subsystem-specific levels.

Its recommendation is common external tiers with subsystem-specific internal implementations.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: The common-tier concept is useful, but must not become a universal model taxonomy that forces incompatible simulation domains into identical levels.

## B5. W04 — Astronomical solver transitions

W04, section 8, lines 790–803, provides a direct example of C-08.

A planet can initially follow a Keplerian orbit. When a spacecraft approaches, its motion can be promoted to numerical N-body simulation.

The transition must preserve position and velocity, while considering acceleration consistency and conservation.

W04 explicitly recommends state-transfer operations with measurable error budgets.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: This is a model transition, not a visual LOD operation.

## B6. W05 — Distributed ownership

W05, section 2.6, lines 376–417, defines a region ownership model with:

- Region identity.
- Owner process.
- Monotonically increasing ownership epoch.
- Lease.
- Simulation tick.
- Versioned region state.
- Explicit migration stages.

The old owner must not continue authoritative simulation after ownership changes.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: Fidelity transitions must be subordinate to authoritative ownership. A model cannot independently claim authority merely because it has been activated.

## B7. W06 — History and determinism

W06, sections 7.2–7.6 and 8, provides entity authority, ordered commands, several levels of determinism and a world-history model supporting branching and What-If simulation.

It explicitly rejects assuming global deterministic lockstep across all simulation domains.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



Assessment: Transitions must be recorded as authoritative, replayable operations, without requiring bitwise deterministic execution everywhere.

## B8. W07 — Presentation separation

W07, sections 19–20, lines 1824–1876, explicitly states that rendering quality is independent of simulation fidelity.

It proposes three representations:

- Authoritative state.
- Presentation state.
- Rendering state.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



Assessment: Unity must not trigger authoritative physical transitions by changing its visual LOD or camera representation.

## B9. W08 — Environmental fidelity

W08, section 1, lines 170–180, proposes four environmental layers:

1. Global Environment.
2. Regional Environment.
3. Local Environment.
4. Interaction Environment.

Crucially, these must not become four disconnected simulations.

Section 12.2, lines 672–682, proposes explicit mass, momentum, energy and water conservation budgets.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: Environmental transitions require explicit coupling and conservation, not merely interpolation between two resolutions.

## B10. W09 — Entity fidelity

W09, section 3, lines 254–313, proposes four operational modes:

| Mode | Representation                  |
| ---- | ------------------------------- |
| L0   | Full simulation                 |
| L1   | Reduced simulation              |
| L2   | Abstract/statistical simulation |
| L3   | Dormant/persistent              |

The document correctly states that these are representations of the same logical entity.

It also makes two particularly important recommendations:

- Fidelity decisions depend on distance, importance, interaction, available capacity and gameplay context.
- Conversion between fidelity levels is domain-specific, not a universal generic conversion of physical components.

Reference:&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Assessment: W09 provides the strongest starting point for the common entity transition architecture.

# C. Problem analysis

## C1. The central architectural problem

The existing documents describe several kinds of fidelity independently.

They do not yet define one common contract that explains how those fidelity systems interact.

This creates five major risks.

1\. Fidelity dimensions are conflated.

A low-resolution terrain representation does not necessarily mean that physics should be simplified.

Likewise, a high-frequency vehicle simulation does not imply that its surrounding atmosphere needs a high-resolution atmospheric solver.

2\. State conversion is underspecified.

A shared canonical state is mentioned in W09, but there is no universal definition of which quantities are mandatory, which are domain-specific and which are irreversibly lost during abstraction.

3\. Conservation is not unified.

W08 identifies conservation budgets, while W03 and W04 identify physical state continuity.

No common transition-level accounting contract connects these requirements.

4\. Ownership and fidelity are separate proposals.

W05 defines ownership migration, while W09 defines fidelity transitions.

Neither fully specifies how a fidelity transition is coordinated when the entity or environmental region changes ownership.

5\. Transition timing is undefined.

The documents discuss fixed timesteps, multiple simulation frequencies, analytical propagation and environmental hierarchies.

They do not establish whether transitions are instantaneous, gradual, overlapping or performed at a synchronization boundary.

These are not merely implementation details. They affect the meaning of the persistent world state.

## C2. What constitutes a fidelity level?

A fidelity level describes a computational representation's ability to reproduce a defined set of physical or logical phenomena within an explicit error budget.

It should describe:

- Which physical phenomena are represented.
- Which state variables are maintained.
- Which numerical model is used.
- Which interactions are supported.
- Spatial resolution.
- Temporal resolution and integration behavior.
- Accuracy and uncertainty.
- Computational cost.

A fidelity level is therefore a capability profile, not simply a number.

For example, two vehicle models can both be Tier 2 while using different tire models.

Similarly, a regional atmospheric solver and an ocean solver can have different numerical resolutions while participating in the same environmental hierarchy.

## C3. Independent fidelity dimensions

I recommend the following dimensions.

1\. Spatial fidelity

How accurately the world is spatially represented.

- Global geometry.
- Regional geometry.
- Local geometry.
- Fine collision geometry.

2\. Physical fidelity

How accurately physical phenomena and interactions are modeled.

- Analytical approximation.
- Simplified dynamics.
- Full entity dynamics.
- Specialized scientific solvers.

3\. Temporal fidelity

How frequently and accurately state evolves.

- Analytical propagation.
- Low-frequency updates.
- Standard simulation stepping.
- High-frequency internal integration.

4\. Environmental fidelity

How accurately surrounding physical fields are resolved.

- Global environmental fields.
- Regional environmental simulation.
- Local detailed simulation.
- Interaction-level environmental coupling.

5\. Interaction fidelity

How accurately relationships between entities are represented.

- Statistical aggregation.
- Approximate interactions.
- Explicit collisions and constraints.
- Detailed coupled physical interaction.

These dimensions are independently selectable, but not universally independent in their physical requirements.

For example, high-fidelity tire contact requires sufficiently detailed terrain collision data. A local turbulence solver requires suitable atmospheric boundary conditions.

The architecture therefore needs a capability compatibility check, not a global fidelity switch.

## C4. Transition triggers

Distance must not be the only transition trigger.

The existing W09 decision function uses distance, importance, interaction, available capacity and gameplay context.

I recommend expanding this into a policy based on:

- Predicted interaction.
- Physical instability or model validity.
- Required accuracy.
- Environmental conditions.
- Player or gameplay importance.
- Computational budget.
- Ownership availability.
- Transition cost.
- Prediction horizon.

The trigger must support both promotion and demotion.

Importantly, a projectile, spacecraft or meteorological event can remain high-fidelity despite being geographically distant.

## C5. State loss

A statistical model cannot reconstruct every detail of the high-fidelity model.

For example, a vehicle represented by a traffic-flow model may retain:

- Position or network segment.
- Average speed.
- Fuel.
- Damage.
- Occupants.
- Ownership.
- Destination.

But it may not retain:

- Individual wheel angular velocities.
- Suspension displacement.
- Tire temperature distribution.
- Detailed contact forces.

The target model must not invent this missing state silently.

It must declare its reconstruction policy and resulting uncertainty.

# D. Cross-system dependencies

| Worker | What C-08 requires                                                     | What the worker requires from C-08                             | Compatibility                                     |
| ------ | ---------------------------------------------------------------------- | -------------------------------------------------------------- | ------------------------------------------------- |
| W01    | Stable coordinates, spatial addressing and environment queries         | Fidelity-independent spatial representation                    | Compatible; clarify metadata                      |
| W02    | Canonical physical state, integration boundaries and solver interfaces | Safe model switching without introducing impulses              | Compatible; contract needed                       |
| W03    | Domain-specific entity transformation adapters                         | Common transition lifecycle and physical continuity            | Compatible; detailed adapter specification needed |
| W04    | Time scales, reference frames and orbital state                        | Position, velocity and conservation continuity between solvers | Compatible; explicit solver handoff needed        |
| W05    | Ownership epochs, checkpoint and migration protocol                    | No overlapping authoritative transitions during migration      | Compatible; joint state machine needed            |
| W06    | Event history, simulation time and branching                           | Replayable and branch-aware transition events                  | Compatible; event contract needed                 |
| W07    | Authoritative world state and versioned replication                    | Stable presentation independent of model changes               | Compatible; replication semantics needed          |
| W08    | Environmental state, field metadata and conservation budgets           | Conservative transfer between environmental resolutions        | Compatible; major contract expansion needed       |
| W09    | Scheduler, capacity information and entity lifecycle                   | Transition scheduling and measurable costs                     | Compatible; central coordination needed           |

## D1. W01 dependency — Spatial foundation

The transition framework must use W01's reference-frame transformations.

A change from orbital to planetary simulation cannot be defined by transferring position alone.

Velocity must be transformed with the correct time-dependent reference-frame semantics.

The canonical state must therefore carry its reference frame and epoch.

Required clarification: a fidelity transition must not be confused with floating-origin rebasing or terrain tile replacement.

## D2. W02 dependency — Physics integration

The physics runtime must own the actual application of physical forces and state integration.

The transition framework must not introduce a second physics authority.

Required contract:

- Solver source.
- Solver target.
- State at a common simulation epoch.
- Valid integration boundary.
- Mass and inertia properties.
- Contact and constraint handling.
- Numerical error budget.

The source solver must not continue integrating authoritative state after transition commit.

## D3. W03 dependency — Physical entity models

Each specialized entity type needs its own transformation adapter.

For example, a vehicle's reduced model must be able to reconstruct a valid detailed vehicle model.

A spacecraft's orbital propagation adapter needs a different transformation.

The framework provides lifecycle and validation; W03 owns the physical interpretation.

## D4. W04 dependency — Astronomy

Astronomical propagation presents a special problem.

Analytical and numerical solvers may describe different approximations of the same physical system.

The transition must preserve the state at a common epoch, while accounting for differences in acceleration and gravitational interactions.

A direct position/velocity copy is not always enough to initialize the target solver correctly.

## D5. W05 dependency — Distributed ownership

The fidelity transition and ownership migration must share an explicit synchronization protocol.

A region migration cannot serialize an entity while its state transformation is incomplete.

The target owner must receive:

- Source model identity and version.
- Target model identity and version.
- Transition status.
- Last committed canonical state.
- Transition event sequence.
- Relevant solver configuration.
- Conservation ledger.
- Pending interactions.

Ownership epoch fencing must remain authoritative.

## D6. W06 dependency — World history

Every committed transition must be part of the authoritative world history.

A What-If branch must be able to replay the transition using the model version, configuration, input state and environmental revision.

A branch may choose a different fidelity policy, but must not mutate its parent world.

## D7. W07 dependency — Unity

Unity must receive the authoritative result through its presentation layer.

A vehicle switching from reduced to full physics must not be recreated as a new entity from the client's perspective.

Entity identity remains stable.

Rendering can change independently.

## D8. W08 dependency — Environment

Environmental transitions require conservative coupling between models.

For example, when a regional atmospheric model becomes active, it cannot simply initialize from a coarse global temperature field without considering the represented energy, mass and water exchanges.

This is a major part of the C-08 solution.

## D9. W09 dependency — Performance

The fidelity manager must not scan the entire universe at every simulation tick.

It must work with:

- Active transition candidates.
- Region-level scheduling.
- Importance and interaction events.
- Available compute budgets.
- Predicted future demand.

Transition overhead itself must be measured.

# E. Alternatives

Three technically credible architectures were considered.

## Alternative A — Independent LOD managers

Each subsystem manages its own fidelity transitions.

Examples:

- Terrain manager.
- Vehicle physics manager.
- Atmospheric LOD manager.
- Orbital solver manager.

Not recommended as the complete architecture

Advantages:

- Simple local ownership.
- Specialized optimization.
- Low initial integration complexity.
- Natural fit for independent solvers.

Disadvantages:

- No unified transition lifecycle.
- Different subsystems may disagree about the authoritative state.
- Conservation between environmental levels becomes difficult.
- Distributed ownership coordination is inconsistent.
- Replay and rollback become domain-specific and potentially incompatible.

This design is appropriate for local selection and scheduling, but insufficient as the global transition contract.

## Alternative B — One global fidelity manager

A central system decides every fidelity level and directly coordinates every simulation transition.

Not recommended

Advantages:

- Centralized policy.
- Uniform monitoring.
- Simple global visibility.
- Easier initial conceptual model.

Disadvantages:

- Potential global bottleneck.
- Unnecessary synchronization.
- Central manager becomes coupled to every simulation domain.
- Difficult extension to future solvers.
- Risk of centralizing physical authority.
- Poor fit for independent regional simulation clocks.

This violates the principle of avoiding unnecessary global synchronization.

## Alternative C — Hierarchical distributed transition framework

A common transition protocol coordinates specialized domain-level adapters.

The central runtime establishes:

- Transition identity.
- Lifecycle.
- Authoritative state.
- Temporal boundary.
- Ownership.
- Validation.
- Error budget.
- Commit and recovery.

The domain-specific adapters perform the actual transformations.

Recommended

Advantages:

- Common architecture without common numerical solvers.
- Preserves domain ownership.
- Supports distributed execution.
- Allows environmental conservation contracts.
- Supports replay and recovery.
- Scales to new simulation domains.
- Avoids global synchronization.

Disadvantages:

- More demanding interface design.
- Requires explicit state schemas.
- Requires domain-specific error budgets.
- Cross-domain transitions need additional coordination.

## E1. Comparison

| Criterion                  | A: Independent | B: Global manager | C: Distributed framework     |
| -------------------------- | -------------- | ----------------- | ---------------------------- |
| Correctness                | Medium         | High potential    | High                         |
| Scalability                | High locally   | Low–medium        | High                         |
| Distributed execution      | Difficult      | Centralized       | Native                       |
| Performance                | Good locally   | Bottleneck risk   | Good with local coordination |
| Complexity                 | Low initially  | Medium            | High initially               |
| Extensibility              | Medium         | Low               | High                         |
| Conservation               | Fragmented     | Central policy    | Domain-owned contracts       |
| Rollback                   | Fragmented     | Centralized       | Versioned local replay       |
| Architecture compatibility | Partial        | Poor              | Strong                       |

Decision: Alternative C.

It provides a shared protocol without imposing one computational model on the entire universe.

# F. Recommended architecture

## F1. Architectural structure

The recommended logical architecture:

Simulation Fidelity Policy

Importance · Interaction · Accuracy · Compute budget

Simulation Fidelity Transition Coordinator

Lifecycle · Validation · Epoch · Commit · Recovery

Entity Adapters

Vehicles · Characters · Aircraft · Spacecraft

Environment Adapters

Atmosphere · Ocean · Climate · Terrain

Astronomy Adapters

Analytical · N-body · Specialized solvers

Aggregate Adapters

Economy · Population · Traffic flows

Authoritative Rust Simulation Runtime

Physics · Environment · Ownership · Scheduler · Persistence

The diagram describes logical responsibilities, not mandatory separate processes.

## F2. Fidelity profile

I recommend a structured fidelity profile rather than one universal tier number.

Conceptually:

| Field                 | Meaning                                |
| --------------------- | -------------------------------------- |
| spatial_profile       | Spatial resolution and representation  |
| physical_profile      | Physical solver and model capability   |
| temporal_profile      | Update and integration characteristics |
| environmental_profile | Environmental resolution and model     |
| interaction_profile   | Supported interaction fidelity         |
| model_version         | Exact model implementation version     |
| configuration_version | Parameter and configuration revision   |
| error_budget          | Permitted approximation errors         |

The existing W03 tiers and W09 L0–L3 modes remain useful as domain-specific classifications.

They should not be forced into a direct one-to-one mapping.

For example, W09 L2 does not necessarily mean W03 Tier 2.

## F3. Canonical state

The transition framework needs a common authoritative state envelope.

This is not a universal physical state containing every possible variable.

It is a stable identity and continuity contract.

Recommended conceptual structure:

| Field                       | Purpose                                  |
| --------------------------- | ---------------------------------------- |
| world_id                    | Persistent world identity                |
| entity_id / domain_state_id | Stable object or field identity          |
| simulation_time             | Authoritative state epoch                |
| state_revision              | Committed state version                  |
| reference_frame             | Coordinate reference                     |
| model_id                    | Current model                            |
| model_version               | Current model version                    |
| fidelity_profile            | Current fidelity dimensions              |
| ownership_region            | Authoritative region                     |
| ownership_epoch             | Fencing token                            |
| domain_state                | Domain-specific state payload            |
| provenance                  | Model, seed, inputs and source revisions |
| uncertainty                 | Represented model uncertainty            |

For environmental fields, the envelope must additionally describe:

- Spatial domain.
- Grid or mesh identity.
- Cell resolution.
- Temporal coverage.
- Units.
- Coordinate conventions.
- Field variables.
- Boundary conditions.
- Quality and uncertainty metadata.

The canonical state must never require converting every domain into a rigid-body representation.

## F4. SimulationFidelityTransition contract

The following is the proposed common architecture contract.

## SimulationFidelityTransition

Proposed architectural contract

1\. Identity

- transition_id
- world_id
- entity_id or environment_domain_id
- transition_revision

2\. Source model

- source_model_id
- source_model_version
- source_fidelity_profile
- source_state_revision

3\. Target model

- target_model_id
- target_model_version
- target_fidelity_profile
- target_configuration_version

4\. Initial state

- canonical_state_reference
- source_simulation_time
- reference_frame
- ownership_epoch
- domain_state_reference

5\. Transformation

- transformation_id
- transformation_version
- domain_adapter_id
- transformation_parameters
- information_loss_declaration

6\. Conservation

- conserved_quantity_set
- initial_quantity_totals
- transferred_quantity_totals
- boundary_fluxes
- external_sources_and_sinks
- residual_budget

7\. Accuracy

- position_error_tolerance
- velocity_error_tolerance
- energy_error_tolerance
- domain_specific_error_tolerances
- uncertainty_bounds

8\. Timing

- transition_start_time
- transition_commit_time
- transition_interval
- synchronization_boundary

9\. Authority

- owner_process_id
- ownership_region
- ownership_epoch
- state_revision
- commit_authority

10\. Recovery

- source_checkpoint_reference
- target_checkpoint_reference
- event_sequence
- rollback_policy

11\. Validation

- compatibility_result
- conservation_result
- continuity_result
- numerical_error_result
- validation_status

The fields describe the architecture. They do not prescribe one serialization format.

## F5. Transition state machine

A transition must have explicit lifecycle states.

1. Requested

   Policy determines that a transition is desirable.
2. Prepared

   Target model and required resources are available.
3. Initialized

   Target model receives the canonical source state and reconstructs its domain-specific representation.
4. Validated

   Compatibility, continuity and conservation checks pass.
5. Committed

   Authoritative ownership of the simulated state transfers to the target model at an agreed simulation boundary.
6. Finalized

   Source representation is retired after commit is durable and recovery requirements are satisfied.

Failure path

Failed preparation or validation returns to the previous authoritative state. A failure after commit requires recovery from the committed target checkpoint or a coordinated compensating transition.

The most important rule:

The source and target may coexist computationally during preparation, but only one may produce authoritative mutations.

The target can run in shadow mode for validation. Shadow results cannot modify persistent world state.

## F6. Transition interval

Not all transitions have the same temporal behavior.

I recommend three categories.

| Transition class | Behavior                                        | Example                     |
| ---------------- | ----------------------------------------------- | --------------------------- |
| Atomic           | Commit at one authoritative simulation boundary | Vehicle model replacement   |
| Windowed         | Coupled transformation over a defined interval  | Regional weather activation |
| Event-driven     | Triggered by a physical or logical event        | Orbital close encounter     |

An atomic transition may take significant wall-clock time to prepare, but its authoritative state handover is defined at a single simulation epoch.

A windowed transition must explicitly define the exchange of quantities throughout the coupling interval.

A transition interval must not be confused with the integration timestep.

## F7. State transformation rules

Every domain adapter must implement a defined transformation from source to target.

The transformation must:

1. Read a consistent source state.
2. Identify required target variables.
3. Transform coordinates and reference frames.
4. Initialize target-specific internal state.
5. Preserve applicable invariants.
6. Calculate and declare information loss.
7. Estimate uncertainty.
8. Validate the target state.
9. Produce a committed state candidate.

A generic component copy is not sufficient.

### Example: vehicle

[Understanding Multibody Dynamics and Simpack](https://images.openai.com/static-rsc-4/G4ydBUbZroVIU6zVLTnfoH28v3c2gWXO8EPtByhOe5OGi6MlEwyFwT3TmcxLC0dp7_ioJxVJJOLvryA6pMAb_fuFsKZo54OAGTOeW6DXuWfqetUq2A62vhla_muYKLJWTFRH9zwhEEw1_ObiD4ZSu5Z4NRZI9nrifjkOfOvuerA?purpose=inline)

[blog.3ds.com](https://blog.3ds.com/brands/simulia/understanding-multibody-dynamics-simpack/?utm_source=chatgpt.com)

A vehicle transitions from simplified vehicle dynamics to detailed suspension and tire simulation.

The transformation needs to preserve:

- Position.
- Linear velocity.
- Orientation.
- Angular velocity.
- Mass.
- Center of mass.
- Inertia tensor.
- Fuel and payload mass.
- Damage.
- Engine state.
- Existing external forces and relevant constraints.

The detailed solver reconstructs wheel and suspension states using available information.

If wheel angular velocity or tire deformation was not retained in the simplified model, the adapter must declare the reconstruction uncertainty.

It must not silently invent physically exact wheel states.

### Example: astronomy

[Scientific Machine Learning Research Bootcamp | Vizuara AI Labs](https://images.openai.com/static-rsc-4/GAhhLc8H8NgAtyhbZrGavS7yFd81b1gM3pOUs0T3Nuc0RhZEKeGaumU331QIRqwJYrESsupXOVYYiBVA94OyQz9NGhQaJRkdhTjxJ1bUFEehLLFO6i8IRfLMi8NegrCNSkofOgrZyzg3U4nNxmIV_pPp4MdkR65BkFVaZps835E?purpose=inline)

[sciml-research.vizuara.ai](https://sciml-research.vizuara.ai/?utm_source=chatgpt.com)

A planet transitions from analytical Keplerian propagation to numerical N-body integration.

Required transfer:

- Position and velocity at a common epoch.
- Mass.
- Gravitational parameters.
- Relevant interacting bodies.
- Reference frame.
- Perturbation configuration.
- Numerical integration configuration.

The target solver must establish a physically consistent initial state.

The resulting difference in acceleration and subsequent trajectory is an expected model difference, not automatically a transition failure.

The error budget must distinguish initialization discontinuity from subsequent numerical divergence.

### Example: environment

[Towards eliminating a decade-old climate model bias – Institute for Atmospheric and Climate Science | ETH Zurich](https://images.openai.com/static-rsc-4/BwGCQ1OypySae8EiUSp7z5Aqj8GtlXkErKr47fh_3Fr-F2377daLTeO_K3HhFdcM6Dbf3SPIdoxgOHftip6AyUDEmhArmX-_KdOeRBToyTkTSNud_HWgP5xJPJfPXUpz375noqCB6Lh0jv1q0vORshEeU8B7zZ6HTXz6Q0n0Myk?purpose=inline)

[iac.ethz.ch](https://iac.ethz.ch/news-and-events/news-en/2023/03/towards-eliminating-a-decade-old-climate-model-bias.html?utm_source=chatgpt.com)

A global environmental model activates a regional high-resolution atmospheric simulation.

The regional model must receive:

- Boundary conditions.
- Initial atmospheric state.
- Temperature and pressure profiles.
- Wind fields.
- Moisture fields.
- Relevant surface conditions.
- Time-dependent external forcing.

The regional domain must exchange fluxes with the global model.

A higher-resolution grid must not create additional atmospheric mass or energy simply because it contains more cells.

## F8. Conservation architecture

This is one of the most important additions to the existing documents.

Conservation must be defined per physical domain and per transition.

For a conserved quantity \\(Q\\):

\\[ Q\_{\text{before}}+ Q\_{\text{sources}}- Q\_{\text{sinks}} = Q\_{\text{after}}+R \\]

Where \\(R\\) is the transition residual.

The permitted residual must be bounded by an explicitly declared error budget.

### Required conservation categories

| Quantity                   | Relevant domains                                |
| -------------------------- | ----------------------------------------------- |
| Mass                       | Rigid bodies, fluids, atmosphere, oceans        |
| Linear momentum            | Mechanics, fluids, orbital systems              |
| Angular momentum           | Rigid bodies, rotating systems, orbital systems |
| Energy                     | Mechanical, thermal, atmospheric, oceanic       |
| Water mass                 | Hydrology, atmosphere, oceans, terrain          |
| Species/material inventory | Chemistry, ecology and material processes       |

Not every domain must conserve every quantity independently.

For example, an atmospheric subdomain can exchange energy and mass with its surroundings. The correct requirement is conservation across the coupled system and its declared boundary fluxes.

### Important distinction: conservation versus approximation

An approximate solver may produce a different trajectory or distribution from a more accurate solver.

That difference is not necessarily a conservation violation.

The transition must distinguish:

- Numerical conservation error.
- Model approximation error.
- State reconstruction error.
- Physical exchange with external systems.
- Intentional physical mutation.

These must not be combined into one unexplained tolerance.

### Environmental coupling

For W08, I recommend a conservative parent-child coupling model.

A coarse global model provides boundary and initial conditions to a fine regional model.

The fine model returns aggregated fluxes and state changes to its parent.

This should use conservative flux accounting, including correction of discrepancies between coarse and fine boundary fluxes.

For finite-volume environmental solvers, flux-register/refluxing approaches are an appropriate technical reference. They allow coarse and fine representations to exchange conserved quantities without introducing artificial gains or losses.

The exact numerical scheme remains owned by W08.

## F9. Error budgets

There must not be one global numerical tolerance.

Each transition declares an error budget appropriate to its physical domain.

Recommended categories:

| Error category       | Example                                     |
| -------------------- | ------------------------------------------- |
| State continuity     | Position and velocity discontinuity         |
| Numerical error      | Integration error                           |
| Model error          | Analytical versus N-body approximation      |
| Reconstruction error | Missing detailed source state               |
| Environmental error  | Difference between coarse and fine fields   |
| Conservation error   | Mass or energy residual                     |
| Temporal error       | Difference caused by temporal interpolation |

The validation result must contain both:

- Absolute error.
- Relative error where mathematically meaningful.

For quantities near zero, relative error alone is unsuitable.

Tolerance must be based on the physical purpose and relevant scales.

For example, a trajectory used for gameplay may tolerate a different error from one used for scientific orbital analysis.

## F10. Distributed ownership and synchronization

The transition framework must follow W05 ownership.

Recommended rules:

1. The current owner creates the transition request.
2. The current owner obtains a consistent source checkpoint.
3. The target model is prepared under the same authority or at a designated target execution context.
4. The target model receives the state and transition metadata.
5. The target state is validated.
6. Ownership and transition commit are coordinated at a defined boundary.
7. The new authoritative model continues simulation.
8. The previous model is retired.

If the transition and region migration happen simultaneously, they must be coordinated as one transactionally recoverable operation.

The ownership epoch must fence all stale commands.

No global world-wide barrier is required.

The synchronization boundary can be local to an entity, physics island, environmental domain or region, depending on the coupling requirements.

## F11. Rollback and replay

A transition is a persistent world event.

It must record:

- Transition identity.
- Source state revision.
- Source model and version.
- Target model and version.
- Transformation version.
- Simulation time.
- Ownership epoch.
- Relevant external inputs.
- Random seeds.
- Configuration.
- Validation results.
- Conservation residuals.
- Commit result.

### Rollback rules

Before commit:

- Discard the target candidate.
- Restore the source state if it was temporarily frozen.
- Continue from the last authoritative source revision.

After commit:

- Do not simply reactivate the old source model using stale state.
- Restore the target from a valid checkpoint and replay its event history, or perform a new explicitly coordinated transition back to a compatible source model.

For What-If branching, a new branch can restore a prior checkpoint and evaluate a different transition policy without modifying the original timeline.

A critical distinction: rollback of a simulation transition is not the same as reversing physical history. If the world has progressed after the transition, restoring an older representation requires replay or a compensating transition.

# G. Cross-system impact

## G1. Documents that remain compatible

The following architectural proposals remain compatible with the recommended architecture:

| Document | Compatible proposals                                                                                     |
| -------- | -------------------------------------------------------------------------------------------------------- |
| W01      | Independent spatial and physical LOD, reference-frame transformations, replaceable environment providers |
| W02      | Fixed authoritative physics timestep, specialized numerical integration                                  |
| W03      | Specialized physical models, multiple fidelity tiers, shared physical state                              |
| W04      | Analytical and numerical orbital propagation, state-preserving solver transitions                        |
| W05      | Single authoritative owner, epoch fencing, checkpoint-based migration                                    |
| W06      | Event history, authoritative state, branch-based simulation                                              |
| W07      | Independent presentation and simulation fidelity                                                         |
| W08      | Hierarchical environmental fidelity, conservation budgets                                                |
| W09      | L0–L3 entity representation, domain-specific transformations, adaptive scheduling                        |

No worker needs to abandon its fundamental architecture.

## G2. Documents requiring clarification

W01

Define environmental sample quality metadata and specify how spatial representation changes relate to simulation state transitions.

W02

Define the common solver handover boundary, including physical state and active constraints.

W03

Define the domain adapter contract and required state reconstruction guarantees for each entity class.

W04

Define acceptable initialization discontinuity and long-term divergence separately for orbital solver transitions.

W05

Define the interaction between region migration and fidelity transitions.

W06

Specify transition events as first-class historical events and define branch replay behavior.

W07

Specify how model changes are communicated through stable entity replication without changing entity identity.

W08

Define conservative coarse-to-fine and fine-to-coarse transfer, including boundary flux accounting.

W09

Define how fidelity scheduling interacts with transition preparation cost and transition failure.

## G3. Documents requiring architectural changes

No fundamental replacement is required.

However, three explicit additions are necessary:

1. W05 needs a joint ownership/fidelity transition protocol.
2. W08 needs a formal conservation-aware environmental transfer contract.
3. W09 needs a shared transition lifecycle specification rather than treating the Fidelity Manager as merely a level-selection mechanism.

These changes are dependencies of C-08, not requests to redesign those workers' domains.

## G4. Decisions requiring coordination

The architecture integrator must coordinate:

- Canonical state envelope with W02, W03 and W09.
- Simulation time semantics with W02, W04 and W06.
- Ownership epoch semantics with W05.
- Conservation budgets with W08.
- Transition scheduling with W09.
- Presentation-state continuity with W07.

The transition framework must not independently redefine any of these existing responsibilities.

# H. Required architectural decisions

The following ADRs are proposed for integration into the central Planetary Engine architecture.

ADR-FID-001

Recommended

Multidimensional fidelity model

Spatial, physical, temporal, environmental and interaction fidelity are independent dimensions, constrained by explicit compatibility requirements.

Reject a single universal LOD variable.

ADR-FID-002

Recommended

Common transition lifecycle

Introduce a shared SimulationFidelityTransition contract with explicit preparation, initialization, validation, commit and recovery.

ADR-FID-003

Recommended

Domain-specific state transformations

Each simulation domain owns its transformation adapters. No generic conversion is assumed to preserve all physical information.

ADR-FID-004

Recommended

Canonical state continuity

All fidelity representations must maintain a mapping to stable authoritative identity, simulation time, reference frame, state revision and domain-specific state.

ADR-FID-005

Recommended

Conservation-aware transition

Every transition declares applicable conserved quantities, external exchanges and permitted residuals.

ADR-FID-006

Recommended

Single authoritative simulation model

Multiple models may coexist for preparation and shadow validation, but only one may commit authoritative state mutations for the same simulated scope.

ADR-FID-007

Recommended

Ownership-aware transitions

Fidelity transitions must respect region ownership, ownership epochs and fencing. Region migration and fidelity transition must support coordinated recovery.

ADR-FID-008

Recommended

Transition history and replay

Every committed transition is versioned and recorded in authoritative world history with sufficient information for supported replay and recovery.

ADR-FID-009

Recommended

Local synchronization boundaries

Transition commits use the smallest synchronization scope that satisfies physical and ownership correctness. No global tick barrier is introduced.

ADR-FID-010

Recommended

Explicit transition error budgets

Continuity, numerical, model, reconstruction, environmental and conservation errors are independently measured and validated.

## H1. Integration priority

| Priority | ADR     | Reason                                            |
| -------- | ------- | ------------------------------------------------- |
| Critical | FID-001 | Establishes the meaning of fidelity               |
| Critical | FID-002 | Establishes the transition protocol               |
| Critical | FID-003 | Prevents invalid generic conversions              |
| Critical | FID-005 | Prevents environmental and physical inconsistency |
| Critical | FID-007 | Prevents distributed ownership conflicts          |
| High     | FID-004 | Defines state continuity                          |
| High     | FID-006 | Defines authority                                 |
| High     | FID-008 | Defines historical recovery                       |
| High     | FID-009 | Defines synchronization                           |
| High     | FID-010 | Makes correctness measurable                      |

These are recommendations awaiting central architecture approval, not existing approved ADRs.

# I. Validation plan

The architecture must be validated using independent physical, environmental, distributed and performance tests.

No benchmark results are assumed.

## I1. Entity transitions

| Test   | Transition                            | Required result                                   |
| ------ | ------------------------------------- | ------------------------------------------------- |
| VEH-01 | Simplified vehicle → detailed vehicle | No unexplained position or velocity discontinuity |
| VEH-02 | Detailed vehicle → simplified vehicle | Relevant momentum and mass properties preserved   |
| VEH-03 | Full simulation → statistical traffic | Explicitly declared information loss              |
| VEH-04 | Statistical traffic → full simulation | Valid reconstruction within declared uncertainty  |
| VEH-05 | Full → dormant → full                 | Persistent identity and relevant state preserved  |

## I2. Astronomical transitions

| Test   | Transition             | Required result                                       |
| ------ | ---------------------- | ----------------------------------------------------- |
| AST-01 | Keplerian → N-body     | Continuous position and velocity                      |
| AST-02 | N-body → analytical    | Bounded approximation error                           |
| AST-03 | Close encounter        | Solver selection does not create artificial impulses  |
| AST-04 | Reference-frame change | Correct position and velocity transformations         |
| AST-05 | Time acceleration      | Consistent transition epoch and integration semantics |

Use reference trajectories to distinguish model error from integration error.

## I3. Environmental transitions

| Test   | Transition                      | Required result                                              |
| ------ | ------------------------------- | ------------------------------------------------------------ |
| ENV-01 | Global → regional atmosphere    | Mass and energy accounting                                   |
| ENV-02 | Regional → local atmosphere     | Continuous boundary conditions                               |
| ENV-03 | Coarse → fine ocean             | Water and momentum conservation                              |
| ENV-04 | Fine → coarse ocean             | Correct aggregation                                          |
| ENV-05 | Global → regional climate       | Consistent energy balance                                    |
| ENV-06 | Regional water → local terrain  | Correct water inventory                                      |
| ENV-07 | Environmental resolution change | No artificial discontinuities in consuming physical entities |

Particularly important is testing repeated transitions.

A region must not accumulate artificial mass, water or energy after repeated promotion and demotion.

## I4. Distributed tests

These tests must be coordinated with W05 and W09.

1. Two processes attempt to prepare a transition for the same entity.
2. Source process crashes during preparation.
3. Target process crashes after initialization.
4. Ownership epoch changes during transition preparation.
5. Old owner attempts to commit after ownership transfer.
6. Network messages arrive out of order.
7. Transition and region migration occur simultaneously.
8. Recovery replays a committed transition.
9. A failed transition restores the source state.
10. Multiple entities transition simultaneously within one physics island.

Required invariant:

\\[ N\_{\text{authoritative owners}}(e,t)=1 \\]

For every active authoritative entity \\(e\\), at any given committed simulation epoch \\(t\\), there must be exactly one valid owner.

## I5. Determinism and history

Tests must verify:

- Same-runtime replay under equivalent inputs.
- Transition event ordering.
- Model version preservation.
- Configuration version preservation.
- Random seed preservation.
- Recovery after incomplete transitions.
- What-If branching from before and after a transition.
- No modification of parent world history by child branches.

Cross-platform bitwise determinism is not a mandatory global requirement.

## I6. Performance validation

Measure:

| Metric                      | Purpose                                 |
| --------------------------- | --------------------------------------- |
| Transition preparation time | Cost before commit                      |
| Transition commit latency   | Authoritative handover cost             |
| Peak memory overhead        | Temporary source and target coexistence |
| Shadow simulation overhead  | Cost of validation                      |
| Transition throughput       | Concurrent transition capacity          |
| Failed transition rate      | Reliability                             |
| Recovery duration           | Fault tolerance                         |
| Conservation residual       | Physical correctness                    |
| State reconstruction error  | Transformation quality                  |
| Scheduler overhead          | Fidelity management cost                |

Test at multiple scales:

- One entity.
- One physics island.
- One environmental region.
- Multiple neighboring regions.
- Large numbers of dormant and abstract entities.
- Simultaneous high-demand transitions.

Do not optimize only for transition throughput. A fast but physically inconsistent transition is a failed result.

## I7. Acceptance invariants

The architecture should not be considered validated until these invariants hold:

1. Logical identity survives every supported fidelity transition.
2. Authoritative ownership never becomes ambiguous.
3. The transition does not silently discard required physical quantities.
4. Declared conservation residuals remain within their budgets.
5. Transition discontinuities remain within domain-specific tolerances.
6. Replay reproduces the committed transition under its declared determinism guarantees.
7. Failure cannot leave two authoritative models.
8. Rendering fidelity cannot change authoritative physical state.
9. A region can transition without requiring global simulation synchronization.
10. The system can identify when a requested transition is physically impossible or insufficiently supported.

# J. Unresolved questions

The following decisions cannot responsibly be finalized by GPT-08 alone.

## J1. Canonical state ownership

W02, W03 and W09 must decide the exact boundary between:

- Universal physical state.
- Domain-specific physical state.
- Gameplay state.
- Environmental state.
- Persistent historical state.

My recommendation is a shared state envelope with domain-specific payloads, rather than one universal component schema.

## J2. Simulation time

W02, W04 and W06 must reconcile:

- Authoritative simulation ticks.
- Integer simulation time.
- Astronomical time scales.
- Region-local scheduling.
- Adaptive numerical timesteps.
- Offline simulation.
- Branch timelines.

C-08 assumes a common simulation-time semantic, but does not require identical integration frequencies.

## J3. Environmental coupling

W08 must determine:

- Which environmental fields are conserved.
- Which quantities are fluxes.
- How boundary conditions are transferred.
- Which interpolation methods preserve conservation.
- How coarse and fine solvers exchange information.
- How simultaneous atmosphere, ocean and terrain transitions are coordinated.

This is the largest scientific dependency.

## J4. Transition granularity

The architecture must distinguish:

- Entity transitions.
- Physics-island transitions.
- Environmental-domain transitions.
- Region transitions.
- Hierarchical aggregate transitions.

It remains open whether some coupled domains must always transition together.

My recommendation is to permit independent transitions unless a physical constraint explicitly requires coupled promotion.

## J5. Simultaneous model transitions

Consider a vehicle entering a region where both the vehicle physics and local atmospheric model are being promoted.

The transition coordinator must establish whether:

- The vehicle waits for the environment.
- The environment is promoted first.
- Both transitions share one synchronization boundary.

My recommendation is capability dependency ordering with a joint commit when the interaction cannot be safely separated.

## J6. Error-budget governance

Who defines acceptable physical error?

My recommendation:

- W02: general physics integration.
- W03: specialized entity dynamics.
- W04: astronomical propagation.
- W08: environmental fields.
- W09: system-level transition cost and error monitoring.

The central architecture should define the error-budget format, not impose one numerical tolerance on all domains.

## J7. Transition hysteresis

A model should not continuously switch between two fidelity levels because of minor changes in distance or computational load.

The policy must account for:

- Promotion and demotion thresholds.
- Minimum residence time.
- Transition cost.
- Predicted future interactions.
- Available resources.

Exact values must be established through workload measurements.

## J8. Model upgrades and persistence

What happens when a model version changes while an entity is dormant?

The architecture needs a policy for:

- Direct state migration.
- Replay under the new model.
- Compatibility adapters.
- Historical version retention.
- Reproducibility of old branches.

This is especially important for long-lived persistent worlds.

## J9. Environmental and entity transition ordering

A regional environmental solver and the physical entities consuming its outputs must agree on which environmental revision applies to a particular simulation epoch.

This needs a shared environment snapshot/version contract between W02, W03 and W08.

# K. Final assessment

Resolved conditionally

C-08 is architecturally resolved at the framework level.

The recommended solution is a hierarchical, distributed, conservation-aware Simulation Fidelity Transition Framework with domain-specific transformation adapters.

It reconciles the central ideas already present in W01–W09 without replacing their specialized simulation architectures.

## K1. What is resolved

- Fidelity is multidimensional rather than a single LOD number.
- Spatial, physical, temporal and environmental representations can evolve independently, subject to compatibility constraints.
- All transitions follow a shared lifecycle.
- State transformation belongs to specialized domain adapters.
- Physical continuity and conservation have explicit validation contracts.
- Only one authoritative model can commit state mutations.
- Ownership migration and fidelity transitions must coordinate.
- Transitions are recorded in world history.
- Rollback and replay have explicit semantics.
- Global synchronization is not required.

## K2. What remains conditional

The final implementation-ready contract depends on decisions from:

| Dependency                              | Responsible worker |
| --------------------------------------- | ------------------ |
| Physical canonical state                | W02                |
| Specialized entity transformations      | W03                |
| Astronomical solver initialization      | W04                |
| Distributed ownership integration       | W05                |
| Historical replay and branching         | W06                |
| Presentation-state continuity           | W07                |
| Environmental conservation and coupling | W08                |
| Scheduler and performance policy        | W09                |

The most important unresolved dependency is W08's conservative environmental coupling.

Without it, the framework could preserve entity state while still introducing inconsistencies in the surrounding physical environment.

## K3. Final architectural recommendation

The central integrator should adopt Alternative C and incorporate ADR-FID-001 through ADR-FID-010 into the master architecture.

One particularly important principle should become a permanent Planetary Engine invariant:

> A fidelity transition changes how the world is calculated, not which world exists.

A vehicle remains the same vehicle. A planet remains the same astronomical body. A regional atmosphere remains part of the same global atmosphere.

The computational representation changes, but persistent identity, physical causality and authoritative history remain continuous.

This is the distinction that allows Planetary Engine to combine gameplay simulation, engineering physics, scientific environmental simulation and astronomical mechanics without building separate disconnected worlds.

## Deliverable summary

# GPT-08 / C-08

Architecture resolution status

Reviewed worker documents

9 / 9

Evaluated alternatives

3

Proposed architectural decisions

10 ADRs

Validation categories

6

Implementation

Not performed

Final status: Resolved conditionally.

Integration priority: High. The transition contract affects almost every simulation domain.

Source repository: [projectzonio/what_if](https://github.com/projectzonio/what_if?utm_source=chatgpt.com)

Important limitation: This resolution is based on the publicly accessible contents of the nine worker documents. It does not claim that the proposed ADRs have been accepted, that the contracts have been implemented, or that validation benchmarks have been executed.

The next architectural step is to have the central integrator reconcile `SimulationFidelityTransition` with the canonical state, simulation clock, region ownership and environmental coupling contracts before declaring the global architecture frozen.