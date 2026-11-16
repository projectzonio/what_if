PLANETARY ENGINE
WORKER 02 – UNIVERSAL PHYSICS RUNTIME
Technical Architecture Specification · Research Phase
Architecture research
Rust runtime
No implementation

1. Executive summary
The Universal Physics Runtime should not be designed as one enormous physics solver.

It should be designed as a multi-domain simulation framework with a shared physical state model and specialized numerical solvers.

This distinction is fundamental to Planetary Engine.

A character walking on a planet, a car driving through a city, an aircraft flying through an atmosphere, and a spacecraft orbiting a moon do not operate under identical numerical conditions. They share physical principles, but their simulation requirements, coordinate systems, time scales and appropriate numerical methods differ considerably.

Trying to solve all of them using one rigid-body solver would introduce unnecessary computational costs and numerical limitations.
Primary architectural recommendation
Separate the runtime into five cooperating layers:
LAYER 1 — Simulation Coordinator

Authoritative time · Tick scheduling · State transitions · Rollback


LAYER 2 — Universal Physical State

Entities · Mass · Inertia · Position · Velocity · Forces · Constraints


LAYER 3A

Rigid Body Solver

Vehicles, characters, objects, structures


LAYER 3B

Domain Solvers

Aerodynamics, propulsion, orbital dynamics




LAYER 4 — Collision & Environment

Broadphase · Narrowphase · Contacts · Terrain · Atmosphere · Water


LAYER 5 — Execution & Persistence

Parallel execution · Snapshots · Replication · Deterministic state history


The critical design principle is that specialized systems must interact through common physical contracts rather than directly depending on one another's internal implementations.

For example, an aircraft should not need to know whether the ground below it is represented by a Unity terrain mesh, a planetary heightfield, or a remote server-side terrain tile. It should interact with the environment through standardized queries and physical responses.

2. Fundamental architectural decision: three independent simulation dimensions
There is an important distinction that should be established before designing the runtime.

Planetary Engine needs three independent forms of partitioning.
Dimension	Purpose	Example
Spatial domain	Coordinate precision and environment	Earth surface, atmosphere, orbit
Constraint island	Determines which bodies physically interact	Car, wheels, suspension, road
Time domain	Determines numerical integration frequency	Vehicle 120 Hz, orbital system 1 Hz

These dimensions must not be coupled.

For example, two vehicles can belong to the same geographic simulation region but belong to different constraint islands. Conversely, a vehicle and a road can be in different storage structures but still participate in the same collision calculation.

A third distinction is necessary for multiplayer: simulation ownership is not necessarily the same as spatial partitioning. Server authority, physical interaction and geographic storage need explicit interfaces.
2.1 Hierarchical coordinate architecture
A universal physics runtime cannot use one ordinary Cartesian coordinate system for the entire universe.

Consider the numerical scales:
- A vehicle operates in meters.
- A planet has a radius of millions of meters.
- A planetary orbit operates at astronomical-unit scales.
- Interstellar distances operate at light-year scales.
A single floating-point coordinate system eventually loses the precision needed for local collision detection.

I recommend hierarchical reference frames:
Universal / Barycentric Frame

Astronomical positions and velocities


Planetary Frame

Planet center, rotation and gravity


Orbital Frame

Spacecraft and orbital mechanics




Regional / Local Frame

Local tangent coordinates and floating origin


Physics Island

Local rigid bodies, colliders and constraints


The frame system should support:
- Parent-child coordinate transformations.
- Position and velocity transformations.
- Rotational transformations.
- Time-dependent reference frames.
- Deterministic migration between regions.
- Local origin rebasing.
NASA JPL's SPICE system is an important reference for this architecture. It explicitly separates reference frames, frame centers, coordinate systems and time-dependent transformations. JPL uses ICRF and TDB as fundamental reference conventions for its solar-system ephemerides. 

ssd.jpl.nasa.gov+2

Recommended numerical representation:
- Global position: hierarchical sector coordinates plus f64 local offsets.
- Local rigid-body calculations: bounded local coordinates, with f32 or f64 selected and validated per backend.
- Orientation: normalized quaternion.
- Local velocities and forces: precision selected by the simulation domain.
- Astronomical time: integer simulation time with an explicit conversion to scientific time scales.
There is a particularly interesting production reference here: Avian supports f64 world-space coordinates while retaining f32 broadphase structures and solver data. Its documentation explains that this reduces the cost of double precision, but broadphase bounds become increasingly conservative at large distances. At approximately \(10^7\) units, f32 coordinate quantization reaches roughly one unit. 

Rust+1

This reinforces an important architectural conclusion:

Double precision does not eliminate the need for local simulation coordinates.

The recommended solution is a hierarchical world with local physics origins, not an enormous global physics scene.

3. Core simulation and numerical integration
3.1 Fixed versus variable timestep
Three approaches are relevant.
Method	Advantages	Limitations
Fixed timestep	Stable numerical conditions, reproducible stepping	Requires accumulator and overload management
Variable timestep	Flexible scheduling and simple integration with frame loops	Variable numerical error, instability and tunneling
Adaptive timestep	Automatically responds to changing numerical difficulty	More complicated scheduling, reproducibility and rollback

For the authoritative interactive physics runtime, fixed timestep should be the default.

The rendering frequency must be independent of the physics frequency.

A client rendering at 144 FPS must not cause a different physical simulation from a client rendering at 60 FPS.

The established accumulator model described by Glenn Fiedler and used in production engines such as PhysX provides the appropriate foundation. 

Gaffer On Games+1

Proposed initial timing configuration
Parameter	Initial value
Authoritative interactive tick	60 Hz
Physics substeps	Configurable, initially 1–4
High-precision local simulation	120 Hz or higher when required
Rendering	Independent
Simulation time	Integer tick counter
Wall-clock time	Scheduling only

