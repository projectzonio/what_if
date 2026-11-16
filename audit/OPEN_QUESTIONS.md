# Otevřené otázky

Otázky z doplňků W01–W09 (oddíl 8), sloučené a seřazené podle toho, co blokuje nejvíc ostatního. Doporučení je návrh doplňku nebo správce repa. Neznamená, že je rozhodnuto.

Legenda: **U** = rozhodnutí uživatele (vize, herní design, rozpočet), **A** = architektonické, uzavře ho druhý audit W10 / ADR, **M** = vyžaduje měření nebo benchmark.

## Priorita 1: blokuje kontrakty

| ID | Typ | Otázka | Doporučení | Zdroj |
| --- | --- | --- | --- | --- |
| OQ-01 | U | Simuluje se jen reálná Země a Sluneční soustava s reálnými daty (EOP, WGS84, EGM), nebo i fiktivní světy? Jak daleko: Sluneční soustava, blízké hvězdy, galaxie? | Obojí. Reálná Země jako referenční svět, fiktivní tělesa jako pluginy s deklarovanou přesností. | W01 Q3, Q8; W04 Q1 |
| OQ-02 | U | Rychlost času v multiplayeru: jedna globální, per-region, nebo per-hráč? Kdo ji smí měnit (host, hlasování, role)? | Jedna globální `TimeScale`. Individuální čas jen jako oddělená větev What-If. | W07 Q1–Q2; W09 O-2; W03 Q3 |
| OQ-03 | U | Cílový maximální warp v multiplayeru a na jaké síti (LAN/WAN)? | Rozhoduje, jestli on-rails domény musí být už ve Stage 3. Odhad stropu ~90× LAN, ~9× WAN bez on-rails. | W09 O-1; W07 Q3 |
| OQ-04 | A | `WorldInstant` = i128, q = 1/1,44·10⁹ s, epocha J2000 TT? Jedna epocha pro všechny světy? | Ano (W01). Na drátě delta, viz CONFLICTS C-02. | W01 Q1, Q6, Q7; W07 Q5; W09 O-6 |
| OQ-05 | A | Velikost buňky L = 2¹⁵ m (≈ 33 km), nebo 2¹² m (≈ 4 km) kvůli jemnějším regionům? | 2¹⁵ m, region = množina buněk. | W01 Q2; W05 Q8 |
| OQ-06 | A | Kdo nese epochu vlastnictví: region, island, entita? | Viz CONFLICTS C-01. | W02 Q1; W05; W06 |
| OQ-07 | U | Ekonomika (inventář, účty) jako agregát v region journalu, nebo samostatná transakční služba/DB? | Region journal. Pokud externí DB, rezervace jen přes sagu s kompenzací. | W06 Q2; W05 Q3 |
| OQ-08 | U | Smí stavba (most) překročit hranici regionu? | Omezit ve Stage 1–3, protože to výrazně zjednoduší mutace. Cenou je omezení herního designu. | W06 Q4 |
| OQ-09 | A | Vlastník orientace a rotace nebeských těles: W04, nebo W01? | Model W04, W01 ho publikuje v grafu rámců. | W04 Q10 |

## Priorita 2: rozsah Stage 1 a herní pravidla

