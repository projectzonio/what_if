# Rejstřík rozporů

Rozpory mezi původními výzkumy W01–W09. Vznikly paralelní prací bez znalosti ostatních workerů („blind work“). Nejde o chyby modelu, jde o chybějící koordinaci.

Stavy: **Rozhodnuto W10** (směr daný auditem v0.1) → **Rozpracováno** (doplňující výzkum dodal kontrakt) → **Uzavřeno** (potvrzeno druhým auditem W10 a zapsáno do `architecture/` a `decisions/ADR/`).

## Rozpory z auditu W10 v0.1

| ID | Rozpor | Zúčastnění | Priorita | Směr W10 | Doplňující výzkum | Stav |
| --- | --- | --- | --- | --- | --- | --- |
| CONFLICT-001 | Globální čas vs. lokální simulační tick | W01, W02, W03, W04, W08, W09 | Critical | `WorldInstant` + `DomainTick` + `WallClock`, astronomické škály jen jako konverze | W01, W07, W09 | Rozhodnuto W10 |
| CONFLICT-002 | Kdo vlastní gravitaci | W01, W02, W03, W04 | Critical | Tři provideři polí, Physics Runtime jediný integrátor | W04 | Rozhodnuto W10 |
| CONFLICT-003 | Entity state není jednotně definován | W01, W02, W04, W09 | Critical | Rozšířený `EntityPhysicalState` (W10 §6), 6D transformace | W01, W03, W09 | Rozhodnuto W10 |
| CONFLICT-004 | Region ownership vs. physics islands | W02, W09 | Critical | Čtyři různé struktury, island má jednoho vlastníka | W02 | Rozhodnuto W10 |
| CONFLICT-005 | Jeden globální čas vs. regionální časové osy | W04, W09 | Critical | Globální `WorldInstant`, žádný globální tick, committed watermark | W09 | Rozhodnuto W10 |
| CONFLICT-006 | World Mutation vs. fyzikální autorita | W02, W05, W06 | High | Lifecycle Committed → PhysicsIntegrated → DerivedSystemsSynchronized → Replicated | W06 | Rozhodnuto W10 |
| CONFLICT-007 | Environmentální LOD vs. fyzikální konzistence | W08 | High | Hlavička `EnvironmentSample`, conservation-aware coupling | W08 | Rozhodnuto W10 |
| CONFLICT-008 | Determinismus vs. distribuovaná simulace | W02, W03, W05, W09 | High | Determinismus po doménách, záznam pořadí cross-region událostí | W02, W09 | Rozhodnuto W10 |

## Nové rozpory z doplňujících výzkumů

Doplní se z oddílu 7 souborů `research/worker-0N/w0N-supplement-01.md`.

| ID | Rozpor | Zúčastnění | Priorita | Navržené řešení | Stav |
| --- | --- | --- | --- | --- | --- |
| – | – | – | – | – | – |