These are proposed starting values, not measured Planetary Engine performance results. Each domain requires validation.

A simulation clock should use an integer tick counter and an explicit rational timestep. It should not accumulate floating-point elapsed time indefinitely.

For example:
\[
t_n = n \cdot \frac{1}{60}
\]

The value of \(n\) is authoritative. Rendering interpolation uses the fractional accumulator without changing the simulation's actual state.
3.2 Numerical integrators
The choice of integrator must depend on the physical system.
Explicit Euler
\[
v_{n+1}=v_n+a_n\Delta t
\]

\[
x_{n+1}=x_n+v_n\Delta t
\]

Simple but poorly suited to many stiff mechanical systems. It can introduce significant energy errors and instability.
Semi-implicit Euler
\[
v_{n+1}=v_n+a_n\Delta t
\]

\[
x_{n+1}=x_n+v_{n+1}\Delta t
\]

Recommended default for rigid bodies
This method updates velocity before position. It has significantly better stability characteristics than explicit Euler for typical game physics.

MuJoCo explicitly identifies semi-implicit Euler as a standard physics-simulation method and does not implement explicit Euler as a supported integrator. 

MuJoCo Documentation+1

Verlet / Velocity Verlet
A second-order integration method particularly useful for conservative systems.
\[
x_{n+1}=x_n+v_n\Delta t+
\frac{1}{2}a_n\Delta t^2
\]

Velocity Verlet also evaluates acceleration at the new position to update velocity.

Advantages:
- Good long-term energy behavior in suitable conservative systems.
- Time-reversible under appropriate conditions.
- Useful for orbital and gravitational simulations.
Limitations:
- Requires force evaluations at different points in time.
- More complicated with velocity-dependent forces, impacts and discontinuous constraints.
Runge-Kutta 4
A fourth-order integration method using four derivative evaluations per step.

It provides high accuracy for smooth differential equations but is not automatically superior for constrained rigid-body simulation.

It is also not symplectic, which makes it less attractive as a universal long-duration orbital integrator.

MuJoCo documents both RK4 and its limitations when compared with implicit single-step integration for velocity-dependent forces. 

MuJoCo Documentation+1

Adaptive high-order integration
For astronomical dynamics, REBOUND provides particularly relevant examples:
- WHFast: symplectic integration for planetary systems dominated by a central gravitational body.
- IAS15: adaptive fifteenth-order integration for high-accuracy gravitational simulations.
- TRACE: hybrid integration switching methods around close encounters.
WHFast requires an appropriate fixed timestep and is not suitable for arbitrary close encounters. IAS15 handles such encounters with adaptive steps. 

REBOUND+2

3.3 Recommended integrator allocation
Simulation subsystem	Preferred method
Rigid bodies	Semi-implicit Euler
Vehicle suspension	Semi-implicit Euler + substeps
Character dynamics	Semi-implicit Euler
Aircraft	Semi-implicit Euler
Rocket propulsion	Semi-implicit Euler with variable mass
Orbital mechanics	Symplectic integrator
High-accuracy orbital analysis	Adaptive RK
Soft constraints	XPBD
Fluid dynamics	Dedicated numerical solver

The universal runtime should expose integrator interfaces rather than forcing every subsystem to use the same method.

4. Rigid body dynamics
4.1 Mathematical model
A rigid body is represented by its position, orientation, linear velocity, angular velocity and mass properties.

The universal state must contain:
Property	Symbol	Description
Position	\(x\)	Center-of-mass position
Orientation	\(q\)	Quaternion rotation
Linear velocity	\(v\)	Translational velocity
Angular velocity	\(\omega\)	Rotational velocity
Mass	\(m\)	Body mass
Inertia tensor	\(I\)	Resistance to angular acceleration
Force	\(F\)	Applied external force
Torque	\(\tau\)	Applied rotational force

Translational motion:
\[
\frac{dx}{dt}=v
\]

\[
m\frac{dv}{dt}=F
\]

Rotational motion:
\[
\frac{dL}{dt}=\tau
\]

where angular momentum is:
\[
L=I_{world}\omega
\]

The world-space inertia tensor is obtained by rotating the body-space inertia tensor:
\[
I_{world}=R I_{body} R^T
\]

For a general rotating rigid body:
\[
I_{world}\dot{\omega}
+\omega\times(I_{world}\omega)=\tau
\]

This distinction matters when simulating aircraft, spacecraft, rotating machinery and vehicles with significant angular momentum.
4.2 Mass and inertia
Mass and angular inertia must be independent physical properties.

Two objects with identical mass but different geometry should not have identical rotational behavior.

For example:
- A compact metal sphere.
- A long metal cylinder.
- A large metal plate.
Their resistance to rotation differs because their mass distributions differ.

The runtime should support:
1. Explicit mass and inertia definitions.
2. Automatic calculation from collider geometry and density.
3. Compound rigid bodies.
4. Independent center-of-mass positioning.
5. Dynamic mass modification.
6. Infinite-mass static bodies.
The center of mass must not be assumed to coincide with the visual object's origin.

Rapier explicitly separates mass, angular inertia and center of mass in its rigid-body model. 

Rapier+1

4.3 Force and impulse model
The universal force interface must distinguish continuous forces from instantaneous impulses.
Type	Unit	Physical effect
Force	N	Changes acceleration
Torque	N·m	Changes angular acceleration
Linear impulse	N·s	Changes linear velocity
Angular impulse	N·m·s	Changes angular velocity

For a force applied away from the center of mass:
\[
\tau=r\times F
\]

For a linear impulse:
\[
v'=v+\frac{J}{m}
\]

For an angular impulse applied at a point:
\[
\omega'=\omega+I_{world}^{-1}(r\times J)
\]

