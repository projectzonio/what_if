Máš pravdu. Ověřil jsem repozitář přímo na GitHubu:

[projectzonio/what_if – Planetary Engine](https://github.com/projectzonio/what_if?utm_source=chatgpt.com).

Repozitář je veřejný a GitHub skutečně zobrazuje všech devět požadovaných dokumentů `w1.md` až `w9.md`. Předchozí požadavek na ZIP tedy nebyl oprávněný. Omlouvám se.

Budu vycházet přímo z repozitáře a řešit C-02 jako součást společné architektury, nikoli jako izolovaný návrh W02.

## První zjištění z W01–W09

V dokumentech už existuje poměrně dobrý základ pro řešení C-02, ale také skutečný kompetenční konflikt.

| Dokument | Stávající odpovědnost / relevantní návrh                                                         |
| -------- | ------------------------------------------------------------------------------------------------ |
| W01      | Planetární prostředí, gravitační pole, rotace planet a referenční soustavy                       |
| W02      | Univerzální fyzikální runtime, vyhodnocování sil a integrace                                     |
| W03      | Specializovaná fyzika entit a environmentální snapshoty; výslovně upozorňuje na konflikt W01/W02 |
| W04      | Astronomické gravitační modely, N-body a `evaluate_gravity`                                      |
| W05–W09  | Další architektonické oblasti, jejichž kontrakty musí zůstat kompatibilní                        |

Zatím nejdůležitější rozpor:

- W01 v oddílu 2.5 (řádky 268–280) požaduje, aby gravitační pole poskytovalo gravitaci odděleně od zdánlivých zrychlení.
- W03 v oddílu 4.3 (řádky 533–546) přiděluje W01 poskytování gravitačního pole a rotace, W02 vyhodnocování gravitační akcelerace a transformací.
- W04 v oddílu 5.3 (řádky 683–688) definuje vlastní rozhraní pro výpočet gravitační akcelerace.

To znamená, že samotné rozhraní `evaluate_gravity` zatím nemá jednoznačně vymezeného vlastníka ani jasně stanovený vztah k planetárnímu environmentálnímu modelu.

Zdrojové dokumenty: [W01](https://github.com/projectzonio/what_if/blob/main/w1.md?utm_source=chatgpt.com), [W02](https://github.com/projectzonio/what_if/blob/main/w2.md?utm_source=chatgpt.com), [W03](https://github.com/projectzonio/what_if/blob/main/w3.md?utm_source=chatgpt.com) a [W04](https://github.com/projectzonio/what_if/blob/main/w4.md?utm_source=chatgpt.com).

# GPT-02 — C-02 Architecture Resolution

Architecture recommendation established

Cross-worker approval required

Subject: Gravity and Physical Authority\
Repository revision: `main` – inspected 16 November 2026\
Scope: W01–W09, with primary responsibility across W01, W02, W03 and W04.

The central finding is that the architecture should not have one universal gravity implementation. It should have one universal gravity evaluation contract, implemented by multiple specialized field providers.

That distinction resolves the apparent ownership conflict without sacrificing specialized astronomical or planetary models.

## A. Executive decision

# C-02 — Gravity and Physical Authority

## Recommended architectural decision

Adopt a layered gravity and force architecture with three distinct responsibilities:

1. **Gravity and Physical Field Providers**
   - W01 owns planetary gravity-field models and planetary rotation/environment data.
   - W04 owns astronomical gravitational source models and astronomical gravity algorithms.
   - W03 owns specialized entity dynamics and non-gravitational domain forces.
2. **Force Evaluation and Composition**
   - W02 owns the universal force-evaluation pipeline.
   - W02 queries the registered providers through generic contracts.
   - W02 resolves source overlap, composes gravitational contributions, applies non-inertial frame corrections and produces the acceleration/force inputs for integration.
3. **Physical Integration and Authoritative State**
   - W02 owns authoritative physical state advancement and integration scheduling.
   - Specialized numerical integration strategies may be supplied by domain solvers, including orbital dynamics, but they execute under W02's authoritative lifecycle and state ownership.

Gravity is simultaneously an environmental property and a physical contribution. Its source models belong to the environment and celestial domains; its evaluation and composition belong to the universal physics runtime.

No entity-specific subsystem may independently implement planetary or astronomical gravity.

The architecture must preserve the distinction between:

- Gravitational acceleration.
- Non-gravitational physical forces.
- Non-inertial apparent acceleration.
- Resulting coordinate-frame acceleration.

All four must remain independently identifiable in diagnostics and validation.

## B. Existing architecture

### B1. W01 — Planetary environment

W01 already establishes the correct conceptual foundation:

- Environment Model includes gravity.
- Environment providers are independent modules.
- Gravity providers can use constant gravity, point masses, multi-body models and spherical harmonics.
- Gravity is evaluated at a specified position and time.

Most importantly, W01 section 2.5, lines 268–280, explicitly states that gravitational acceleration must be returned separately from apparent acceleration in rotating frames. It assigns the application of inertial terms to the physics integrator.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



### B2. W02 — Universal Physics Runtime

W02 establishes five layers, including the simulation coordinator, universal physical state, rigid-body solver, domain solvers and execution/persistence.

Its authoritative tick already contains:

1. Coordinate and environment update.
2. Force evaluation.
3. Velocity prediction.
4. Collision detection.
5. Constraint preparation and solving.
6. State integration and correction.

W02 also explicitly supports multi-rate scheduling and independent domain solvers.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://raw.githubusercontent.com\&sz=32)

GitHub+2



However, its force-evaluation phase lists gravity alongside vehicle, aircraft and propulsion providers without sufficiently distinguishing gravity-field evaluation from physical force composition.

### B3. W03 — Specialized entity physics

W03 section 4.3 is particularly important.

It proposes:

- W01 supplies gravitational fields and planetary rotation.
- W02 evaluates gravitational acceleration and frame transformations.
- W03 consumes the result and does not implement gravity.

This is a good initial ownership proposal.

W03 also defines a specialized force contribution contract containing the receiving entity, force vector, application point, reference frame, source component and validity interval.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



### B4. W04 — Celestial and astronomical simulation

W04 introduces:

- Direct Newtonian gravity.
- Point-mass gravity.
- Multipole expansions.
- Barnes–Hut.
- Fast multipole methods.
- Galactic gravitational potentials.
- Spherical harmonic gravity fields.

Its section 5.3 defines `GravityRequest` and `GravityResponse`, with target position, reference frame, epoch, source entities, approximation tolerance, requested fidelity, acceleration, potential and estimated error.

This is a valuable proposal. However, W04 also proposes a celestial runtime with its own authoritative state and scheduling responsibilities. That creates an ownership overlap with W02.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+2



### B5. Other workers

The remaining documents introduce constraints that the gravity architecture must preserve:

- W05: distributed authoritative regions, multirate simulation and independent scheduling.
- W06: one persistent physical world shared by different gameplay domains.
- W07: Unity presentation and prediction without physical authority.
- W08: environmental fidelity levels and coupling between astronomical, planetary and local environmental models.
- W09: physics islands, regional ownership, recovery and scalable execution.

These are architectural constraints, not reasons to create separate gravity implementations.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://raw.githubusercontent.com\&sz=32)

GitHub+4



## C. Problem analysis

### C1. The actual ownership conflict

The problem is not that multiple systems calculate gravity. Multiple gravity algorithms are necessary.

The problem is that the documents do not clearly distinguish:

- Ownership of the gravitational source.
- Ownership of the gravitational field representation.
- Ownership of the numerical algorithm.
- Ownership of gravity composition.
- Ownership of physical state integration.

These are separate responsibilities.

### C2. Double-counting gravitational sources

Consider Earth and the Sun.

A local Earth gravity model may include:

- Earth's monopole contribution.
- Earth's oblateness.
- Higher-order gravity harmonics.

An astronomical gravity solver may simultaneously include Earth as a point-mass source.

If both complete contributions are summed, Earth's gravitational monopole is counted twice.

This is a correctness failure, not merely a numerical approximation.

The architecture therefore requires explicit source identity and model-coverage semantics.

### C3. Gravitational acceleration versus apparent acceleration

A gravity provider must not silently return effective gravity that already contains centrifugal acceleration.

Otherwise, W02 may apply centrifugal acceleration again.

The distinction is:

\\[ \mathbf a\_{\mathrm{gravity}} \neq \mathbf a\_{\mathrm{apparent}} \\]

A body in a rotating coordinate system may experience additional coordinate-dependent terms even though the underlying gravitational field has not changed.

### C4. Domain-specific force ownership

W03 correctly proposes that aircraft, vehicles, rockets and other specialized models calculate their own physical contributions.

However, these contributions must not replace the universal physical state or independently advance time.

The same applies to propulsion, aerodynamic drag, lift, tire forces, buoyancy and contact interactions.

### C5. Astronomical and local planetary transition

A spacecraft may transition between:

- Interplanetary flight.
- Planetary approach.
- Orbital insertion.
- Atmospheric flight.
- Surface interaction.

The underlying gravitational reality must remain continuous.

Changing solver fidelity must not change the identity of the gravitational sources or produce artificial impulses.

## D. Cross-system dependencies

| Worker | Requirement from C-02                    | Required coordination                               |
| ------ | ---------------------------------------- | --------------------------------------------------- |
| W01    | Planetary field and rotation providers   | Define field coverage and frame metadata            |
| W02    | Universal composition and integration    | Own evaluation pipeline and physical state          |
| W03    | Stable gravity/environment inputs        | Consume results, supply specialized forces          |
| W04    | Astronomical sources and algorithms      | Become a provider under W02's lifecycle             |
| W05    | Region ownership and multirate execution | Coordinate provider execution and snapshots         |
| W06    | Shared physical world                    | No gameplay-specific gravity                        |
| W07    | Authoritative state                      | Client gravity prediction remains non-authoritative |
| W08    | Environmental snapshots                  | Share consistent planetary rotation and field data  |
| W09    | Distributed execution and fidelity       | Preserve source consistency across regions and LOD  |

### D1. Critical dependency: W01 ↔ W04

The planetary gravity provider and astronomical gravity solver must agree on which source contributions they represent.

A planetary harmonic field must be identifiable as the complete or partial gravitational field of a particular celestial body.

### D2. Critical dependency: W01 ↔ W02

W01 supplies environmental data. W02 evaluates it in the context of a physical body and its selected reference frame.

W02 must not need to understand how W01 stores spherical harmonic coefficients or constructs planetary field models.

### D3. Critical dependency: W02 ↔ W03

W03 supplies force and torque contributions.

W02 owns accumulation, physical application, integration and state advancement.

### D4. Critical dependency: W02 ↔ W04

W04 must not introduce a second authoritative physical world or an independent simulation clock.

Its orbital and celestial algorithms must operate under W02's physical state and scheduling contracts.

This requires clarification in W04 section 4.1 and alignment with W04 section 5.4.

### D5. Distributed execution

W05 and W09 require that physical ownership can be distributed without creating two authoritative owners of the same body.

A gravity query must therefore identify:

- Its evaluation epoch.
- Its source-state revision.
- Its reference frame.
- Its model version.
- Its validity and accuracy.

A remote gravity provider must return a consistent snapshot or an explicit failure, never silently combine different epochs.

## E. Alternatives

### Alternative 1 — Centralized gravity inside W02

All gravity models, planetary fields, astronomical sources and force evaluation would be implemented inside the universal physics runtime.

Advantages:

- Simple central ownership.
- Straightforward integration.
- One apparent gravity API.

Disadvantages:

- W02 becomes dependent on planetary and astronomical implementation details.
- Scientific models become difficult to replace independently.
- Astronomical and environmental subsystems lose clear domain ownership.
- Extensibility becomes increasingly difficult.

Verdict: Reject.

### Alternative 2 — Independent gravity ownership in W01 and W04

W01 calculates planetary gravity. W04 calculates astronomical gravity. Each system provides its own complete gravity result to consumers.

Advantages:

- Strong domain separation.
- Specialized models remain independent.
- Easy scientific development.

Disadvantages:

- Duplicate sources.
- Ambiguous source precedence.
- No common composition policy.
- Inconsistent treatment of reference frames.
- Difficult transitions between local and astronomical fidelity.

Verdict: Reject as a complete architecture. Retain the provider specialization, but not independent composition authorities.

### Alternative 3 — Federated gravity providers with universal composition

W01 and W04 own specialized gravitational field providers. W03 owns specialized non-gravitational dynamics. W02 owns generic provider orchestration, source composition, apparent-acceleration handling and physical integration.

Advantages:

- Clear ownership.
- Independent scientific implementations.
- No requirement for one universal gravity algorithm.
- Consistent treatment of gravitational sources.
- Supports distributed and multirate execution.
- Preserves domain extensibility.

Disadvantages:

- Requires explicit provider contracts.
- Requires source-overlap metadata.
- Requires carefully defined reference-frame semantics.
- Requires coordination between multiple workers.

Verdict: Recommended.

### Comparative assessment

| Criterion             | Alternative 1    | Alternative 2                 | Alternative 3         |
| --------------------- | ---------------- | ----------------------------- | --------------------- |
| Physical correctness  | High potential   | Risk of duplication           | High                  |
| Scalability           | Moderate         | Moderate                      | High                  |
| Distributed execution | Difficult        | Difficult                     | Good                  |
| Performance           | Potentially good | Potentially good              | Provider-dependent    |
| Complexity            | High coupling    | Hidden integration complexity | Explicit coordination |
| Extensibility         | Limited          | Moderate                      | High                  |
| Compatibility         | Weak             | Partial                       | Strong                |

The third alternative provides the best balance between scientific specialization and universal physical authority.

## F. Recommended architecture

### F1. Ownership model

W01 — Planetary Gravity Provider

Owns:

- Planetary mass and gravity-field source data.
- Planetary gravity models.
- Spherical harmonic coefficients.
- Local gravity approximations.
- Planetary rotation and orientation data.
- Field validity and accuracy metadata.

Does not own:

- Universal force composition.
- Physical integration.
- Entity-specific forces.

W04 — Astronomical Gravity Provider

Owns:

- Celestial gravitational source models.
- Point-mass source representations.
- Direct N-body algorithms.
- Barnes–Hut and multipole algorithms.
- Galactic potentials.
- Astronomical gravity approximations.
- Solver error estimation.

Does not own:

- Independent universal physics state.
- A second physical integration authority.
- Independent composition of planetary and specialized entity forces.

W03 — Specialized Dynamics Providers

Owns:

- Vehicle forces.
- Aerodynamic forces.
- Propulsion.
- Rotor and actuator forces.
- Character locomotion contributions.
- Specialized torques.
- Domain-specific mass and inertia changes.

Does not own:

- Gravity implementation.
- Universal force composition.
- Authoritative state integration.

W02 — Universal Physics Runtime

Owns:

- Provider registration and execution.
- Gravity source composition.
- Non-inertial acceleration calculation.
- Force and torque accumulation.
- Physical integration.
- Constraint solving.
- Authoritative physical state.
- Simulation substep coordination.

### F2. Interface architecture

The common interface should distinguish three provider categories.

| Provider interface        | Output                                        |
| ------------------------- | --------------------------------------------- |
| Gravity Field Provider    | Gravitational acceleration and field metadata |
| Frame Kinematics Provider | Frame transforms and frame acceleration data  |
| Physical Force Provider   | Force, torque and application point           |

A gravity request should contain:

- Target position.
- Target velocity when required by the selected evaluation.
- Reference frame identifier.
- Simulation epoch.
- Requested fidelity.
- Numerical error tolerance.
- Source selection or exclusion policy.

A gravity response should contain:

- Gravitational acceleration vector.
- Optional gravitational potential.
- Optional gravity gradient tensor.
- Reference frame.
- Epoch.
- Source coverage.
- Model identifier and revision.
- Estimated numerical error.

The existing W04 `GravityRequest` and `GravityResponse` should be extended rather than discarded.

### F3. Source composition

The composition service must distinguish:

1. Complete source representation.
2. Partial source representation.
3. Correction to another source model.
4. Approximate aggregate representation.

For each celestial body, the selected models must declare their coverage.

Example:

| Source        | Active model                      |
| ------------- | --------------------------------- |
| Earth         | High-fidelity planetary harmonics |
| Moon          | Point-mass approximation          |
| Sun           | Point-mass approximation          |
| Distant stars | Aggregate potential               |

Earth's harmonic model replaces Earth's point-mass contribution in this evaluation. It does not get added to it.

The Moon and Sun remain independent contributors.

### F4. Evaluation equations

For an inertial frame:

\\[ \mathbf a\_{\mathrm{inertial}} = \mathbf g\_{\mathrm{total}} + \frac{\sum_i \mathbf F_i}{m} \\]

where:

\\[ \mathbf g\_{\mathrm{total}} = \sum_j \mathbf g_j \\]

The source summation is subject to the explicit coverage and replacement rules above.

For a rotating and translating frame:

\\[ \begin{aligned} \mathbf a\_{\mathrm{relative}} ={}& \mathbf g\_{\mathrm{total}} +\frac{\sum_i\mathbf F_i}{m} -\mathbf a\_{\mathrm{origin}}\\\ &-2\boldsymbol\omega\times\mathbf v\_{\mathrm{relative}}\\\ &-\boldsymbol\omega\times (\boldsymbol\omega\times\mathbf r)\\\ &-\dot{\boldsymbol\omega}\times\mathbf r \end{aligned} \\]

All quantities must be expressed in the same evaluation frame.

The additional terms are:

- Translational frame-origin acceleration.
- Coriolis acceleration.
- Centrifugal acceleration.
- Euler acceleration.

These are not additional gravitational sources.

The signs follow from expressing inertial acceleration in a rotating and translating coordinate system. W01's equation in section 2.5 describes the corresponding transformation in the opposite direction.

### F5. Domain-specific force composition

W03 contributions should be accumulated using their application points.

For a force applied away from the center of mass:

\\[ \boldsymbol\tau = (\mathbf r\_{\mathrm{application}}-\mathbf r\_{\mathrm{COM}}) \times\mathbf F \\]

W02 must preserve the distinction between:

- Force at the center of mass.
- Force applied at a local point.
- Pure torque.
- Gravitational acceleration.
- Frame-induced apparent acceleration.

Gravity is naturally represented as acceleration independent of target mass. Its equivalent force, when required by a rigid-body solver, is:

\\[ \mathbf F_g=m\mathbf g \\]

The conversion must occur once.

### F6. Evaluation order

Recommended authoritative evaluation sequence:

1. Resolve physical state and epoch.
2. Resolve the entity's active reference frame.
3. Obtain the immutable environmental snapshot.
4. Query the applicable gravity providers.
5. Resolve gravitational source coverage and overlap.
6. Compose gravitational acceleration.
7. Evaluate non-gravitational domain-specific forces.
8. Calculate non-inertial frame corrections.
9. Convert acceleration contributions into the solver's required representation.
10. Accumulate forces and torques.
11. Execute prediction, collision and constraint solving.
12. Integrate and commit authoritative state.
13. Emit diagnostics and state events.

The precise internal order of force evaluation and integration substeps remains the responsibility of the selected solver backend.

### F7. Fidelity levels

Gravity evaluation must support multiple computational fidelity levels.

| Fidelity | Example                                                 |
| -------- | ------------------------------------------------------- |
| Tier 0   | Prescribed or constant local acceleration               |
| Tier 1   | Point-mass planetary and celestial sources              |
| Tier 2   | Multiple sources and selected planetary harmonics       |
| Tier 3   | High-order harmonics, perturbations and detailed N-body |
| Tier 4   | Specialized scientific or relativistic models           |

These tiers are proposed categories, not a claim that the current implementation already supports them.

Changing fidelity must preserve physical state and avoid artificial impulses.

### F8. Failure and recovery

A gravity provider failure must not silently return zero gravity.

The response must distinguish:

- Valid result.
- Valid approximation.
- Stale result.
- Out-of-domain result.
- Unsupported fidelity.
- Provider failure.

For an authoritative simulation, the runtime must either:

- Use an explicitly permitted lower-fidelity fallback.
- Preserve the previous valid state and suspend the affected integration.
- Reject the update.

The fallback must be deterministic and recorded in diagnostics.

### F9. Distributed execution

Gravity providers should be read-only during a force-evaluation phase.

Their inputs should be immutable, epoch-tagged snapshots.

Parallel gravity queries can execute independently for different targets, provided the source snapshot and model revision are consistent.

The final reduction must follow a defined deterministic ordering where reproducibility is required.

No global barrier across the entire universe is necessary. Synchronization is required only for the physical dependencies of the active simulation domain.

## G. Cross-system impact

### Compatible documents

- W05: region-based authoritative execution and multirate scheduling.
- W06: one shared physical world.
- W07: server-authoritative physics.
- W09: independent physics islands and distributed execution.

### Documents requiring clarification

- W01: distinguish planetary field ownership from gravity composition.
- W03: explicitly define force-provider versus force-composition responsibilities.
- W08: clarify that gravity is supplied through the common environment contract, while frame corrections remain in W02.
- W05: specify how remote provider snapshots are synchronized with region ownership.

### Documents requiring architectural changes

W02: Introduce explicit gravity composition and source-overlap handling into the force-evaluation architecture.

W04: Remove the implication that its celestial runtime independently owns universal physical state advancement. Retain specialized celestial state models and gravitational algorithms as domain contributions under the common authoritative runtime.

### Decisions requiring joint coordination

W01, W02 and W04 must agree on:

- Gravitational source identity.
- Field coverage.
- Reference-frame semantics.
- Epoch and time-scale semantics.
- Fidelity and error contracts.
- Provider versioning.

W02 and W03 must agree on:

- Force units.
- Application points.
- Torque accumulation.
- Variable-mass semantics.
- Evaluation intervals.

W02, W05 and W09 must agree on:

- Snapshot consistency.
- Regional ownership.
- Provider availability.
- Fallback policy.
- Recovery behavior.

## H. Required architectural decisions

| ID           | Decision                   | Recommendation                                      |
| ------------ | -------------------------- | --------------------------------------------------- |
| ADR-GRAV-001 | Gravity provider ownership | W01 and W04 own specialized providers               |
| ADR-GRAV-002 | Gravity composition        | W02 exclusively owns universal composition          |
| ADR-GRAV-003 | Source overlap             | Explicit coverage and replacement metadata          |
| ADR-GRAV-004 | Non-inertial effects       | W02 applies frame corrections exactly once          |
| ADR-GRAV-005 | Specialized forces         | W03 provides contributions; W02 accumulates         |
| ADR-GRAV-006 | Integration authority      | W02 owns authoritative integration lifecycle        |
| ADR-GRAV-007 | Astronomical solver        | W04 provides specialized algorithms under W02       |
| ADR-GRAV-008 | Environmental contract     | Immutable, epoch-tagged samples                     |
| ADR-GRAV-009 | Fidelity transitions       | Preserve physical state and continuity              |
| ADR-GRAV-010 | Distributed gravity        | Consistent source snapshots and explicit fallback   |
| ADR-GRAV-011 | Diagnostic provenance      | Preserve source, model and numerical-error metadata |

All eleven decisions require central integrator acceptance before implementation.

## I. Validation plan

### I1. Mathematical invariants

1. A single point-mass source must reproduce Newtonian acceleration.
2. Multiple independent sources must produce the expected vector sum.
3. A spherical harmonic model must reproduce its reference field.
4. Replacing a point-mass approximation with a complete harmonic model must not double-count the monopole.
5. Gravitational acceleration must remain independent of target mass.
6. Non-inertial terms must be applied exactly once.
7. Frame transformations must preserve physical trajectories.

### I2. Reference-frame tests

Test:

- Stationary object in a rotating planetary frame.
- The same object in an inertial frame.
- Constant angular velocity.
- Changing angular velocity.
- Translating frame origin.
- Coriolis effects on moving objects.
- Centrifugal effects at different radii.
- Euler acceleration under angular acceleration.

### I3. Cross-domain tests

Earth, Moon and Mars

The same physical object model must operate under different gravitational environments without entity-specific gravity constants.

Aircraft

Gravity must remain independent of lift, drag and wind.

Rocket

Propulsion and changing mass must interact with gravity without duplicating gravitational force.

Spacecraft

Transition between interplanetary and local planetary gravity models must preserve position and velocity.

Surface vehicle

Planetary rotation and local gravity must not produce artificial lateral or vertical acceleration.

### I4. Distributed tests

- Two regions querying the same celestial source at a shared epoch.
- Region ownership migration.
- Gravity provider interruption.
- Recovery from a stale source snapshot.
- Deterministic source reduction.
- Fidelity transition during orbital flight.
- High-density physics island workload.

### I5. Measurable criteria

| Measurement         | Acceptance condition                      |
| ------------------- | ----------------------------------------- |
| Source accounting   | No duplicate gravitational contribution   |
| Frame corrections   | Each required term applied exactly once   |
| Reference error     | Within declared model tolerance           |
| Epoch consistency   | All composed fields use compatible epochs |
| Fidelity transition | No unexplained velocity discontinuity     |
| Recovery            | Explicit and reproducible fallback        |
| Authority           | Exactly one authoritative state owner     |
| Performance         | Measured at each selected fidelity tier   |

Numerical tolerances and performance thresholds must be established during validation. No benchmark result is assumed here.

## J. Unresolved questions

1. What is the canonical global reference frame and its precise inertial definition?
2. Which time scale is authoritative for each physical and astronomical operation?
3. Should the engine use a unified gravitational potential representation in addition to acceleration?
4. What are the exact altitude and distance validity domains for planetary harmonic models?
5. How should tidal interactions and extended-body gravity gradients be exposed to entity dynamics?
6. Which relativistic models are required for the initial scientific scope?
7. What is the maximum permissible gravity-field staleness for each simulation fidelity?
8. How should the system select between direct N-body, tree gravity and analytical propagation?
9. Which numerical solver is responsible for orbital integration when a spacecraft enters a high-contact local simulation?
10. How should gravitational source ownership migrate when astronomical simulation regions are redistributed?

These questions do not invalidate the recommended ownership architecture. They require additional decisions or scientific validation.

## K. Final assessment

Status: RESOLVED CONDITIONALLY

The architectural ownership problem can be resolved with a federated provider architecture and a single universal composition and integration authority.

The proposed solution preserves:

- Planetary and astronomical specialization.
- One physically coherent world.
- Explicit gravitational source ownership.
- Correct non-inertial frame handling.
- Specialized physical models.
- Distributed execution.
- Multiple simulation fidelity levels.

The remaining condition is agreement between W01, W02 and W04 on source coverage, frame semantics, provider contracts and authoritative state advancement.

No implementation is authorized by this document.

The central architecture integrator should accept or reject ADR-GRAV-001 through ADR-GRAV-011 and incorporate the resulting contracts into the global architecture.

## Source verification and architectural evidence

Hlavní podklady pro toto rozhodnutí:

| Dokument | Kontrolované části                             |
| -------- | ---------------------------------------------- |
| W01      | 2.3–2.5, 4.1–4.2, environment providers        |
| W02      | 6.5–6.6, 7.1–7.2, 8.2–8.3                      |
| W03      | 4.1–4.4, 8.1–8.5, 9.1–9.3                      |
| W04      | 3.3, 4.1, 5.2–5.4, gravitační modely           |
| W05      | Multirate scheduling, region ownership         |
| W06      | Sdílený svět, oddělení gameplay a fyziky       |
| W07      | Autorita Rust runtime versus Unity             |
| W08      | Environmentální reference frames a fidelity    |
| W09      | Distributed physics, physics islands, recovery |

Zásadní zdrojové pasáže:

- [W01 – Non-inertial acceleration](https://github.com/projectzonio/what_if/blob/main/w1.md?utm_source=chatgpt.com#L268)
- [W02 – Simulation lifecycle](https://github.com/projectzonio/what_if/blob/main/w2.md?utm_source=chatgpt.com#L619)
- [W03 – Gravity ownership](https://github.com/projectzonio/what_if/blob/main/w3.md?utm_source=chatgpt.com#L533)
- [W04 – Gravitational models](https://github.com/projectzonio/what_if/blob/main/w4.md?utm_source=chatgpt.com#L324)
- [W04 – Gravity interface](https://github.com/projectzonio/what_if/blob/main/w4.md?utm_source=chatgpt.com#L683)
- [W09 – Distributed physics constraints](https://github.com/projectzonio/what_if/blob/main/w9.md?utm_source=chatgpt.com#L794)

### Můj závěr pro centrálního integrátora

Nejdůležitější není pouze schválit rozdělení W01/W02/W04.

Je potřeba zabránit tomu, aby se v dalších fázích objevily tři nezávislé implementace:

- Planetary gravity.
- Astronomical gravity.
- Entity gravity.

Správný model je jeden gravitační systém složený z více specializovaných poskytovatelů.

W02 je vlastníkem fyzikálního vyhodnocení a integrace, nikoliv vlastníkem všech fyzikálních modelů.

To je podle mě nejdůležitější rozhodnutí C-02.

Další krok bych doporučil až po porovnání tohoto návrhu s výstupy ostatních GPT workerů, zejména kvůli případným konfliktům v jejich návrzích W02, W04 a W09.