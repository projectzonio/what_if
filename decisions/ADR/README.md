# Architecture Decision Records

Rozhodnutí, která W10 v auditu v0.1 (§11) nechal otevřená. Všechna jsou ve stavu **Proposed**: směr je doporučený, ale nepřijatý. Přijetí (**Accepted**) vyžaduje doplňující výzkum a druhý audit W10, u implementačních voleb i benchmark.

| ADR | Rozhodnutí | Doporučený směr W10 | Konflikt | Podklad | Stav |
| --- | --- | --- | --- | --- | --- |
| [ADR-001](ADR-001-kanonicka-reprezentace-casu.md) | Kanonická reprezentace času | Integer/fixed-point WorldInstant | CONFLICT-001, CONFLICT-005 | W01 | Proposed |
| [ADR-002](ADR-002-astronomicke-casove-konverze.md) | Astronomické časové konverze | Centralizovaná služba | CONFLICT-001 | W01 | Proposed |
| [ADR-003](ADR-003-gravity-composition.md) | Gravity composition | Sjednocený field provider | CONFLICT-002 | W04 | Proposed |
| [ADR-004](ADR-004-physics-backend.md) | Physics backend | Benchmark Rapier / Avian / vlastní řešení | – | W02 | Proposed |
| [ADR-005](ADR-005-ecs.md) | ECS | Benchmark Bevy / Flecs / SoA | – | W02, W09 | Proposed |
| [ADR-006](ADR-006-region-boundary-physics.md) | Region boundary physics | Physics island ownership + boundary protocols | CONFLICT-004 | W02 | Proposed |
| [ADR-007](ADR-007-environment-grid.md) | Environment grid | Cubed-sphere / hybrid | CONFLICT-007 | W01, W08 | Proposed |
| [ADR-008](ADR-008-persistence.md) | Persistence | Hybrid journal + snapshots | – | W05, W06 | Proposed |
| [ADR-009](ADR-009-ownership-coordinator.md) | Ownership coordinator | Fenced ownership s konzistentním registry | CONFLICT-004 | W05 | Proposed |
| [ADR-010](ADR-010-distributed-time.md) | Distributed time | Globální čas + regionální committed watermark | CONFLICT-005 | W09 | Proposed |
| [ADR-011](ADR-011-networking.md) | Networking | QUIC / datagramy podle typu provozu | – | W07 | Proposed |
| [ADR-012](ADR-012-wire-format.md) | Wire format | Verzované binární kontrakty | CONFLICT-003 | W07, W09 | Proposed |
| [ADR-013](ADR-013-world-mutation.md) | World mutation | Revision-based transakční model | CONFLICT-006 | W06 | Proposed |
| [ADR-014](ADR-014-environment-fidelity.md) | Environment fidelity | Conservation-aware coupling | CONFLICT-007 | W08 | Proposed |
| [ADR-015](ADR-015-cross-platform-determinism.md) | Cross-platform determinism | Po doménách, ne globálně | CONFLICT-008 | W02, W09 | Proposed |