This provides the common interface for:
- Engine propulsion.
- Aerodynamic forces.
- Tire forces.
- Suspension.
- Explosions.
- Impacts.
- Weapon recoil.
- Gravitational fields.
- Environmental interactions.
A specialized module should calculate its contribution and submit it to the physical body. It should not independently overwrite the body's velocity or position.
4.4 Constraint solver
Rigid bodies need constraints to represent physical relationships.

Examples:
- A vehicle wheel connected to a chassis.
- A door connected to a building.
- A character's articulated skeleton.
- A robotic arm.
- A spacecraft docking mechanism.
- A suspension system.
Two important approaches:

Sequential impulses / Projected Gauss-Seidel

The solver iteratively calculates impulses that satisfy contact and joint constraints.

Advantages:
- Efficient.
- Suitable for real-time rigid-body interactions.
- Warm starting improves convergence.
- Naturally integrates with collision response.
Limitations:
- Constraints are approximate.
- Large mass ratios can cause instability.
- Long joint chains can stretch.
- Solver iteration count affects accuracy.
XPBD — Extended Position-Based Dynamics

Uses position-level constraint corrections with compliance.

Its constraint update can be expressed as:
\[
\Delta\lambda=
-\frac{C(x)}
{\sum_i w_i|\nabla C_i|^2+\alpha/\Delta t^2}
\]

Where:
- \(C(x)\) is the constraint error.
- \(w_i\) is inverse mass.
- \(\alpha\) is compliance.
- \(\Delta t\) is the substep.
Advantages:
- Controllable constraint compliance.
- Good for soft constraints and articulated structures.
- Less sensitivity to timestep than traditional position-based constraints.
Limitations:
- Requires careful calibration.
- Does not automatically replace an impulse-based contact solver.
- Different physical behaviors require different constraint formulations.
An interesting implementation reference is Avian: its current architecture uses impulse-based contact solving while supporting XPBD for joints. 

Rust+2


Recommendation
 Use an impulse-based solver for rigid-body contacts and evaluate XPBD as a complementary constraint system.
Do not force every physical relationship through XPBD or every constraint through the same solver.

5. Collision architecture
Collision detection should be an independent subsystem.

It should provide contact information to the rigid-body solver without owning the physical state of the bodies.
5.1 Collision pipeline
Collision geometry

Collider shapes, transforms and material properties


Broadphase

Find potentially intersecting pairs using bounding volumes


Narrowphase

Determine actual intersections, distances and contact geometry


Contact manifold generation

Contact points, normals, penetration depth and material interaction


Constraint solver

Collision response, friction, restitution and position correction


This two-stage broadphase/narrowphase architecture is used by Rapier and Avian. 

Rapier+1

5.2 Broadphase algorithms
The broadphase must reduce the number of expensive geometric intersection tests.

Let:
- \(N\) = number of colliders.
- \(K\) = number of potentially intersecting pairs.
Algorithm	Typical complexity	Advantages	Limitations
Brute force	\(O(N^2)\)	Simple reference implementation	Does not scale
Sweep and prune	\(O(N\log N)\) sorting; often near-linear with coherent motion	Efficient in temporally coherent scenes	Performance degrades with dense overlap
Spatial hashing	Expected \(O(N+K)\) with bounded cell occupancy	Good for uniform object distributions	Sensitive to cell size and density
BVH	Typically \(O(\log N+K)\) queries	General-purpose hierarchical search	Tree maintenance and balancing costs

These are typical operating characteristics, not worst-case guarantees. All methods can degrade under unfavorable distributions.

Recommendation: use a hierarchical collision architecture.
1. Global spatial index identifies nearby regions.
2. Regional broadphase identifies potentially interacting entities.
3. Local BVH handles dynamic collision candidates.
4. Static terrain and architectural geometry use persistent spatial acceleration structures.
5. Narrowphase handles the actual geometric calculations.
This is especially important for Planetary Engine.

A single BVH containing every collider on an entire planet is not an appropriate default architecture.
5.3 Narrowphase algorithms
Different shape combinations benefit from different algorithms.
Algorithm	Appropriate use
Analytical collision tests	Sphere-sphere, sphere-plane
SAT	Convex polyhedra
GJK	Distance and intersection between convex shapes
EPA	Penetration depth and contact normal after GJK
Triangle BVH	Complex static meshes
Heightfield queries	Terrain
Convex decomposition	Complex concave objects

GJK (Gilbert-Johnson-Keerthi) works with support mappings and can calculate distances between convex objects.

When objects penetrate, EPA (Expanding Polytope Algorithm) can calculate penetration depth and contact normal.

Parry, the collision library used by Rapier and Avian, provides these algorithms and specialized shape dispatch. 

Rust+1

Important limitations:
- GJK and EPA are iterative.
- Degenerate geometry can cause convergence problems.
- Very small penetration depths require numerical tolerances.
- Concave geometry requires decomposition or specialized mesh processing.
- Complex meshes can generate many contact candidates.
The runtime should expose a standardized collision interface, not require every specialized physics module to implement its own collision detection.
5.4 Continuous collision detection
Discrete collision detection can miss collisions when an object moves too far between two simulation steps.

This is tunneling.

For example, a projectile moving through a thin wall may be outside the wall at both sampled positions.

Two important solutions:

Swept CCD

Calculate the path of an object during a timestep and identify the first time of impact.

Speculative contacts

Generate constraints for objects expected to collide before they physically overlap.

Rapier and Avian support CCD approaches, including swept collision detection and speculative contacts. 

Rapier+2

Recommendation:
- Static objects: ordinary discrete collision.
- Ordinary dynamic objects: discrete collision with substeps.
- Fast-moving objects: selective CCD.
- Projectiles: swept collision.
- High-speed vehicles: CCD based on speed and geometry.
- Planetary terrain: specialized terrain queries.
CCD should not be enabled indiscriminately for every object because its computational cost can become substantial.
5.5 Collision materials
A collider should reference a physical material definition.

