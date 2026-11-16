WORKER 01 – UNIVERSAL WORLD ARCHITECTURE
Planetary Geometry, Coordinate Systems & Environment Interface

Architecture research
 Implementation prohibited
 Rust · Node.js · Python · Unity

1. Executive summary
Základní architektonické rozhodnutí: Planetary Engine by neměl být postaven jako herní engine s planetárním terénem. Měl by být postaven jako univerzální prostorový a environmentální simulační systém, nad kterým mohou fungovat různé typy simulací.

To je zásadní rozdíl.

Pokud chceme, aby automobil, člověk, letadlo, družice a kosmická loď sdílely jeden fyzický svět, nemohou mít vlastní nezávislé reprezentace prostoru a prostředí. Musí sdílet společný prostorový model, časovou osu, referenční soustavy a environmentální poskytovatele.

Doporučuji oddělit pět základních vrstev:
1. Spatial Foundation – globální geometrie, souřadnicové systémy a transformace.
2. World Geometry – planety, povrchy, terén, oceány a prostorová hierarchie.
3. Environment Model – gravitace, atmosféra, počasí, voda a povrchové vlastnosti.
4. Simulation Partitioning – nezávislé prostorové oblasti pro fyziku, síťovou simulaci a streaming.
5. World Interface – jednotný kontrakt, přes který všechny simulované objekty získávají informace o okolním prostředí.
Důležité je neztotožňovat geometrickou reprezentaci planety s fyzikální reprezentací světa. Renderovací dlaždice, kolizní geometrie a serverové simulační oblasti mohou používat odlišné struktury, ale musí odpovídat stejnému globálnímu modelu.

2. Coordinate system architecture
2.1 The fundamental problem
Planetary Engine must operate at radically different spatial scales:
Scale	Example	Required spatial behavior
Human	Character, building	Millimeter–meter precision
Local	Vehicle, aircraft	Centimeter-level simulation
Regional	City, terrain region	Kilometer scale
Planetary	Earth, Mars	Thousands of kilometers
Orbital	Satellites, moons	Thousands to millions of kilometers
Astronomical	Solar system	Billions of kilometers
Interstellar	Star systems	Light-year scale

A single Cartesian coordinate system is mathematically sufficient for an idealized universe, but a single floating-point representation is not a good engineering solution for all these scales.

Recommendation: hierarchical reference frames with explicit precision management.

The canonical position of an object must be independent of the coordinate representation used by a particular subsystem.
2.2 Comparison of coordinate systems









ECEF – Earth-Centered, Earth-Fixed

Cartesian coordinates with origin at Earth's center, rotating with Earth.
- Suitable for global terrestrial positioning.
- Does not suffer from longitude singularities in its Cartesian representation.
- Useful for terrain, surface objects and planetary indexing.
- Not inertial: Earth's rotation must be accounted for in dynamics.












ECI – Earth-Centered Inertial

A family of geocentric inertial coordinate systems.
- Suitable for orbital mechanics.
- Does not rotate with Earth's surface.
- Useful for satellites, spacecraft and orbital trajectories.
- ECI must be qualified by a specific frame definition; it is not one universally unique frame.












Geodetic coordinates

Latitude, longitude and ellipsoidal height.
- Human-readable geographical positioning.
- Excellent for maps, terrain datasets and geographic APIs.
- Not ideal for direct physics calculations.
- Must distinguish ellipsoidal height from height above mean sea level.












ENU / NED – Local tangent frames

Local Cartesian coordinate systems.
- ENU: East, North, Up.
- NED: North, East, Down.
- Ideal for local vehicle and character simulation.
- Must be explicitly associated with an origin and orientation.












Barycentric coordinates

Coordinates relative to the center of mass of a system.
- Suitable for astronomical mechanics.
- Supports planetary and satellite ephemerides.
- Avoids treating the Sun or an individual planet as a universal inertial origin.
- Essential for a consistent astronomical reference hierarchy.



These are complementary systems, not competing alternatives.

NASA's SPICE architecture is particularly relevant: it explicitly distinguishes inertial, body-fixed, spacecraft, fixed-offset and dynamic frames, and provides transformations between them. 

naif.jpl.nasa.gov+1

2.3 Recommended reference-frame hierarchy







cosmos.esa.int


Proposed hierarchy:
UNIVERSE
│
├── BARYCENTRIC FRAME
│   │
│   ├── STAR SYSTEM
│   │   │
│   │   ├── STAR-CENTERED INERTIAL FRAME
│   │   │
│   │   ├── PLANET A
│   │   │   ├── Planet-Centered Inertial
│   │   │   ├── Planet Body-Fixed
│   │   │   ├── Planetary Geodetic
│   │   │   └── Local ENU / NED
│   │   │
│   │   ├── PLANET B
│   │   │   └── ...
│   │   │
│   │   └── MOONS
│   │       └── ...
│   │
│   └── OTHER STAR SYSTEMS
│
└── LOCAL SIMULATION FRAMES
    ├── Physics origins
    ├── Vehicle frames
    ├── Character frames
    └── Sensor frames


The hierarchy is conceptual. The actual frame graph must allow direct transformations between any connected frames, rather than requiring every operation to traverse the complete hierarchy.

Important distinction: a frame's origin and its orientation are separate properties. A planet-centered inertial frame and a planet-fixed frame may share the same origin at a given instant but have different orientations and angular velocities.
2.4 Six-dimensional state transformations
A common architectural mistake is to transform only position and velocity using the same rotation matrix.

That is insufficient when converting between rotating and inertial frames.

For a rotating frame, the transformation of velocity must account for the time derivative of the rotation matrix.

Let:
- \(R(t)\) be the rotation matrix.
- \(r\) be position relative to the rotating origin.
- \(\omega\) be angular velocity.
- \(v_r\) be velocity measured in the rotating frame.
Then:
\[
v_i=R(t)\left(v_r+\omega\times r\right)+v_o
\]

where \(v_o\) is the inertial velocity of the frame origin.

The corresponding state transformation is a six-dimensional transformation:
\[
S=
\begin{bmatrix}
r\\v
\end{bmatrix}
\]

\[
S_B=X_{AB}(t)S_A
\]

NASA SPICE implements this distinction through pxform for position and sxform for Cartesian state vectors. Its rotating-frame transformation contains the derivative of the rotation matrix. 

naif.jpl.nasa.gov+1

This has direct consequences:
- A stationary vehicle on Earth's surface has zero velocity in ECEF.
- The same vehicle has nonzero inertial velocity because Earth rotates.
- An aircraft's velocity relative to the atmosphere differs from its velocity relative to the ground.
- A satellite's velocity must not be interpreted as its surface-relative velocity.
2.5 Non-inertial acceleration
The engine must also define how acceleration is represented.

In a rotating frame:
\[
a_i =
a_r+
2\omega\times v_r+
\omega\times(\omega\times r)+
\dot{\omega}\times r
\]

when the origin is fixed and the frame rotates with angular velocity \(\omega\), with the corresponding vectors expressed consistently in the rotating frame.

