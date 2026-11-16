# PLANETARY ENGINE

## WORKER 06 – Universal Gameplay, Strategy & World Construction



Architecture research

Implementation prohibited



Deliverable: Universal Gameplay Architecture & World Mutation System Specification

### 1. Executive direction

The central architectural decision should be:

Gameplay is a collection of independent simulation domains operating on one shared, persistent world—not a collection of separate game modes with separate worlds.

This distinction determines almost everything else.

For example:

- A racing vehicle and a military vehicle use different gameplay rules but interact with the same terrain, gravity, materials and physical objects.
- An RTS factory and a player-operated factory are the same persistent entity. Their interfaces and control mechanisms differ.
- A building placed by an administrator, a construction game or an AI-controlled faction must produce the same world mutation.
- A road built in a strategy game must be usable by physical vehicles, navigation systems, logistics simulations and other players.
- An economic transaction must not create resources independently of the physical and production systems.

I recommend five architectural boundaries:

1\. Gameplay Modules

Racing · FPS · Survival · Flight · RTS · Economy · Construction · Exploration

2\. Universal Gameplay Services

Interaction · Inventory · Ownership · Factions · Progression · Missions · AI · Permissions

3\. World Mutation System

Commands · Validation · Transactions · Revisions · History · Replication

4\. Shared World State

Entities · Buildings · Terrain · Resources · Infrastructure · Ownership · Economy

5\. Planetary & Physical Simulation

Coordinates · Gravity · Atmosphere · Terrain · Water · Materials · Physical interactions

An important distinction: gameplay services may request world changes, but they must not directly overwrite authoritative world state.

The World Mutation System becomes the common integration point for players, strategy systems, construction tools, AI and administrative interfaces.

### 2. Fundamental architectural principles

#### 2.1 Separate gameplay rules from physical simulation

The engine must not contain separate physical worlds for each genre.

Instead, a gameplay module defines what an entity can do, what rules apply to its actions, and how it interacts with other entities.

| Gameplay module | Own responsibility                     | Shared systems                        |
| --------------- | -------------------------------------- | ------------------------------------- |
| Racing          | Vehicle competition, laps, checkpoints | Physics, terrain, roads               |
| FPS             | Weapons, damage, abilities             | Character physics, collision          |
| Survival        | Needs, crafting, inventory             | Environment, resources                |
| Flight          | Flight operations, navigation          | Atmosphere, gravity, aerodynamics     |
| Space combat    | Weapons, targeting, tactical rules     | Orbital mechanics, spacecraft physics |
| RTS             | Orders, territory, production          | Economy, entities, logistics          |
| Construction    | Blueprints, placement, building        | Terrain, materials, world mutations   |
| Economy         | Prices, trade, production              | Ownership, resources, logistics       |
| Exploration     | Discovery, mapping, interaction        | Universal world state                 |

A gameplay module should be independently activatable, versionable and removable without changing the underlying physical identity of entities.

Important: Activating a gameplay module must not mean spawning a second representation of the world.

#### 2.2 Capability-based entities

I recommend a capability-composition model rather than a rigid inheritance tree.

An entity is an identity with a collection of independently defined capabilities.

For example:

Entity: Industrial Factory

One persistent entity

Physical capability Collision · Mass · Structure

Construction capability Blueprint · Building state

Production capability Recipes · Machines

Economic capability Value · Costs · Accounts

Ownership capability Owner · Faction

Logistics capability Input · Output · Storage

Replication capability Authority · Network state

This allows the same factory to participate in:

- An RTS production interface.
- A first-person maintenance simulation.
- A construction editor.
- A persistent economic simulation.
- An AI-controlled industrial network.

The capabilities are not gameplay-specific copies of the factory.

Established engineering approach: ECS (Entity Component System). Bevy ECS provides a particularly relevant Rust reference: entities hold components, systems operate on component queries, and system access metadata enables parallel execution.

*(zdroj: bevy.org +1)*

Limitation: ECS alone does not solve persistence, network authority, transactions, historical state or deterministic execution. Those require separate architectural contracts.

My recommendation is to evaluate Bevy ECS, `hecs` and `shipyard` as implementation candidates, but keep the public entity and persistence contracts independent of any particular ECS.

#### 2.3 Gameplay abilities and interactions

A second useful reference is Unreal Engine's Gameplay Ability System.

Its important architectural ideas include:

- Self-contained abilities.
- Explicit activation and cancellation.
- Costs and cooldowns.
- Temporary attribute modifications.
- Asynchronous multi-stage actions.
- Server/client execution and prediction.

These concepts are applicable beyond RPGs.

*(zdroj: Epic Developer Community +2)*

I propose a universal `Interaction / Ability` subsystem.

Examples:

| Action               | Capability or ability                          |
| -------------------- | ---------------------------------------------- |
| Open a door          | Interactable + state transition                |
| Fire a weapon        | Weapon + projectile creation                   |
| Construct a building | Construction + resource reservation            |
| Repair a spacecraft  | Maintenance + material consumption             |
| Issue an RTS order   | Command authority + task execution             |
| Trade resources      | Market + inventory transaction                 |
| Launch a rocket      | Vehicle state transition + physical simulation |

An ability should be a stateful execution workflow, not an unrestricted function that can directly modify arbitrary entities.

A possible lifecycle:

`Requested → Validated → Reserved → Executing → Completed / Failed / Cancelled`

For long-running activities, the execution state must be persistent. A factory must not forget an unfinished production job after a server restart.

### 3. Strategy and RTS architecture

A strategy game is not fundamentally a different rendering or physics system. It is a higher-level simulation operating on the same world.

I recommend separating the following domains.

Strategy Simulation

Command System

Orders, priorities, task assignment, command hierarchy

Territory & Factions

Ownership, borders, diplomacy, alliances, jurisdiction

Production & Logistics

Factories, resources, transportation, supply chains

Military & AI

Units, formations, tactical decisions, strategic planning

World Integration

Physical entities, infrastructure, terrain, environmental conditions

#### 3.1 Real-time strategy

RTS should use a command-driven model.

A player does not directly control the internal state of every unit. The player issues an order, which is evaluated and executed by the authoritative simulation.

Example:

`MoveUnit → PathPlanning → Navigation → PhysicalMovement → ArrivalEvent`