Minimum properties:
- Static friction.
- Dynamic friction.
- Restitution.
- Rolling resistance.
- Collision filtering.
- Optional directional friction.
Collision material properties should be independent of visual materials.

A concrete surface, wet asphalt, ice, sand and water must produce different physical responses even when rendered using similar shaders.

The environment subsystem should be able to provide surface material data dynamically.

6. Physics composition – specialized modules
This is one of the most important parts of the universal runtime.

Specialized physics must extend the universal physical model rather than create separate disconnected physics worlds.
6.1 Vehicle physics




















10










Recommended composition:
- One primary rigid body representing the chassis.
- Individual wheel states.
- Suspension constraints.
- Tire contact models.
- Drivetrain.
- Braking system.
- Aerodynamic drag and downforce.
A basic suspension model:
\[
F_s=-kx-cv
\]

Where:
- \(k\) = spring stiffness.
- \(x\) = suspension displacement.
- \(c\) = damping coefficient.
- \(v\) = suspension velocity.
Tire forces should depend on slip ratio, slip angle, normal load and surface friction.

Pacejka's Magic Formula is a potential higher-fidelity tire model. A simpler friction-limited model should be available for performance-oriented simulation.

The important architectural requirement:

The vehicle module calculates wheel forces and applies them to the chassis through the universal force interface.

It does not need a separate physics runtime.
6.2 Character physics
Characters require two operating modes.

Kinematic character controller
- Responsive movement.
- Capsule collision.
- Ground detection.
- Slope handling.
- Step climbing.
- Collision-aware movement.
Dynamic character representation
- Ragdolls.
- Physical impacts.
- Pushing and pulling.
- Physical interaction with other objects.
The two modes should be able to exchange state.

Rapier's character controller is a useful reference: it calculates corrected movement against the environment, but does not automatically apply that movement to the body. Its built-in controller handles translation rather than rotation. 

Rapier+1

6.3 Aircraft physics







instructor-pang.tistory.com


Aircraft physics should use:
- Rigid-body dynamics.
- Aerodynamic surfaces.
- Atmospheric density.
- Air-relative velocity.
- Wind velocity.
- Control surface deflection.
- Propulsion.
- Angular moments.
The basic aerodynamic dynamic pressure is:
\[
q=\frac{1}{2}\rho V^2
\]

Lift:
\[
L=C_LqA
\]

Drag:
\[
D=C_DqA
\]

NASA's aerodynamic documentation establishes these equations and explains the dependence on air density, velocity, geometry and aerodynamic coefficients. 

NASA+2

The aircraft module must receive atmospheric properties from the environment subsystem.

It should not contain a hardcoded Earth atmosphere.

This is essential for flying on other planets and in different atmospheric conditions.
6.4 Rocket physics
Rocket simulation requires variable mass.

This is a significant distinction from ordinary rigid bodies.

The rocket's mass changes as propellant is expelled:
\[
\frac{dm}{dt}=-\dot m_{exhaust}
\]

The thrust model includes:
\[
T=\dot m_{exhaust}V_e+A_e(p_e-p_0)
\]

The ideal rocket equation:
\[
\Delta v=V_e\ln\left(\frac{m_0}{m_f}\right)
\]

The runtime must support:
- Changing mass.
- Changing center of mass.
- Changing inertia tensor.
- Engine thrust vectors.
- Mass flow.
- Propellant consumption.
- Engine gimbal and reaction torque.
- Atmospheric pressure effects.
NASA explicitly explains why the standard constant-mass form of Newton's second law cannot simply be applied to a rocket without accounting for expelled mass and momentum. 

NASA+1

6.5 Spacecraft and orbital mechanics







docs.tudat.space


Spacecraft require two cooperating simulation systems:
1. Orbital dynamics.
2. Local rigid-body dynamics.
During orbital flight, the spacecraft should be integrated by the orbital solver.

During docking, landing or physical contact, it needs access to the rigid-body solver.

The transition must preserve:
- Position.
- Orientation.
- Linear velocity.
- Angular velocity.
- Mass.
- Inertia.
- Reference frame.
- Simulation timestamp.
The transition cannot be implemented as a simple teleport.

The orbital subsystem should support both analytical gravitational fields and numerical N-body simulation.

REBOUND is a particularly useful scientific reference because it demonstrates multiple orbital integrators and hybrid approaches for handling close encounters. 

rebound.hanno-rein.de+2

6.6 Common interface
Every specialized module should implement a shared conceptual contract:
Operation	Responsibility
Initialize	Create domain-specific state
Evaluate	Calculate forces and torques
Constrain	Register specialized constraints
Environment query	Obtain relevant environmental properties
Synchronize	Exchange data with universal physical state
Validate	Check physical state and numerical validity
Serialize	Preserve domain-specific state

A vehicle, aircraft and rocket should all be able to use the same underlying body model.

Their differences belong in their specialized modules.

7. Simulation lifecycle and execution contracts
The runtime should own the simulation lifecycle.

Specialized modules should not independently advance the world.
7.1 Proposed authoritative tick
1. Command collection
   
   Collect player inputs, external commands, creation/destruction requests and scheduled events.
2. Tick boundary
   
   Apply commands in deterministic order. Resolve entity changes and update physical properties.
3. Coordinate and environment update
   
   Resolve reference frames, gravity, atmospheric state, wind, water and terrain information.
4. Force evaluation
   
   Execute vehicle, aircraft, propulsion, gravity and other domain-specific force providers.
5. Velocity prediction
   
   Integrate external forces and predict unconstrained body motion.
6. Collision detection
   
   Update broadphase, execute narrowphase and identify continuous collision events.
7. Constraint preparation
   
   Build contact constraints, joints and solver islands.
8. Constraint solving
   
   Resolve contacts, friction, restitution and joint constraints.
9. State integration and correction
   
   Commit final position, orientation and velocities. Correct remaining penetration.