The additional terms represent:
- Coriolis acceleration.
- Centrifugal acceleration.
- Euler acceleration.
For moving frame origins, translational acceleration must also be included.

Architecture decision: gravity providers return gravitational acceleration separately from apparent acceleration in a rotating reference frame. The physics integrator is responsible for applying the appropriate inertial terms.

This prevents the common error of adding centrifugal effects twice.
2.6 Geodetic conversion model
For an oblate ellipsoid:
- \(a\) = semi-major axis.
- \(f\) = flattening.
- \(e^2=f(2-f)\).
- \(\varphi\) = geodetic latitude.
- \(\lambda\) = longitude.
- \(h\) = ellipsoidal height.
Prime vertical radius:
\[
N(\varphi)=
\frac{a}{\sqrt{1-e^2\sin^2\varphi}}
\]

ECEF conversion:
\[
X=(N+h)\cos\varphi\cos\lambda
\]

\[
Y=(N+h)\cos\varphi\sin\lambda
\]

\[
Z=(N(1-e^2)+h)\sin\varphi
\]

This conversion must be performed against the selected body's ellipsoid, not against a hardcoded Earth radius.

For Earth, WGS 84 provides the reference definition. Its semi-major axis is 6,378,137 metres and its inverse flattening is 298.257223563. 

WGS 84

For other planetary bodies, use body-specific shape and orientation parameters sourced from planetary constants and orientation models. NAIF PCK explicitly supports physical and cartographic parameters for planets, moons, comets and asteroids. 

naif.jpl.nasa.gov+1

Additional requirement: the system must explicitly distinguish:
- Planetocentric latitude: angle measured from the body's center.
- Planetographic latitude: latitude defined relative to the local surface normal.
- Ellipsoidal height: distance relative to the reference ellipsoid.
- Terrain elevation: height relative to the actual terrain model.
- Orthometric height: height relative to a defined geoid or equivalent gravity reference surface.
These values are not interchangeable.
2.7 Floating-point precision and hierarchical coordinates
Recommended architecture
Use three different coordinate representations:
Layer	Representation
Authoritative global state	Hierarchical double-precision coordinates
Local simulation	Double precision, localized origin
Rendering	Float32 relative to camera/client origin

A useful canonical representation is:
\[
P_{\text{global}}=P_{\text{anchor}}+P_{\text{local}}
\]

For very large coordinate ranges, the anchor should itself be hierarchical or cell-based rather than one enormous floating-point vector.

For example:
Position
├── ReferenceFrameID
├── AnchorID
├── CellCoordinate
│   ├── X: int64
│   ├── Y: int64
│   └── Z: int64
└── LocalOffset
    ├── X: float64
    ├── Y: float64
    └── Z: float64


This is a proposed representation, not a claim that every subsystem must use this exact encoding.

Why not simply use double everywhere?

Double precision is adequate for many planetary and astronomical calculations, but it does not eliminate every precision problem. Relative-coordinate subtraction, extremely large baselines and GPU float-only rendering still matter.

PhysX documents scene-origin shifting as a solution to large-world precision problems, while also warning that shifting scene state has computational and integration costs. 

PhysX SDK Documentation+1

Floating origin must never modify authoritative world coordinates.

It only changes the local coordinate representation presented to a particular simulation or rendering context.

This is particularly important for multiplayer: two clients can use different rendering origins while observing the same authoritative world state.
2.8 Astronomical time and frame consistency
Spatial coordinates alone are insufficient. Every transformation must be evaluated at a specified epoch.

Recommended separation:
Time system	Responsibility
Simulation time	Authoritative world progression
TAI	Continuous atomic time reference
UTC	Human-readable civil time
UT1	Earth rotation calculations
TT	Terrestrial dynamical calculations
TDB	Solar-system ephemerides

Use an integer simulation time representation based on SI time increments, with explicit conversions to external astronomical time scales.

Do not use UTC as the internal physics clock because leap seconds make it unsuitable as a uniformly advancing simulation time coordinate.

The IERS maintains Earth-orientation data, reference-frame conventions and time-scale resources. JPL DE ephemerides use TDB for their dynamical time coordinate. 

IERS+2


3. Planetary geometry and terrain architecture
3.1 Geometry representation alternatives
There are three principal approaches worth evaluating.









A. Latitude/longitude grid

Regular rectangular geographic grid.

Advantages:
- Direct compatibility with GIS datasets.
- Simple addressing and data import.
- Convenient for geographic weather models.
Limitations:
- Longitude singularity at poles.
- Cell area changes with latitude.
- Severe geometric distortion near poles.
- Difficult uniform planetary subdivision.
Verdict: retain as an input/output and scientific data representation, not as the primary terrain topology.












B. Cube sphere

Project six cube faces onto a sphere.

Advantages:
- Six regular top-level regions.
- Natural quadtree subdivision.
- No geographic pole singularity.
- Predictable neighboring tile relationships.
- Convenient GPU mesh generation.
Limitations:
- Projection distortion.
- Special handling at six-face boundaries.
- Different projections produce different cell-size distributions.
Verdict: recommended primary planetary terrain topology.












C. Icosphere

Subdivide an icosahedron and project its triangular faces onto a sphere.

Advantages:
- Approximately uniform triangular elements.
- No latitude/longitude singularity.
- Useful for spherical numerical discretization.
Limitations:
- Irregular vertex valence.
- More complicated structured quadtree addressing.
- Triangular topology complicates some terrain and texture workflows.
Verdict: worth investigating for scientific discretization and certain fluid or atmospheric models, but not my first choice for the common terrain hierarchy.



3.2 Recommended solution: cube-sphere quadtree
Use six cube faces, each recursively divided into four children.







redblobgames.com


The basic subdivision:
\[
N(L)=6\cdot4^L
\]

Where \(L\) represents subdivision level.
Level	Total theoretical tiles
0	6
1	24
2	96
4	1,536
8	393,216
12	100,663,296
16	25,769,803,776

These are theoretical counts for a complete tree. The engine must never need to instantiate the complete tree. Only relevant regions and their required ancestors should be loaded.

Tile addressing proposal:
PlanetTileID
├── BodyID
├── GeometrySchemeVersion
├── FaceID       [0..5]
├── Level
├── X
└── Y


This provides deterministic addressing independent of rendering distance.

The projection should be defined once in the geometry specification and shared by terrain generation, spatial indexing, collision construction and data ingestion.

A useful production reference is Google's S2 Geometry. Its implementation subdivides six cube faces into quadrilateral spherical cells, supports 30 subdivision levels and uses a 64-bit cell identifier. Its nonlinear face mapping helps produce more uniform cell sizes. 

S2Geometry+1

I recommend investigating whether S2's mapping can serve as the common surface-addressing basis rather than independently inventing a second global cube projection.

However, S2 should not automatically become the entire geometry engine. Its proven strength is spherical indexing and cell hierarchy, not a complete planetary terrain, collision or simulation architecture.
3.3 Terrain LOD
Rendering LOD and physics LOD must be separate.

For rendering, use screen-space geometric error.

