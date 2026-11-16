# Rejstřík rozporů

Rozpory mezi výzkumy W01–W09. Vznikly paralelní prací bez znalosti ostatních workerů („blind work“). Nejde o chyby modelu, jde o chybějící koordinaci. Doplňky kola 1 běžely taky paralelně, a proto mají vlastní oddíl C (rozpory mezi doplňky).

Stavy: **Rozhodnuto W10** (směr z auditu v0.1) → **Rozpracováno** (doplněk dodal kontrakt) → **Uzavřeno** (potvrzeno druhým auditem W10 a zapsáno do `architecture/` a `decisions/ADR/`).

## A. Rozpory z auditu W10 v0.1

| ID | Rozpor | Směr W10 | Kontrakt dodal | Stav |
| --- | --- | --- | --- | --- |
| CONFLICT-001 | Globální čas vs. lokální tick | `WorldInstant` + `DomainTick` + `WallClock` | W01 §3.1–3.3, W07 §3.1, W09 §3.2 | Rozpracováno |
| CONFLICT-002 | Kdo vlastní gravitaci | Tři provideři, Physics Runtime jediný integrátor | W04 §3 (navrhuje zúžit provider, viz C-07) | Rozpracováno |
| CONFLICT-003 | Entity state není jednotný | `EntityPhysicalState` W10 §6, 6D transformace | W01 §3.4–3.6, W09 §3.1, W02 §3.2, W03 §3.3 | Rozpracováno |
| CONFLICT-004 | Region ownership vs. physics islands | Čtyři různé struktury, island má jednoho vlastníka | W02 §3, W05 §3.3 | Rozpracováno (C-01) |
| CONFLICT-005 | Globální čas vs. regionální osy | Globální `WorldInstant`, committed watermark | W09 §3.2, §4 | Rozpracováno |
| CONFLICT-006 | World Mutation vs. fyzikální autorita | Lifecycle Committed → … → Replicated | W06 §3, §4 | Rozpracováno (C-04) |
| CONFLICT-007 | Environment LOD vs. fyzikální konzistence | `EnvironmentSample` hlavička, conservation-aware coupling | W08 §3 | Rozpracováno |
| CONFLICT-008 | Determinismus vs. distribuce | Po doménách, záznam pořadí cross-region událostí | W09 §4, W05 §4, W03 NC-303, W08 NEW-806 | Rozpracováno |

## B. Nové rozpory mezi původními w01–w09 (našly je doplňky)

Seskupené podle tématu. Původní ID odkazují do oddílu 7 příslušného doplňku. Navržené řešení je vždy **návrh** doplňku a čeká na druhý audit W10.