The navigation system can plan a route, but the physical simulation determines whether the vehicle or character actually follows it.

A road may be destroyed after the route is planned. A bridge may collapse. A vehicle may run out of fuel.

The strategy layer must respond to those actual world conditions.

#### 3.2 Turn-based strategy

Turn-based gameplay should be implemented as a scheduling and rule configuration over the simulation.

Possible modes:

- Fully paused physical simulation during a player's turn.
- Discrete world turns.
- Simultaneous orders followed by resolution.
- Hybrid turns with real-time physical processes continuing.

The underlying world does not need a separate turn-based representation.

However, turn boundaries must be explicit simulation events, and the engine must define which subsystems advance during each phase.

#### 3.3 Territory and factions

Territory ownership should be distinct from physical geography.

A region can have:

- Geographical boundaries.
- Administrative jurisdiction.
- Economic ownership.
- Military control.
- Access rights.
- Environmental protection rules.

These are different concepts and may overlap.

For example, a player can own a factory, operate it inside another faction's territory, and hold a temporary license to extract resources from surrounding land.

Do not encode these relationships into one `owner_id` field.

Use separate, versioned relations with effective times and permissions.

#### 3.4 AI-controlled entities

AI should be a consumer of the same public gameplay interfaces as human players.

Recommended separation:

1. Perception: gather world observations.
2. World model: maintain relevant knowledge.
3. Planning: select goals.
4. Task allocation: assign work.
5. Execution: issue ordinary world/gameplay commands.
6. Evaluation: assess outcomes and replan.

AI must not bypass validation or transaction processing.

Different AI fidelity levels are useful: a nearby military unit may need tactical decisions at high frequency, while a distant industrial faction can use aggregated economic decisions at a lower frequency.

This connects directly to the universal simulation scheduler and fidelity system.

### 4. Universal construction system

This is one of the most important subsystems in the entire project.

The construction system must not belong to RTS. RTS is simply one consumer of construction capabilities.

The same system must support:

- A player placing a building in survival mode.
- An RTS player constructing an industrial complex.
- An administrator building a city.
- An external world editor generating infrastructure.
- An AI faction expanding its territory.
- A Python world-generation process placing predefined structures.

All of these operations must use the same authoritative construction pipeline.

#### 4.1 Construction architecture

I recommend five independent layers.

Construction System

1\. Blueprint System

Defines what can be constructed, dimensions, materials, components, capabilities and constraints.

2\. Placement System

Position, orientation, terrain alignment, collision checks, attachment and spatial constraints.

3\. Construction Execution

Material reservations, construction stages, labor, machinery, completion and interruption.

4\. World Mutation

Authoritative creation of entities and changes to terrain, resources and infrastructure.

5\. Physical Integration

Collision geometry, mass, structural properties, terrain integration and environmental effects.

#### 4.2 Blueprint definition

A blueprint should describe the intended result, not execute construction itself.

Its data should include:

| Property               | Purpose                                |
| ---------------------- | -------------------------------------- |
| Blueprint ID           | Stable identity                        |
| Version                | Blueprint compatibility                |
| Dimensions             | Spatial footprint                      |
| Placement rules        | Valid placement conditions             |
| Material requirements  | Required resources                     |
| Construction stages    | Work sequence                          |
| Entity template        | Resulting entity capabilities          |
| Physical specification | Mass, collision, structural properties |
| Dependencies           | Required infrastructure                |
| Permissions            | Who can construct it                   |
| Visual reference       | Client-side asset                      |

Blueprints should be immutable once published. Changing a blueprint creates a new version.

Existing buildings retain their original blueprint version unless explicitly migrated.

This prevents an asset update from unexpectedly changing thousands of existing structures.

#### 4.3 Construction lifecycle

Proposed state machine:

`Draft → PlacementValidated → ResourcesReserved → Foundation → Construction → Operational`

Alternative transitions:

- Paused.
- Blocked.
- Damaged.
- Abandoned.
- Cancelled.
- Demolished.

An important distinction is that a building under construction is already a persistent world entity.

Its construction state changes over time, and its physical representation can evolve with construction progress.

A completed building does not need to be deleted and recreated.

#### 4.4 Terrain and infrastructure mutation

Terrain editing is fundamentally different from placing an ordinary object.

A terrain mutation can affect:

- Height fields.
- Terrain mesh or volumetric representation.
- Collision structures.
- Navigation.
- Drainage and water flow.
- Geological materials.
- Vegetation.
- Roads and foundations.
- Local environmental conditions.

Therefore, terrain mutations must publish invalidation information to dependent systems.

For example:

`TerrainChanged → CollisionRebuild → NavigationUpdate → HydrologyUpdate → ClientReplication`

These updates should be versioned and coordinated.

The terrain change itself must have one authoritative commit. Derived systems can update asynchronously, but must advertise which world revision they have processed.

A client must not assume that navigation or collision data is current merely because the terrain revision has changed.

#### 4.5 Construction granularity

I recommend distinguishing three classes:

| Class          | Examples                                | Representation                          |
| -------------- | --------------------------------------- | --------------------------------------- |
| Structural     | Buildings, bridges, factories           | Persistent physical entities            |
| Infrastructure | Roads, railways, pipelines, power grids | Spatial networks and connected entities |
| Environmental  | Excavation, forests, water channels     | Versioned terrain/environment fields    |

A road should not necessarily be represented as one enormous physical entity.

A road network is better represented as a graph with persistent segments, junctions and associated physical surfaces.

This allows construction, pathfinding, traffic, logistics and destruction to operate on related but independently optimized representations.

#### 4.6 External editor compatibility

The external editor must use the same blueprint catalogue and mutation API as the game.

It should not have privileged access to the underlying world database.

Its workflow:

1. Request a regional world snapshot.
2. Load available blueprints.
3. Create a local editing workspace.
4. Preview modifications.
5. Submit a mutation batch.
6. Receive validation results.
7. Commit the accepted transaction.
8. Receive the authoritative revision and replication events.

This gives us a unified interface for Unity editor tools, browser-based administration and offline Python tooling.

### 5. World Mutation System

Core architectural subsystem

The proposed command pipeline is:

#### 01

WorldCommand

Requested change and actor intent

#### 02

Validation

Permissions, constraints, revisions, resources

#### 03

Transaction

Atomic application and conflict checking