A simplified screen-space error relationship is:
\[
SSE \approx
\frac{e_g H}
{2d\tan(\theta/2)}
\]

Where:
- \(e_g\): geometric approximation error.
- \(H\): viewport height in pixels.
- \(d\): distance from the camera.
- \(\theta\): vertical field of view.
This permits the client to choose an appropriate terrain representation according to its viewing conditions.

Physics requires a different criterion:
- Maximum permitted collision approximation error.
- Object velocity.
- Collision prediction horizon.
- Object dimensions.
- Required contact accuracy.
A distant tile may be rendered at very low resolution while its collision or physical representation remains available at a different resolution.

This separation is essential for aircraft, high-speed vehicles and spacecraft.
3.4 Tile continuity and boundary management
This is one of the highest-risk parts of the architecture.

A planetary terrain system must guarantee continuity across:
1. Adjacent tiles on the same cube face.
2. Different quadtree levels.
3. Transitions between cube faces.
4. Different collision and rendering resolutions.
5. Procedural and imported terrain data.
I recommend four complementary mechanisms.
1
Canonical boundary sampling

Every boundary vertex must resolve to the same global sample coordinate regardless of which tile requests it.
2
Balanced quadtree

Maintain a maximum one-level difference between directly adjacent tiles (2:1 balancing). This limits the number of boundary configurations.
3
Transition geometry

Use precomputed edge-stitching topology, geomorphing or skirts as appropriate. Skirts are a visual fallback, not a substitute for correct physical continuity.
4
Shared terrain source

Parent and child representations must derive from the same versioned terrain source and deterministic sampling rules.

The 2:1 constraint is a proposed engineering policy, not a requirement imposed by the OGC standard. Research on quadtree terrain crack removal demonstrates why neighbor LOD differences and boundary topology need explicit treatment. 

scitepress.org+1

For deterministic procedural generation, a tile must be a function of:
\[
Tile=f(BodyID,TileID,Seed,GeneratorVersion,DataRevision)
\]

The same inputs must produce the same canonical geometry.

A generator update must not silently alter already persisted world geometry. It requires an explicit version or migration.
3.5 Terrain representation
I recommend separating planetary shape from terrain displacement.
\[
P_{\text{surface}} =
P_{\text{reference shape}}+
h(\varphi,\lambda,t)\,n
\]

Where:
- \(P_{\text{reference shape}}\) is the reference ellipsoid surface position.
- \(h\) is terrain displacement.
- \(n\) is the appropriate reference-surface normal.
This is a conceptual normal-displacement model; an implementation must define its exact mapping for each body shape.

The geometry subsystem should support:
Representation	Purpose
Reference ellipsoid	Planetary baseline
Heightfield	Continents, mountains and seabed
Bathymetry	Ocean-floor elevation
Triangle mesh	Local detailed geometry
Signed distance field	Caves, overhangs and volumetric terrain
Collision proxy	Efficient physical interaction
Visual mesh	Camera-dependent rendering

A heightfield alone cannot correctly represent caves, tunnels and arbitrary overhangs. These require volumetric or explicit geometric representations.
3.6 Spatial indexing alternatives
The spatial index must not be confused with the rendering tile hierarchy.
System	Strength	Recommended use
S2	Hierarchical spherical quadrilaterals	Planetary surface indexing
H3	Hierarchical hexagonal cells	Analytics, regional aggregation and flow
Cube quadtree	Structured planetary geometry	Terrain and surface data
Octree	Three-dimensional subdivision	Volumetric spatial queries
BVH	Bounding-volume hierarchy	Collision and geometry queries
Spatial hash	Fast local neighborhood lookup	Dynamic objects and local physics

H3 deserves particular attention for economy, population, weather aggregation and strategic gameplay.

However, H3's hierarchical containment is exact in its index structure but only approximate geometrically between parent and child cells. It also contains twelve pentagonal cells at each resolution. Consequently, it should not be treated as a universal physical terrain mesh. 

H3+1

3.7 Rendering technology versus world geometry
A useful production reference is OGC 3D Tiles.

It provides hierarchical structures for streaming massive 3D content, including buildings, photogrammetry and point clouds.

Its important limitation is that it defines how content is organized and delivered, not the physical semantics of the simulation. 

Streaming Massive 3D Geospatial Data+1

It is therefore a good reference for the client streaming layer, not the authoritative world model.

Another valuable reference is Geometry Clipmaps by Losasso and Hoppe (SIGGRAPH 2004). Their viewer-centered nested regular grids provide incremental terrain updates and graceful rendering complexity control. 

hhoppe.com

Recommendation: investigate a hybrid:
- Cube-sphere quadtree for global addressing.
- Adaptive quadtree meshes for regional geometry.
- Local geometry clipmaps for high-detail rendering.
- Independent collision hierarchy.
- Separate spatial indices for server-side object queries.
This avoids forcing a single representation to solve five different problems.

4. Universal environment architecture
4.1 The Universal Environment Provider
This is arguably the most important interface in Worker 01.

Every physical entity should be able to ask:
Given my position and the current simulation time, what physical conditions exist around me?

It should not need to know how the world calculates those conditions.

For example, a vehicle should not care whether gravity came from a spherical model, a spherical harmonic expansion or a custom planetary gravity field.

The same applies to atmospheric density and wind.

Proposed architecture:







ccpp-techdoc.readthedocs.io


                  UNIVERSAL WORLD INTERFACE
                            │
             ┌──────────────┴──────────────┐
             │                             │
       POSITION + TIME              WORLD FRAME
             │                             │
             └──────────────┬──────────────┘
                            │
                   ENVIRONMENT QUERY
                            │
       ┌────────────┬───────┴──────┬─────────────┐
       │            │              │             │
    GRAVITY      SURFACE       ATMOSPHERE      WATER
       │            │              │             │
       └────────────┴───────┬──────┴─────────────┘
                            │
                    MATERIAL PROVIDER
                            │
                            ▼
                   ENVIRONMENT SAMPLE
                            │
              ┌─────────────┼─────────────┐
              │             │             │
           VEHICLES     CHARACTERS     SPACECRAFT


The providers are independent modules implementing common contracts.

They can be replaced without changing the physics objects consuming their data.
4.2 Environment providers
A. Gravity provider

Returns gravitational information at a specified position and time.

Possible models:
Model	Complexity	Application
Constant gravity	O(1)	Simplified local simulation
Point-mass gravity	O(1) per source	Basic planetary gravity
Multi-body gravity	O(B)	Orbital mechanics
Spherical harmonics	Approximately O(L²) terms in a straightforward truncated expansion	Nonuniform planetary gravity
Detailed local gravity model	Model-dependent	Scientific or specialized simulations

Point-mass gravity:
\[
\vec g=-\frac{GM}{r^3}\vec r
\]

For a solar-system simulation, gravitational contributions from multiple bodies must be considered.

For Earth, EGM2008 provides geopotential coefficients for spherical harmonic expansion and a geoid referenced to WGS 84. 

Earth Gravitational Model+1