| ID | Téma | Rozpor | Původní ID | Navržené řešení |
| --- | --- | --- | --- | --- |
| NC-01 | Čas | Nikde není společné kvantum času. W02 má racionální dt 1/60, W09 `simulation_tick` bez jednotky, W01 „integer SI“ bez kvanta. Kroky 60 a 50 Hz nejsou komenzurabilní. | W01 N-04, W09 R-2 | `WorldInstant` = i128 kvant q = 1/1,44·10⁹ s, každá doména má dt jako celý násobek q |
| NC-02 | Čas | Jediný `server_tick` / `simulation_tick` (W05, W06, W07, W09) vs. `DomainTick` per doména z W10 | W05 N-05, W06 N-02, W07 1+4, W09 R-5 | `committed_instant` + mapa `DomainId → DomainTick`; příkazy a journal nesou `effective_instant` |
| NC-03 | Čas | Rychlost času: W09 per-region `time_scale` vs. W04/W07 jedna globální rychlost. Nikdo neřeší, kdo smí rychlost měnit. | W09 R-1, W07 2 | Jedna globální `TimeScale` v Time Authority, změny jako bariéry v budoucím `WorldInstant` |
| NC-04 | Čas | Lease ve světovém čase by při zrychlení expiroval 1000× rychleji | W05 N-12 | Lease a timeouty se měří jen `WallClock` |
| NC-05 | Čas | Časová škála a epocha: W04 TDB u každého stavu vs. `WorldInstant` | W01 N-05, W04 N-7 | Stav nese jen `WorldInstant`, škálu a epochu nese provider nebo rámec |
| NC-06 | Prostor | Tři mřížky: W01 cube-sphere, W05 S2/H3, W08 cubed-sphere + curvilinear oceán | W01 N-01, W08 NEW-801 | Cube-sphere S2-kompatibilní podle W01, H3 jen import/export. Environment mřížky jsou shluky buněk W01, oceán má výjimku s remapem. |
| NC-07 | Prostor | ECEF double (W07) vs. cell + local (W01). Velikost buňky nikde není. | W01 N-03, N-06, W07 5 | Pozice = frame + anchor + cell i64 + local f64, L = 2¹⁵ m. ECEF jen jako odvozený pohled. |
| NC-08 | Prostor | V jakých osách je ω v `v_I = R(v_R + ω×r) + v_O` (W01, W10 §4) | W01 N-02 | `ω_R` v osách rotujícího rámce, `FrameTransform` nese osy explicitně |
| NC-09 | Prostor | Island přes více buněk nemá definovaný rámec | W02 N-204 | Rámec islandu = rámec anchoru |
| NC-10 | Prostor | Kdo vlastní orientaci a rotaci těles (W04 vs. W01) | W04 N-6 | Návrh: model W04, W01 ho publikuje v grafu rámců. **Rozhodnutí uživatele.** |
| NC-11 | Gravitace | Tři vlastníci vyhodnocení gravitace (w01, w03 §4.3, w04 §5.3). Gravitace dvakrát v pipeline w02. | W04 N-1, N-2 | Gravity Service skládá, Physics Runtime aplikuje jednou |
| NC-12 | Gravitace | W04 skládá drag, SRP a outgassing do jednoho vzorce s gravitací. Volající si vybírá zdroje (`source_entities`). Oblateness je duplicitní s harmonickým modelem. | W04 N-3, N-4, N-5 | Gravity API jen gravitace, zdroje z konfigurace, registr `covers` s tvrdou chybou při překryvu |
| NC-13 | Gravitace | Pozice těles mají dva zdroje pravdy (efemeridy a N-body) | W04 N-10 | Jeden `state_source` na těleso a region |
| NC-14 | Gravitace | Gravitace v `EnvironmentSample` (W10, W08) i ve W04 | W08 NEW-802 | V `EnvironmentSample` je jen pass-through z W04 |
| NC-15 | Gravitace | Dvě geometrické reference: elipsoid WGS84 vs. referenční koule EGM (jiné GM a a_ref) | W04 N-8 | Držet odděleně. Konstanty ověřit v NGA TR8350.2 a EGM2008. |
| NC-16 | Autorita | Granularita epochy: W05 na regionu, W09 na entitě, W02 islandy, W10 má obojí | W02 N-201, W05 N-01, N-11 | Viz C-01 |
| NC-17 | Autorita | „Migrace“ znamená u W05 region mezi procesy a u W09 entitu mezi regiony | W05 N-02 | `RegionHandoff` vs. `EntityTransfer` |
| NC-18 | Autorita | Kdo rozhoduje o vlastnictví (W05 coordinator vs. W09 cílový region). Pořadí obnovy W05 vs. W09. | W05 N-03, N-04 | Rozhoduje coordinator. Při obnově nejdřív nová epocha, pak snapshot a replay. |
| NC-19 | Autorita | Identita vlastníka (process / node / region), restart uzlu nerozlišen | W05 N-06 | `owner_node_id` + `owner_incarnation` |
| NC-20 | Autorita | `expected_owner_epoch` v příkazu hráče prozrazuje interní stav a klient může lhát | W05 N-08 | Doplňuje ho gateway, klient posílá jen `entity_id` a `sequence_number` |
| NC-21 | Autorita | Hot-region řešení W09 dovoluje rozdat islandy workerům bez pravidla jednoho vlastníka | W02 N-207 | Rozlišit `exec_generation` a `owner_epoch` |
| NC-22 | Fyzika | „Nezávislá“ časová dimenze (W02) vs. společný krok islandu | W02 N-202 | Island má společný `step_rate` |
| NC-23 | Fyzika | Migrační kontrakt W09 §11.3 nemá island, manifoldy ani warm-start | W02 N-203 | Rozšířit `MigrationRecord` |
| NC-24 | Fyzika | Rollback (W02) vs. distribuce. Selektivní CCD vs. nutnost zaručit `v_max` u hranice. | W02 N-205, N-206 | Server bez rollbacku, rollback jen na klientovi. `velocity_bound` ve stavu. |
| NC-25 | Fyzika | Mezi-regionální CCD nikdo nevlastní (W01 chce swept dotazy, W02 řeší CCD jen lokálně) | W09 R-4 | Viz C-06 |
| NC-26 | Fyzika | Statika jako sdílená vstupní data vs. mutace světa | W02 N-208 | Statika není island, platí podle revize světa |
| NC-27 | Domény | Původní w03 přiřazuje rámce W02 a gravitaci W01/02, což nesedí s baseline W10 | W03 NC-301 | Rámce W01, gravitace W04, integrace W02 |
| NC-28 | Domény | „Fidelity“ ve dvou osách (W09/W04 L0–L3 vs. varianty modelů W03) a kolize pojmů fidelity / LOD / domain | W03 NC-302, W09 R-7 | `FidelityLevel = (SimTier, ModelVariant)`. Glosář: fidelity = entita, LOD = prostředí a rendering, domain = integrátor. |
| NC-29 | Domény | Pevný tick vs. adaptivní integrace (orbit) a jejich determinismus | W03 NC-303 | Deterministický step controller, kroky v journalu domény |
| NC-30 | Domény | Kdo vlastní vztlak a odpor ve vodě (W03 vs. W08) | W03 NC-304 | W08 dodává `WaterSample`, W03 počítá síly, Physics je aplikuje |
| NC-31 | Domény | Staging vytváří entity a nikdo neurčil, kdo dává ID a vlastníka | W03 NC-305 | Deterministické ID z (parent_id, event_seq), vlastník = vlastník rodiče |
| NC-32 | Domény | Interakce s on-rails entitami, rotace atmosféry, interiér pohyblivého nosiče | W03 NC-306..308 | `R_int` pro vstup na rails, výchozí `v_atm = ω×r`, režim `Attached` |
| NC-33 | Mutace | Skalární `world_revision` vs. více vlastníků regionů | W06 N-01 | Viz C-04 |
| NC-34 | Mutace | Tři revizní pojmy, W07 `uint32` přeteče | W06 N-03 | `state_version` (Physics) + `structure_revision` (Mutation), obojí u64 |
| NC-35 | Mutace | Kdo vlastní ekonomické transakce (W05 externí služba vs. W06 Mutation jako jediná cesta) | W06 N-04 | **Rozhodnutí uživatele** (OQ-07) |
| NC-36 | Mutace | Dva kanály příkazů (W05 vs. W06), pořadí vs. idempotence | W06 N-05, N-06 | Jeden `CommandEnvelope` s typovaným payloadem |
| NC-37 | Mutace | Rozsah event sourcingu v journalu | W06 N-07, W05 N-07 | Journal = mutace, migrace, checkpointy a vstupy. Spojitá fyzika = snapshot + deterministický replay po doméně. |
| NC-38 | Mutace | Větve What-If vs. revize | W06 N-08 | `BranchId` jako prefix revize |
| NC-39 | Mutace | Vlastník islandu ≠ cílový region mutace. Kdo vytváří kolizní geometrii. | W06 N-09, N-10 | Mutace míří na region, strukturální změna na vlastníka islandu. Geometrii vytváří Physics. |
| NC-40 | Mutace | Mutace platí „teď“, distribuovaně ale potřebují `effective_instant` | W09 R-8 | `effective_instant ≥ max W` dotčených regionů |
| NC-41 | Mutace | Autorita vody po mutaci terénu (W06 vs. W08) | W08 NEW-804 | Voda je prognostická ve W08, W06 jen invaliduje |
| NC-42 | Environment | Na který `WorldInstant` se vztahuje dotaz na prostředí | W08 NEW-803 | Instant kroku, ve kterém se síla aplikuje |
| NC-43 | Environment | Zpětnou vazbu entity na atmosféru nedefinuje nikdo | W08 NEW-805 | Stage 1 jednosměrně. **Rozhodnutí uživatele** (OQ-13). |
| NC-44 | Environment | Je environment doména s vlastním replay logem? | W08 NEW-806 | Ano. Replay čte zaznamenané vzorky. |
| NC-45 | Environment | Nest přes hranici ownership regionu nemá jednoho autora | W08 NEW-807 | Nest jen v rámci jednoho regionu |
| NC-46 | Distribuce | Obnova z checkpointu posune watermark do minulosti a poruší monotonii GVT. Konzistentní řez při recovery nikdo nepopisuje. | W09 R-3, W05 N-09 | Viz C-09 |
| NC-47 | Distribuce | Tři úrovně konzistence W09 nepokrývají cross-region události | W09 R-6 | Přidat úroveň causal/ordered |
| NC-48 | Distribuce | HLA time management jiná abstrakce než regiony | W09 R-9 | Jen mapování pojmů, žádná RTI |
| NC-49 | Klient | Unity fyzika na klientovi nemá paritní model vůči Rust fyzice | W07 3 | `model_version` + sdílené testovací vektory |
| NC-50 | Klient | Floating origin vs. historie predikce | W07 7 | Historie v (frame, anchor, cell, local) double, rebase přepočítá |
| NC-51 | Klient | W07 „evaluate QUIC“ vs. rozhodnutý ADR-011. Podpora QUIC ve WebGL a na mobilu neověřena. | W07 6 | Fallback, viz OQ-16 |
| NC-52 | Externí | SPICE jako runtime závislost nebo jen validace. Fiktivní planety nemají IAU/IERS modely. | W01 N-07, N-08 | SPICE jen validace. `RotationModel` jako plugin. |