10. Sleep and activation management
    
    Update sleeping bodies and wake bodies affected by forces or collisions.
11. Event generation
    
    Emit collision, contact, impact, constraint and state-change events in deterministic order.
12. State checkpoint
    
    Calculate state hashes, record rollback information and prepare replication data.
The exact ordering of integration and collision substeps is a solver-backend responsibility. The important requirement is that the external lifecycle and phase contracts remain consistent.
7.2 Multi-rate scheduling
Not every physical system needs to execute at the same frequency.

Proposed architecture:
Domain	Scheduling
Interactive rigid bodies	Fixed high-frequency tick
Vehicle dynamics	Fixed tick with optional substeps
Character movement	Fixed tick
Aircraft	Fixed tick
Atmospheric simulation	Independent configurable cadence
Orbital dynamics	Larger or adaptive numerical steps
Geological/environmental changes	Event-driven or low frequency
Economy and construction	Event-driven

A domain may execute several substeps during one authoritative world interval.

It may also produce interpolated environmental data between its update boundaries.

The coordinator must ensure that all cross-domain interactions use consistent timestamps.

This is how a spacecraft can orbit a planet while a character walks on its surface without requiring the entire planetary universe to run at the character's physics frequency.

8. Rust runtime architecture
8.1 ECS versus object-oriented architecture
This is an important architectural decision.
Criterion	ECS / data-oriented	Object-oriented
Memory locality	Excellent potential	Often fragmented
SIMD	Natural fit	More difficult
Parallel processing	Easier to organize	Requires more synchronization
Entity composition	Flexible	Often inheritance-heavy
Runtime extensibility	Systems and components	Interfaces and polymorphism
Deterministic execution	Requires explicit scheduling	Also requires explicit ordering
Large entity counts	Good potential	Depends heavily on implementation

Recommendation: data-oriented architecture with ECS-style entity composition.

The hot simulation path should not be built around thousands of independent objects containing their own update methods.

Instead:
- Entities are identifiers.
- Components hold physical state.
- Systems process collections of components.
- Domain modules operate on groups of entities.
- The scheduler determines execution dependencies.
The Bevy ECS is worth evaluating independently of the Bevy rendering engine. It can be used as a standalone Rust crate and supports parallel system scheduling. 

Rust+1

However, Planetary Engine should not become dependent on the complete Bevy application architecture merely to obtain ECS functionality.
8.2 Recommended ownership model
Use a single authoritative owner for each physics world.

Conceptually:
PhysicsWorld

Owns physical state and all mutable simulation structures

- Entity registry
- Body storage
- Collider storage
- Constraint storage
- Contact caches
- Spatial acceleration structures
- Simulation clock
- Solver state
- Domain state
- Rollback history
  
  External systems
Read-only state access
Allowed

Queued mutation commands
Allowed

Direct concurrent mutation
Prohibited


This matches Rust's ownership model and avoids shared mutable access to physical state.

Important implementation principles:
- Use generational handles for runtime references.
- Use stable identifiers for persistent entities.
- Use deferred command buffers for modifications.
- Avoid per-body mutexes.
- Keep frequently accessed physical data contiguous.
- Separate immutable geometry from mutable body state.
8.3 Proposed Rust module structure
This is a proposed logical module structure, not an implementation instruction.
physics_runtime/
│
├── core/
│   ├── world
│   ├── entity
│   ├── body
│   ├── collider
│   └── material
│
├── math/
│   ├── vectors
│   ├── matrices
│   ├── quaternions
│   └── numerical
│
├── frames/
│   ├── reference_frames
│   ├── transformations
│   ├── floating_origin
│   └── spatial_domains
│
├── dynamics/
│   ├── rigid_body
│   ├── mass_properties
│   ├── forces
│   ├── impulses
│   └── integrators
│
├── collision/
│   ├── broadphase
│   ├── narrowphase
│   ├── manifolds
│   ├── ccd
│   └── spatial_queries
│
├── constraints/
│   ├── contacts
│   ├── joints
│   ├── impulse_solver
│   └── xpbd
│
├── domains/
│   ├── vehicles
│   ├── characters
│   ├── aerodynamics
│   ├── propulsion
│   └── orbital
│
├── scheduler/
│   ├── simulation_clock
│   ├── tick_coordinator
│   └── parallel_executor
│
├── persistence/
│   ├── snapshots
│   ├── rollback
│   └── serialization
│
├── networking/
│   ├── replication
│   └── authoritative_state
│
└── diagnostics/
    ├── profiling
    ├── state_validation
    └── deterministic_hashing


The physical runtime should be usable independently of Unity, Node.js and Python.
8.4 Rust library evaluation
Research snapshot: November 2026. Library versions and features should be verified again before dependency selection.









Rapier
Strongest initial candidate
- Independent Rust physics engine.
- Rigid bodies, joints, collision and CCD.
- Parallel execution.
- Optional cross-platform determinism.
- Serialization and snapshots.
- Separate f32 and f64 variants.
Sources: Official documentation · Determinism · Serialization












Avian
ECS architecture reference
- ECS-native architecture.
- XPBD joints.
- Parry collision backend.
- f32/f64 options.
- Modular plugin system.
- Optional enhanced determinism.
Its current documentation lists joint motors and articulations as unsupported, which matters for future robotic and complex mechanical systems.

Sources: Repository · Documentation












Parry
Collision subsystem candidate
- Collision detection without requiring a complete dynamics engine.
- GJK/EPA.
- BVH.
- Shape queries.
- Mesh and compound geometry.
Interesting if we eventually implement our own dynamics solver while retaining an established collision backend.

Sources: Repository · Query algorithms












MuJoCo
Scientific reference
- Articulated dynamics.
- Numerical integration.
- Constraint solving.
- Robotics and mechanical systems.
Useful for studying high-quality articulated physics and numerical stability.