#### 04

WorldState

Authoritative committed state

#### 05

Replication

Events, snapshots and downstream updates

#### 5.1 Command versus event

This distinction is mandatory.

A command requests something that may happen.

An event records something that has happened.

| Command           | Resulting event      |
| ----------------- | -------------------- |
| PlaceBuilding     | BuildingPlaced       |
| DemolishBuilding  | BuildingDemolished   |
| TransferOwnership | OwnershipTransferred |
| ModifyTerrain     | TerrainModified      |
| CreateRoad        | RoadSegmentCreated   |
| ProduceResource   | ResourceProduced     |
| ExecuteTrade      | TradeCompleted       |

A rejected command must never generate a successful world mutation event.

Commands can be rejected, while committed events are immutable historical facts.

#### 5.2 Command structure

Proposed logical contract:

```
{
  "command_id": "uuid",
  "command_type": "PlaceBuilding",
  "schema_version": 1,
  "actor": {
    "actor_id": "uuid",
    "actor_type": "player",
    "permissions_context": "uuid"
  },
  "target_region": "region-id",
  "expected_revisions": {
    "world": 120,
    "entities": {
      "entity-id": 14
    }
  },
  "simulation_time": {
    "tick": 250000
  },
  "payload": {},
  "idempotency_key": "uuid"
}
```

The example is a conceptual contract, not a finalized wire schema.

Important fields:

- `command_id`: identifies the request.
- `actor`: identifies who initiated it.
- `target_region`: identifies the authoritative world partition.
- `expected_revisions`: protects against stale modifications.
- `simulation_time`: defines when the operation enters the simulation timeline.
- `payload`: command-specific parameters.
- `idempotency_key`: prevents duplicate application.

The actor's identity is not sufficient to establish permission. Permissions must be evaluated against current authoritative ownership, faction membership, jurisdiction and access policies.

#### 5.3 Validation stages

I recommend dividing validation into distinct stages.

Stage A – Structural validation

- Valid command type.
- Supported schema version.
- Valid entity identifiers.
- Correct units and coordinate frames.
- Valid numeric ranges.

Stage B – Authorization

- Actor identity.
- Ownership.
- Permissions.
- Faction relationships.
- Administrative authority.

Stage C – World validation

- Target exists.
- Placement is physically valid.
- Terrain conditions permit the operation.
- Required dependencies exist.
- Relevant entity revisions match.

Stage D – Resource validation

- Required materials exist.
- Resources are available.
- Accounts permit the transaction.
- Resources are not already reserved.

Stage E – Transaction validation

- No conflicting mutation.
- All required changes can commit.
- Transaction boundary is valid.
- Cross-region ownership is respected.

Only after these stages can the command become an authoritative mutation.

#### 5.4 Transactions and atomicity

A transaction must guarantee that a logical operation either commits completely or does not commit.

Example: constructing a factory.

The operation may require:

1. Deducting construction materials.
2. Reserving land.
3. Creating the building entity.
4. Creating infrastructure connections.
5. Establishing ownership.
6. Creating construction jobs.

We must not end up with materials deducted but no factory created.

For operations involving multiple regions, I recommend avoiding unrestricted global transactions.

Instead:

- Prefer one authoritative region per transaction.
- Use explicit multi-region coordination for operations that genuinely require it.
- Use reservation/prepare/commit workflows where distributed atomicity is necessary.
- Support compensating transactions for recoverable distributed workflows.

Do not use a global database transaction as the default mechanism for an entire planet.

#### 5.5 Event sourcing versus conventional persistence

This is an important architectural choice.

Three approaches exist:

| Architecture        | Advantages                                  | Limitations                          |
| ------------------- | ------------------------------------------- | ------------------------------------ |
| State snapshots     | Fast loading and querying                   | Weak history without additional logs |
| Full event sourcing | Audit, replay, historical reconstruction    | Event volume and replay complexity   |
| Hybrid              | Current state + event history + checkpoints | More complicated persistence design  |

I recommend the hybrid approach.

Canonical world state is maintained in efficient materialized representations. Every committed mutation also generates an immutable historical record.

Snapshots periodically capture the state at a known revision.

This gives us:

- Fast world loading.
- Historical reconstruction.
- Undo/redo.
- Administrative auditing.
- What-if branches.
- Reproducible mutation sequences.

AWS and Microsoft document event sourcing's benefits as well as its significant costs: schema evolution, idempotency, replay, optimistic concurrency and projection consistency. It should therefore be applied selectively to authoritative mutations, not indiscriminately to every continuously changing physical value.

*(zdroj: AWS Prescriptive Guidance +2)*

Important distinction: Vehicle position changing at every physics tick does not necessarily need a permanent event in the world mutation history. Continuous simulation state should have its own snapshot/checkpoint and replay strategy.

#### 5.6 Undo and redo

Undo must not mean deleting an already committed historical event.

Instead, every reversible mutation should define a compensating operation.

Example:

`BuildingPlaced → BuildingDemolished` is not a true inverse if the building has since been damaged, modified, occupied or used in production.

A valid undo operation must check whether the original conditions still permit reversal.

Recommended model:

- Original command.
- Committed event.
- Inverse/compensation specification.
- Current-state validation.
- New compensating command.
- New committed event.

For complex operations, undo may require manual conflict resolution.

#### 5.7 Versioning and concurrent editing

The strongest general approach is optimistic concurrency control.

A client edits revision 120.

Another user commits revision 121.

The first user's edit is then submitted against revision 120.

If the changes overlap, the server rejects the stale operation or returns a conflict requiring reconciliation.

OpenStreetMap offers a useful real-world reference: versioned objects, optimistic locking, changesets and conflict responses. It also demonstrates why a changeset and an atomic transaction are not automatically the same thing.

*(zdroj: OpenStreetMap Wiki +1)*

I recommend two separate collaboration mechanisms:

A. Authoritative world editing

- Revision-based concurrency.
- Server-side validation.
- Atomic transactions.
- Conflict rejection or explicit resolution.

B. Collaborative editor workspace

- Shared drafts.
- Live cursor and selection state.
- Collaborative blueprint editing.
- Intermediate changes.
- Non-authoritative previews.

CRDT technology such as Yjs is worth evaluating for the second category. Its shared data structures automatically merge concurrent operations without ordinary merge conflicts.