## C. Rozpory mezi doplňky navzájem (kolo 1)

Tohle zjistil správce repa při slučování. Doplňky se navzájem neviděly, některé se proto rozcházejí nebo jeden mění baseline W10. Tady má rozhodnout druhý audit W10.

| ID | Rozpor | Kdo | Doporučení správce |
| --- | --- | --- | --- |
| C-01 | **Kdo nese epochu vlastnictví.** W02: epochu drží island, entita ji dědí, `EntityPhysicalState` dostane `island_id` + `island_epoch`. W05: dvouúrovňový token `region_epoch` (coordinator) + `entity_generation` (+1 při převodu), registr drží jen regiony a `EntityRoute`, island je odvozený. W06: razítko mutace nese obě identity. | W02, W05, W06 | Spojit: fencing = `region_epoch` (W05), jednotka převodu = island (W02), `entity_generation` = ochrana při převodu. Pole `owner_epoch` v W10 §6 přejmenovat, aby bylo jasné, čí epochu nese. |
| C-02 | **Šířka `WorldInstant`.** W01: i128 kvant q = 1/1,44·10⁹ s od J2000 TT. W07: i64 s jednotkou dohodnutou v handshake. W09: jen celočíselnost a dělitelnost. | W01, W07, W09 | Autoritativně i128 podle W01. Na drátě delta vůči referenci snapshotu (i64 nebo varint), což řeší i W01 OQ „i128 v journalu“. |
| C-03 | **Rychlost času se sbíhá, ale pravidla jsou na třech místech.** W07: jeden rate na `world_epoch`, per-player jen jako větev. W09: globální `TimeScale` s bariérami a strop `s_max ≈ L_min / t_win` (~90× LAN, ~9× WAN). W03: doména deklaruje `max_warp`, jinak on-rails nebo ořez. | W03, W07, W09 | Shoda. Zapsat jako jedno ADR („Time Authority“): globální rate, bariéry, `max_warp` domén, strop synchronizace. |
| C-04 | **Revize světa.** W06: `world_revision` není globální čítač, ale per-region `(owner_epoch, seq)` + `RevisionVector`. W10 §6 a W08 ale v `EnvironmentSample` počítají se skalárním `world_revision`. W02 váže platnost statiky na `world_revision`. | W06, W08, W02, W10 | `EnvironmentSample` a ghosty nesou revizi regionu (nebo vektor), ne skalár. Upravit hlavičku W10 §6. |
| C-05 | **Čtyři „handoffy“.** W02 migrace islandu, W03 `DomainHandoff` (změna domény, region stejný), W05 `RegionHandoff` + `EntityTransfer`, W09 handoff entity k budoucímu `handoff_instant`. W02, W05 a W09 popisují tutéž operaci (entita nebo island mění region) ze tří stran. | W02, W03, W05, W09 | Jeden protokol `EntityTransfer`: W05 dodá fencing a commit, W02 obsah (island snapshot s manifoldy), W09 časování (`handoff_instant`). `DomainHandoff` z W03 je jiná operace. Sjednotit v glosáři. |
| C-06 | **Mezi-regionální CCD.** W02: CCD dělá vlastník pohybujícího se tělesa s invariantem `halo ≥ v·Δt + r`. W09: swept dotaz přes hranici vlastní W09, nejdřívější `t_hit` a deterministický tie-break. | W02, W09 | Doplňují se. Uvnitř halo platí W02, rychlejší objekty mimo halo (balistické entity) řeší W09. Zapsat jako jedno pravidlo. |
| C-07 | **W04 mění baseline W10.** Zdánlivé síly (Coriolis, odstředivá, Euler) nemá dodávat „Environmental Acceleration Provider“, ale kinematika rámce z W01, a aplikuje je Physics Runtime. W03 NC-307 na to navazuje korotující atmosférou `v_atm = ω×r`. | W04, W01, W03, W10 | Přijmout. Zúžit nebo zrušit Environmental Acceleration Provider v CONFLICT-002. |
| C-08 | **Fidelity.** W03 navrhuje dvouosou `FidelityLevel (SimTier, ModelVariant)`, W09 navrhuje glosář fidelity / LOD / domain. | W03, W09 | Kompatibilní. Přijmout obojí do glosáře. |
| C-09 | **Obnova a sousedé.** W05: nová epocha → snapshot + replay, snapshot nese `sent_seq/recv_seq`. W09: re-emit s hash kontrolou vs. output commit, doporučuje hybrid. | W05, W09 | Společné ADR (W09 O-3). |