Source: Computation documentation












REBOUND
Orbital reference
- N-body gravitational dynamics.
- Symplectic integration.
- Adaptive integration.
- Hybrid close-encounter handling.
Important reference for the astronomical simulation domain, not a replacement for rigid-body physics.

Source: Official documentation



8.5 Recommended library strategy
I would not select Avian as the universal runtime simply because it has an ECS architecture.

Likewise, I would not automatically make Rapier the entire simulation engine.

My recommendation:
1. Define Planetary Engine's own physical state contracts.
2. Evaluate Rapier as the first rigid-body backend.
3. Evaluate Avian as an alternative ECS-oriented backend.
4. Evaluate Parry independently for collision requirements.
5. Keep orbital dynamics as an independent domain solver.
6. Ensure that all backends can be replaced without changing gameplay-domain interfaces.
This avoids tying the entire planetary simulation to the internal architecture of one third-party library.

9. Threading, parallel execution and SIMD
9.1 Threading architecture
The most important opportunity for parallelization is independent physical simulation.

There are several naturally parallel operations:
- Force evaluation for independent bodies.
- Broadphase candidate generation.
- Narrowphase collision testing.
- Independent constraint islands.
- Environmental field evaluation.
- Spatial queries.
However, bodies connected through contacts or joints cannot simply be solved independently.

A vehicle with four wheels and suspension is one coupled constraint system.
9.2 Constraint islands
Build a graph:
- Nodes represent physical bodies.
- Edges represent contacts and constraints.
Connected components represent independent constraint islands.
Illustrative execution graph
Island A

Vehicle chassis

Wheels · Suspension
Worker 1


Island B

Falling objects

Independent contacts
Worker 2


Island C

Character ragdoll

Articulated joints
Worker 3


Island D

Static environment

Collision queries only
Shared read access




Independent islands can be processed concurrently. Bodies connected through new contacts may cause islands to merge on subsequent ticks.

This is an established strategy in PhysX and other production physics engines. 

PhysX SDK Documentation+1

9.3 Rust execution
Recommended candidate:

Rayon.

Rayon provides:
- Work stealing.
- Parallel iterators.
- Custom thread pools.
- Fork-join execution.
- Safe data-parallel processing.
Rayon documentation.

However, one important warning:

Parallel execution does not automatically guarantee deterministic results.

Rayon's parallel reductions can group operations in different ways. Floating-point addition is not associative:
\[
(a+b)+c\neq a+(b+c)
\]

Therefore, the runtime must control:
- Job partitioning.
- Force accumulation order.
- Contact pair ordering.
- Constraint ordering.
- State commit order.
Rapier provides an interesting reference here: its enhanced-determinism mode supports parallel solving with results independent of the Rayon thread count, subject to its documented conditions. 

Rapier+1

9.4 SIMD
SIMD is useful for:
- Vector integration.
- Force calculations.
- Broadphase bounding-box operations.
- Large particle systems.
- Environmental field sampling.
The architecture should support SIMD without making it a mandatory dependency of every subsystem.

Important consideration: Rust's portable SIMD API is still marked nightly-only in the current standard library documentation. Therefore, the runtime should not depend on its stabilization for its initial architecture. 

Rust+1

Recommended strategy:
1. Scalar implementation as reference.
2. Compiler auto-vectorization.
3. Target-specific SIMD where beneficial.
4. Optional optimized backends.
5. Numerical equivalence testing between implementations.
For cross-platform deterministic simulation, SIMD width and instruction selection must be part of the determinism profile.
9.5 GPU compute
Defer full GPU physics
GPU compute is potentially valuable for:
- Large particle systems.
- Fluid simulation.
- Environmental field calculations.
- Large-scale collision candidate generation.
- N-body approximations.
- Parallel terrain interaction.
However, rigid-body simulation contains irregular dependencies and synchronization between contacts and constraints.

GPU execution also introduces memory transfer and synchronization costs.

PhysX demonstrates that GPU rigid-body simulation is feasible, but its documentation also identifies CPU/GPU feature compatibility limitations. 

PhysX SDK Documentation+1

My recommendation:
- CPU remains the authoritative physics executor initially.
- GPU acceleration is an optional backend.
- GPU calculations must not silently introduce nondeterministic authoritative state.
- Evaluate GPU performance against CPU including data transfer costs.

10. Determinism, rollback and multiplayer
This subsystem deserves special attention because Planetary Engine is intended to support persistent multiplayer worlds.
10.1 Determinism levels
There are several different guarantees that should not be confused.
Level	Guarantee
D0	Same process, same execution
D1	Same machine and build, repeatable results
D2	Different machines with compatible deterministic configuration
D3	Different platforms and processors, bit-identical results

Rapier documents that its default behavior is locally deterministic, while cross-platform determinism requires additional configuration and restrictions. Its enhanced-determinism mode cannot be combined with its 8-lane SIMD configuration. 

Rapier+1

IEEE 754 compliance alone is not enough for an entire simulation to become deterministic.

The complete execution must also control:
- Entity creation order.
- Entity destruction order.
- Collision pair ordering.
- Random number generation.
- Floating-point operations.
- Mathematical functions.
- Parallel reductions.
- Solver iteration counts.
- Simulation timestep.
- Domain-specific plugin versions.
10.2 Recommended determinism policy
I recommend:
Mandatory
D1 — Deterministic authoritative simulation

Same binary, same inputs and same initial state must produce reproducible results.

Target
D2 — Cross-machine reproducibility

Support compatible authoritative simulation configurations across different server machines.

Optional
D3 — Universal bit-identical execution

Investigate for specific subsystems where lockstep simulation requires it.

I would not make D3 a mandatory condition for the entire universe.

It would constrain numerical methods, compiler optimization, SIMD, GPU usage and third-party libraries unnecessarily.

