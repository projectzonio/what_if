# Kontrakty rozhraní: index

Kde je který kontrakt definovaný. Tabulka z W10 §6 doplněná o kontrakty z doplňků kola 1. Všechny jsou **návrh**. Finální znění uzavře druhý audit W10 (Master Architecture Baseline) a zapíše se do `architecture/`.

## Kontrakty z W10 §6

| Kontrakt | Producent | Konzument | Definice | Otevřené body |
| --- | --- | --- | --- | --- |
| `WorldInstant`, `WorldDuration` | Time Authority (W01) | všichni | W01 §3.1 | šířka na drátě (CONFLICTS C-02) |
| `DomainTick`, `DomainClock` | Domain Scheduler | domény | W01 §3.2 | `committed_instant` + mapa ticků (NC-02) |
| `TimeScaleTable` (TAI/TT/TDB/UTC/UT1) | Time Authority | Celestial, klient | W01 §3.3 | V-01 přestupné sekundy |
| `ReferenceFrameTransform` (`FrameNode`, `FrameTransform`) | Spatial Runtime (W01) | fyzika, gravitace, environment, klient | W01 §3.5–3.6 | vlastník orientace těles (OQ-09) |
| `EntityPhysicalState` | Physics Runtime (W02) | Entity Mechanics, replikace, persistence | W10 §6 + W01 §3.4 (spatial), W02 §3.2 (island), W03 §3.3 (identita při přechodu), W09 §3.1 (wire) | epocha vlastnictví (C-01) |
| `GravitySample` | Gravity Service (W04) | Physics Runtime | W04 §3.2–3.4 | zúžení provideru (C-07) |
| `EnvironmentSample` | Environment Runtime (W08) | Entity Mechanics, Physics | W10 §6 + W08 §3.3 | revize (C-04), gravitace jen pass-through (NC-14) |
| `SimulationStep` | Domain Scheduler | domény | W09 §3.2 | |
| `RegionOwnership` | Ownership Coordinator (W05) | Distributed Runtime | W05 §3.2 | technologie coordinatoru (OQ-22) |
| `WorldMutation` | Mutation System (W06) | World Runtime | W06 §3.1–3.2 | ekonomika (OQ-07) |
| `StateSnapshot` | Simulation Runtime | Persistence | W05 §3.1 | |
| `ReplicationSnapshot` | Distributed Runtime | Unity | W07 §3.3 | `server_tick` nahrazen `world_instant` + watermark |

## Nové kontrakty z doplňků

| Kontrakt | Účel | Definice |
| --- | --- | --- |
| `SpatialState` (frame, anchor, cell i64, local f64) | hierarchická pozice | W01 §3.4 |
| `Quantity<Unit>` | typová kontrola SI | W01 §3.7 |
| `PhysicsIsland`, `IslandOwnership`, `EdgeClass` | jednotka autority fyziky | W02 §3.1 |
| `GhostBody` / `ForeignBodyView` | read-only data souseda přes hranici | W02 §3.1 |
| `ImpactEvent` | přechodný náraz mezi islandy různých vlastníků | W02 §3.1 |
| `MigrationRecord` | převod islandu (manifoldy, warm-start) | W02 §3.1 |
| `DomainHandoff`, `HandoffRecord` | přechod mezi fyzikálními doménami | W03 §3.2 |
| `FidelityLevel (SimTier, ModelVariant)` | dvouosá fidelity | W03 NC-302 |
| `GravityComposer`, registr `covers` | skládání gravitace bez překryvů | W04 §3.1–3.4 |
| `FencingToken`, `Lease` | bezpečnost a dostupnost vlastnictví | W05 §3.1 |
| `EntityRoute`, `EntityAuthority` | směrování a token entity | W05 §3.3 |
| `RegionHandoff`, `EntityTransfer` | změna procesu regionu / změna regionu entity | W05 §3.4 |
| `JournalSegment` | journal s podmíněným zápisem | W05 §3.1 |
| `WorldMutationCommand`, `MutationRecord`, `MutationResult` | lifecycle mutace | W06 §3.1–3.2 |
| `RevisionVector` | revize per region a subsystém | W06 §3.3 |
| `CommandEnvelope` | jeden kanál příkazů | W06 N-05 |
| `TimeControlState`, `TimeControlEvent` | globální rychlost času, pauza, bariéry | W07 §3.2 |
| `ClientWorldClock`, `PresentationClock` | časy klienta | W07 §3.1 |
| `RemapOperator`, `FluxAccumulator`, `BudgetLedger` | konzervativní coupling prostředí | W08 §3.4–3.5 |
| `LodTransitionRecord` | přechod LOD prostředí | W08 §3.6 |
| `RegionWatermark`, `BoundaryExchange`, `BoundaryEntityView` | časová synchronizace regionů | W09 §3.1–3.2 |

## Glosář (návrh, sjednotí W10)

| Pojem | Význam |
| --- | --- |
| domain | sada modelů a integrátor (W03 D0–D9) |
| fidelity | úroveň simulace entity: `SimTier` (zda) × `ModelVariant` (jak) |
| LOD | úroveň detailu prostředí a renderingu |
| `DomainHandoff` | entita mění doménu, region zůstává |
| `EntityTransfer` | entita nebo island mění ownership region (W02 obsah, W05 fencing, W09 časování) |
| `RegionHandoff` | region mění proces nebo uzel |
| watermark | nejvyšší potvrzený `WorldInstant` regionu |
| GVT | minimum watermarků a zpráv na cestě |