*(zdroj: Yjs Docs +1)*

I would not use CRDT merging as the authority mechanism for physical world changes. Two independently accepted edits cannot automatically guarantee that a building fits, that materials exist or that the resulting geometry is valid.

### 6. Economic simulation

The economy should be a persistent simulation domain rather than a set of gameplay-specific counters.

The fundamental model should distinguish:

1. Physical resources.
2. Inventories.
3. Production.
4. Transportation.
5. Ownership.
6. Markets.
7. Financial accounting.
8. Consumption.

#### 6.1 Resource model

Resources should have a universal identity and typed properties.

Examples:

- Iron ore.
- Refined steel.
- Water.
- Fuel.
- Electricity.
- Food.
- Timber.
- Semiconductor components.
- Construction materials.

A resource definition should include:

| Property                | Description                                  |
| ----------------------- | -------------------------------------------- |
| Resource ID             | Stable identity                              |
| Physical properties     | Density, state, temperature where applicable |
| Measurement unit        | kg, m³, kWh, units, etc.                     |
| Storage constraints     | Temperature, pressure, capacity              |
| Conversion rules        | Refinement and processing                    |
| Degradation             | Spoilage, corrosion, energy loss             |
| Economic classification | Tradable, restricted, strategic              |
| Provenance              | Production source and history                |

Do not represent every resource as a generic floating-point number.

A quantity of water, an electrical energy reserve and a number of electronic components are not interchangeable data types.

Use typed quantities with explicit units and conversion rules.

#### 6.2 Inventory system

Inventory should be a persistent entity or capability.

Examples:

- Factory warehouse.
- Player backpack.
- Cargo ship.
- Fuel tank.
- Planetary resource deposit.
- Spacecraft storage.

Inventory operations must be transactional.

A resource transfer should never result in duplication.

For a transfer:

\\[ Q\_{\text{source,new}}=Q\_{\text{source,old}}-Q \\]

\\[ Q\_{\text{destination,new}}=Q\_{\text{destination,old}}+Q \\]

The transfer is committed only if both sides succeed.

For lossy physical transportation, the loss must be explicitly represented and accounted for.

#### 6.3 Production chains

Production should be modeled as a directed dependency graph.

Example:

`Iron Ore → Processing → Steel → Components → Machines → Factory Equipment`

A production recipe can be described as:

\\[ \mathbf{y}=f(\mathbf{x},\Delta t,\mathbf{c}) \\]

Where:

- \\(\mathbf{x}\\) = input resources.
- \\(\Delta t\\) = production time.
- \\(\mathbf{c}\\) = machine and environmental conditions.
- \\(\mathbf{y}\\) = output resources.

For a simple linear recipe:

\\[ Q\_{\text{output}}=r\cdot Q\_{\text{input}} \\]

But realistic production needs more:

- Machine capacity.
- Energy requirements.
- Labor.
- Maintenance.
- Temperature.
- Waste.
- Production efficiency.
- Input availability.
- Output storage capacity.

A factory can exist physically but be economically unproductive because it lacks electricity, labor, materials or transport.

#### 6.4 Logistics

Logistics should be a separate simulation domain.

It consumes:

- Physical transportation networks.
- Vehicle availability.
- Storage capacity.
- Road and railway conditions.
- Travel time.
- Fuel and energy.
- Ownership and permissions.

A logistics order is not a teleportation operation.

A shipment must have an actual location, quantity, transport state and estimated arrival.

The physical transport subsystem determines whether the cargo reaches its destination.

#### 6.5 Markets and prices

I recommend separating three price concepts:

- Production cost.
- Market price.
- Transaction price.

They must not be assumed equal.

A market can use supply and demand:

\\[ P=f(S,D) \\]

A simple illustrative price model:

\\[ P\_{t+1}=P_t\left[1+\alpha\frac{D_t-S_t}{\max(S_t,\epsilon)}\right] \\]

Where:

- \\(P_t\\) = current price.
- \\(S_t\\) = available supply.
- \\(D_t\\) = demand.
- \\(\alpha\\) = price adjustment coefficient.

This is a proposed simplified model, not a complete economic theory.

For larger economies, we should investigate:

- Input-output economic models.
- Network-flow optimization.
- Multi-agent markets.
- Discrete-event production simulation.
- Supply-demand equilibrium.
- Resource allocation optimization.

A useful distinction: economic markets may operate at regional or planetary scales, while individual inventories and physical transport remain local.

#### 6.6 Ownership and accounting

Ownership is a legal/economic relation, not a physical property.

A building can be:

- Physically located in a region.
- Owned by a corporation.
- Operated by a faction.
- Financed by another organization.
- Accessible to authorized players.

Ownership transfers must be World Mutation System transactions.

Financial accounts should use explicit debit/credit entries or equivalent double-entry accounting for monetary balances.

The economic subsystem must not independently create physical materials when a transaction is completed.

Trading changes ownership and inventories; production changes physical resource quantities.

These are distinct operations.

### 7. Multiplayer authority and replication

The architecture must support a persistent world with potentially thousands of concurrent players.

This cannot be solved by simply broadcasting every entity update to every client.

I recommend separating three concepts:

1. Simulation authority.
2. Persistence ownership.
3. Network visibility.

These are not the same thing.

A server may simulate a region, a database service may persist it, and several clients may receive different representations of it.

#### 7.1 Authoritative simulation

Rust owns the canonical authoritative simulation state.

Node.js handles application-level orchestration, authentication, session management, control-plane operations and coordination with persistence services.

Unity receives authoritative snapshots and events, performs presentation and may predict permitted local interactions.

The client never commits authoritative world mutations directly.

#### 7.2 Entity authority

Every actively simulated entity must have an identifiable simulation authority.

Proposed logical fields:

```
{
  "entity_id": "uuid",
  "authority": {
    "region_id": "region-42",
    "server_id": "simulation-node-7",
    "authority_epoch": 18
  },
  "revision": 245,
  "replication_policy": "interest_based"
}
```

`authority_epoch` is important.

When simulation ownership transfers between servers, the epoch changes. This prevents an old server from continuing to submit valid-looking updates after losing authority.

#### 7.3 Replication types

Different gameplay systems need different replication strategies.