## D. Tvrzení k ověření z primárních zdrojů

Doplňky je samy označily jako neověřené. Jsou vhodné pro GPT research.

| ID | Tvrzení | Kde | Primární zdroj k ověření |
| --- | --- | --- | --- |
| V-01 | CGPM 2026 schválilo UTC bez přestupných sekund od 2027 (tolerance 1 h) | W01 | BIPM, rezoluce CGPM |
| V-02 | Bloková struktura 6×6 stavové transformace `sxform` | W01, W03 | NAIF SPICE `sxform_c` dokumentace |
| V-03 | Konstanty WGS84 vs. EGM2008 (GM, a_ref), tide system C20 | W04 | NGA TR8350.2, dokumentace EGM2008 |
| V-04 | Sémantika etcd `LeaseKeepAlive` a fencing přes `Txn` | W05 | etcd API docs |
| V-05 | Interní island management Rapier, kritéria Jolt `LargeIslandSplitter`, PhysX CCD | W02 | Zdrojové kódy a manuály |
| V-06 | Atomicita OSM diff uploadu | W06 | OSM API v0.6 wiki |
| V-07 | Jones 1999 / ESMF conservative remap, flux korekce na hranici nestu ve WRF, pořadí akumulace a remapu v OASIS/CESM | W08 | ESMF, WRF a CESM dokumentace |
| V-08 | Mattern/Samadi GVT, HLA IEEE 1516 time management, Kulkarni HLC | W09 | Původní články, standard |
| V-09 | Unity `Physics.simulationMode` s manuálním krokem, QUIC ve WebGL a na mobilu | W07 | Unity docs |
| V-10 | Hodnoty KSP pack/unpack vzdáleností | W03 | KSP wiki / zdroje modů |