The architecture should therefore distinguish:
- Gravitational acceleration.
- Gravitational potential.
- Surface normal.
- Effective gravity in a rotating frame.
These are not interchangeable.

B. Terrain and surface provider

Returns:
- Terrain elevation.
- Surface position.
- Surface normal.
- Signed distance from surface.
- Terrain type.
- Surface material.
- Water depth.
- Collision representation availability.
The provider must support arbitrary planetary bodies, not just Earth.

C. Atmospheric provider

Returns:
- Density \(\rho\), kg/m³.
- Static pressure \(p\), Pa.
- Temperature \(T\), K.
- Wind velocity, m/s.
- Humidity.
- Speed of sound.
- Dynamic viscosity.
- Atmospheric composition.
The basic physical relationship is:
\[
p=\rho R T
\]

For a hydrostatic atmosphere:
\[
\frac{dp}{dh}=-\rho g
\]

The model must account for the fact that atmosphere is a three-dimensional, time-dependent field, not simply an altitude lookup.

For Earth, the U.S. Standard Atmosphere 1976 provides a validated baseline model with layered temperature, pressure and density profiles. Its sea-level reference values include 1.225 kg/m³ density, 101325 Pa pressure and 288.15 K temperature. 

ntrs.nasa.gov+1

However, this is not a weather simulator.

The architecture needs two distinct components:
1. Reference atmosphere – stable baseline and atmospheric structure.
2. Dynamic atmosphere – wind, temperature variation, humidity, weather and other time-dependent effects.
At sufficiently high altitudes, a conventional continuum atmosphere model becomes inadequate. Rarefied gas effects must be considered for spacecraft and atmospheric entry.

D. Water provider

The water provider should not simply return a sea-level height.

It must support:
- Water surface elevation.
- Bathymetry.
- Water depth.
- Current velocity.
- Waves.
- Temperature.
- Salinity.
- Density.
- Ice and phase state.
- Tidal variation.
A boat, submarine, swimmer and aircraft flying above the ocean require different information from the same underlying water model.

E. Material provider

Returns surface physical characteristics:
- Material identity.
- Roughness.
- Static and dynamic friction properties.
- Restitution.
- Thermal conductivity.
- Heat capacity.
- Moisture.
- Bearing strength.
- Permeability.
One important distinction: friction is not always a universal scalar property of the ground. Tire-rubber-on-asphalt and metal-on-asphalt interactions can behave differently.

The environment provider should expose material properties; the contact/physics model should calculate the resulting interaction.
4.3 Environment sample consistency
Every environment response must contain metadata.

Proposed structure:
Field	Purpose
schema_version	Contract compatibility
world_revision	World state revision
simulation_tick	Sampling time
provider_id	Originating provider
model_version	Physical model version
data_revision	Source data version
validity_interval	Time interval of validity
uncertainty	Sampling/model uncertainty
quality	Exact, interpolated, approximate or unavailable

An unavailable atmospheric measurement must not silently return zero density.

Likewise, missing gravity data must not be interpreted as zero gravity.

This is particularly important when moving between planets, atmospheres and vacuum.

5. Spatial partitioning architecture
5.1 The fundamental architectural rule
Rendering tiles, physics partitions, server regions, streaming regions and persistence partitions must be independent systems.

They can reference common spatial identifiers, but they must not inherit each other's boundaries or update rules.

This is particularly important for multiplayer and for simulations involving objects moving between radically different scales.
5.2 Five independent partition systems
1. Rendering partitions
Client








github.com


Responsible for:
- Camera-dependent LOD.
- Mesh resolution.
- Texture resolution.
- Visual streaming.
- Culling and visibility.
Different clients can render the same location using different LOD levels.


2. Physics partitions
Simulation runtime








haroldserrano.com


Responsible for:
- Active simulation regions.
- Collision detection.
- Broadphase acceleration structures.
- Local physics coordinate frames.
- Simulation frequency.
- Interaction between neighboring regions.
Physics partitions must be based on physical interaction and simulation requirements, not camera visibility.


3. Server regions
Authority








blog.csdn.net


Responsible for:
- Authoritative entity ownership.
- Server load distribution.
- Region migration.
- Multiplayer synchronization.
- Cross-region interaction.
Server boundaries should be adjustable without changing the geometry of the planet.


4. Streaming regions
Data delivery








dev.epicgames.com


Responsible for:
- Terrain data availability.
- Object and asset loading.
- Environmental data caching.
- Predictive loading.
- Memory budgets.
Streaming regions are based on data dependencies and anticipated movement.


5. Persistence partitions
Storage








wasilzafar.com


Responsible for:
- Persistent object state.
- World modifications.
- Economy and ownership.
- Event history.
- Snapshots.
- Recovery and replication.
Persistence partitioning should be driven by access patterns, ownership and durability requirements.


5.3 Physics partitions versus physics islands
An important distinction:

A physics partition is a spatial or computational allocation.

A physics island is a group of dynamically interacting objects that can be solved independently from disconnected groups.

PhysX documents island management using connected components of an interaction graph, including contacts and constraints. 

PhysX SDK Documentation

Planetary Engine should not confuse the two.

For example:
- Two cars driving 100 metres apart may occupy one spatial partition but belong to separate physics islands.
- A long connected train may span multiple spatial cells while requiring coordinated constraint solving.
- Two aircraft may share an atmospheric region without having any direct physical interaction.
5.4 Proposed partition hierarchy
UNIVERSE
│
├── ASTRONOMICAL REGIONS
│   ├── Star system A
│   ├── Star system B
│   └── Interstellar regions
│
├── PLANETARY REGIONS
│   ├── Surface spatial index
│   ├── Atmospheric volume index
│   ├── Ocean volume index
│   └── Orbital regions
│
└── ACTIVE SIMULATION REGIONS
    ├── Physics partition A
    │   ├── Physics islands
    │   └── Active entities
    │
    └── Physics partition B
        ├── Physics islands
        └── Active entities


This is a logical hierarchy. It does not imply that each node must be a separate process or server.
5.5 Cross-partition interactions
This requires a defined protocol.

Recommended principles:
1. Every authoritative entity has exactly one owner at a given simulation tick.
2. Neighboring partitions maintain sufficient interaction information for boundary calculations.
3. Cross-boundary contacts are coordinated rather than independently applied twice.
4. Entity migration occurs at a defined simulation boundary.
5. Migration carries complete state, including velocity, orientation, constraints and relevant physical state.
6. The receiving partition validates the entity's last authoritative revision before accepting ownership.
For high-speed objects, simple position-based region migration is insufficient.

A projectile or aircraft can cross a partition boundary between two simulation steps. The architecture must support swept-volume queries and predictive interaction with neighboring partitions.

For constrained objects spanning regions, either the interacting physics island must be co-located or the system must provide an explicitly coordinated distributed solver. This is an open engineering decision, not something that a spatial grid alone solves.
5.6 Spatial partitioning recommendations
Domain	Recommended structure
Planetary surface	Cube-sphere quadtree / S2
Atmospheric volume	3D adaptive grid or octree
Ocean volume	Surface grid plus depth dimension
Orbital space	Hierarchical 3D spatial index
Local collision	BVH / broadphase spatial hash
Rendering	Adaptive quadtree / clipmaps
Server ownership	Independent region allocation
Persistence	Independent logical storage partitioning