| Data                    | Recommended replication             |
| ----------------------- | ----------------------------------- |
| Character movement      | Predicted state + correction        |
| Remote vehicles         | Interpolated snapshots              |
| Building placement      | Reliable committed events           |
| Inventory               | Authoritative state changes         |
| Economy                 | Transaction events and projections  |
| Terrain                 | Versioned regional updates          |
| Distant planetary state | Low-frequency aggregated updates    |
| Editor workspace        | Collaborative draft synchronization |

Open 3D Engine provides useful references for server-authoritative multiplayer, component-based replication, RPCs, prediction, reconciliation and delta synchronization.

*(zdroj: docs.o3de.org +2)*

Unity Netcode documentation also illustrates how fixed simulation ticks, snapshots, prediction and interpolation can be separated.

*(zdroj: docs.unity.cn)*

0.51.1-preview\.21+2



These are architectural references, not recommendations to move simulation authority into Unity.

#### 7.4 Determinism

There is an important architectural trap here.

A fully deterministic lockstep model is attractive for RTS and strategy games.

Factorio documents how deterministic lockstep works: clients execute the same simulation from synchronized inputs, and any difference can produce desynchronization.

*(zdroj: Factorio Wiki +1)*

However, our universe contains:

- Different numerical solvers.
- Continuous physical simulation.
- Different simulation fidelity levels.
- Astronomical time scales.
- Different execution frequencies.
- Potentially distributed regional servers.

We should not assume that the entire universe can run as one deterministic lockstep simulation.

Instead, I recommend three determinism levels:

| Level | Guarantee                                                         | Application                             |
| ----- | ----------------------------------------------------------------- | --------------------------------------- |
| D1    | Authoritative state and ordered commands                          | General multiplayer                     |
| D2    | Reproducible subsystem replay within defined numerical tolerances | Economy, strategy, world mutations      |
| D3    | Bitwise deterministic execution                                   | Selected discrete simulation subsystems |

D3 should be an explicit subsystem property, not a global engine assumption.

The Rust simulation runtime must define the canonical ordering of commands and state transitions.

For D2 replay, record all external inputs, simulation configuration, algorithm versions, random seeds and relevant environment revisions.

For continuous physical simulation, snapshots and authoritative corrections remain necessary.

#### 7.5 Interest management

A client must only receive data relevant to its current needs.

The visibility model should account for:

- Physical distance.
- Line of sight where relevant.
- Gameplay context.
- Ownership.
- Permissions.
- Active interactions.
- Strategic observation.
- Simulation fidelity.
- User interface requirements.

For example, an RTS player may need to see a whole regional factory network, while a first-person player needs detailed nearby collision and animation state.

These are different projections of the same authoritative world.

#### 7.6 Regional partitioning

The world should be partitionable into simulation regions.

Each region has:

- An authoritative simulation owner.
- A persistence checkpoint.
- A mutation sequence.
- An entity registry.
- A spatial index.
- A replication scope.
- Cross-region interaction contracts.

A region transfer must preserve:

- Entity identity.
- Position and velocity.
- Simulation time.
- Active tasks.
- Ownership.
- Pending commands.
- Physical interaction constraints.

Do not transfer only the visual representation.

The hardest problem will be entities interacting across region boundaries, particularly vehicles, aircraft, projectiles and large physical structures.

This must be coordinated with Worker 02's physical simulation architecture.

### 8. Undo, history, branching and What-If simulation

This subsystem has an opportunity to become one of the most distinctive parts of Planetary Engine.

A persistent world should support more than saving and loading.

It should support historical inspection and alternative futures.

I propose a world history architecture with four operations:

History

Inspect committed mutations and their authors, causes and revisions.

Compensation

Reverse eligible changes through new authoritative commands.

Branching

Create alternative world timelines from a known revision.

What-If Simulation

Evaluate alternative construction, economic, environmental or strategic decisions without changing the canonical world.

#### 8.1 World branches

A branch should reference:

- Parent world.
- Parent revision.
- Parent simulation time.
- World configuration version.
- Subsystem versions.
- Branch-specific mutations.
- Branch-specific simulation inputs.

It should not immediately duplicate the entire planet.

Use copy-on-write or equivalent immutable checkpoint references.

A branch initially shares the parent state and stores only its differences.

This is particularly important for planetary-scale worlds.

#### 8.2 Branch merge

Merging a branch into the canonical world is not simply applying its event list.

The branch may contain changes that are no longer valid.

For example:

- A factory was constructed on land that another player has since occupied.
- A resource deposit has been depleted.
- A road network has been redesigned.
- A building blueprint has been updated.
- The economy has changed significantly.

Therefore, branch merging should be implemented as a new command-validation process against the target world's current revision.

#### 8.3 Reproducibility

A reproducible world branch needs more than a seed.

It needs:

- World-generation algorithm version.
- Initial world-state reference.
- Simulation subsystem versions.
- Deterministic random streams.
- Command/event history.
- External input history.
- Simulation clock.
- Relevant configuration snapshots.

This is essential for scientific experimentation and debugging as well as gameplay.

### 9. Proposed module structure

I recommend organizing the Rust runtime around domain boundaries rather than gameplay genres.

The following is a proposed logical structure, not an instruction to create these modules during the research phase.

```
planetary_engine/
│
├── core/
│   ├── entity_identity/
│   ├── world_state/
│   ├── simulation_clock/
│   ├── coordinates/
│   ├── revisions/
│   └── capabilities/
│
├── gameplay/
│   ├── interaction/
│   ├── abilities/
│   ├── inventory/
│   ├── progression/
│   ├── missions/
│   └── permissions/
│
├── strategy/
│   ├── commands/
│   ├── factions/
│   ├── territory/
│   ├── military/
│   ├── ai/
│   └── task_management/
│
├── construction/
│   ├── blueprints/
│   ├── placement/
│   ├── construction_jobs/
│   ├── infrastructure/
│   └── terrain_mutation/
│
├── world_mutation/
│   ├── commands/
│   ├── validation/
│   ├── transactions/
│   ├── revisions/
│   ├── history/
│   ├── compensation/
│   └── replication/
│
├── economy/
│   ├── resources/
│   ├── inventory/
│   ├── production/
│   ├── logistics/
│   ├── markets/
│   ├── ownership/
│   └── accounting/
│
├── multiplayer/
│   ├── authority/
│   ├── regions/
│   ├── replication/
│   ├── interest_management/
│   └── migration/
│
├── persistence/
│   ├── snapshots/
│   ├── event_history/
│   ├── checkpoints/
│   └── world_branches/
│
└── interfaces/
    ├── application_api/
    ├── simulation_api/
    ├── client_replication/
    └── editor_api/
```

