# PLANETARY ENGINE — WORKER 07

## Unity Client & Network Synchronization



Architecture research

Implementation prohibited



Role: Senior Unity Engine Architect / Multiplayer Networking Specialist

Objective: Design a Unity client capable of rendering and interacting with a persistent, physically coherent planetary universe, while keeping simulation authority in the Rust runtime.

### 1. Executive summary

The most important architectural decision is to separate three things that conventional game engines frequently combine:

1. World truth: What physically exists, where it exists and what state it has.
2. Simulation: How the world evolves over time.
3. Presentation: How a particular client sees and interacts with the world.

Unity should own the third responsibility and selected parts of the second for local responsiveness. It must not become the source of truth for the persistent universe.

I recommend a hybrid architecture:

- Rust owns authoritative physics, entity state, collision outcomes, gameplay rules and simulation time.
- Unity owns rendering, input collection, audio, animation, UI, visual effects, asset streaming and presentation interpolation.
- Unity may predict selected local actions, but the server remains authoritative.
- Global coordinates remain independent of Unity's local floating-point coordinate system.
- Network replication is relevance-based, hierarchical and multirate.
- Planetary rendering and gameplay simulation use separate spatial representations connected through stable identifiers and coordinate transformations.

Critical architectural principle: The client must be able to render a planet, enter its atmosphere, drive a vehicle, fly an aircraft, leave the planet and navigate space without changing the underlying world identity or simulation model.

## 2. Coordinate systems and planetary precision

This is the foundation on which every other client subsystem depends.

### 2.1 Why Unity coordinates cannot represent the universe directly

Unity's conventional `Transform.position` uses three 32-bit floating-point values.

A float has approximately 7 decimal significant digits. Its representable spacing increases with distance from zero.

For example:

| Distance from origin | Approximate float spacing |
| -------------------- | ------------------------- |
| 1 km                 | 0.061 mm                  |
| 10 km                | 0.977 mm                  |
| 100 km               | 7.8 mm                    |
| 1,000 km             | 6.25 cm                   |
| 10,000 km            | 1 m                       |
| 100,000 km           | 8 m                       |
| 1 million km         | 64 m                      |

These values are approximate and depend on the magnitude and direction of individual components.

A planetary simulation cannot use a single Unity scene coordinate system for global positions.

Consequences include:

- Unstable physics calculations.
- Jittering transforms.
- Poor collision precision.
- Z-fighting.
- Instability in shaders.
- Inaccurate camera movement.
- Problems with animation and particle systems.

Unity HDRP already addresses some rendering precision problems through camera-relative rendering. It translates rendering coordinates relative to the camera, but this does not solve the underlying simulation coordinate problem.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



### 2.2 Recommended three-level coordinate architecture

Level 1 — Universe coordinates

# ECEF / double precision

Rust authoritative world representation

Global entity position, astronomical position, orbital state and stable world identity.

Coordinate transformation

Level 2 — Planetary coordinates

# WGS84 / local tangent frame

Planetary navigation and regional positioning

Latitude, longitude, ellipsoidal altitude, ENU/NED frames and terrain tile identity.

Floating-origin transformation

Level 3 — Unity rendering coordinates

# Local float coordinates

Unity scene and GPU rendering

Camera-relative transforms, local physics presentation, meshes and effects.

The key distinction:

A floating-origin shift must never change an entity's authoritative position.

It changes only the coordinate transformation used to display that entity.

### 2.3 ECEF and WGS84

WGS84 provides an established geodetic reference system.

The ellipsoid parameters are:

\\[ a=6378137\text{ m} \\]

\\[ f=\frac{1}{298.257223563} \\]

\\[ e^2=f(2-f) \\]

For latitude \\(\phi\\), longitude \\(\lambda\\), altitude \\(h\\):

\\[ N(\phi)=\frac{a}{\sqrt{1-e^2\sin^2\phi}} \\]

\\[ X=(N+h)\cos\phi\cos\lambda \\]

\\[ Y=(N+h)\cos\phi\sin\lambda \\]

\\[ Z=(N(1-e^2)+h)\sin\phi \\]

ECEF is a useful planetary coordinate representation because all clients can refer to the same global location without depending on their local origin.

However, WGS84 should not be treated as the universal coordinate system for the entire astronomical universe.

Recommended distinction:

- ECEF: positions relative to a planet's body-fixed frame.
- ECI or another inertial frame: orbital and astronomical dynamics.
- Local ENU/NED: planetary navigation.
- Unity local coordinates: rendering and presentation.

Each planet must have a stable body-frame transform and an explicit relationship to the astronomical reference frame.

### 2.4 Floating origin

The client should maintain a local origin near the active player or camera.

\\[ P\_{local}=P\_{global}-O\_{global} \\]

Where:

- \\(P\_{global}\\): authoritative global position.
- \\(O\_{global}\\): current client rendering origin.
- \\(P\_{local}\\): Unity local position.

When the camera exceeds a configured threshold, the local origin is shifted.

All relevant presentation objects must receive the same transformation.

This includes:

- Terrain.
- Vehicles.
- Characters.
- Dynamic rigidbody proxies.
- Particle systems.
- Lights.
- Cameras.
- Audio emitters.
- Navigation markers.
- Visual effects.

Important: Do not repeatedly subtract a floating-origin offset from authoritative positions. Keep authoritative coordinates separate and recompute local transforms from the current origin.

### 2.5 Floating-origin implementation alternatives

| Approach                          | Advantages              | Limitations                                                                |
| --------------------------------- | ----------------------- | -------------------------------------------------------------------------- |
| Global scene shifting             | Simple conceptual model | Every active object must be shifted correctly                              |
| Local origin per scene/region     | Better isolation        | Cross-region presentation becomes more complex                             |
| Camera-relative rendering         | Good GPU precision      | Does not solve CPU-side physics                                            |
| Double-precision Unity transforms | High numerical range    | Standard Unity components and physics are not universally double-precision |
| Hybrid coordinate layers          | Scalable and flexible   | More engineering and validation                                            |

Recommendation: Hybrid.

Keep global positions in a dedicated coordinate layer using doubles. Unity scene objects use floats relative to a local origin. Use HDRP camera-relative rendering for the final rendering stage.

### 2.6 Floating-origin failure modes

Particular attention is required for:

1. Rigidbody velocity changing during origin shifts.
2. Joints breaking or acquiring incorrect anchor positions.
3. Terrain seams appearing after origin changes.
4. Particle systems retaining old world positions.
5. Shader world-space coordinates becoming inconsistent.
6. Network interpolation buffers mixing different origins.
7. Audio Doppler calculations using incorrect coordinate deltas.
8. Large-scale shadow cascades becoming unstable.

The network layer must therefore transmit global or sector-relative coordinates, never Unity scene coordinates.

## 3. Planetary rendering architecture

### 3.1 The rendering problem

A planet is not simply an enormous terrain mesh.

A planetary renderer must handle radically different viewing scales:

- Entire planet.
- Continental regions.
- Mountain ranges.
- Cities.
- Roads.
- Individual buildings.
- Characters and vehicles.

A single mesh LOD hierarchy is not sufficient for the complete problem.

I recommend separating planetary surface rendering from local gameplay environment rendering.