A particularly important point: the atmosphere cannot be represented as a surface-only quadtree.

It is a three-dimensional volume. Atmospheric data may use a different spatial structure and resolution from the terrain beneath it.

6. Universal World Interface specification
This is the proposed contract that all simulation systems should use.
6.1 Core interface responsibilities
The Universal World Interface (UWI) should expose six principal capabilities.
Operation	Responsibility
TransformState	Transform positions, velocities and orientations between frames
ResolveBody	Identify the relevant celestial body and its reference models
SampleSurface	Query terrain, elevation, normals and materials
SampleEnvironment	Query gravity, atmosphere, water and environmental conditions
QuerySpatialRegion	Resolve spatial partitions and neighboring regions
QueryBodyState	Obtain celestial body positions, velocities and orientation at a given epoch

These are logical operations, not a commitment to a particular RPC framework.
6.2 Canonical position contract
Every position must identify its reference frame.

Proposed conceptual contract:
{
  "schema_version": "1.0",
  "reference_frame": "EARTH_FIXED",
  "body_id": "EARTH",
  "encoding": "CELL_OFFSET",
  "anchor_id": "EARTH_FIXED_ROOT",
  "cell": {
    "x": 0,
    "y": 0,
    "z": 0
  },
  "offset_m": {
    "x": 0.0,
    "y": 0.0,
    "z": 0.0
  }
}


The numerical example is illustrative, not a real position.

The contract must support other encodings:
- Body-centered Cartesian.
- Barycentric Cartesian.
- Geodetic.
- Local ENU/NED.
- Hierarchical cell-offset.
Geodetic coordinates should be an explicit alternative representation, not an implicit interpretation of an unqualified vector.
6.3 State contract
An object needs more than a position.
WorldState
│
├── EntityID
├── SimulationInstant
├── Position
│   ├── ReferenceFrame
│   └── Coordinates
│
├── Orientation
│   ├── Quaternion
│   └── ReferenceFrame
│
├── LinearVelocity
│   ├── Vector3
│   └── ReferenceFrame
│
├── AngularVelocity
│   ├── Vector3
│   └── ReferenceFrame
│
└── WorldRevision


The frame and units of every vector must be explicit.

A quaternion without a reference-frame definition is not sufficient for a universal spatial contract.
6.4 Environment query contract
Proposed logical request:
{
  "schema_version": "1.0",
  "query_id": "example-query",
  "simulation_tick": 100000,
  "position": {
    "body_id": "EARTH",
    "reference_frame": "EARTH_FIXED"
  },
  "requested_fields": [
    "GRAVITY",
    "SURFACE",
    "ATMOSPHERE",
    "WATER",
    "MATERIAL"
  ],
  "accuracy_profile": "VEHICLE_GRADE"
}


The full production contract must include the actual position coordinates, requested time, optional local frame and required accuracy.

The important principle is selective querying.

A spacecraft does not need road material properties. A character does not need high-order planetary gravity harmonics.

The provider should calculate only the requested information.
6.5 Environment response contract
EnvironmentSample
│
├── QueryID
├── SimulationTick
├── WorldRevision
│
├── Gravity
│   ├── AccelerationVector
│   ├── ReferenceFrame
│   └── GravitationalPotential
│
├── Surface
│   ├── Elevation
│   ├── SurfaceNormal
│   ├── SignedDistance
│   └── MaterialID
│
├── Atmosphere
│   ├── Density
│   ├── Pressure
│   ├── Temperature
│   ├── WindVelocity
│   ├── Humidity
│   └── SpeedOfSound
│
├── Water
│   ├── Phase
│   ├── Depth
│   ├── SurfaceElevation
│   ├── CurrentVelocity
│   └── Temperature
│
└── Provenance
    ├── ProviderVersions
    ├── DataRevisions
    ├── Validity
    └── Uncertainty


Every vector has an associated frame. Every quantity has an SI unit.

The actual implementation should use typed structures rather than unvalidated arbitrary key/value fields.
6.6 Example: vehicle environment query
A car should query its environment at its relevant contact locations, not merely at the center of its chassis.

Required information:
Query	Reason
Gravity	Weight and vehicle dynamics
Surface normal	Contact geometry
Surface elevation	Ground clearance
Material	Tire interaction
Water	Aquaplaning and immersion
Air density	Aerodynamic forces
Wind velocity	Relative airspeed
Temperature	Environmental and thermal effects

The vehicle physics system then combines this information with the vehicle's own parameters.

For example, the environment returns surface characteristics; the tire model calculates the resulting tire-road forces.
6.7 Example: character environment query
A character requires:
- Local gravitational acceleration.
- Local up direction.
- Terrain elevation.
- Collision geometry.
- Surface normal.
- Material properties.
- Water depth and velocity.
- Local atmospheric conditions.
The character should not need to understand whether it is on Earth, Mars or a custom artificial planet.

The same movement system can consume the same contract.
6.8 Example: aircraft environment query
An aircraft requires a different subset:
- Gravity.
- Atmospheric density.
- Pressure.
- Temperature.
- Wind velocity.
- Speed of sound.
- Terrain clearance.
- Water surface position.
The aerodynamic model uses air-relative velocity:
\[
\vec v_{\text{air}}=
\vec v_{\text{aircraft}}-
\vec v_{\text{atmosphere}}
\]

Both vectors must first be expressed in the same reference frame.

This makes wind and planetary rotation part of the actual flight model rather than visual effects.
6.9 Example: spacecraft environment query
A spacecraft uses a different coordinate regime.

Its primary state is barycentric or body-centered inertial.

Required queries:
- Celestial body positions and velocities.
- Gravitational field contributions.
- Body orientations.
- Atmospheric density where relevant.
- Radiation and thermal environment if modeled.
- Surface information during landing or low-altitude operations.
For a spacecraft orbiting Earth:
1. Its primary trajectory is integrated in an inertial frame.
2. Earth orientation is evaluated at the simulation epoch.
3. Its body-fixed position is calculated when required.
4. Atmospheric drag is evaluated using atmospheric velocity and density.
5. On landing, the surface provider supplies the local geometry and contact properties.
The same planet therefore serves cars, characters, aircraft and spacecraft without requiring four different planet implementations.

7. Proposed software architecture
7.1 Rust – Simulation and spatial foundation
Rust owns the authoritative world model and simulation runtime.