For multiplayer, a server-authoritative architecture is preferable to requiring every client to produce exactly identical physics results.
10.3 Rollback architecture
Rollback is useful for:
- Multiplayer prediction.
- Reconciliation.
- Debugging.
- Reproducing physical incidents.
- Testing deterministic world states.
Recommended architecture:
Tick 1000 — Full checkpoint


Tick 1001 · Inputs and commands

Tick 1002 · Inputs and commands

Tick 1003 · Inputs and commands

Tick 1030 — Next full checkpoint


When a rollback is required:
1. Restore the most recent checkpoint.
2. Restore solver and collision state.
3. Restore domain-specific state.
4. Replay commands in original tick order.
5. Compare state hashes.
6. Resume normal simulation.
The snapshot must contain more than positions and velocities.

It needs to preserve:
- Rigid-body states.
- Contact manifolds.
- Warm-start impulses.
- Joint state.
- Sleeping state.
- Broadphase state.
- Narrowphase state.
- CCD state.
- Domain-specific state.
- Random generator state.
- Reference-frame state.
Rapier's serialization implementation is a useful reference because its snapshots include the broadphase, narrowphase, contact state, islands and other internal structures. 

Rapier+1

Important limitation: Rapier explicitly warns that its internal snapshots are not stable across different library builds and configurations.

Therefore:

Internal rollback snapshots and permanent world-save formats must be two different systems.

Permanent persistence requires our own versioned serialization contract.
10.4 Multiplayer integration
Proposed responsibility split:
Technology	Responsibility
Rust	Authoritative physics and simulation
Node.js	Session control, authentication, orchestration and persistence coordination
Unity	Rendering, input and client-side prediction
Python	Offline scientific analysis, validation and tooling

Client inputs should reference simulation ticks.

The server validates and applies them at authoritative tick boundaries.

For large multiplayer populations, spatial server ownership can be partitioned into regions. Cross-region interactions require explicit authority transfer or interaction coordination.

The physics engine itself must not assume that one server process owns every physical object in the universe.

11. Performance and benchmark plan
No credible performance numbers should be assigned to our future runtime before an actual benchmark exists.

We can, however, define the complexity model and test requirements.
11.1 Computational complexity
Let:
- \(B\) = active rigid bodies.
- \(P\) = broadphase candidate pairs.
- \(C\) = contact and joint constraints.
- \(I\) = solver iterations.
Operation	Expected computational behavior
Basic body integration	\(O(B)\)
Force evaluation	\(O(B)\) for bounded per-body providers
BVH broadphase	Typically \(O(B\log B+P)\)
Narrowphase	Depends on candidate count and geometry
Constraint solving	Approximately \(O(IC)\)
Direct N-body gravity	\(O(B^2)\)
Hierarchical approximate gravity	Typically \(O(B\log B)\)

The direct N-body model is unsuitable for large astronomical populations without additional acceleration strategies.
11.2 Memory estimation
A preliminary design estimate for a canonical rigid-body state is approximately 200–300 bytes per body, depending on precision, storage layout and included mass properties.

This excludes:
- Colliders.
- Meshes.
- Broadphase structures.
- Contact caches.
- Joints.
- Domain-specific state.
- Allocator overhead.
Illustrative rigid-body state memory
Planning estimate at 256 bytes per body; not measured runtime memory.


0 MB70 MB140 MB210 MB280 MB1,00010,000100,0001,000,000










      



This illustrates why data layout and sleeping-body management matter.
11.3 Benchmark scenarios
The benchmark should not only measure a large pile of cubes.

It must exercise different physical workloads.
Test	Scenario	What it validates
B01	Single body falling	Integration accuracy
B02	1,000-body pile	Contact stability
B03	High mass-ratio stack	Solver robustness
B04	Long joint chain	Constraint convergence
B05	Vehicle on rough terrain	Suspension and tire physics
B06	Fast projectile through thin geometry	CCD
B07	10,000 moving entities	Broadphase scaling
B08	100,000 static terrain colliders	Static spatial acceleration
B09	Large coordinate shifts	Numerical precision
B10	Orbital N-body system	Scientific integration
B11	Snapshot and restore	Rollback correctness
B12	Parallel execution	Thread scaling and determinism

11.4 Metrics
Every benchmark must report:
- Average timestep duration.
- p50, p95 and p99 timestep duration.
- Broadphase execution time.
- Narrowphase execution time.
- Constraint solver execution time.
- Number of candidate pairs.
- Number of active contacts.
- Solver iterations.
- Allocations per tick.
- Memory per active body.
- CPU scaling across thread counts.
- Snapshot creation and restoration time.
- Deterministic state-hash agreement.
For a 60 Hz simulation:
\[
T_{tick}=16.667\text{ ms}
\]

A provisional performance acceptance target could be:
- Physics p95 below 25% of the tick budget.
- Physics p99 below 50% of the tick budget.
- No sustained simulation backlog.
- No uncontrolled allocation growth.
These are proposed targets, not measured performance claims. They must be evaluated on a defined reference server.

12. Risk register
Risk	Severity	Mitigation
One integrator for every physical domain	High	Domain-specific numerical solvers
Global coordinate precision loss	High	Hierarchical frames and local origins
Nondeterministic parallel reductions	High	Ordered accumulation and deterministic scheduling
Solver instability at extreme mass ratios	High	Constraint calibration and specialized solvers
CCD performance degradation	Medium	Selective CCD
Domain migration discontinuities	High	Explicit frame and velocity transformations
Rollback missing solver state	High	Complete checkpoints
Third-party snapshot incompatibility	Medium	Versioned persistence contract
GPU/CPU synchronization overhead	Medium	CPU-authoritative initial architecture
Excessive ECS abstraction	Medium	Data-oriented hot path
Too many active physics entities	High	Sleeping, regional scheduling and simulation LOD

