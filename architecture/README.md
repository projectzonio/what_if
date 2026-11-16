# Architektura

Sem patří konsolidovaná architektura (Master Architecture Baseline). Vytvoří ji **druhý audit Workeru 10** z doplňků kola 1 a rozhodnutí uživatele. Postup je v `audit/W10_ROUND2_PROMPT.md`.

Plánované soubory podle struktury repa:

| Soubor | Obsah | Hlavní podklady |
| --- | --- | --- |
| `MASTER_ARCHITECTURE.md` | vrstvy, vlastnictví, fáze ticku, glosář | W10 §3, §5; `integration/` |
| `COORDINATE_SYSTEM.md` | rámce, 6D transformace, cell + local, cube-sphere | W01 suppl. §3.4–3.6 |
| `TIME_SYSTEM.md` | `WorldInstant`, `DomainTick`, časové škály, `TimeScale`, watermark | W01 §3.1–3.3, W07 §3.1–3.2, W09 §3.2 |
| `ENTITY_MODEL.md` | `EntityPhysicalState`, domény, fidelity, složené entity | W10 §6, W03, W09 §3.1 |
| `PHYSICS_MODEL.md` | islands, gravitace, environment coupling | W02, W04, W08 |
| `NETWORK_MODEL.md` | ownership, boundary protokol, replikace, klient | W05, W07, W09 |
| `PERSISTENCE_MODEL.md` | journal, snapshoty, recovery, mutace | W05, W06 |

Dokud tyto soubory nevzniknou, platí `audit/WORKER_10_REVIEW.md` + `audit/CONFLICTS.md` + `integration/INTERFACE_CONTRACTS.md`.