I recommend the following logical module boundaries:
planetary_world/
│
├── world_core/
│   ├── identifiers
│   ├── units
│   ├── simulation_time
│   ├── body_catalog
│   └── world_revision
│
├── reference_frames/
│   ├── frame_graph
│   ├── frame_transforms
│   ├── inertial_frames
│   ├── body_fixed_frames
│   ├── local_frames
│   ├── ephemeris
│   └── time_conversion
│
├── spatial_index/
│   ├── cube_quadtree
│   ├── spherical_index
│   ├── volumetric_index
│   ├── spatial_hash
│   └── bounding_volumes
│
├── planet_geometry/
│   ├── body_shapes
│   ├── ellipsoid
│   ├── terrain
│   ├── bathymetry
│   ├── tile_addressing
│   ├── tile_adjacency
│   ├── lod
│   ├── surface_sampling
│   └── collision_geometry
│
├── environment/
│   ├── environment_interface
│   ├── gravity
│   ├── atmosphere
│   ├── weather
│   ├── wind
│   ├── hydrosphere
│   ├── surface_materials
│   └── environmental_cache
│
├── simulation_partitioning/
│   ├── physics_regions
│   ├── simulation_islands
│   ├── interaction_boundaries
│   ├── region_migration
│   └── local_simulation_frames
│
├── world_interface/
│   ├── world_queries
│   ├── environment_queries
│   ├── spatial_queries
│   ├── body_queries
│   └── schema_validation
│
└── world_runtime/
    ├── simulation_scheduler
    ├── world_snapshot
    ├── authoritative_state
    └── deterministic_execution


This is a proposed module decomposition, not a prescribed crate count.

Some modules will eventually need to be split into independent Rust crates, particularly if separate teams or workers develop them.
7.2 Node.js – Application server and control plane
Node.js should manage:
- World lifecycle.
- User sessions.
- Authentication and permissions.
- Simulation instance management.
- Server-region allocation.
- Administrative operations.
- Persistent-world management.
- API gateway and orchestration.
It should not be placed in the per-entity physics query path.

A simulation tick should not require a round trip through Node.js to determine gravity or atmospheric density.
7.3 Python – Scientific and offline tooling
Python should provide:
- Planetary data ingestion.
- Coordinate conversion pipelines.
- Scientific reference calculations.
- Terrain preprocessing.
- Gravity model preprocessing.
- Atmospheric and oceanographic data preprocessing.
- Dataset validation.
- Model comparison.
- Offline world generation.
- Reproducibility verification.
A useful principle is to use Python as the scientific validation environment for Rust implementations.

For example, Rust's gravity provider should be testable against independently calculated reference values, not only against itself.
7.4 Unity / C# – Client
Unity is responsible for:
- Rendering.
- Local camera-relative coordinates.
- Visual terrain LOD.
- Asset streaming.
- Atmospheric and ocean rendering.
- User interaction.
- Audio and presentation.
- Optional local prediction.
The client should receive authoritative world state and translate it into its own local coordinate system.
Important
Unity's rendering origin must be independent of the authoritative server's reference origin.

Different clients must be able to observe the same world from different local origins.
7.5 Inter-process interfaces
I recommend a language-neutral schema definition shared by Rust, Node.js, Python and C#.

Candidates:
Technology	Advantages	Considerations
Protocol Buffers	Mature schema evolution and broad language support	Serialization overhead
FlatBuffers	Efficient access to serialized data	More restrictive schema and workflow
Custom binary format	Full control	Maintenance and compatibility burden

Initial recommendation:
- Internal Rust interfaces: strongly typed native structures.
- Control-plane communication: versioned RPC contracts.
- High-frequency state exchange: dedicated binary transport.
- Offline scientific datasets: versioned data formats.
- Unity: generated C# contract types.
The exact transport protocol should be evaluated in a later networking worker.

8. Performance and resource considerations
No benchmark is claimed here. The following are algorithmic complexity estimates and capacity-planning considerations.
8.1 Computational complexity
Operation	Expected complexity	Main dependency
Frame transformation	O(depth), then O(1) with cached transforms	Frame graph
Quadtree tile lookup	O(L)	Tile depth
Spherical index lookup	O(1) for direct cell encoding	Index implementation
Heightfield sampling	O(1) after tile resolution	Resident tile data
Local BVH collision query	Typically O(log N)	BVH quality
Spatial hash neighborhood	O(k) for examined cells/objects	Cell occupancy
Multi-body gravity	O(B)	Number of gravity sources
Truncated gravity harmonics	Approximately O(L²) terms	Harmonic degree
Grid atmosphere interpolation	O(1)	Grid resolution
Water-field interpolation	O(1) per regular-grid sample	Data residency

Here, \(L\) is the gravity harmonic degree or quadtree level depending on context; these are different variables despite sharing notation in some literature. \(B\) is the number of contributing gravity bodies.
8.2 Memory management
Terrain tile memory can be estimated before implementation.

For a 257 × 257 heightfield stored as float32:
\[
M=257\cdot257\cdot4
\]

\[
M=264\,196\text{ bytes}\approx258\text{ KiB}
\]

This is for one uncompressed scalar channel.

The actual memory cost increases with:
- Normal maps.
- Material identifiers.
- Collision data.
- Multiple LOD levels.
- Water and bathymetry data.
- Cache duplication.
- Metadata and acceleration structures.
The engine therefore needs independent budgets for:
1. Client rendering memory.
2. Server physics memory.
3. Environmental data cache.
4. Persistent world storage.
5. Offline preprocessing memory.
A tile's presence in persistent storage must not imply that it is resident in the simulation runtime.
8.3 CPU and GPU responsibilities
Workload	Preferred execution
Authoritative physics	CPU
Frame transformations	CPU
Global spatial indexing	CPU
Environmental sampling	CPU initially, optimized with batching
Terrain mesh generation	CPU/GPU hybrid
Terrain rendering	GPU
Atmospheric rendering	GPU
Ocean rendering	GPU
Offline terrain preprocessing	Python/CPU, optionally GPU
Scientific validation	Python/CPU

GPU acceleration for rendering does not imply that authoritative physics must run on the GPU.

A particularly important constraint is reproducibility. Floating-point GPU operations may differ across hardware and execution environments. The architecture should not depend on cross-vendor bitwise-identical GPU results for authoritative state.
8.4 Batching and caching
The environment interface should support batch queries.

Instead of:
Vehicle 1 → Gravity query
Vehicle 1 → Atmosphere query
Vehicle 2 → Gravity query
Vehicle 2 → Atmosphere query
Vehicle 3 → Gravity query
Vehicle 3 → Atmosphere query


Prefer:
SIMULATION TICK
    │
    ├── Batch gravity queries
    ├── Batch atmospheric queries
    ├── Batch surface queries
    └── Batch water queries


Potential optimizations:
- Shared environmental samples for nearby objects.
- Cached frame transformations.
- Resident tile caches.
- Spatially coherent batch processing.
- Precomputed gravity fields where the accuracy requirements permit.
- Multiresolution environmental grids.
- Time-interpolated environmental data.
Caching must be time-aware. Reusing a wind sample indefinitely is physically incorrect.

9. Testing and validation plan
The most important principle is independent verification.

A planetary geometry system that generates visually convincing terrain can still be mathematically wrong.

