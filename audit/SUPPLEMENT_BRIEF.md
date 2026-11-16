# Zadání doplňujících výzkumů W01–W09 (kolo 1)

Zdroj zadání: `audit/WORKER_10_REVIEW.md` §12 („Co potřebuji ještě od Workerů 01–09“). Výstupy jdou do `research/worker-0N/w0N-supplement-01.md` a jsou podkladem pro druhý audit Workeru 10 (Master Architecture Baseline).

## Pravidla pro každého workera

- Fáze architektonického výzkumu: **žádná implementace**, kód jen jako ilustrační datové struktury.
- Původní výzkumy `research/worker-0N/w0N-original.md` jsou důvěryhodný podklad. Vznikly paralelně naslepo; rozpory mezi nimi jsou chybějící koordinace, ne chyba autora.
- **Šetři kontext:** soubory nečti celé. V `audit/WORKER_10_REVIEW.md` stačí §2, §6 a řádky tvého tématu (grep). Ve vlastním původním výzkumu i v cizích hledej grepem relevantní sekce a čti jen je. Nejvýš ~6 webových vyhledávání, jen pro ověření klíčových tvrzení proti primárním zdrojům.
- Témata ostatních workerů nepřebírej. Kde na nich závisíš, napiš „závisí na W0X“ a navrhni jen rozhraní.
- Jazyk: česky, identifikátory anglicky. Diagramy jako ```` ```mermaid ```` bloky. Rozlišuj **ověřeno** / **návrh** / **odhad**.
- Rozsah 300–700 řádků. Nic necommituj, needituj jiné soubory.

## Závazný baseline Workeru 10 (zkráceně)

| Téma | Rozhodnutí W10 |
| --- | --- |
| Čas (CONFLICT-001/005) | `WorldInstant` = kanonický celočíselný okamžik; `DomainTick` = lokální krok subsystému/regionu; `WallClock` = provozní. TAI/TT/TDB/UTC/UT1 jen jako konverze `WorldInstant`. Žádný globální synchronní tick; regiony mají committed watermark. Akcelerace času nesmí naslepo násobit timestepy. |
| Gravitace (CONFLICT-002) | Celestial / Planetary / Environmental Acceleration Provider dodávají pole; **Physics Runtime je jediný, kdo aplikuje síly a integruje**. Pozor na dvojí odstředivou složku v rotující soustavě. |
| Stav entity (CONFLICT-003) | `EntityPhysicalState` dle W10 §6: world/entity id, state_version, timestamp (world_instant, domain_tick), spatial (frame, anchor, cell, local_position, orientace, rychlosti), physical (mass, inertia), simulation (domain, fidelity, model_version), authority (owner_region, owner_node, owner_epoch). Transformuje se celý 6D stav. |
| Regiony (CONFLICT-004) | Spatial region ≠ Ownership region ≠ Physics island ≠ Execution partition. Island má jednoho vlastníka, soused dodává jen boundary data. |
| Mutace (CONFLICT-006) | Committed → PhysicsIntegrated → DerivedSystemsSynchronized → Replicated; revision tracking. |
| Environment (CONFLICT-007) | `EnvironmentSample` nese provider, model_version, rozlišení, validity, interpolaci, nejistotu, world_revision, frame, SI. Conservation-aware coupling tam, kde se veličina přenáší. |
| Determinismus (CONFLICT-008) | Po doménách, ne bitově globálně; zaznamenané pořadí cross-region událostí. |
| Škálování | Stage 1 jeden Rust proces s logickými regiony → 2 paralelní → 3 multi-process → 4 distribuovaný → 5 geo. |
| Stack | Rust autoritativní runtime, Node.js control plane, Unity jen klient, Python vědecká/validační vrstva. |

## Úkoly

| Worker | Téma | Hlavní vazby v W10 | Číslování nových testů |
| --- | --- | --- | --- |
| 01 | Finální prostorový a časový kontrakt: reprezentace `WorldInstant` (jednotka, šířka, epocha, rozsah), konverze časových škál a přestupné sekundy, graf referenčních rámců a 6D transformace (ω×r), hierarchická pozice cell/anchor/local (sjednotit w01 cube-sphere, w05 H3/S2, w07 ECEF double/float origin), typová kontrola SI | CONFLICT-001/003, §4, ADR-001/002, VAL-001/002 | VAL-1xx |
| 02 | Physics islands přes regionální hranice: vznik/merge/split islandu, přidělení vlastníka, ghost/proxy tělesa, hysteréze, obří islandy (vlak, most), CCD přes hranici, platnost už ve Stage 1 | CONFLICT-004, R04, ADR-006, VAL-007 | VAL-2xx |
| 03 | Přechody mezi fyzikálními doménami: katalog domén a integrátorů, handoff protokol s hysterézí, kontinuita hybnosti/energie, změna rámce a fidelity (on-rails ↔ plná fyzika), složené entity (postava ve vozidle, staging, docking), atmosféra→vakuum, voda↔vzduch, time warp; rozpis VAL-REFERENCE-001 krok po kroku | VAL-003/004, R03/R07 | VAL-3xx |
| 04 | Sjednocení gravitačních providerů: kdo dodává který příspěvek (centrální těleso, J2..Jn/EGM, třetí tělesa, slapy, anomálie), gravitational vs. gravity (WGS84), zdánlivé síly v rotujícím rámci, přepínání modelů bez skoků, `GravitySample` (hodnota, gradient, platnost, nejistota), výkon dotazů | CONFLICT-002, R02, ADR-003, VAL-005 | VAL-4xx |
| 05 | Fencing, ownership a fault recovery: sloučený `RegionOwnership` (w05 fencing + w09 + W10 §6), ownership coordinator (registry, lease, fencing tokeny), protokol migrace, recovery ze snapshotu + journalu, RPO/RTO, co platí už ve Stage 1 | R05, ADR-008/009, VAL-008/010 | VAL-5xx |
| 06 | World Mutation lifecycle a revision consistency: celý stavový automat (vč. Rejected/Compensated), revision vector, mutace přes hranici regionů, cyklus Physics → event → Mutation, souběžné mutace, journal a idempotence | CONFLICT-006, R08, ADR-013, VAL-009 | VAL-6xx |
| 07 | Unity prediction při akcelerovaném čase: čas klienta vs. `WorldInstant`, time scale jako stav serveru, prediction/reconciliation podle typu entity a rychlosti času, multiplayer a time warp (slučitelnost s jedním `WorldInstant`), floating origin, `ReplicationSnapshot` | CONFLICT-001, VAL-012, ADR-011/012 | VAL-7xx |
| 08 | Conservation-aware LOD coupling: klasifikace modelů (konzervativní/diagnostický/vizuální), downscaling/upscaling, conservative remapping, two-way nesting, časový coupling a akumulace toků, přechody LOD bez skoků, mutace terénu → hydrologie, výkon dotazů | CONFLICT-007, R07/R11, ADR-007/014, VAL-004/011 | VAL-8xx |
| 09 | Distributed time a boundary synchronization: watermarky a GVT, konzervativní vs. optimistická synchronizace, boundary protokol (skew, inter/extrapolace, zpožděné zprávy), rychlé objekty, pauza/akcelerace, pořadí cross-region událostí; rozšíření vlastního entity kontraktu dle CONFLICT-003 | CONFLICT-003/005/008, R06, ADR-010, VAL-006/013 | VAL-9xx |

## Struktura výstupu

1. Shrnutí rozhodnutí (tabulka)
2. Řešené konflikty W10 (CONFLICT-ID → rozhodnutí → odůvodnění)
3. Kontrakt(y): informační model + ilustrační struktura
4. Matematika / algoritmy / stavové automaty
5. Rozhraní na ostatní workery („závisí na W0X“)
6. Validační testy (navázat na VAL-001..014, nové podle tabulky)
7. Nové rozpory mezi původními w01–w09, které W10 nemá
8. Otevřené otázky pro uživatele / ADR
9. Zdroje (URL a co z nich plyne)