#### 9.1 Technology responsibilities

| Technology | Responsibility                                                                                    |
| ---------- | ------------------------------------------------------------------------------------------------- |
| Rust       | Gameplay state, commands, validation, transactions, economic simulation, authoritative simulation |
| Node.js    | Sessions, accounts, authentication, API gateway, orchestration, control plane                     |
| Python     | Offline world generation, economic experiments, balancing, scientific analysis, bulk editing      |
| Unity/C#   | Gameplay presentation, input, interfaces, local prediction, world editor                          |

The most important boundary:

Python and Unity may prepare and request world changes. Rust validates and commits them.

Node.js should not become a second authoritative simulation runtime.

#### 9.2 ECS considerations

ECS is suitable for the runtime's entity-oriented execution.

However, I would avoid forcing every persistent object into one universal ECS storage model.

Some data is better represented as:

- Spatial graphs.
- Terrain tiles.
- Resource databases.
- Economic ledgers.
- Immutable blueprint definitions.
- Regional aggregate state.

ECS should be a runtime execution mechanism, not the mandatory storage format for the entire universe.

Bevy's official documentation specifically identifies system access analysis and parallel execution as strengths, but also notes that default parallel scheduling is not inherently deterministic.

*(zdroj: Rust +2)*

### 10. API and data contracts

These are the proposed public contracts between the major subsystems.

| Interface         | Direction                       | Responsibility                              |
| ----------------- | ------------------------------- | ------------------------------------------- |
| `WorldQuery`      | Client/editor → Rust            | Read authoritative world state              |
| `WorldCommand`    | All actors → Rust               | Request world mutation                      |
| `WorldEvent`      | Rust → consumers                | Notify committed changes                    |
| `ConstructionAPI` | Gameplay/editor → Rust          | Blueprint and construction operations       |
| `EconomyAPI`      | Gameplay → Rust                 | Resource, production and trading operations |
| `OwnershipAPI`    | Gameplay → Rust                 | Ownership and access relations              |
| `StrategyAPI`     | Client/AI → Rust                | Issue strategic commands                    |
| `ReplicationAPI`  | Rust → Unity                    | Publish authorized world state              |
| `HistoryAPI`      | Editor/admin → Rust             | Query historical revisions                  |
| `BranchAPI`       | Admin/scientific tooling → Rust | Create and inspect alternate timelines      |

#### 10.1 World query contract

Conceptually:

```
{
  "request_id": "uuid",
  "query_type": "regional_snapshot",
  "region_id": "region-42",
  "requested_revision": 120,
  "include": [
    "entities",
    "infrastructure",
    "ownership",
    "resources"
  ],
  "consistency": "authoritative_snapshot"
}
```

A query should specify the consistency level.

A cached strategic overview and an authoritative placement check are not equivalent.

#### 10.2 Mutation result

```
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
```

The distinction between `committed` and `derived_updates` is deliberate.

A successful world transaction must not imply that every dependent subsystem has already finished rebuilding its data.

#### 10.3 Event envelope

Every event should carry:

- Event ID.
- Event type.
- Schema version.
- Entity ID or aggregate ID.
- Region ID.
- World revision.
- Simulation tick.
- Causation ID.
- Correlation ID.
- Actor identity.
- Payload.

This supports auditing, causality tracking, replay, replication and diagnostics.

### 11. Integration dependencies

Worker 06 is highly dependent on the other architecture workers.

The interfaces below should be treated as explicit coordination requirements.

| Worker / subsystem              | Required contract                                                       | Importance |
| ------------------------------- | ----------------------------------------------------------------------- | ---------- |
| Worker 01 – Environment         | Terrain, materials, atmosphere, water and environmental queries         | Critical   |
| Worker 02 – Physics             | Entity state, collision, forces, physical integration, simulation ticks | Critical   |
| Worker 03 – Specialized physics | Vehicle, character, aircraft and spacecraft capabilities                | High       |
| Worker 04 – Celestial           | Universal time, coordinate frames, orbital state, gravity               | High       |
| World generation                | Stable geographic addressing and reproducible generated objects         | Critical   |
| Persistence                     | Snapshots, revisions, recovery and historical state                     | Critical   |
| Multiplayer                     | Authority, regional ownership, migration and replication                | Critical   |
| Unity client                    | Editor, gameplay interaction, presentation and prediction               | High       |

#### 11.1 Most important dependency: physical consequences of construction

Consider placing a bridge.

The construction system:

1. Validates placement.
2. Reserves resources.
3. Creates a construction entity.
4. Progresses construction.
5. Publishes the completed structural geometry.

The physics system then needs to:

- Register the bridge's collision geometry.
- Establish its physical properties.
- Determine its interaction with vehicles and characters.
- Account for structural failure if supported.

The environment system may need to:

- Update water flow.
- Update terrain interactions.
- Update environmental fields.

Navigation must then update its graph.

The economic system may register a new infrastructure asset and associated maintenance costs.

This is why the World Mutation System must publish structured, versioned events rather than simply notify clients that something changed.

#### 11.2 Cross-subsystem consistency

I recommend defining a dependency revision vector.

For example:

```
World Revision:       121
Terrain Revision:      44
Collision Revision:    43
Navigation Revision:   42
Economy Revision:      89
Replication Revision: 120
```

These numbers are illustrative.

They make it possible to identify stale derived systems.

A construction operation can commit at revision 121 while collision and navigation are still processing revisions 44 and 43.

The engine must explicitly define which operations require derived-system synchronization before they can proceed.

For example, vehicle navigation may temporarily avoid a newly built road until the navigation graph catches up.

### 12. Performance and scalability analysis

The biggest performance risk is not individual gameplay systems. It is the interaction between them.

A single building can participate in physical simulation, construction, economy, logistics, ownership, replication and persistence.

If every subsystem polls every entity every tick, the architecture will not scale.

#### 12.1 Complexity estimates

These are algorithmic expectations, not measured Planetary Engine benchmarks.