Particularly important risk: simulation LOD
A persistent planetary world cannot afford to simulate every physical entity at the same precision.

The architecture should eventually support:
- Full interactive physics.
- Simplified physical simulation.
- Analytical motion.
- Statistical or aggregated simulation.
- Dormant persistent state.
This must be implemented without changing the underlying physical identity of an entity.

For example, a vehicle outside the active simulation region should not need to run its suspension solver at 120 Hz.

Its state can be advanced using a simplified model and restored to detailed simulation when it becomes relevant.

This is an architectural requirement, not merely a performance optimization.

13. Open architectural decisions
The following decisions should be left open for the main orchestrator rather than silently fixed by this worker.
ID	Decision	Current recommendation
PHY-01	Physics backend	Evaluate Rapier first
PHY-02	ECS implementation	Data-oriented, library-neutral contracts
PHY-03	Global numerical precision	f64 hierarchical coordinates
PHY-04	Local solver precision	Benchmark f32 versus f64
PHY-05	Authoritative tick	Start evaluation at 60 Hz
PHY-06	Rigid-body integrator	Semi-implicit Euler
PHY-07	Orbital integrator	Dedicated symplectic solver
PHY-08	Contact solver	Sequential impulses / PGS
PHY-09	Additional constraints	Evaluate XPBD
PHY-10	Collision architecture	Hierarchical BVH
PHY-11	Determinism	D1 mandatory, D2 target
PHY-12	Rollback	Checkpoint plus deterministic replay
PHY-13	GPU	Deferred
PHY-14	Simulation LOD	Required architectural capability

13.1 Critical unresolved question: global versus local simulation
The biggest architectural question is how far the universal physical runtime should extend.

Should one physical runtime own:
- All planetary rigid bodies?
- All local interactive bodies?
- Orbital objects?
- Astronomical gravitational interactions?
My recommendation is that one simulation framework owns all these domains, but individual solvers operate on separate simulation contexts.

They share:
- Physical entity identity.
- Reference frames.
- Time.
- Units.
- Force and impulse contracts.
- Persistence.
- Events.
They do not necessarily share one solver instance.

This provides universal physical consistency without imposing one numerical method on incompatible physical systems.

14. Final architecture recommendation
Recommended architecture
Universal Physics Runtime
Proposed target architecture
Simulation Coordinator

Integer clock · Deterministic command queue · Domain scheduler

Universal Physical State

Stable entity identity · Rigid bodies · Mass properties · Forces · Constraints

Rigid-body domain

Rapier candidate · Collision · CCD · Contacts


Specialized domains

Vehicles · Characters · Aircraft · Rockets


Orbital domain

N-body · Gravity · Orbital integrators


Environment interface

Terrain · Atmosphere · Water · Gravity fields



Execution infrastructure

Parallel scheduler · Constraint islands · SIMD · Snapshotting · Rollback

External interfaces

Unity · Node.js · Python · Persistent world storage


Final conclusions
I consider the following principles essential:
1. A universal physical state model, not a universal solver.
2. Hierarchical coordinate frames and local physics origins.
3. Fixed authoritative timesteps for interactive physics.
4. Domain-specific numerical integration.
5. Impulse-based rigid-body contact solving.
6. Specialized force providers for vehicles, aircraft and propulsion.
7. Hierarchical broadphase and local collision acceleration.
8. Constraint-island parallelization.
9. Explicit determinism and rollback contracts.
10. Simulation LOD for persistent planetary-scale operation.
The most important design decision is to preserve a common physical universe while allowing specialized numerical systems to operate independently.

This is what will allow a vehicle, character, aircraft and spacecraft to interact with the same world without requiring the entire universe to run through a single rigid-body solver.

15. Research references and primary sources
The following sources are particularly important for the next architecture review.
Research area	Primary source
Rust physics architecture	Rapier
Cross-platform determinism	Rapier determinism
Physics snapshots	Rapier serialization
Collision algorithms	Parry query dispatcher
GJK / EPA	EPA documentation
ECS architecture	Bevy ECS
Alternative Rust physics	Avian repository
Numerical integration	MuJoCo computation
Fixed timestep	Fix Your Timestep
Orbital mechanics	REBOUND
Symplectic integration	WHFast
Adaptive orbital integration	IAS15
Reference frames	NASA SPICE frames
Astronomical coordinate systems	JPL ephemerides
Aerodynamics	NASA lift equation
Variable-mass propulsion	NASA rocket equation
Parallel physics	PhysX simulation


16. Handover to the Planetary Engine orchestrator
WORKER 02 — DELIVERY STATUS
Architecture research completed
- [x] Numerical integration investigation
- [x] Rigid-body dynamics
- [x] Collision architecture
- [x] Physics composition
- [x] Rust architecture
- [x] Threading and SIMD
- [x] Determinism and rollback
- [x] Multiplayer integration
- [x] Performance methodology
- [x] Risk register
- [x] Open architectural decisions
- [x] Primary research references
  
  
  Implementation has not been performed. No library has been irrevocably selected.

Instructions for the orchestrator:

Do not treat the proposed architecture as a final technology selection.

The next architecture integration phase should compare this report against:
- Planetary rendering and world-coordinate architecture.
- Terrain and environmental simulation.
- Atmospheric and hydrological simulation.
- Persistent world and multiplayer architecture.
- Astronomical and orbital simulation.
The most important integration dependency is the reference-frame contract. It must be shared between the planetary engine, physics runtime, orbital mechanics and environment systems before implementation begins.

My strongest recommendation is to resolve that contract before selecting the final rigid-body backend.

The physics runtime will depend on it far more deeply than on any particular collision library.

Až budou hotové ostatní workery, můžu jejich výsledky sjednotit do jednoho architektonického dokumentu, odhalit konfliktní návrhy a připravit rozhodovací registr pro orchestrátora.
🧩Sjednotit výstupy workerůKlepnutím nastavíte