Each subsystem needs reference-based tests.
9.1 Coordinate system tests
Test	Validation
Geodetic → ECEF → geodetic	Round-trip position accuracy
ECEF → ENU → ECEF	Local-frame consistency
Inertial → body-fixed → inertial	Transform closure
Position and velocity transformation	Correct rotating-frame velocity
Frame transformation over time	Correct angular velocity
Dateline crossing	No discontinuity
North and South Pole	Stable local-frame behavior
Planetary body transition	Correct frame origin and velocity
Large-distance coordinates	No unacceptable precision loss

Reference comparisons should use GeographicLib and NASA SPICE where applicable. 

geographiclib.sourceforge.io+1

Proposed acceptance criteria:
- Terrestrial coordinate round-trip error: at most 1 mm for supported normal operating ranges.
- Local-origin shifting: no change in canonical world position.
- State transformations: agreement with independent reference calculations within a documented numerical tolerance.
- No unhandled discontinuities at poles, face boundaries or longitude wraparound.
These are proposed internal numerical targets, not measured results.
9.2 Planetary geometry tests
Required geometry test suite
- [ ] Complete coverage of all six cube faces.
- [ ] Face-edge coordinate continuity.
- [ ] Correct neighboring tile identification.
- [ ] Quadtree parent-child consistency.
- [ ] Deterministic tile generation.
- [ ] Boundary elevation continuity.
- [ ] Normal continuity.
- [ ] LOD transition stability.
- [ ] Collision/render geometry agreement.
- [ ] Correct terrain-to-ellipsoid conversion.
- [ ] Pole and antipodal test cases.
- [ ] Triaxial body support.

Recommended quantitative tests:
- Shared boundary samples must resolve to the same canonical terrain values.
- Tile generation with identical inputs must produce identical canonical output hashes where bitwise determinism is promised.
- Collision approximation error must remain below the configured physics tolerance.
- LOD transitions must not produce visible cracks or physically traversable gaps.
9.3 Environment tests
Provider	Reference validation
Gravity	WGS 84 normal gravity and EGM2008 reference values
Atmosphere	U.S. Standard Atmosphere 1976
Wind	Reference vector field and frame transformation
Water	Bathymetry and water-surface continuity
Materials	Contact-model test cases
Surface normal	Analytical ellipsoid and terrain-gradient comparison

Atmospheric testing should include pressure, temperature and density at multiple altitude layers, not only sea level.
9.4 Integration tests
The same physical object should produce consistent results regardless of its local coordinate representation.

Example:
1. Create a vehicle in an Earth-fixed frame.
2. Transform its state into an inertial frame.
3. Simulate it.
4. Transform the resulting state back to Earth-fixed.
5. Compare with an equivalent simulation using the appropriate rotating-frame equations.
Other integration tests:
- Character walking across a terrain tile boundary.
- Vehicle crossing a server-region boundary.
- Aircraft transitioning between atmospheric data tiles.
- Spacecraft transitioning between dominant gravitational sources.
- Landing on a rotating planetary body.
- Different clients observing the same authoritative object from different floating origins.
9.5 Performance tests
The following measurements should be collected from the beginning:
Metric	Why
Frame transform latency	Coordinate overhead
Environment query latency	Physics overhead
Batch throughput	Scaling behavior
Active terrain tile count	Memory demand
Collision BVH size	Physics memory
Environmental cache hit rate	Data efficiency
Region migration time	Multiplayer behavior
Simulation tick duration	Authoritative runtime
p50/p95/p99 query latency	Tail behavior

The workload should be increased progressively from isolated objects to dense multi-object regions and then to thousands of concurrent entities.

The performance target must be established from the actual simulation workload, not borrowed from an unrelated engine benchmark.

10. Risk register
Risk	Severity	Proposed mitigation
Incorrect reference-frame transformations	Critical	Explicit frame IDs and six-dimensional transforms
Floating-point precision loss	Critical	Hierarchical coordinates and local origins
Cube-face boundary discontinuities	Critical	Canonical sampling and adjacency specification
Rendering/physics LOD coupling	Critical	Independent representations
Gravity double-counting	Critical	Separate gravitational and apparent acceleration
Environmental data inconsistency	High	Tick-based snapshots and versioned samples
Cross-region physics divergence	Critical	Explicit ownership and interaction protocol
Non-deterministic procedural generation	High	Versioned generators and canonical sampling
Incorrect planetary shape assumptions	High	Body-specific shape models
Excessive environmental query cost	High	Batching, interpolation and caching
Inadequate upper-atmosphere model	High	Multiple atmospheric regimes
Excessive memory consumption	High	Independent caches and memory budgets
Coordinate/time-scale mismatch	Critical	Explicit time-scale conversion and epoch metadata


11. Open architectural decisions
These decisions should be resolved before implementation. Some are sufficiently important that they affect the design of several other workers.
11.1 Global spatial architecture
Decision	Option A	Option B	My recommendation
Primary surface topology	Cube-sphere	Icosphere	Cube-sphere
Surface indexing	Custom quadtree	S2-compatible hierarchy	Investigate S2 compatibility
Global coordinates	Pure float64	Hierarchical cell-offset	Hierarchical representation
Local physics coordinates	ENU	Arbitrary local frames	Support both
Earth reference	WGS84	ITRF realization	Support both with explicit accuracy profiles
Other planetary shapes	Sphere	Oblate/triaxial/irregular	Multiple shape providers
Terrain	Heightfield only	Hybrid geometry	Hybrid

11.2 Astronomical architecture
A critical point: there is no need to invent one enormous Cartesian coordinate system with a fictional universal origin.

A more robust architecture uses:
- Galactic reference frames for galactic-scale positioning.
- Star-system barycentric frames.
- Planet-centered inertial frames.
- Planet-fixed frames.
- Local simulation frames.
For the initial implementation, Newtonian celestial mechanics and established ephemerides provide a practical foundation.

If the project eventually requires cosmological expansion, relativistic effects or intergalactic-scale simulation, those require a separate cosmological coordinate and physics model. They should not be approximated by simply increasing the size of the existing planetary coordinate system.

JPL's DE440/DE441 ephemerides are useful references for the solar-system portion. They use fitted Chebyshev polynomials for planetary positions and velocities and define their dynamical time coordinate in TDB. 

ssd.jpl.nasa.gov+2

11.3 Physics and environment
Still unresolved:
1. Which planetary bodies receive detailed gravity models?
2. What is the minimum acceptable accuracy of gravity for different gameplay classes?
3. Will the atmosphere use a global 3D grid, adaptive volumetric cells or a hybrid?
4. Will oceans be physically simulated or represented by precomputed environmental fields initially?
5. Which environmental fields must be deterministic across all servers?
6. Which fields can use interpolation and controlled approximation?
7. How are dynamic world modifications reflected in collision and environment providers?
11.4 Multiplayer and persistence
The following decisions belong partly to subsequent architecture workers:
- Static versus dynamically reassigned server regions.
- Simulation tick synchronization.
- Cross-partition collision handling.
- Ownership migration.
- Replication and recovery.
- Persistence consistency.
- World revisioning.
- Deterministic replay requirements.
The most important decision is whether the simulation requires:

A. Deterministic reproducibility: same initial state and inputs produce results within defined numerical tolerances.

B. Bitwise deterministic execution: identical results across supported machines and execution environments.

Option B is substantially more restrictive, particularly with floating-point calculations, multithreading and GPU computation.

I recommend defining deterministic guarantees separately for world generation, physics, environmental sampling and network state replication.

12. Integration dependencies
Worker 01 establishes foundational contracts for almost every other subsystem.
Dependency graph
WORLD CORE

IDs · Units · Time · Body catalog


Reference Frames
Transforms · Ephemerides

Spatial Index
Quadtree · S2 · Octree



World Geometry
Planet · Terrain · Surface

Environment
Gravity · Atmosphere · Water



UNIVERSAL WORLD INTERFACE

Common contracts for all simulation objects


Physics Runtime
Vehicles · Characters · Aircraft

Astronomical Runtime
Orbits · Spacecraft · Celestial bodies

World Simulation
Economy · Construction · Editing

Multiplayer
Authority · Replication · Persistence



Dependencies for other research workers
Subsequent subsystem	What it needs from Worker 01
Physics and dynamics	Frames, units, time, gravity interface
Planetary terrain	Body shapes, tile addressing, geometry hierarchy
Atmosphere and weather	3D spatial addressing, time and environment contracts
Ocean simulation	Planet geometry, bathymetry, fluid coordinate model
Astronomical mechanics	Barycentric frames, ephemerides, body orientation
Networking	Canonical entity position and state contracts
Distributed simulation	Spatial region identification and migration semantics
Rendering	Geometry addressing, local coordinate frames and streaming contracts
Persistence	Stable body/entity/tile IDs and world revisions
Construction and economy	Stable geographic addressing and material/surface queries

The Universal World Interface should be agreed upon early, even if individual providers are implemented much later.

That prevents every subsequent subsystem from inventing its own coordinate conventions.

13. Recommended next steps
I would not begin implementation immediately after this research.

There are several areas where a deeper investigation will materially reduce architectural risk.
Priority 1
Reference Frame and Precision Specification

Resolve:
- Exact frame graph.
- Hierarchical coordinate representation.
- State transformations.
- Time-scale conversions.
- Floating-origin contract.
- Astronomical frame hierarchy.
Deliverable: formal spatial coordinate specification.


Priority 2
Planetary Geometry Deep Dive

Compare:
- S2 versus custom cube-sphere.
- Quadtree addressing.
- Cross-face adjacency.
- 2:1 balancing.
- Terrain boundary continuity.
- Collision and visual LOD.
- Geometry clipmaps.
Deliverable: planetary geometry and tile specification.


Priority 3
Universal Environment Model

Investigate:
- Multi-body gravity.
- Atmospheric physical models.
- Dynamic weather.
- Ocean and hydrodynamic fields.
- Surface materials.
- Multiresolution interpolation.
Deliverable: environmental provider specification.


Priority 4
Spatial Partitioning and Distributed Simulation

Investigate:
- Physics islands.
- Distributed collision.
- Region ownership.
- Cross-region interaction.
- Entity migration.
- Persistent spatial partitioning.
Deliverable: distributed simulation architecture.


These investigations can run in parallel, but they should share the proposed UWI and canonical coordinate contracts as research assumptions. Any worker finding a contradiction should explicitly report it rather than silently changing the shared architecture.

14. Final architectural assessment
Recommended direction
The research supports the following architecture:
Universal World Model
Proposed foundation for Planetary Engine

1. Spatial foundation

Hierarchical reference frames, double-precision world coordinates, explicit time and state transformations.

2. Planetary geometry

Cube-sphere quadtree, body-specific shapes, deterministic terrain addressing and independent collision geometry.

3. Environmental foundation

Independent gravity, atmosphere, weather, water and material providers.

4. Simulation infrastructure

Independent physics partitions, server regions, streaming regions and persistence partitions.

5. Universal World Interface

One common contract through which every physical entity interacts with the same world.

6. Application and client layers

Rust simulation runtime, Node.js control plane, Python scientific tooling and Unity presentation.

Evidence classification
It is important that subsequent implementation agents understand which parts are established and which remain architectural proposals.
Classification	Findings
Established science	ECEF, inertial frames, geodetic transformations, gravitational potential, rotating-frame mechanics
Production-proven	NASA SPICE, GeographicLib, S2, OGC 3D Tiles, PhysX origin shifting, Geometry Clipmaps
Proposed architecture	UWI, hierarchical global coordinates, independent partition systems, common environmental contracts
Experimental	Fully deterministic distributed planetary simulation, globally coupled high-resolution environment
Unverified	Performance at thousands of concurrent players, actual CPU/memory costs of the combined system

My five strongest recommendations
1. Do not make Unity the owner of planetary coordinates. The Rust runtime owns canonical spatial state. Unity receives local representations.

2. Do not use one terrain hierarchy for everything. Rendering, collision, environmental fields and server regions have different requirements.

3. Treat reference frames as a first-class subsystem. Position, velocity and acceleration transformations are foundational, not utility functions.

4. Make environmental providers independent and replaceable. This allows different physical fidelity levels without changing the objects consuming the environment.

5. Standardize UWI before developing dependent simulation systems. This is the most important integration deliverable from Worker 01.

15. Primary research sources
The following are the most useful technical references for subsequent workers. These are primary documentation, scientific papers or official standards rather than secondary summaries.
Reference	Research relevance
NASA NAIF – Reference Frames	Frame graph, inertial/body-fixed transformations and state vectors
NASA NAIF – PCK Required Reading	Planetary shapes, orientation and physical constants
IERS – Data, Products and Tools	Earth orientation, ITRF, ICRF and time
NGA – WGS 84	Earth reference ellipsoid and geodetic system
NGA – Earth Gravitational Model	EGM2008 and geopotential
Google S2 – Cell Hierarchy	Cube projection, spherical quadtree and indexing
Uber H3 – Technical Overview	Alternative global spatial indexing
OGC – 3D Tiles	Production geospatial streaming standard
Geometry Clipmaps – Losasso & Hoppe	Large-scale terrain rendering
NVIDIA PhysX – Simulation	Physics simulation loops and island management
GeographicLib – Geodesics	Accurate ellipsoid calculations
NASA – Atmospheric Entry Research	Atmospheric physical models
JPL – Planetary Ephemerides	Astronomical position and velocity models


Worker 01 conclusion: The Universal World Interface and its underlying coordinate/frame architecture should become the foundational architectural contract for the entire Planetary Engine.

The geometry, environment, physics, networking and gameplay systems can then evolve independently while remaining physically connected to the same persistent universe.

No implementation has been performed in this research phase.

The next logical research package is Worker 02 – Universal Physics and Dynamics, building directly on the reference-frame and environmental contracts established here.

Můžu příště připravit a rozpracovat právě Worker 02 – včetně gravitační dynamiky, rigid-body fyziky, aerodynamiky, hydrodynamiky a interakce mezi různými měřítky simulace.
🪐Prepare Worker 02 next weekKlepnutím nastavíte