| Operation                 | Expected complexity         | Optimization                              |
| ------------------------- | --------------------------- | ----------------------------------------- |
| Entity lookup             | O(1) average                | Indexed stable IDs                        |
| ECS component queries     | O(N) over matching entities | Archetype/chunk storage                   |
| Spatial lookup            | O(log N + K) typical        | Spatial partitioning                      |
| Graph pathfinding         | O((V+E) log V) typical      | Hierarchical routing                      |
| Resource inventory lookup | O(1) average                | Indexed resource IDs                      |
| Production scheduling     | Workload-dependent          | Event-driven scheduling                   |
| Transaction validation    | O(C + D)                    | Localized dependency checks               |
| Event append              | O(1) amortized              | Append-only storage                       |
| Snapshot creation         | O(S)                        | Incremental checkpoints                   |
| Replication               | O(changed state)            | Interest management and delta compression |

Where:

- N = entities.
- K = spatial results.
- V/E = graph vertices/edges.
- C = command changes.
- D = dependencies.
- S = serialized state size.

#### 12.2 Multi-rate simulation

Not every gameplay subsystem needs the same execution frequency.

I recommend aligning gameplay scheduling with the universal simulation scheduler.

Illustrative starting points for profiling, not established performance requirements:

| Subsystem                      | Candidate update rate |
| ------------------------------ | --------------------- |
| Character interaction          | 30–60 Hz              |
| Tactical combat                | 10–30 Hz              |
| Vehicle simulation             | Physics scheduler     |
| Construction progress          | 1–10 Hz               |
| Factory production             | Event-driven / 1 Hz   |
| Regional economy               | 0.1–1 Hz              |
| Strategic AI                   | 0.1–2 Hz              |
| Long-term economic development | 0.01–0.1 Hz           |

The actual rates must be determined by gameplay requirements and simulation fidelity.

A factory should not calculate its entire production state 60 times per second if nothing relevant has changed.

#### 12.3 Event-driven versus polling

Prefer event-driven processing for:

- Resource availability.
- Production completion.
- Construction stage changes.
- Ownership transfers.
- Market transactions.
- Infrastructure modifications.

Use periodic processing when continuous evolution is required.

For example, production can schedule its next completion event, while machine temperature and physical conditions may need continuous or fixed-rate simulation.

#### 12.4 Memory considerations

Avoid keeping multiple complete copies of the same world state for:

- Gameplay.
- Strategy.
- Construction.
- Economy.
- Editor.
- Replication.

These systems should operate on shared authoritative identities and appropriately isolated projections.

Historical branches should use copy-on-write checkpoints.

Replication should use client-specific projections rather than full-world copies.

The most significant memory risks will likely be:

1. Excessive historical state.
2. Large numbers of active entity components.
3. Replication buffers.
4. Spatial indexes.
5. Construction and infrastructure graphs.
6. Simultaneously active world branches.
7. Unbounded event queues.

### 13. Testing and validation plan

The architecture needs validation before implementation decisions become permanent.

I recommend dividing testing into seven groups.

#### Test group A – Universal gameplay

| Test                         | Expected result                       |
| ---------------------------- | ------------------------------------- |
| Activate racing module       | Existing world remains unchanged      |
| Activate RTS module          | Same physical entities are accessible |
| Activate construction module | Same blueprint catalogue              |
| Disable gameplay module      | Persistent world state remains        |
| Multiple modules interact    | No duplicated entity identities       |

#### Test group B – Construction

- Place building on valid terrain.
- Reject placement inside an occupied structure.
- Place a building using an external editor.
- Complete construction with sufficient resources.
- Pause construction when resources are unavailable.
- Destroy a road and invalidate affected navigation.
- Change terrain beneath an existing structure.
- Verify physical collision after construction.

#### Test group C – World Mutation

- Duplicate command submission.
- Invalid permissions.
- Stale entity revision.
- Concurrent modifications.
- Partial transaction failure.
- Server crash during commit.
- Recovery from checkpoint.
- Undo after unrelated subsequent mutations.
- Historical replay.
- Branch creation and merge.

#### Test group D – Economy

- Two factories competing for the same resources.
- Resource transfer without duplication.
- Interrupted transportation.
- Production with missing electricity.
- Storage overflow.
- Market price changes.
- Ownership transfer during production.
- Economic simulation after server restart.

#### Test group E – Multiplayer

- Multiple players constructing in the same region.
- Simultaneous edits to one building.
- Players with different permissions.
- Regional authority migration.
- Replication recovery after disconnect.
- Delayed and duplicated event delivery.
- Client prediction correction.
- Old server attempting to commit after losing authority.

#### Test group F – Reproducibility

- Replay the same command sequence.
- Compare resulting world revisions.
- Compare economic results.
- Replay with identical configuration and random seeds.
- Test version migration.
- Compare branch and canonical-world divergence.

#### Test group G – Scale

Use progressively larger synthetic scenarios.

| Scenario                           | What to measure                            |
| ---------------------------------- | ------------------------------------------ |
| 10,000 entities                    | ECS and spatial-query overhead             |
| 100,000 entities                   | Memory and scheduling                      |
| 1 million persistent entities      | Snapshot and query performance             |
| 10,000 active economic objects     | Production scheduling                      |
| 1,000 concurrent editor operations | Transaction contention                     |
| 1,000 concurrent players           | Replication and authority                  |
| Large construction batch           | Mutation throughput and derived-system lag |

These are proposed test loads, not predictions that the architecture will achieve a particular throughput.

Record:

- CPU time per subsystem.
- Peak memory.
- Command acceptance latency.
- Transaction conflict rate.
- Event backlog.
- Snapshot generation time.
- Replication bandwidth.
- Regional migration time.
- Derived-state synchronization delay.

### 14. Risk register

Critical architectural risks