[Astronaut Photo ART002-E-23319](https://images.openai.com/static-rsc-4/uaX3k1qgyAH9SZyrA4t2g_uHxNKR-VlkW6bPc34EoeJ-igECiFw_ZPMPs1TMtmlm4H-FhIt9H7wZ-TqX-YbJehq6T0K4wbT8ng50rrTw_jFUeELosvKN0teZCLjcBTKVdNVzdb6wI9QWwRCyihSyzXmjTvcm8rnAA4X7wAwnKTk?purpose=inline)

[GitHub - mathis-s/PlanetaryTerrain: Unity Asset for spherical terrain with dynamic quadtree-based LOD](https://images.openai.com/static-rsc-4/AEfQIxxr_edhkmzr_aGb8ph-84BtlKACUYfyX0eKFRNArH4hhK1Vk9tDW_tgKjKZ2AxaVaolZT4kclym7EQDn0SU5JLOSz8-k7Mh5eLiC0DKzf9nUKd9QJFTxy_JYThjb5i-VEoDNieLiT24OWpR3zzdxE53Pl4cwDoEe08MBBU?purpose=inline)

[High-Resolution Imagery with Planet Satellite Tasking | Planet](https://images.openai.com/static-rsc-4/KwscyAGla2nyRtPP_6d3CroALw5VoaakGUAiDegPmvlUVJHmXDaDjk6QuekXW8sf-1wVg4w39-HDXmJY2Xx5zz-rW6IgaU690Xgkb8JQ6eyKoLH-hTbQjG86d3UmGGVNsHMUglOJUNJo_gkDMJCkxkP4r1KmfwzVLkAf-O2nLq4?purpose=inline)

9

### 3.2 Cube-sphere terrain

A cube-sphere maps six cube faces onto a sphere.

Each face can be subdivided recursively into four child patches.

This creates a quadtree.

\\[ N\_{children}=4N\_{parent} \\]

At depth \\(d\\), the theoretical number of patches is:

\\[ N_d=6\cdot4^d \\]

This is an important property for scalable planetary terrain.

The renderer does not need to load all patches. It loads only the patches visible and relevant to the current camera.

Advantages:

- Uniform global coverage.
- No singularity at the poles.
- Natural hierarchical LOD.
- Suitable for streaming.
- Compatible with procedural terrain.
- Independent of local gameplay tile dimensions.

Limitations:

- Patch boundary management.
- LOD transitions.
- Projection distortion.
- Terrain stitching.
- Horizon visibility.
- GPU memory management.

### 3.3 Two-tier terrain representation

I recommend two related but independent representations.

Tier A — Planetary terrain

- Cube-sphere patches.
- Low and medium resolution.
- Planet-scale elevation.
- Biome and material information.
- Continental geography.
- Large mountain formations.
- Ocean and coastline representation.

Tier B — Gameplay terrain

- Existing 10 × 10 km tile concept as a reference requirement.
- High-resolution terrain.
- Roads.
- Buildings.
- Vegetation.
- Collision proxies.
- Gameplay objects.
- Destruction and modification.

[Behind the Scenes – Planet LOD | Patreon](https://images.openai.com/static-rsc-4/bmykznN3A2wva7Gvd0KsmQW0YZwg3xYLcVcurTQDIbAwyJIzdnePY2FoXfVAM6MWh_Y2RtfVHZxzGhsSMaYRKvOjJTEtR1heHOeRCc5PNPhGaGsxm85K9kyGO_wD6GT_QLBeYhPS7ic0FBeL_cYtZpIppLBBFyntKWacW6-Vaig?purpose=inline)

[Tessellated Terrain Rendering with Dynamic LOD - victorbush](https://images.openai.com/static-rsc-4/UvxxsU1-qJPqIrXwqr5hwwa2fOY77_AaSUpTitvzdReVW8TsR5JjXk-ubyFgy7tQlRFFkFyBfXMlaFO2XE6abrD6OcdX7NcRD4NXf_EXwc4VPRmm8CQv-xe__YPSUlnX3feHMTmGssfmfvSaRG_z9zvex8cnKHCYleetmdiDqGk?purpose=inline)

[TerrainRenderingInFarCry5](https://images.openai.com/static-rsc-4/OnLaodx1sN7vEtYsM3kluLyZQxc10bfie-cYJmdIq5BDu7dctamQlnO_9wIk9Plq9y4I4ISI7xMFOYeTQwjwALu3pCuF-OSUgnsg9wY7XxZyKv5rkaFD9mRPv8WtrMLNZlt93Sh-TmXwj6-Qw-kbdvIIAomKPJAthPm95i9GVRw?purpose=inline)

12

These representations must share a consistent underlying world coordinate system.

The planetary renderer should not independently generate a different mountain range from the gameplay terrain.

Instead, the planetary representation is a lower-resolution view of the same underlying terrain data.

### 3.4 LOD selection

A useful initial approach is screen-space geometric error.

\\[ SSE=\frac{E\cdot H}{2d\tan(FOV_y/2)} \\]

Where:

- \\(E\\): geometric approximation error in world units.
- \\(H\\): viewport height in pixels.
- \\(d\\): camera distance to the patch.
- \\(FOV_y\\): vertical field of view.

A patch is subdivided when its projected error exceeds a threshold.

The actual implementation must account for the camera's distance to the patch bounding volume rather than simply measuring distance to the patch center.

Additional selection inputs:

- Horizon visibility.
- Camera movement.
- Current GPU load.
- Streaming bandwidth.
- Terrain residency.
- Gameplay relevance.
- Shadow requirements.

### 3.5 Terrain streaming

The terrain streaming system should be independent of the gameplay simulation.

Recommended pipeline:

1\. Spatial request

Camera and gameplay relevance determine required terrain patches.

2\. Asset resolution

Determine whether data exists locally or must be downloaded/generated.

3\. Background processing

Decode elevation, materials, mesh data and collision metadata.

4\. GPU upload

Upload meshes, textures and material data.

5\. Residency management

Maintain active patches, LOD transitions and eviction policies.

A critical design decision: Unity should not generate or download every terrain asset synchronously on the main thread.

Use background workers for decoding and preparation, and budget the main-thread/GPU upload work per frame.

### 3.6 Rendering technology decision

| Technology               | Suitability                                                                              |
| ------------------------ | ---------------------------------------------------------------------------------------- |
| Unity HDRP               | Recommended high-fidelity rendering baseline                                             |
| Unity URP                | Alternative for lower-end hardware                                                       |
| Custom SRP               | Possible, but high maintenance                                                           |
| Custom planetary shaders | Required for specialized planet rendering                                                |
| Unity Terrain            | Useful for local gameplay regions, not sufficient as the global planetary representation |

HDRP camera-relative rendering is documented in Unity's Graphics repository.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



Recommendation: HDRP initially, with a renderer architecture that does not hardwire planetary terrain to HDRP-specific data structures.

This leaves room for a lower-cost URP client later.

### 3.7 Atmosphere, clouds, oceans and celestial rendering

These are separate rendering subsystems, not ordinary terrain materials.

Atmosphere

The atmosphere should be evaluated using planetary altitude, atmospheric composition, density profiles and illumination.

A simplified exponential density model:

\\[ \rho(h)=\rho_0e^{-h/H} \\]

Where \\(H\\) is the atmospheric scale height.

For a physically based appearance, the renderer needs:

- Rayleigh scattering.
- Mie scattering.
- Ozone absorption.
- Sun direction and intensity.
- Planet radius.
- Camera altitude.
- Atmospheric composition.

HDRP's Physically Based Sky already provides a useful baseline for spherical planetary atmosphere, including exponentially decreasing atmospheric density and ozone-layer modelling.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Clouds

Separate cloud representations into:

| Altitude/scale | Representation                              |
| -------------- | ------------------------------------------- |
| Orbital view   | Planet-scale cloud maps                     |
| High altitude  | Volumetric cloud fields                     |
| Local flight   | Volumetric cloud volumes                    |
| Ground view    | Local cloud shadows and atmospheric effects |

HDRP supports cloud layers and volumetric clouds, including camera traversal through cloud volumes.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



Oceans

This is an important integration limitation.

HDRP's native water system offers ocean, sea, lake, pool and river surfaces, underwater rendering, foam, caustics and deformation.

However, Unity documents that its water implementation does not support spherical or vertical water bodies.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



Therefore, the global ocean must use a custom planetary water representation.

The native HDRP water system can be used in local gameplay regions, provided its limitations are respected.

I recommend:

- Planetary ocean: spherical shader and planetary surface geometry.
- Regional ocean: high-resolution wave rendering.
- Local interactions: buoyancy and water interaction proxies.
- Server: authoritative water state, currents and physical forces.
- Client: visual wave simulation and local presentation.

Do not assume that a visually convincing wave simulation is a physically authoritative ocean model.

Celestial objects

The renderer should support:

- Stars.
- Planets.
- Moons.
- Asteroid fields.
- Rings.
- Nebulae.
- Black holes.
- Accretion disks.

These objects need multiple rendering regimes.

A star may be a billboard or emissive point from one camera distance and a detailed volumetric representation from another.

The rendering representation changes, but the astronomical entity identity remains stable.

## 4. Unity client responsibilities

### 4.1 Responsibility boundaries

The most dangerous architectural mistake would be allowing Unity's local physics to gradually become authoritative.

I propose the following responsibility matrix.

| System               | Unity client                  | Rust server                     |
| -------------------- | ----------------------------- | ------------------------------- |
| Input collection     | Authoritative input source    | Validation and consumption      |
| Character movement   | Prediction                    | Authoritative movement          |
| Vehicle physics      | Visual prediction             | Authoritative physics           |
| Aircraft             | Presentation/prediction       | Flight dynamics                 |
| Spacecraft           | Presentation/prediction       | Orbital and spacecraft dynamics |
| Collision            | Cosmetic prediction           | Authoritative collision         |
| Terrain rendering    | Full responsibility           | Terrain data authority          |
| Terrain modification | Preview                       | Authoritative modification      |
| Economy              | UI                            | Authoritative                   |
| Construction         | Preview                       | Validation and persistence      |
| AI                   | Animation/presentation        | Authoritative decisions         |
| Weather              | Visual effects                | Physical weather state          |
| Audio                | Full responsibility           | Events and source state         |
| Animation            | Full responsibility           | Gameplay-relevant state         |
| Multiplayer          | Replication and interpolation | Entity ownership and authority  |

### 4.2 Client architecture layers

Presentation layer

Cameras, rendering, UI, audio, animation, effects

Gameplay interaction layer

Input contexts, player controllers, vehicle interfaces, editor tools

Prediction and interpolation layer

Local prediction, reconciliation, snapshot buffers, visual smoothing

Network client layer

Transport, serialization, entity replication, relevancy, connection state

World presentation layer

Global coordinate conversion, terrain streaming, local world representation

Platform layer

Jobs, Burst, memory management, asset cache, diagnostics

These should be separate modules with explicit interfaces.

### 4.3 Recommended Unity technology choices

| Function                       | Technology                                            |
| ------------------------------ | ----------------------------------------------------- |
| Rendering                      | HDRP                                                  |
| Performance-oriented rendering | URP alternative                                       |
| High-volume data processing    | C# Jobs + Burst                                       |
| Large entity presentation      | Entities Graphics / instancing                        |
| Local scene objects            | GameObjects                                           |
| Asset loading                  | Addressables                                          |
| UI                             | UI Toolkit                                            |
| Input                          | Input System                                          |
| Visual effects                 | VFX Graph                                             |
| Audio                          | Unity Audio / dedicated audio middleware if justified |
| Networking                     | Custom client protocol                                |
| Diagnostics                    | Unity Profiler + custom telemetry                     |

I would not make the entire client ECS-only.

A hybrid GameObject/ECS architecture is more appropriate:

- ECS for large populations of replicated entities.
- GameObjects for complex player-controlled presentation, cameras and interaction objects.
- Shared coordinate and entity identity layer.
- Explicit conversion boundaries.

This avoids forcing every type of object into the same representation.

## 5. Networking architecture

This is arguably the most important part of Worker 07.

### 5.1 Why conventional Unity multiplayer frameworks are insufficient as the primary architecture

Unity Netcode for Entities is a useful reference implementation of prediction, rollback, ghost replication and snapshot systems.

Its prediction architecture runs the same simulation on the client and server, using fixed timesteps and rollback.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://docs.unity.cn\&sz=32)

0.51.1-preview\.21+1



But our server is Rust.

We should not couple the authoritative simulation to Unity's ECS runtime or require the Rust server to reproduce Unity's internal ghost model.

Unity Netcode should therefore be treated as:

- A source of established networking concepts.
- A potential reference for snapshot and relevancy algorithms.
- A possible option for isolated Unity-to-Unity subsystems.

It should not define the Planetary Engine wire protocol.

Recommendation: Custom cross-language networking protocol.

### 5.2 Transport alternatives

| Transport                | Advantages                                       | Limitations                                                     |
| ------------------------ | ------------------------------------------------ | --------------------------------------------------------------- |
| UDP + custom reliability | Maximum control                                  | Reliability, congestion control and security must be designed   |
| Unity Transport          | Existing Unity integration                       | Rust compatibility and protocol integration require custom work |
| QUIC                     | Reliable streams, congestion control, encryption | Additional complexity and datagram support considerations       |
| WebSocket                | Browser compatibility, easy integration          | TCP head-of-line blocking for real-time state                   |
| TCP                      | Simple and reliable                              | Unfavorable for time-sensitive snapshot traffic                 |

I recommend investigating QUIC as the primary transport candidate.

QUIC datagrams provide unreliable application datagrams within a QUIC connection, using the connection's congestion controller. They are not a replacement for reliable streams, and the receiver may drop datagrams.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://www.rfc-editor.org\&sz=32)

rfc-editor.org



This gives us a useful division:

- Reliable streams: authentication, session setup, commands, persistent transactions and large control messages.
- Unreliable datagrams: snapshots, frequent input and transient state updates.
- Separate asset delivery: HTTP-based or dedicated content distribution.

Unity Transport is also a valid alternative for a UDP-based client transport, but it should not be confused with a complete cross-language networking solution.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub+1



### 5.3 Recommended network topology

Unity client

Input / prediction / interpolation / rendering

QUIC candidate / encrypted connection

Node.js control plane

Authentication / sessions / orchestration / APIs

Internal authenticated service interface

Rust simulation runtime

Authority / physics / entities / world state

Persistence and world data

World state / entity state / terrain / events

One important qualification: Node.js should not become a mandatory relay for every real-time state packet.

It is the control plane, not the physics execution layer.

The network architecture should permit direct client-to-authoritative-simulation connections or dedicated Rust networking gateways where appropriate.

### 5.4 Separate reliable and unreliable channels

The protocol should use logical channels, regardless of the final transport.

| Channel            | Reliability                         | Data                                 |
| ------------------ | ----------------------------------- | ------------------------------------ |
| Session            | Reliable                            | Authentication, session setup        |
| Commands           | Reliable                            | Construction, economy, world editing |
| Input              | Unreliable sequenced                | Player input frames                  |
| State snapshots    | Unreliable                          | Entity positions and velocities      |
| Events             | Reliable or explicitly acknowledged | Important gameplay events            |
| Cosmetic events    | Unreliable                          | Particles, transient effects         |
| Streaming metadata | Reliable                            | Terrain and asset references         |

Avoid sending large asset payloads through the real-time snapshot channel.

Also avoid making all commands reliable and globally ordered if they are logically independent. Excessive ordering creates unnecessary coupling.

### 5.5 Snapshot interpolation

Remote entities should normally be rendered slightly behind authoritative simulation time.

Suppose the client receives:

- Snapshot A at time \\(t_0\\).
- Snapshot B at time \\(t_1\\).

The interpolation factor is:

\\[ \alpha=\frac{t_r-t_0}{t_1-t_0} \\]

Where \\(t_r\\) is the client render time.

Position:

\\[ P(t_r)=(1-\alpha)P_0+\alpha P_1 \\]

Orientation should use quaternion spherical interpolation:

\\[ Q(t_r)=Slerp(Q_0,Q_1,\alpha) \\]

For entities with sufficiently smooth motion and velocity data, Hermite interpolation can produce better trajectories.

The client maintains a snapshot buffer and adapts its interpolation delay to network jitter and packet loss.

This is an established approach for non-deterministic networked physics.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://www.gafferongames.com\&sz=32)

Gaffer On Games+1



### 5.6 Client prediction and reconciliation

The local player needs immediate response.

A recommended flow:

1. Capture player input.
2. Assign an input sequence number.
3. Apply local predicted movement.
4. Send input to the server.
5. Server validates and simulates the input.
6. Server sends authoritative state and last processed input sequence.
7. Client compares predicted state against authoritative state.
8. Client corrects its predicted simulation.
9. Client visually smooths the correction.

The client must retain a bounded history of unacknowledged inputs.

When an authoritative update arrives, inputs already processed by the server are removed from the history. Remaining inputs are replayed where the local prediction model supports it.

The important distinction:

Physics correction and visual correction are not the same operation.

The simulation state should be corrected to the authoritative result. A separate visual smoothing layer can hide small discontinuities.

Do not gradually blend an incorrect authoritative physics state into a new state and then use that blended value as the basis for further physics calculations.

This distinction is also emphasized in Gaffer On Games' state synchronization discussion.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://gafferongames.com\&sz=32)

Gaffer On Games+1



### 5.7 What should be predicted?

| Entity                     | Prediction strategy                                        |
| -------------------------- | ---------------------------------------------------------- |
| Player character           | Full local movement prediction                             |
| Player-controlled car      | Selective vehicle prediction                               |
| Player-controlled aircraft | Selective flight prediction                                |
| Spacecraft                 | Input response prediction, server-authoritative trajectory |
| Remote cars                | Snapshot interpolation                                     |
| Remote characters          | Snapshot interpolation                                     |
| Projectiles                | Limited prediction or trajectory interpolation             |
| Static buildings           | No continuous prediction                                   |
| Construction               | Local preview, server confirmation                         |
| Economy                    | No prediction of authoritative transactions                |
| World editing              | Optimistic UI preview, authoritative commit                |

A crucial design choice is to avoid predicting every entity.

For example, predicting a 50-vehicle traffic simulation locally would introduce considerable computational work and reconciliation complexity without providing meaningful player responsiveness.

Prediction is a latency optimization, not a requirement that the entire world run twice.

## 6. Network relevancy and entity ownership

### 6.1 Why proximity alone is insufficient

A planetary world can contain millions of entities.

A client must not receive every entity simply because it exists in the persistent universe.

The server needs an Area of Interest (AOI) system.

However, a conventional spherical distance check is insufficient.

Consider a player in a spacecraft:

- A nearby spacecraft is relevant.
- A planet may be relevant despite being millions of kilometres away.
- A distant star may be visually relevant.
- A distant battle may be relevant to a strategic observer.
- An entity on the opposite side of a planet may be physically nearby in a different geometric sense, but not visually accessible.

Relevancy therefore needs multiple dimensions.

### 6.2 Proposed relevancy model

Each entity has relevance properties:

| Property              | Purpose                           |
| --------------------- | --------------------------------- |
| Spatial proximity     | Distance from observer            |
| Visibility            | Potential visibility              |
| Interaction           | Can the player interact with it?  |
| Gameplay relationship | Team, ownership, combat           |
| Strategic relevance   | Important world-level information |
| Temporal relevance    | Events that need to be observed   |
| Subscription          | Explicitly requested information  |

I recommend combining:

1. Hierarchical spatial indexing.
2. Distance-based relevance.
3. Visibility and occlusion filtering.
4. Gameplay relationship filtering.
5. Explicit subscriptions.
6. Per-client bandwidth budgets.

A player entering a strategic overview should receive a different relevancy set from a player driving a car.

Unity's Netcode for Entities documents an explicit relevancy mechanism and importance-based snapshot prioritization. These are useful references, even though the actual implementation should reside in our cross-language networking architecture.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://docs.unity.cn\&sz=32)

1.5.0-exp.2+1



### 6.3 Hierarchical spatial relevance

Use different spatial representations for different scales.

| Scale                | Spatial index                         |
| -------------------- | ------------------------------------- |
| Universe             | Galaxy / stellar-system hierarchy     |
| Planet               | Planet identifier + cube-sphere patch |
| Regional             | Tile / hierarchical grid              |
| Local gameplay       | Spatial hash or BVH                   |
| Dynamic interactions | Local physics broadphase              |

The server should determine relevance.

Unity can additionally perform visual frustum culling and occlusion culling to reduce rendering work.

These are two different operations:

- Server AOI determines which information is sent.
- Client visibility determines which already-known objects are rendered.

Do not confuse them.

### 6.4 Ownership and authority

Every networked entity should have an explicit authority model.

I propose:

| Authority            | Meaning                                    |
| -------------------- | ------------------------------------------ |
| Server authoritative | Rust owns simulation and state             |
| Client input owner   | One client supplies validated inputs       |
| Shared observation   | Multiple clients observe the same state    |
| Editor-authorized    | User can submit modification commands      |
| System-owned         | AI or world simulation controls the entity |

Ownership must not mean that the client owns the actual entity state.

A player controlling a spacecraft owns the right to issue commands, not the right to declare its authoritative position.

This distinction is particularly important for:

- Vehicles.
- Aircraft.
- Spacecraft.
- Construction systems.
- Multiplayer economy.
- Destructible environments.

## 7. State compression and replication

### 7.1 Do not replicate complete objects continuously

A networked entity should have a stable identity and a collection of independently replicated state components.

Example:

| Component        | Replication frequency            |
| ---------------- | -------------------------------- |
| Position         | High                             |
| Orientation      | High                             |
| Linear velocity  | High when needed                 |
| Angular velocity | High when needed                 |
| Health           | On change                        |
| Inventory        | On change                        |
| Configuration    | On change                        |
| Appearance       | On change                        |
| Ownership        | On change                        |
| AI state         | According to relevance           |
| Animation        | Compressed events/state          |
| Static metadata  | Initial spawn or explicit update |

The server sends only changed data when appropriate.

### 7.2 Snapshot delta compression

A snapshot should normally reference a baseline.

\\[ \Delta S=S_t-S\_{baseline} \\]

The client reconstructs the current state using the baseline and transmitted changes.

The protocol must track baseline identity and handle missing baselines.

A baseline that the client does not possess cannot be used safely.

Possible strategies:

- Periodic full snapshots.
- Acknowledged baseline selection.
- Snapshot history.
- Explicit entity creation and destruction.
- Recovery when a baseline becomes unavailable.

### 7.3 Quantization

Global ECEF coordinates should not be transmitted as three uncompressed 64-bit floats in every update.

Instead, use hierarchical encoding.

For example:

- Planet ID.
- Region/tile ID.
- Position relative to region.
- Quantized local position.
- Orientation.
- Velocity.
- Timestamp/tick.

This is considerably more efficient when entities are spatially clustered.

Orientation can use quaternion compression, such as smallest-three representation.

Velocity should use quantization ranges appropriate to entity type.

A slow pedestrian and an orbital spacecraft should not share the same velocity encoding assumptions.

### 7.4 Avoid a universal entity packet

I recommend entity-specific replication schemas built on shared primitives.

Examples:

- Character state.
- Ground vehicle state.
- Aircraft state.
- Spacecraft state.
- Projectile state.
- Static building state.
- Construction state.
- Strategic entity state.

All schemas should use common entity identity, timestamp, versioning and coordinate conventions.

### 7.5 Replication scheduling

Not all entities need the same update frequency.

Proposed initial design targets, not measured benchmarks:

| Entity category           | Initial update target |
| ------------------------- | --------------------- |
| Local player input        | 60 Hz                 |
| Local predicted character | 60 Hz                 |
| Nearby moving vehicles    | 20–30 Hz              |
| Nearby characters         | 15–30 Hz              |
| Distant dynamic entities  | 1–10 Hz               |
| Strategic entities        | 0.2–2 Hz              |
| Static entities           | Event-driven          |
| Economy and configuration | Event-driven          |

These are tuning starting points.

The final rates must be determined using actual latency, bandwidth, entity count and motion characteristics.

The client should interpolate at its rendering frame rate independently of network snapshot frequency.

## 8. Time synchronization

This subsystem deserves its own explicit design.

The server simulation has an authoritative timeline.

The client has:

1. A predicted timeline.
2. An interpolated timeline.

The client must estimate server time and maintain a stable relationship between these timelines.

Unity's networking documentation describes the distinction between prediction ticks and interpolation ticks and adjusts interpolation delay using RTT, jitter and packet arrival behavior.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://docs.unity.cn\&sz=32)

1.1.0-exp.1+1



### 8.1 Proposed time model

Every authoritative update should include:

- Server simulation tick.
- Simulation time.
- World epoch or timeline identifier.
- Entity state version.
- Relevant event sequence.

The client maintains:

- Estimated server tick.
- Input target tick.
- Last authoritative tick.
- Interpolation tick.
- Snapshot buffer.
- RTT estimate.
- Jitter estimate.

### 8.2 Important distinction: simulation time versus real time

Planetary Engine must eventually support time acceleration, pauses, replays and alternate what-if timelines.

Therefore, network time cannot simply be represented by Unity's `Time.time`.

Use two separate concepts:

Wall-clock/network time

Used for:

- RTT.
- Packet arrival.
- Timeouts.
- Connection management.
- Client-server clock estimation.

Simulation time

Used for:

- Physics.
- World evolution.
- Entity state.
- Events.
- Prediction.
- Replays.

A simulation that advances 100 times faster than real time must not accidentally cause network timeouts or interpolation buffers to advance 100 times faster.

This is especially important for the What-If Simulation Platform.

### 8.3 Unity local physics

Unity's fixed timestep is appropriate for fixed-rate local presentation and prediction loops, but it should not control the authoritative simulation clock.

Unity documents that its physics advances in fixed time steps, and that heavy frames can trigger multiple physics updates.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://docs.unity3d.com\&sz=32)

マニュアル: Understand physics performance issues+1



We should implement an explicit simulation-time adapter rather than assuming that Unity's fixed update is equivalent to Rust's simulation tick.

## 9. Multiple gameplay perspectives

All perspectives should be different presentations of the same persistent world.

They should not have independent entity models.

[DayZ - Screenshots der Standalone-Version](https://images.openai.com/static-rsc-4/ll_4a_5Ue3NxpcSt2yWgdacYjWgAMrvJ5uCC3b-KGoYGI5MPX_kHGwU1hDJEJZJYN2RCCwEVlL-ju44EpgEE9E327YaMGTmGZynwjfuWFSi5ce7JfIqwXr6jDQaEycZueco9qo5KcRC-bEdwCVbC39-KqjBqqjqCdFnZPxClN2k?purpose=inline)

First person

Local camera, head movement, interaction, weapon/tool presentation and high-frequency local feedback.

[Bad Way on Steam](https://images.openai.com/static-rsc-4/lABdyRZwFrw6VEOyskWuTYoCLucREqCtXieQb3fuMu40RvmBcRlVoAWpMS_MupEDi98gGzk76h3Cd8QnLfEfBxQ73UFEiIiJXiSKf_r0SmTwCP3T4zeyfrIGKXSCJHTKAYOaaWdF0dhi7jLlyyyH41m5RdPScJ_5m0nZm65WlGg?purpose=inline)

Third person

Character animation, camera collision, movement prediction and contextual interactions.

[Screenshot of Forza Horizon 4 (Windows, 2018) - MobyGames](https://images.openai.com/static-rsc-4/KtGdgaIyyKwGfAL9jBwzvvfIy1aTJxafUuWaa06mcIPEH-RoQf4sqCoTIDlQhCfuNGIgZOebfmeYqzLUn14AntT5CqYcCo48lkBcWqqonotacDGK72cwUP2mkG2E1ZRT4FehMk0q1hi3Jk5saIbL7mtKrIZ28xKwruE2dwmegtg?purpose=inline)

Vehicle camera

Cockpit, chase camera, dashboard, vehicle telemetry and camera-relative presentation.

[Fenix Simulations Releases A320 V2 Block 1 - FSElite](https://images.openai.com/static-rsc-4/tVSXJJTkJ_IADuYPeyi3mQ7qBJ160mvHqjghA0CuXvzRpzZwazj26Uljm2k99pQrxX-ws-untRo2W5uB4p5m1OmI77qAeOeVNyUfetEQibg4FzBK3OCNV6Jee9HR5-RtQCrVZWlDcHq0p11c5DvBvGqsVNu5TukTwhqjYpBj41M?purpose=inline)

Aircraft

Cockpit instruments, flight displays, external cameras and atmospheric transitions.

[Reentry - A Space Flight Simulator - Reentry has been updated to version 0.65 - TSS for Gemini - Steam News](https://images.openai.com/static-rsc-4/nSNyQwvDrDKLx55qr448yCWaMBqkfMneNAdKUsjEMVLP-_kgbgsSts18NYGh0aYA_xThAz-gmBt4UgOklh-VDEe2bxHt5DlZic9hvap9FhY5c8bx7MvLX0JTNCFGRS8FbqdOrGPGBdHoNruOKrAyMWQ5QiZPvw7gjjzKVP7nYLQ?purpose=inline)

Spacecraft

Orbital navigation, attitude indicators, relative reference frames and orbital visualization.

[v1.65 Early Access Update - GalCiv IV: Supernova » Forum Post by redskittlesonly](https://images.openai.com/static-rsc-4/FTxr3o8TDbV5booZ196DNqsxpKCAe3PETy4R4NwdJ6fBC6YKobaImkzd0mswYA2nh72_mR2W3IcVGc-Vh0DVRlu_hw6JI_vtIk6P6JHCy1hAtD8EFVZppW5w1wodr3VngNRv-Bva5Eb7r8MgSXUyL6vIa6D613t_KblMQ1c6IyM?purpose=inline)

Strategic overview

Planetary map, ownership, economy, logistics, entities and large-scale operations.

World editor

Terrain modification, construction previews, object placement and collaborative editing.

### 9.1 Perspective switching

A camera switch must not cause:

- Entity recreation.
- Simulation reset.
- Ownership changes.
- Network session restart.
- Physics state changes.

It should change:

- Active camera.
- Input mapping.
- UI context.
- Presentation detail.
- Rendering LOD.
- AOI subscription profile where necessary.

This means gameplay modes are primarily client-side presentation and interaction contexts.

### 9.2 Strategic overview

The strategic overview has different requirements from first-person gameplay.

It should receive aggregated state rather than all detailed entity transforms.

For example:

- City population.
- Ownership boundaries.
- Resource production.
- Military formations.
- Infrastructure condition.
- Economic indicators.

Detailed entity data can be requested when the player zooms into a particular region.

This is a major opportunity to reduce bandwidth and client memory.

## 10. Proposed Unity module structure

The following is a logical architecture, not an instruction to create these files during research.

```
PlanetaryEngine.Unity/
│
├── Core/
│   ├── Bootstrap/
│   ├── Configuration/
│   ├── Diagnostics/
│   └── Lifecycle/
│
├── World/
│   ├── Coordinates/
│   ├── ReferenceFrames/
│   ├── FloatingOrigin/
│   ├── PlanetRegistry/
│   └── WorldIdentity/
│
├── Rendering/
│   ├── Planetary/
│   ├── Terrain/
│   ├── Atmosphere/
│   ├── Clouds/
│   ├── Oceans/
│   ├── Celestial/
│   ├── LOD/
│   └── Streaming/
│
├── Networking/
│   ├── Transport/
│   ├── Protocol/
│   ├── Serialization/
│   ├── TimeSync/
│   ├── Replication/
│   ├── Relevancy/
│   ├── Snapshots/
│   └── Connection/
│
├── Simulation/
│   ├── Prediction/
│   ├── Reconciliation/
│   ├── Interpolation/
│   ├── InputHistory/
│   └── PresentationPhysics/
│
├── Entities/
│   ├── Characters/
│   ├── Vehicles/
│   ├── Aircraft/
│   ├── Spacecraft/
│   ├── Buildings/
│   └── Effects/
│
├── Gameplay/
│   ├── FirstPerson/
│   ├── ThirdPerson/
│   ├── Driving/
│   ├── Flight/
│   ├── Space/
│   ├── Strategy/
│   └── WorldEditor/
│
├── Content/
│   ├── Addressables/
│   ├── AssetCache/
│   ├── TerrainCache/
│   └── AssetVersioning/
│
├── UI/
│   ├── HUD/
│   ├── Navigation/
│   ├── Inventory/
│   ├── Strategy/
│   └── Editor/
│
└── Audio/
    ├── SpatialAudio/
    ├── Environment/
    └── Events/
```

### 10.1 Architectural dependency rules

- Rendering must not depend directly on the transport implementation.
- Entity presentation must not define authoritative entity state.
- Gameplay input must not write directly into authoritative state.
- World coordinates must not depend on Unity `Transform`.
- Network serialization must not depend on Unity GameObjects.
- Terrain rendering must not own terrain persistence.
- Camera systems must not control the floating-origin authority.
- UI must issue commands through application interfaces.

These boundaries will make it possible to replace rendering or transport components without redesigning the simulation.

## 11. API and data contracts

The client/server contract must be independent of Unity and Rust internal structures.

### 11.1 Common entity identity

Recommended conceptual schema:

```
EntityIdentity
    entity_id: 128-bit stable identifier
    entity_type: uint32
    schema_version: uint16
    authority_epoch: uint32
```

An entity ID must remain stable across:

- Client reconnects.
- Server migration.
- World persistence.
- Region transitions.
- Rendering representation changes.

The authority epoch prevents stale ownership or authority messages from being mistaken for current state.

### 11.2 World position

```
WorldPosition
    reference_frame_id: uint64
    body_id: uint64
    region_id: uint64
    position_x: float64
    position_y: float64
    position_z: float64
```

The representation is conceptual.

For the actual wire protocol, we should support multiple coordinate encodings:

- ECEF.
- Body-fixed coordinates.
- Inertial coordinates.
- Region-relative coordinates.
- Local gameplay coordinates.

The coordinate reference frame must always be explicit.

### 11.3 Entity state snapshot

```
EntitySnapshot
    entity_id: uint128
    server_tick: uint64
    state_revision: uint32
    position: WorldPosition
    orientation: compressed_quaternion
    linear_velocity: compressed_vector
    angular_velocity: compressed_vector
    component_mask: uint64
    flags: uint32
```

Optional components should be transmitted separately or encoded using component masks.

### 11.4 Input command

```
InputCommand
    session_id: uint128
    controlled_entity_id: uint128
    input_sequence: uint32
    target_server_tick: uint64
    input_schema_version: uint16
    input_payload: bytes
```

Inputs should represent player intentions rather than direct authoritative outcomes.

For example:

- Steering angle.
- Throttle.
- Brake.
- Look direction.
- Movement direction.
- Interaction request.

Not:

- Set vehicle position.
- Set vehicle velocity.
- Declare collision outcome.

### 11.5 Snapshot envelope

```
SnapshotEnvelope
    protocol_version: uint16
    world_epoch: uint64
    server_tick: uint64
    baseline_id: uint32
    snapshot_sequence: uint32
    entity_count: uint16
    payload_length: uint32
    payload: bytes
```

The exact field widths remain open for protocol optimization.

### 11.6 Event contract

Events need a different model from continuous snapshots.

```
WorldEvent
    event_id: uint128
    world_epoch: uint64
    simulation_tick: uint64
    entity_id: uint128
    event_type: uint32
    event_version: uint16
    payload: bytes
```

Examples:

- Building constructed.
- Vehicle destroyed.
- Player entered region.
- Ownership changed.
- Door opened.
- Resource transaction completed.

Important persistent events need reliable delivery or a recoverable event history.

Cosmetic events can be transient and unreliable.

### 11.7 Schema evolution

Every network contract needs:

- Protocol version.
- Schema version.
- Explicit compatibility rules.
- Unknown-field handling.
- Feature negotiation.
- Controlled deprecation.

Avoid relying on C# or Rust native memory layouts for serialization.

Use explicit encoding and decoding.

Candidate serialization technologies include:

| Format        | Advantages                                    | Limitations                            |
| ------------- | --------------------------------------------- | -------------------------------------- |
| Protobuf      | Mature cross-language ecosystem               | Verbosity and encoding overhead        |
| FlatBuffers   | Efficient access without full deserialization | More complex schema/tooling            |
| MessagePack   | Compact and relatively simple                 | Requires disciplined schema/versioning |
| Custom binary | Maximum control                               | Maintenance and compatibility burden   |

Recommendation: Evaluate Protobuf for control-plane messages and a purpose-built binary format for high-frequency state replication.

Do not commit to a single format before profiling actual message distributions.

## 12. Performance considerations and budgets

This is where architectural decisions become measurable.

The Unity client must scale across different hardware classes.

I recommend three initial target profiles.

Profile A — High-end desktop

Primary visual target

- 60–120 FPS.
- High-detail terrain.
- Volumetric atmosphere.
- High-quality shadows.
- Detailed vehicle and character rendering.
- Dense local object populations.

  Profile B — Mid-range desktop
- 60 FPS target.
- Reduced terrain LOD.
- Reduced volumetric resolution.
- Limited shadow distance.
- Aggressive asset streaming.

  Profile C — Low-end desktop
- 30–60 FPS target.
- Reduced geometry complexity.
- Simplified clouds and atmosphere.
- Reduced draw calls.
- Lower texture resolution.
- More aggressive entity presentation culling.

These are proposed product targets, not measured performance guarantees.

### 12.1 CPU budget

At 60 FPS:

\\[ T\_{frame}=16.67\text{ ms} \\]

At 30 FPS:

\\[ T\_{frame}=33.33\text{ ms} \\]

The budget must account for:

- Main thread.
- Render thread.
- Job workers.
- Network processing.
- Entity presentation.
- Animation.
- Terrain streaming.
- Garbage collection.
- Physics proxies.
- UI.

The main thread should not perform large synchronous deserialization, terrain generation or mass entity instantiation.

### 12.2 GPU budget

Major GPU costs:

- Terrain rendering.
- Planetary atmosphere.
- Volumetric clouds.
- Water.
- Shadows.
- Reflections.
- Post-processing.
- Vegetation.
- Draw calls.
- Overdraw.

Planetary rendering needs dynamic quality scaling.

The renderer should be able to reduce visual quality without changing simulation state.

### 12.3 Memory budget

Client memory must be divided into independent pools:

| Pool       | Contents                           |
| ---------- | ---------------------------------- |
| Core       | Runtime and application            |
| World      | Active world data                  |
| Terrain    | Terrain patches and textures       |
| Entities   | Presentation objects               |
| Networking | Snapshot buffers and serialization |
| Assets     | Loaded asset cache                 |
| Rendering  | GPU resources                      |
| Audio      | Audio clips and streams            |
| Temporary  | Decode and upload buffers          |

Terrain and asset caches need explicit limits and eviction policies.

Do not allow asset streaming to consume all available memory simply because the player moves quickly.

### 12.4 Network bandwidth

A simple planning equation:

\\[ B=N\cdot F\cdot S\cdot8 \\]

Where:

- \\(N\\): replicated entities.
- \\(F\\): updates per second.
- \\(S\\): bytes per entity update.

Example, purely illustrative:

500 entities × 20 updates/s × 32 bytes:

\\[ B=2.56\text{ Mbit/s} \\]

This excludes packet headers, transport overhead, acknowledgements, retransmissions and other protocol data.

The important lesson is that bandwidth grows linearly with entity count, update frequency and payload size.

Therefore AOI and relevance-based scheduling are fundamental architectural requirements, not optional optimizations.

### 12.5 Network packet size

QUIC datagrams cannot be fragmented at the QUIC DATAGRAM frame level. Their usable size is constrained by negotiated transport parameters and path MTU.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://quicwg.org\&sz=32)

QUIC



Therefore:

- Keep real-time packets small.
- Avoid IP fragmentation.
- Split snapshots across independent datagrams where necessary.
- Use explicit sequence and baseline recovery.
- Prioritize important entity updates.
- Use reliable streams for larger structured transfers.

## 13. Integration dependencies

Worker 07 cannot be implemented independently of several other architecture areas.

| Worker/subsystem           | Dependency                                              |
| -------------------------- | ------------------------------------------------------- |
| Universe simulation        | Astronomical reference frames and object identities     |
| Planetary geometry         | Ellipsoid, terrain and planetary coordinate definitions |
| Terrain generation         | Tile identity, elevation, material and LOD data         |
| Physics runtime            | Authoritative entity state and movement models          |
| Character simulation       | Character controller state and input contract           |
| Vehicle simulation         | Vehicle state and control contract                      |
| Aircraft simulation        | Flight dynamics and atmospheric reference               |
| Space simulation           | Orbital coordinates and reference frames                |
| World persistence          | Entity identity, revisions and world epochs             |
| Multiplayer infrastructure | Session, ownership and AOI management                   |
| Node.js control plane      | Authentication and session lifecycle                    |

### 13.1 Critical shared interfaces

The following must be agreed upon before client implementation:

1. Universal entity identity.
2. Planet and body identity.
3. Coordinate reference frame identifiers.
4. Simulation tick and time model.
5. Entity state schema.
6. Input command schema.
7. World epoch and revision model.
8. Terrain tile identity.
9. Asset content identifiers.
10. AOI subscription contract.

Without these, the client and Rust runtime will inevitably develop incompatible assumptions.

## 14. Testing and validation plan

The client should be validated through independent test categories.

### 14.1 Coordinate and rendering tests

- Move across the entire planetary surface.
- Cross the equator and both poles.
- Travel around the planet.
- Move from ground to orbital altitude.
- Transition between planetary and astronomical coordinate frames.
- Perform floating-origin shifts during vehicle movement.
- Verify stable terrain seams.
- Verify stable shadow and atmosphere rendering.

Acceptance criteria:

- No coordinate discontinuities.
- No accumulated position drift from origin shifts.
- No visible terrain cracks beyond defined LOD tolerances.
- Stable camera movement at the required operating distances.

### 14.2 Network tests

Simulate:

| Condition  | Test                             |
| ---------- | -------------------------------- |
| LAN        | Low-latency baseline             |
| 50 ms RTT  | Normal broadband scenario        |
| 100 ms RTT | Moderate latency                 |
| 200 ms RTT | High-latency scenario            |
| 300 ms RTT | Severe latency                   |
| 1% loss    | Occasional packet loss           |
| 5% loss    | Degraded connection              |
| 10% loss   | Stress condition                 |
| Jitter     | Variable packet arrival          |
| Reordering | Out-of-order delivery            |
| Burst loss | Temporary connection degradation |

Test combinations, not just isolated variables.

### 14.3 Prediction tests

- Character movement.
- Vehicle acceleration.
- Braking.
- Steering.
- Collision correction.
- Aircraft control.
- Spacecraft attitude changes.
- Server rejection of invalid commands.
- Client reconnect with outstanding inputs.

Measure:

- Prediction error.
- Reconciliation frequency.
- Correction magnitude.
- Visual discontinuity.
- Input-to-response latency.

### 14.4 Scale tests

Simulate progressively increasing entity counts.

| Test population | Objective                      |
| --------------- | ------------------------------ |
| 100             | Baseline                       |
| 1,000           | Typical populated region       |
| 10,000          | Large local environment        |
| 100,000         | Large strategic representation |
| 1,000,000       | Relevance and streaming stress |

These are test populations, not expected simultaneously rendered entities.

The client should only instantiate and render a fraction of the world's total entities.

### 14.5 Rendering tests

Measure separately:

- Planet rendering.
- Terrain LOD.
- Atmospheric rendering.
- Cloud rendering.
- Ocean rendering.
- Dense vegetation.
- Cities.
- Large object populations.
- Space environment.

Use actual standalone builds, not only Unity Editor profiling.

Unity's GPU Usage Profiler can be used to measure GPU workloads in Play Mode and builds, subject to its documented platform limitations.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://docs.unity3d.com\&sz=32)

Unity Manual



### 14.6 Cross-language validation

The Rust and C# implementations must agree on:

- Coordinate conversion.
- Quaternion conventions.
- Quantization.
- Serialization.
- Tick interpretation.
- Entity revisions.
- Snapshot reconstruction.

I recommend shared golden test vectors.

For example, a single test vector should be encoded by Rust and decoded by C#, and vice versa, with exact expected values and explicitly defined tolerances.

## 15. Risk register

| Risk                                | Severity | Mitigation                                      |
| ----------------------------------- | -------- | ----------------------------------------------- |
| Floating-point precision loss       | Critical | Double global coordinates and floating origin   |
| Client/server physics divergence    | Critical | Server authority and bounded prediction         |
| Excessive replication bandwidth     | Critical | AOI, compression and multirate updates          |
| Planetary LOD seams                 | High     | Shared patch boundaries and validation          |
| Terrain streaming stalls            | High     | Async pipelines and bounded upload budgets      |
| Unity/Rust protocol incompatibility | Critical | Versioned language-independent contracts        |
| QUIC datagram MTU limitations       | High     | Packet budgets and explicit segmentation        |
| HDRP performance                    | High     | Quality tiers and renderer abstraction          |
| Memory exhaustion                   | High     | Explicit cache budgets and eviction             |
| Time-scale synchronization bugs     | Critical | Separate wall-clock and simulation time         |
| Perspective switching state loss    | High     | Separate presentation from world identity       |
| Prediction correction artifacts     | High     | State correction and separate visual smoothing  |
| Server migration                    | High     | Stable identity and authority epochs            |
| Client manipulation                 | Critical | Server validation of all authoritative commands |

### 15.1 Biggest architectural risk

The greatest risk is not rendering performance.

It is allowing the Unity client to become coupled to the internal implementation of the Rust simulation.

That would make every future change to physics, entity state, world time or simulation architecture require client rewrites.

The client should consume stable contracts, not internal Rust structures.

## 16. Open architectural decisions

These require agreement across workers before implementation.

| ID     | Decision            | Recommendation                           |
| ------ | ------------------- | ---------------------------------------- |
| U07-01 | HDRP or URP         | HDRP baseline, URP possible later        |
| U07-02 | Coordinate model    | ECEF + inertial frames + local origin    |
| U07-03 | Planet terrain      | Cube-sphere quadtree                     |
| U07-04 | Gameplay terrain    | Independent high-detail regional tiles   |
| U07-05 | Transport           | Evaluate QUIC                            |
| U07-06 | Replication         | Custom cross-language binary protocol    |
| U07-07 | Client architecture | Hybrid ECS/GameObject                    |
| U07-08 | Prediction          | Selective, local-player focused          |
| U07-09 | Remote entities     | Snapshot interpolation                   |
| U07-10 | Entity relevance    | Hierarchical AOI                         |
| U07-11 | Asset distribution  | Addressables-compatible content delivery |
| U07-12 | Simulation time     | Explicit server tick and world time      |
| U07-13 | Server migration    | Stable identity and authority epochs     |
| U07-14 | Multiplayer scale   | AOI-based rather than global replication |

One particularly important decision is whether the client will connect directly to Rust simulation gateways or communicate through a separate networking edge service.

I recommend retaining both possibilities in the architecture, rather than hardcoding the Node.js control plane as the real-time packet relay.

## 17. Recommended implementation sequence

No implementation is authorized in this research phase. The following is the recommended future dependency order.

1. Foundation — World identity and coordinates

   Establish entity IDs, planetary reference frames, ECEF, inertial coordinates and floating-origin transformations.

   Highest priority
2. Networking foundation

   Establish Rust/C# protocol contracts, transport evaluation, session lifecycle, tick synchronization and entity replication.
3. Minimal Unity world client

   Render a planetary surface, receive authoritative entity positions and display remote entities.
4. Prediction and reconciliation

   Implement the conceptual architecture for character and vehicle control, with server authority.
5. Planetary rendering

   Cube-sphere LOD, terrain streaming, atmospheric rendering, oceans and celestial objects.
6. Gameplay perspectives

   First-person, third-person, vehicle, aircraft, spacecraft, strategic overview and editor.
7. Scale and multiplayer

   AOI, bandwidth management, entity prioritization, server migration and large-population testing.

## 18. Primary technical references

These are the most useful sources for the subsequent implementation architecture.

| Subject                               | Reference                                                                                                                                                                                                                                |
| ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| HDRP camera-relative rendering        | [Unity Graphics — Camera-relative rendering](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/Camera-Relative-Rendering.md?plain=1\&utm_source=chatgpt.com) |
| HDRP planetary atmosphere             | [Physically Based Sky](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/physically-based-sky-volume-override-reference.md?utm_source=chatgpt.com)           |
| HDRP clouds and environmental effects | [HDRP features](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/HDRP-Features.md?plain=1\&utm_source=chatgpt.com)                                          |
| HDRP ocean limitations                | [Water capabilities](https://github.com/Unity-Technologies/Graphics/blob/master/Packages/com.unity.render-pipelines.high-definition/Documentation~/water-capabilities-of-the-water-system.md?utm_source=chatgpt.com)                     |
| Unity prediction                      | [Netcode prediction](https://docs.unity.cn/Packages/com.unity.netcode%401.1/manual/prediction.html?utm_source=chatgpt.com)                                                                                                               |
| Unity interpolation                   | [Netcode interpolation](https://docs.unity.cn/Packages/com.unity.netcode%401.5/manual/interpolation.html?utm_source=chatgpt.com)                                                                                                         |
| Unity time synchronization            | [Network time synchronization](https://docs.unity.cn/Packages/com.unity.netcode%401.1/manual/time-synchronization.html?utm_source=chatgpt.com)                                                                                           |
| Unity entity relevancy                | [Ghost relevancy](https://docs.unity.cn/Packages/com.unity.netcode%401.5/manual/optimizations.html?utm_source=chatgpt.com)                                                                                                               |
| Unity Transport                       | [Unity Transport](https://github.com/Unity-Technologies/com.unity.multiplayer.docs/blob/main/transport/about.md?plain=1\&utm_source=chatgpt.com)                                                                                         |
| Networked physics                     | [Gaffer — Snapshot Interpolation](https://www.gafferongames.com/post/snapshot_interpolation/?utm_source=chatgpt.com)                                                                                                                     |
| State synchronization                 | [Gaffer — State Synchronization](https://gafferongames.com/post/state_synchronization/?utm_source=chatgpt.com)                                                                                                                           |
| Snapshot compression                  | [Gaffer — Snapshot Compression](https://new.gafferongames.com/post/snapshot_compression/?utm_source=chatgpt.com)                                                                                                                         |
| QUIC                                  | [RFC 9000](https://www.rfc-editor.org/rfc/rfc9000.html?utm_source=chatgpt.com)                                                                                                                                                           |
| QUIC datagrams                        | [RFC 9221](https://www.rfc-editor.org/rfc/rfc9221.html?utm_source=chatgpt.com)                                                                                                                                                           |
| Unity physics timing                  | [Unity physics performance](https://docs.unity3d.com/jp/current/Manual/physics-performance-issues.html?utm_source=chatgpt.com)                                                                                                           |

# 19. Final architectural conclusions

Recommended architecture

I would carry the following decisions into the master Planetary Engine architecture.

1\. Unity is a presentation and interaction client, not a second authoritative simulation server.

2\. The global world is represented independently of Unity coordinates. Double-precision global coordinates, planetary reference frames and local floating origins are mandatory.

3\. Planetary rendering and gameplay terrain are separate representations of shared world data. Cube-sphere quadtree LOD provides the global framework; high-detail regional tiles provide gameplay detail.

4\. Networking is custom and cross-language. Unity Netcode is a research reference, not the protocol foundation. QUIC is the leading transport candidate.

5\. Prediction is selective. Predict the player's immediate interactions; interpolate the majority of remote entities.

6\. Network relevance is hierarchical. The client receives information appropriate to its gameplay perspective, not the entire universe.

7\. Simulation time and network time are independent. This is mandatory for accelerated simulations, replay and what-if scenarios.

8\. Every gameplay perspective operates on the same entity identity and world state.

9\. Rendering quality is independent of simulation fidelity. A low-end client and a high-end client must observe the same authoritative world despite different visual detail.

10\. All cross-system communication uses stable, versioned contracts. No dependency on Unity or Rust internal memory representations.

## 20. My most important additional recommendation

I would introduce a concept called World Presentation State.

It would sit between authoritative world replication and Unity rendering.

For every entity, the client maintains three distinct representations:

A. Authoritative state

Last confirmed Rust state, server tick, revision and authority metadata.

B. Presentation state

Interpolated or locally predicted state, visual correction and current presentation timestamp.

C. Rendering state

Unity Transform, mesh, animation, camera-relative coordinates, effects and visual LOD.

Why is this important?

Imagine a spacecraft moving between two reference frames, with its local Unity origin changing during the transition.

- Authoritative state remains unchanged.
- Presentation state calculates the appropriate interpolated position.
- Rendering state converts that position into the current Unity coordinate system.

The same architecture works for a character, a vehicle, a planet or a galaxy.

It also makes it possible to change the rendering implementation without touching the networking or simulation architecture.

This separation is, in my opinion, one of the strongest foundations for a genuinely universal simulation client.

WORKER 07 — RESEARCH DELIVERABLE

Architecture recommendations complete

Implementation status: Not started. No existing project code assumed or modified.

Handoff to orchestrator: Coordinate contracts, simulation time, entity identity, reference frames, networking authority and terrain representation must be reconciled with the other Planetary Engine research workers before architecture freeze.