| ID | Typ | Otázka | Doporučení | Zdroj |
| --- | --- | --- | --- | --- |
| OQ-10 | U | VAL-REFERENCE-001, kroky 5–7: jak se postava dostane z letadla do rakety? | Potvrdit upřesněnou variantu ve W03 §6.2. | W03 Q1 |
| OQ-11 | U | Nástup do vozidla fyzikálně (výměna hybnosti), nebo „zero-impact“? Chodné interiéry s fiktivními silami už ve Stage 1? | Zero-impact pod prahem hmotnosti. Ve Stage 1 jen sedadla (`Attached`). | W03 Q2, Q5; W02 Q4 |
| OQ-12 | U | Ambice meteorologie: procedurální / regionálně konzistentní / plný numerický model? | Určí, jestli je třída `Conservative` nutná pro vzduch, nebo jen pro vodu. | W08 Q3 |
| OQ-13 | U | Smí hráč a entity měnit atmosféru (rotor, výbuch, požár)? | Ve Stage 1 jednosměrně, `EnvironmentImpulse` od Stage 3. | W08 Q4 |
| OQ-14 | U | Které tělesa dostanou detailní gravitační model? Gravitace entit (stanice)? Mění mutace terénu gravitaci? | Země (EGM), Měsíc, Mars volitelně, ostatní bodově + J2. Entity a mutace gravitaci ve Stage 1 nemění. | W04 Q1, Q5, Q6 |
| OQ-15 | U | Přijatelná ztráta fyzikálního stavu při pádu regionu (RPO)? Jiná pro hru a pro vědeckou simulaci? | Stage 1: RPO 0 pro transakce, ≤ 1 s pro události, RTO ≤ 30 s (odhad). | W05 Q2 |
| OQ-16 | U | Klienti: WebGL a mobil? Pak QUIC potřebuje fallback (WebSocket/WebTransport). | Rozhodne ADR-011. | W07 Q7 |
| OQ-17 | A | Rozsah Stage 1 domén: D0–D4 a D8 hned, orbit (D5/D6), docking a FSI později? | Ano. | W03 Q8 |
| OQ-18 | A | Tvrdá konzervace vody a hmoty už ve Stage 2? Co při porušení (`Violated`)? | Vodu a hmotu tvrdě, energii a hybnost jen měřit. Při porušení vrátit LOD přechod, nikdy nezastavit svět. | W08 Q2, Q6 |
| OQ-19 | A | Obří islandy (vlak, most): limit, nebo partitioned coupling? | Limit až do Stage 3. | W02 Q2 |
| OQ-20 | A | Fail-closed, nebo fail-open pro objekt bez `PhysicsIntegrated`? | Fail-closed pro kolize. | W06 Q3 |
| OQ-21 | A | Unity klientská fyzika: vypnout (vlastní kinematický model), nebo `Physics.simulationMode` s ručním krokem? | Ověřit (V-09). | W07 Q4 |

## Priorita 3: provoz a pozdější fáze

| ID | Typ | Otázka | Doporučení | Zdroj |
| --- | --- | --- | --- | --- |
| OQ-22 | A | Technologie coordinatoru od Stage 3: etcd / Postgres / vlastní Raft? | Rozhraní kompatibilní s etcd za traitem, ve Stage 1 lokální implementace. | W05 Q1 |
| OQ-23 | A | Ztráta journalu: zablokovat region, nebo degradovaný provoz? Skok epochy po disaster recovery? | Třída A zablokovat, třída C povolit s auditem. | W05 Q4, Q6 |
| OQ-24 | A | Retence journalu, idempotency tabulky, `CrossRegionEventLog` a auditu ownership | Do posledního stabilního snapshotu + retence (dny až týdny). | W05 Q5; W06 Q5; W09 O-7 |
| OQ-25 | A | Limit `causation_depth` a co při překročení | Odložit a vyhlásit alarm. | W06 Q6 |
| OQ-26 | A | Politika přestupných sekund do budoucna | Ověřit V-01. | W01 Q4 |
| OQ-27 | A | SPICE v produkčním runtime, nebo jen při validaci? | Jen validace (licence, determinismus). | W01 Q5 |
| OQ-28 | A | Tide system (tide-free / mean / zero-tide), slapy pevné Země, relativistické členy | Rozhodnout tide system teď, slapy a relativita jen ve vědecké vrstvě. | W04 Q3, Q4, Q8 |
| OQ-29 | A | Time Warp (optimistická synchronizace) uvnitř procesu? | Ne ve Stage 1–2. | W09 O-4 |
| OQ-30 | A | Kdo vlastní `Δt_max(domain)` při zrychlení? | Doména (W03) publikuje, Time Authority omezuje. | W09 O-5 |
| OQ-31 | A | Island graf nad fyzikálním backendem, nebo uvnitř něj (Jolt vs. Rapier/Avian)? | Nad backendem. | W02 Q7 |
| OQ-32 | A | Replay vědeckých výsledků: stačí tolerance, nebo bitový replay vybraných událostí? | Tolerance, bitově jen journal událostí. | W08 Q9 |
| OQ-33 | U | Datové zdroje (ERA5 aj.): licence a objem | Řešit až s volbou meteorologie (OQ-12). | W08 Q8 |

## Vyžaduje měření (M)

Všechny prahy a tolerance v doplňcích jsou **odhad**. Spike nebo benchmark potřebují:

- `N_persist`, `t_dwell`, `t_cooldown`, limity islandů, zpoždění impulsu přes hranici (W02)
- `ε_r`, `ε_v`, `ε_a` a prahy hystereze handoffu domén (W03)
- `H_valid` horizonty predikce podle typu entity a rate (W07)
- tolerance bilancí (např. 1e-6 / simulovaný den) a cena dotazu `EnvironmentSample` (W08)
- režie boundary protokolu pro 1 000 hranic, `L_min`, `t_win` (W09)
- latence commitu journalu a derivací (W06 VAL-613), renew storm leasů (W05)
