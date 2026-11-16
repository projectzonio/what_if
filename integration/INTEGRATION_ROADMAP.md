# Integrační roadmapa

Stav: **návrh v0.1** (2026-11-16). Vychází z W10 §8 (stages), §9 (validace), §12 (pořadí) a z doplňků kola 1.

## Fáze výzkumu (teď)

1. ~~Výzkumy W01–W09~~
2. ~~Audit W10 v0.1~~
3. ~~Doplňky kola 1~~ (`research/worker-0N/w0N-supplement-01.md`)
4. **Rozhodnutí uživatele:** `audit/OPEN_QUESTIONS.md`, priorita 1 (OQ-01 až OQ-09)
5. **Ověření tvrzení:** `audit/CONFLICTS.md` oddíl D (V-01 až V-10), vhodné pro GPT
6. **Druhý audit W10:** uzavřít C-01 až C-09, Master Architecture Baseline do `architecture/`, ADR na Accepted
7. Implementační gates: které VAL testy musí projít před každou fází

## Fáze implementace (až po bodu 6)

W10: nejdřív jeden Rust proces, pak paralelizace, až pak distribuce. Doplňky se shodují, že **kontrakty pro distribuci musí existovat už ve Stage 1**, i když běží v jednom procesu. Pak se nebude nic přepisovat.

| Stage | Obsah (W10 §8) | Povinné už v této fázi (doplňky) |
| --- | --- | --- |
| 1 Single-process | jeden Rust proces, víc logických regionů | `WorldInstant` i128 + `DomainTick` (W01). `SpatialState` cell + local (W01). `IslandId`, `IslandOwnership` s epochou, ghost API, journal migrací, testy s emulovanou latencí (W02). `DomainHandoff` s hysterezí a error budgetem, domény D0–D4 a D8 (W03). Registr `covers`, oddělené typy gravitačního a zdánlivého zrychlení, třídy dotazů T0–T2 (W04). Epocha, zámek datového adresáře, podmíněný zápis journalu, automat `EntityTransfer` (W05). Jeden journal a koordinátor v procesu, stejné kontrakty jako distribuovaně (W06). Globální `TimeScale` s bariérami (W07, W09). |
| 2 Parallel | paralelní islandy, ECS/SoA benchmarky, domain scheduling | benchmark ADR-004 (physics backend) a ADR-005 (ECS). Tvrdá konzervace vody a hmoty (OQ-18). |
| 3 Multi-process | ownership, fencing, migrace, recovery | síťový lease a externí consensus store (OQ-22), boundary protokol s lookahead (W09), on-rails domény podle OQ-03 |
| 4 Distributed universe | dynamické rozdělení zátěže | partitioned coupling obřích islandů (W02), per-island lease (W02 Q8) |
| 5 Geographic | více datacenter | lease přes datacentra (W05 Q7) |

## Referenční integrační test

VAL-REFERENCE-001 (W10 §9) je ve W03 §6 rozepsaný na 12 kroků s invarianty: doména, rámec a vlastník v každém kroku. Kroky 5–7 čekají na OQ-10.

## Číslování validačních testů

| Rozsah | Zdroj |
| --- | --- |
| VAL-001..014, VAL-REFERENCE-001 | W10 §9 |
| VAL-101..112 | W01 čas a prostor |
| VAL-201..214 | W02 physics islands |
| VAL-301..316 | W03 přechody domén |
| VAL-401..414 | W04 gravitace (klíčový VAL-404: dvojí odstředivá) |
| VAL-501..520 | W05 ownership a recovery, fault injection |
| VAL-601..613 | W06 mutace |
| VAL-701..712 | W07 klient a čas |
| VAL-801..822 | W08 konzervace a LOD |
| VAL-901..914 | W09 distribuovaný čas |