| Risk                                                | Severity | Mitigation                                      |
| --------------------------------------------------- | -------- | ----------------------------------------------- |
| Gameplay systems directly mutate world state        | Critical | Mandatory mutation interface                    |
| Different genres create duplicate world entities    | Critical | Universal entity identity                       |
| Construction bypasses physics                       | Critical | Versioned physical integration                  |
| Resource duplication during concurrent transactions | Critical | Atomic inventory operations                     |
| Cross-region transaction inconsistency              | Critical | Explicit distributed transaction model          |
| Excessive event history                             | High     | Checkpoints and selective event sourcing        |
| ECS becomes the entire storage architecture         | High     | Separate runtime and persistence models         |
| Global deterministic lockstep becomes a bottleneck  | High     | Per-subsystem determinism contracts             |
| CRDT used for authoritative physical changes        | High     | Restrict CRDT to collaborative drafts           |
| Derived systems lag behind world mutations          | High     | Revision tracking and synchronization contracts |
| Blueprint updates invalidate old structures         | High     | Immutable blueprint versions                    |
| AI bypasses player-facing rules                     | High     | Common command interfaces                       |
| Branch merge corrupts canonical state               | High     | Revalidation against current revision           |
| Global economy becomes computationally expensive    | High     | Hierarchical economic aggregation               |
| Authority migration duplicates active simulation    | Critical | Epoch fencing and ownership transfer            |

### 15. Open architectural decisions

These are the decisions I would not finalize without coordinating with the other workers.

Decision 01 – ECS architecture

Should we use Bevy ECS as the primary Rust execution layer, or maintain a smaller engine-independent entity abstraction?

My preference: evaluate Bevy ECS first

Decision 02 – Mutation transaction boundaries

Should a world transaction be restricted to one region, with distributed workflows for cross-region changes?

My preference: regional atomicity

Decision 03 – Event sourcing scope

Which state changes require permanent historical events, and which belong in periodic simulation snapshots?

My preference: hybrid persistence

Decision 04 – Collaborative editing

Should editor drafts use Yjs or another CRDT, with the Rust runtime retaining final authority?

My preference: separate draft collaboration

Decision 05 – Economic fidelity

Should the economy support both detailed physical production and aggregated regional macroeconomic simulation?

My preference: hierarchical economy

Decision 06 – Construction representation

How much construction detail is physically simulated, and when can large infrastructure be represented by aggregated geometry?

My preference: adaptive fidelity

Decision 07 – World history and branching

Should What-If branches be first-class persistent worlds with independent simulation timelines?

My preference: yes

### 16. Recommended next steps

I would organize the next architecture phase in this order:

Priority 1 – Establish shared contracts

Before choosing implementation libraries, finalize:

- Universal entity identity.
- World revision model.
- Simulation tick and universal time.
- Coordinate and spatial addressing.
- Physical state interface.
- Regional authority.
- World mutation event format.

These are cross-worker dependencies.

Priority 2 – Finalize World Mutation System

This is the most important deliverable from Worker 06.

Specify:

- Command schemas.
- Validation interfaces.
- Transaction boundaries.
- Event contracts.
- Idempotency.
- Revision handling.
- Undo/compensation.
- Branching.
- Persistence integration.

Priority 3 – Finalize construction and economy domains

Specify their data models independently, then define how they consume the mutation interface.

Priority 4 – Define gameplay module contracts

Create a common module lifecycle:

`Register → Activate → Execute → Suspend → Resume → Deactivate`

Gameplay modules should declare their capabilities, dependencies, state requirements and simulation scheduling needs.

Priority 5 – Architecture validation

Create reference scenarios for racing, FPS, RTS, construction, industrial economy and external editing.

Every scenario must interact with the same world state.

## 17. Primary research references

The following are the most useful technical references identified during this investigation.

| Technology / research                                                                                                                                          | Architectural relevance                      |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------- |
| [Bevy ECS documentation](https://docs.rs/bevy/latest/bevy/ecs/)                                                                         | Rust ECS and parallel execution              |
| [Unreal Gameplay Ability System](https://dev.epicgames.com/documentation/en-us/unreal-engine/gameplay-ability-system-for-unreal-engine) | Modular gameplay abilities                   |
| [O3DE Multiplayer](https://docs.o3de.org/docs/user-guide/networking/multiplayer/overview/)                                              | Server authority and replication             |
| [Microsoft Event Sourcing](https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing)                                | History, replay and concurrency              |
| [AWS Event Sourcing](https://docs.aws.amazon.com/prescriptive-guidance/latest/cloud-design-patterns/event-sourcing-pattern.html)        | Event architecture and trade-offs            |
| [OpenStreetMap API](https://wiki.openstreetmap.org/wiki/OSM_Protocol_Version_0.6)                                                       | Versioning, changesets and editing conflicts |
| [Yjs documentation](https://docs.yjs.dev/)                                                                                              | Collaborative editing and CRDT               |
| [Factorio determinism](https://wiki.factorio.com/Desynchronization)                                                                     | Deterministic lockstep limitations           |
| [Unity prediction architecture](https://docs.unity.cn/Packages/com.unity.netcode%401.3/manual/prediction-high-level-explanation.html)   | Fixed ticks and client reconciliation        |
| [FMI standard](https://fmi-standard.org/docs/main/)                                                                                     | Coupled simulation systems and scheduling    |

# Final architectural recommendation

Recommended direction

I would make one design decision central to the entire Planetary Engine:

The World Mutation System is the universal interface between intention and persistent reality.

It should not be a construction tool, an RTS service or an administrative API. It should be a fundamental engine subsystem.

Everything that changes the persistent world must ultimately pass through it.

That includes:

- Players.
- AI agents.
- RTS commands.
- Construction systems.
- Economic transactions.
- Environmental modifications.
- World-generation tools.
- External editors.
- Administrative operations.

The physical simulation remains responsible for determining how the world behaves after changes occur.

The gameplay layer determines why an actor wants to change something.

The mutation layer determines whether that change is valid and commits it.

The persistence layer guarantees that the result survives.

The replication layer makes the result visible to authorized clients.

## The resulting architecture

Any Gameplay / Editor / AI / Administrator

Universal World Mutation System

Validation · Transactions · Revisions · History

One Authoritative Persistent World

Entities · Infrastructure · Economy · Environment

Simulation

Rust

Persistence

Versioned state

Control plane

Node.js

Presentation

Unity

The resulting system can host an RTS game, a racing game, an industrial simulator, a survival game and an external planet editor simultaneously without creating five separate versions of reality.

My strongest recommendation: Resolve the entity identity, revision, time, transaction and physical-integration contracts with Workers 01–04 before freezing Worker 06's interfaces. These are the foundations that will determine whether the universal gameplay architecture remains genuinely universal or gradually becomes a collection of disconnected subsystems.