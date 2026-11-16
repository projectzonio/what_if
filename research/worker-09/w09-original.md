WORKER 09 — PERFORMANCE, SCALABILITY & DISTRIBUTED SIMULATION
Planetary Engine | Technical Architecture Specification
Architecture research
Rust simulation runtime
No implementation

Mission: Navrhnout výkonnostní a distribuovanou architekturu, která umožní provozovat jeden persistentní fyzikálně konzistentní vesmír od lokální simulace až po potenciálně 10 000+ současně připojených hráčů.
1. Executive summary
Zásadní architektonický závěr
Planetary Engine nemůže být navržen jako jeden obrovský herní server, jeden ECS world ani jeden globální physics loop.

Musí jít o hierarchickou distribuovanou simulační platformu, ve které jsou odděleny:
1. Fyzická reprezentace vesmíru.
2. Vlastnictví simulačního stavu.
3. Frekvence simulace.
4. Detail fyzikálního výpočtu.
5. Replikace stavu klientům.
6. Persistentní ukládání.
7. Distribuce výpočetní zátěže.
Klíčový princip:

Vesmír je jeden logický systém, ale jeho simulace je rozdělena mezi mnoho nezávislých výpočetních oblastí.

To znamená, že hráč může přejít z pěšího pohybu do vozidla, následně do letadla a do vesmíru, aniž by se měnil základní model světa. Mění se pouze aktivní simulační model, výpočetní rozlišení a příslušná autorita.
Předběžné doporučení
Hierarchical Distributed Simulation
Global Universe State

Persistentní identita, čas, globální souřadnice a kauzální vztahy


Region Simulation

Vlastnictví regionů a autoritativní simulace


Simulation Scheduler

LOD, tick rate, prioritizace a rozpočty


Entity Runtime

ECS, fyzika, AI a agregované entity


Replication Runtime

Interest management, snapshots a delty




Distributed Rust Simulation Nodes

Region ownership, worker pools, fault recovery a horizontal scaling


Node.js Control Plane · Persistence · Unity Clients

Orchestrace, účty, matchmaking, API, ukládání a prezentace


Toto je navrhovaný cílový model, nikoliv tvrzení, že již existuje hotová technologie, která jej automaticky zajistí.
2. Detailed technical investigation
2.1 Simulation load: hráči nejsou hlavní jednotkou výpočetní zátěže
Počet připojených hráčů není dostatečný ukazatel výkonu.

Deset hráčů v jednom městě může být výpočetně náročnějších než tisíc hráčů rozprostřených po planetě.

Rozhodující faktory:
- Počet aktivních fyzikálních objektů.
- Počet vzájemných kolizí a interakcí.
- Počet AI agentů v okolí.
- Počet vozidel a jejich fyzikálních modelů.
- Počet síťových pozorovatelů jednotlivých entit.
- Počet objektů vyžadujících vysokou simulační přesnost.
- Hustota interakcí mezi regiony.
- Počet změn persistentního stavu.
Proto navrhuji sledovat pět nezávislých veličin:
Metrika	Význam
Connected players	Připojení hráči
Active entities	Entity s aktivním simulačním modelem
Physics interactions	Kontakty, kolize, constrainty
Replication workload	Síťová práce
Simulation complexity	Celková výpočetní náročnost

Důležitý důsledek: Kapacitní model musí pracovat s hustotou simulace, nikoliv pouze s počtem hráčů.
2.2 Výpočetní model a tick budget
Pro serverový runtime doporučuji oddělit simulační čas od reálného času.

Základní vztah:
\[
T_{budget}=\frac{1000}{f_{tick}}\text{ ms}
\]

Frekvence	Celkový budget
10 Hz	100 ms
20 Hz	50 ms
30 Hz	33,33 ms
50 Hz	20 ms
60 Hz	16,67 ms
100 Hz	10 ms

Pro první výkonnostní model doporučuji:
Navrhovaný hlavní simulační profil
30 Hz
33,33 ms na simulační cyklus

Operace	Budget
Physics	12 ms
ECS / gameplay	5 ms
AI / environment	4 ms
Replication preparation	4 ms
Region coordination	2 ms
Reserve	6,33 ms
Celkem	33,33 ms

Počáteční hypotéza pro benchmarky, nikoliv naměřené hodnoty. Jednotlivé subsystémy mohou běžet asynchronně nebo na odlišných frekvencích.
Tento model nesmí být interpretován tak, že všechny operace musí být synchronně dokončeny v každém ticku. To by zbytečně svazovalo celý systém nejpomalejším subsystémem.

Doporučuji:
- Physics používá vlastní fixed timestep.
- AI používá adaptivní plánování.
- Environment může využívat nižší frekvenci.
- Persistence běží asynchronně.
- Replication používá vlastní frekvenci a prioritizaci.
- Region coordination používá vlastní synchronizační body.
2.3 Amdahlův zákon a skutečné limity paralelizace
Zásadní matematický limit představuje Amdahlův zákon:
\[
S(N)=\frac{1}{(1-P)+\frac{P}{N}}
\]

Kde:
- \(P\) je paralelizovatelná část výpočtu.
- \(N\) je počet výpočetních vláken.
- \(S(N)\) je dosažené zrychlení.
Příklad při 90% paralelizovatelnosti:
Vlákna	Teoretické zrychlení
1	1×
2	1,82×
4	3,08×
8	4,71×
16	6,40×
32	7,80×

To ukazuje, proč není vhodné spoléhat na pouhé přidávání CPU jader.

Pro Planetary Engine budou kritická zejména:
- Synchronizace fyzikálních constraintů.
- Kolize mezi paralelními pracovními oblastmi.
- Sdílený přístup k ECS komponentám.
- Globální pořadí událostí.
- Region ownership.
- Serializace a replikace.
Doporučení: Navrhovat simulační subsystémy tak, aby pracovaly nad vlastními datovými oblastmi a komunikovaly pomocí explicitních zpráv, eventů a synchronizačních bariér.

3. Entity scaling a víceúrovňová simulace
3.1 Čtyři provozní režimy entity
Nedoporučuji dělit entity pouze na aktivní a neaktivní. Potřebujeme více stupňů reprezentace.
L0 — Full simulation
Maximum fidelity








unrealengine.com


Plná fyzika, kolize, gameplay, AI, interakce a přesná síťová replikace.

Typické případy: hráč, jeho vozidlo, bezprostřední okolí, projektily.

L1 — Reduced simulation
Reduced fidelity

Zjednodušená fyzika, méně časté AI rozhodování, agregované environmentální interakce.

Typické případy: okolní doprava, vzdálenější NPC, vzdálené fyzické objekty.

L2 — Abstract simulation
Statistical model

Simulace pomocí stavových přechodů, pravděpodobností a agregovaných veličin.

Typické případy: ekonomika města, vzdálená populace, dopravní toky, průmysl.

L3 — Dormant / persistent
No continuous runtime

Uložení posledního stavu, časové značky a pravidel pro pozdější obnovení.

Typické případy: neaktivní regiony, nepoužívané budovy a vzdálené objekty.

Pozor: tyto úrovně nejsou čtyři různé typy entity. Jsou to různé výpočetní reprezentace stejného logického objektu.

Například automobil:
1. L0: kompletní dynamika podvozku, pneumatiky, kontakt s vozovkou.
2. L1: zjednodušená dynamika vozidla.
3. L2: pohyb v dopravním toku jako stav dopravní sítě.
4. L3: uložená poloha, stav paliva, poškození a čas poslední aktualizace.
Přechod mezi úrovněmi musí zachovat relevantní stav. Jinak se z toho stane teleportace nebo ztráta fyzikální konzistence.
3.2 Přechody mezi LOD
Navrhuji samostatný Simulation Fidelity Manager.

Rozhodovací funkce:
\[
F(e)=f(D,I,R,C,G)
\]

Kde:
- \(D\) = vzdálenost od relevantních hráčů.
- \(I\) = důležitost entity.
- \(R\) = míra interakce.
- \(C\) = dostupná výpočetní kapacita.
- \(G\) = gameplay kontext.
Příklad:
Situace	Fidelity
Hráč sedí ve vozidle	L0
Vozidlo jede 100 m od hráče	L0/L1 podle interakcí
Vozidlo jede 2 km od hráče	L1/L2
Dopravní provoz 50 km daleko	L2
Opuštěný region	L3

Vzdálenost sama o sobě nesmí být rozhodující.

Příklad: raketa vystřelená hráčem může být vzdálená 10 km, ale stále musí mít přesnou trajektorii a relevantní fyzikální stav.

Proto musí existovat importance override, který dovolí entitě udržovat vysokou fidelitu nezávisle na vzdálenosti.
3.3 Přechod musí zachovat fyzikální stav
Každá úroveň musí implementovat převod do společného canonical state.
\[
S_{canonical}\rightarrow S_{L0},S_{L1},S_{L2},S_{L3}
\]

Například:
- Pozice a rychlost.
- Orientace a úhlová rychlost.
- Hmotnost a fyzikální parametry.
- Energie.
- Poškození.
- Inventář.
- Vlastnictví.
- Stav gameplay.
- Čas poslední simulace.
- Deterministické identifikátory a seed.
Při přechodu L2 → L0 se musí rekonstruovat dostatek informací, aby nebylo nutné vymýšlet nový stav objektu.

To ovšem neznamená, že statistický model dokáže rekonstruovat každý detail. Ztrátová informace musí být explicitně evidována.

Architektonické rozhodnutí: Fidelity přechody jsou doménovou funkcí každého typu entity, nikoliv univerzálním generickým převodem fyzikálních komponent.

4. Rust performance architecture
4.1 ECS: archetype storage versus sparse storage
ECS je vhodný model pro rozsáhlou simulaci, ale samotná volba ECS frameworku neurčuje výkon.

Rozhodující je způsob ukládání komponent a plánování systémů.
Přístup	Výhody	Nevýhody
Archetype ECS	Cache locality, efektivní iterace	Přesuny při změně archetypu
Sparse-set ECS	Rychlé změny komponent	Horší sekvenční přístup v některých scénářích
Hybrid ECS	Flexibilita	Vyšší komplexita
Custom SoA	Kontrola nad layoutem	Náklady na vlastní infrastrukturu

Doporučené technologie k evaluaci:
- Bevy ECS – archetypové ECS a systémový scheduler.
- Flecs – vyspělý ECS s paralelním vykonáváním a staging.
- Vlastní SoA storage pro extrémně výkonné fyzikální a environmentální subsystémy.
Flecs používá staging pro bezpečné změny ECS struktury během paralelního vykonávání. Změny se shromažďují jako příkazy, aby nedocházelo k nebezpečné realokaci komponentních polí během iterace. 

Flecs

Doporučení:

Nezačínat implementaci vlastního ECS.

Nejdříve benchmarkovat Bevy ECS a Flecs proti reprezentativním simulačním workloadům. Custom SoA použít tam, kde měření prokáže, že generický ECS představuje skutečné úzké místo.
4.2 Rust threading model
Navrhuji kombinovat tři typy paralelizace.
1. Data parallelism
Rayon
Paralelní zpracování velkých množin nezávislých entit.
- AI batch processing.
- Environment updates.
- Hromadné transformace.
- Statistické výpočty.
  
  2. Async I/O
Tokio
- Síťová komunikace.
- Persistence.
- Region messages.
- Background jobs.
- External service communication.
  
  3. Dedicated simulation workers
Controlled threads
- Physics islands.
- Region simulation.
- Time-critical simulation tasks.
- Synchronization boundaries.

Rayon nabízí work-stealing scheduler a paralelní iterátory. Je vhodný pro CPU-bound výpočty, kde lze práci rozdělit na nezávislé části. 

*(zdroj: Rust +1)*

Tokio doporučuji pro I/O a síťové úlohy, nikoliv jako náhradu fyzikálního scheduleru.

Důležité pravidlo: nesdílet neomezeně jeden thread pool mezi physics, síťovou komunikací a background persistence. Přetížená databázová operace nesmí způsobit vynechání fyzikálního ticku.
4.3 SIMD a cache locality
Pro fyzikální a environmentální výpočty je vhodný především SoA layout.

AoS (Array of Structures):
Entity 1: position, velocity, mass
Entity 2: position, velocity, mass
Entity 3: position, velocity, mass


SoA (Structure of Arrays):
positions[]
velocities[]
masses[]


SoA umožňuje sekvenčně načítat pouze potřebné komponenty a lépe využívat SIMD.

Doporučené použití:
- Particle systems.
- Atmosférické výpočty.
- Hromadná integrace pohybu.
- Environmentální simulace.
- Velké množiny podobných objektů.
Pozor na přehnanou optimalizaci: SIMD není automaticky rychlejší pro všechny operace. Například nepravidelné přístupy k paměti a větvení mohou jeho výhodu zcela eliminovat.

Rust std::simd je podle dokumentace stále experimentální/nightly API. Pro produkční runtime bych proto nepovažoval jeho stabilitu za předpoklad. Lze benchmarkovat stabilní std::arch, compiler vectorization a specializované matematické implementace. 

Rust

4.4 Memory architecture
Doporučuji oddělit:
Paměťová oblast	Obsah
Hot state	Aktivní fyzikální komponenty
Warm state	Entity s redukovanou simulací
Cold state	Persistentní a dormant entity
Spatial index	Regiony, proximity a kolizní kandidáti
Replication buffers	Síťové snapshoty a delty
Event buffers	Události a mezisystémové zprávy
Persistence buffers	Změny čekající na zápis

Základní požadavek: fyzikální systém nemá při každém ticku procházet celé univerzum.

Musí zpracovávat pouze příslušné pracovní množiny.

Pro odhad paměti:
\[
M_{total} =
N_hB_h+N_wB_w+N_cB_c+M_{indexes}+M_{buffers}
\]

Kde \(N\) je počet entit dané kategorie a \(B\) průměrná paměťová náročnost entity.

Zásadní je měřit nejen velikost komponent, ale také:
- Allocator overhead.
- Fragmentaci.
- Indexy.
- Snapshot history.
- Event queues.
- Dočasné pracovní buffery.

5. Distributed simulation architecture
5.1 Region ownership
Toto považuji za jednu z nejdůležitějších architektonických částí celého Planetary Engine.

Nedoporučuji přidělovat serverům hráče. Doporučuji přidělovat jim autoritu nad simulačními oblastmi.







members.delphidigital.io


Každá simulační oblast má:
- Region ID.
- Spatial bounds.
- Owner node.
- Simulation state.
- Simulation clock.
- Entity registry.
- Boundary state.
- Neighbor connections.
- Replication subscriptions.
- Persistent checkpoint.
Region nemusí odpovídat geografickému čtverci.

Může reprezentovat:
- Část města.
- Fyzikální oblast.
- Dopravní síť.
- Vnitřní prostor budovy.
- Vzdušný prostor.
- Orbitální oblast.
- Vesmírný sektor.
Pro planetární povrch může být geografická hierarchie založená například na hierarchickém prostorovém dělení. Pro astronomické vzdálenosti je vhodnější samostatná hierarchie prostorových sektorů.
5.2 Prostorové dělení
Možnosti:
Algoritmus	Výhoda	Nevýhoda
Uniform grid	Jednoduchost a rychlé dotazy	Nerovnoměrná hustota
Quadtree	Adaptivní 2D dělení	Složitější aktualizace
Octree	Adaptivní 3D dělení	Náklady na správu
BVH	Efektivní prostorové dotazy	Složitější dynamické aktualizace
Hilbert/Morton ordering	Lokalita a hierarchické indexování	Samo o sobě neřeší vlastnictví

Doporučuji hybrid:
- Hierarchické prostorové adresování pro globální svět.
- Uniform grid pro husté aktivní oblasti.
- BVH pro fyzikální dotazy.
- Explicitní region ownership pro distribuci.
Hierarchické prostorové indexování nesmí být zaměňováno za samotnou distribuovanou simulační architekturu.
5.3 Region boundary problem
Největší problém distribuované fyziky vzniká na hranicích regionů.

Příklad:

Automobil jede přes hranici regionu A → B.

Nesmí dojít k:
- Dvojímu vlastnictví automobilu.
- Ztrátě fyzikálního stavu.
- Dvojímu započítání kolize.
- Zastavení simulace.
- Nekonzistentnímu času.
- Ztrátě navázaných objektů.
Doporučený postup migrace:
1. Pre-migration
   
   Region A identifikuje blížící se přechod a připraví region B.
2. State transfer
   
   Přenese se kompletní canonical state entity a potřebné závislosti.
3. Ownership reservation
   
   Region B potvrdí připravenost a rezervuje nové vlastnictví.
4. Authority transfer
   
   Změní se epoch/generation ownership token.
5. Commit
   
   Region B se stává autoritativním vlastníkem. Region A již nesmí přijímat nové autoritativní změny.
6. Replication update
   
   Aktualizují se síťové subscription a pozorovatelé.
Doporučuji použít versionované ownership tokeny a fencing mechanismus.

Příklad:
Entity ID:       0x12345678
Current Owner:   REGION_A
Owner Epoch:     42

New Owner:       REGION_B
New Epoch:       43


Zastaralá zpráva s epoch 42 nesmí změnit stav entity po potvrzení epoch 43.
5.4 Přesuny mezi servery a konzistence
Pro produkční architekturu je vhodné rozlišovat tři úrovně konzistence:
Úroveň	Použití
Strong consistency	Vlastnictví, transakce, ekonomické operace
Bounded consistency	Fyzikální komunikace sousedních regionů
Eventual consistency	Statistiky, analytika, vzdálené agregace

Nedoporučuji globální synchronizaci každého fyzikálního ticku.

Taková architektura by znamenala, že pomalejší region může zpomalovat celý vesmír.

Místo toho:
- Každý region má vlastní autoritativní časovou osu.
- Sousední regiony komunikují přes definované boundary interfaces.
- Časové rozdíly jsou explicitně evidovány.
- Kritické interakce používají synchronizační protokoly.
- Globální stav se skládá z verzovaných lokálních stavů.
Pro další studium je důležitý standard IEEE High Level Architecture (HLA). Jeho současná verze IEEE 1516-2025 definuje framework pro propojení samostatných simulací do federace. Je to relevantní referenční architektura pro oddělení simulátorů a jejich interoperabilitu, nikoliv hotový multiplayer engine. 

IEEE Standards Association

5.5 Distributed time
Doporučuji nepředpokládat jediný globální synchronní tick.

Každý region má:
region_id
simulation_tick
simulation_time
last_committed_time
time_scale
owner_epoch


Při interakci mezi regiony musí být definováno, jaký časový stav je platný.

Zejména je třeba řešit:
- Entity přecházející mezi regiony.
- Projektily a rychlé objekty.
- Kolize přes region boundaries.
- Zpožděné síťové zprávy.
- Odlišné frekvence simulace.
- Pause a time acceleration.
- Rekonstrukci stavu po výpadku.
Pro globální kauzální simulace lze studovat HLA time management a konzervativní/distribuované synchronizační algoritmy. Pro běžný real-time gameplay však doporučuji lokální autoritu a explicitní časové hranice, nikoliv globální lockstep.

6. Network replication architecture
6.1 Server-authoritative model
Unity klient nesmí být autoritou nad fyzikálním stavem persistentního univerza.

Doporučený tok:
Unity Client

Input, local prediction, rendering


Rust Authoritative Runtime

Validation, simulation, correction, state replication


Persistent World State

Canonical state, events, checkpoints


Doporučuji kombinovat:
- Client-side prediction.
- Server reconciliation.
- Snapshot interpolation.
- State synchronization.
- Interest management.
Není nutné používat jednu metodu pro všechny objekty.

Například hráčovo vlastní vozidlo potřebuje jiný síťový model než vzdálená NPC nebo vzdálený dopravní provoz.
6.2 Interest management
Pro 10 000 hráčů je zásadní, aby každý klient nedostával kompletní stav světa.

Každý hráč má definovanou oblast zájmu:
\[
I(p)=\{e\mid Relevant(e,p)\}
\]

Relevance může záviset na:
- Vzdálenosti.
- Viditelnosti.
- Zvuku.
- Interakci.
- Gameplay vztazích.
- Vlastnictví.
- Skupině nebo frakci.
- Speciálních událostech.
Navrhuji kombinovat:
1. Spatial interest.
2. Gameplay interest.
3. Event-driven interest.
4. Priority-based replication.
6.3 Replication priority
Nedoporučuji odesílat všechny entity ve stejné frekvenci.

Příklad počátečního profilu:
Objekt	Cílová frekvence
Vlastní hráč / predikční stav	30–60 Hz
Blízké vozidlo	20–30 Hz
Blízké NPC	10–20 Hz
Vzdálená NPC	1–5 Hz
Environmentální stav	0,1–2 Hz
Ekonomické agregace	Event-driven

Jde o návrhové rozsahy, nikoliv naměřené optimální hodnoty.

Gaffer On Games popisuje prioritní akumulaci, díky které jsou důležité objekty aktualizovány častěji a objekty, které se do packetu nevejdou, neztratí svoji prioritu. Součástí řešení je jitter buffer a řízení síťového bandwidth budgetu. 

*(zdroj: Gaffer On Games +1)*

6.4 Orientační síťová kapacita
Pro předběžný model definujeme:
\[
B=P\cdot K\cdot f\cdot b
\]

Kde:
- \(P\) = počet hráčů.
- \(K\) = počet replikovaných objektů na hráče.
- \(f\) = aktualizace za sekundu.
- \(b\) = průměrná velikost aktualizace v bytech.
Pro ilustraci použijme:
- 50 objektů na hráče.
- 10 aktualizací za sekundu.
- 24 bytů na aktualizaci.
Teoretický payload bez hlaviček, transportního overheadu, retransmisí a dalších dat.
Hráči	Serverový egress
100	1,2 MB/s
1 000	12 MB/s
5 000	60 MB/s
10 000	120 MB/s

Orientační agregovaný síťový payload podle zvoleného modelu, nikoliv benchmark.


0 Mbps300 Mbps600 Mbps900 Mbps1,2 tis. Mbps1001 0005 00010 000










      



Při 10 000 hráčích tedy vychází samotný modelový payload přibližně na 960 Mbps agregovaného odchozího provozu.

V praxi bude rozhodující také distribuce hráčů mezi regiony a nerovnoměrnost jejich zájmových oblastí.

Důležitý poznatek: deset tisíc hráčů nemusí znamenat deset tisíc hráčů na jednom serveru. Musí však být možné rozdělit jejich provoz bez narušení společného světa.

7. Persistence, recovery a fault tolerance
7.1 Persistence není součást fyzikálního ticku
Databáze nesmí být kritickou synchronní závislostí fyzikálního výpočtu.

Doporučuji kombinaci:
- In-memory authoritative state.
- Append-only event journal.
- Periodic snapshots.
- Asynchronous persistence.
- Recovery z posledního checkpointu.
Základní recovery model:
\[
S_t=Snapshot_{t_0}+\sum_{i=t_0}^{t}Events_i
\]

To umožní obnovit region z posledního snapshotu a následných událostí.

Pozor: ne všechny fyzikální stavy lze bezpečně rekonstruovat pouze z událostí, pokud nejsou zaznamenány všechny relevantní vstupy a jejich pořadí.
7.2 Crash recovery
Při výpadku serveru:
1. Scheduler zjistí nedostupnost uzlu.
2. Region přejde do stavu recovering.
3. Nový node získá nový ownership epoch.
4. Načte checkpoint.
5. Aplikuje event journal.
6. Provede validační kontrolu.
7. Znovu otevře region pro interakce.
8. Klientům odešle nový authoritative snapshot.
Pro kritické ekonomické operace je nutná transakční ochrana proti duplicitám. Pro fyziku je nutná definovaná politika případné ztráty posledních neuložených ticků.
7.3 Load balancing
Nejjednodušší řešení je přesouvat regiony podle jejich aktuální zátěže.

Doporučuji monitorovat:
- CPU time per region.
- Physics step time.
- Entity count.
- Collision count.
- Network egress.
- Number of connected players.
- Queue depth.
- Memory usage.
- Cross-region message rate.
Samotný počet hráčů není dobrým load-balancing metrikem.

Pro orchestrace game serverů je relevantní Agones. Jeho dokumentace rozlišuje packed a distributed scheduling a podporuje autoscaling fleet. Je to užitečný infrastrukturní vzor, nikoliv řešení migrace živých fyzikálních regionů. 

*(zdroj: Agones +1)*


8. Simulation fidelity a fyzikální konzistence
8.1 Determinismus
Pro Planetary Engine je potřeba rozlišit:
Vlastnost	Význam
Reproducibility	Opakovatelnost experimentu
Local determinism	Stejný výsledek při stejném prostředí
Cross-platform determinism	Stejný výsledek na různém HW
Network consistency	Shoda autoritativního a klientského stavu

Například Rapier dokumentuje lokální determinismus při stejném počátečním stavu, verzi knihovny, kompilátoru a pořadí operací. Cross-platform determinismus má další podmínky včetně fixed timestep a kontroly matematických funkcí. 

*(zdroj: Rapier +1)*

To je důležité upozornění:

Neměli bychom stavět globální multiplayer architekturu na předpokladu, že libovolné fyzikální simulace budou automaticky bitově deterministické.

Doporučuji:
- Server authoritative physics.
- Fixed timestep pro kritické subsystémy.
- Verzi fyzikálního modelu.
- Seedované náhodné procesy.
- Explicitní pořadí důležitých událostí.
- Snapshoty a replay.
- Hashování canonical state.
- Deterministické modely tam, kde jsou skutečně dosažitelné.
8.2 Adaptivní fyzikální přesnost
Není nutné, aby každý objekt používal stejný fyzikální model.

Příklad automobilu:
Režim	Model
L0	Plná dynamika, pneumatiky, odpružení, kolize
L1	Reduced rigid-body model
L2	Kinematický dopravní model
L3	Časově rekonstruovaný stav

Příklad orbitálního tělesa:
Režim	Model
L0	Přesná lokální orbitální integrace
L1	Reduced numerical integration
L2	Analytická aproximace
L3	Keplerian state propagation

Důležité: změna fyzikálního modelu musí mít definovanou chybu aproximace.

Doporučuji každému modelu přiřadit:
- Numerical accuracy.
- Maximum allowed state error.
- Valid operating range.
- Integration method.
- Transition conditions.
Tím se z LOD stane měřitelná simulační vlastnost, nikoliv pouze grafické nastavení.

9. Capacity model: 1 až 10 000+ hráčů
9.1 Zásadní omezení kapacitních odhadů
Bez konkrétního CPU, memory layoutu, fyzikálního workloadu, hustoty entit a síťového profilu nelze poctivě určit, kolik hráčů zvládne jeden server.

Proto rozlišuji:
- Výpočtový model: matematicky odvoditelné vztahy.
- Kapacitní hypotéza: plánovací scénář.
- Benchmark: skutečně naměřený výsledek.
Následující tabulka je plánovací specifikace, nikoliv garantovaná kapacita.
9.2 Scénáře
Scénář	Hráči	Architektonický model
S1	1	Single-node simulation
S2	100	Jeden nebo více region workers
S3	1 000	Horizontálně distribuované regiony
S4	5 000	Více simulačních uzlů a dynamický ownership
S5	10 000+	Clusterová simulace s adaptivním LOD

Pro každý scénář musí benchmark sledovat čtyři různé režimy:
1. Rovnoměrně rozprostřené hráče.
2. Hráče koncentrované v několika regionech.
3. Všechny hráče v jednom hustém regionu.
4. Hráče s vysokou interakční aktivitou.
Čtvrtý scénář je zásadní. Simulace 1 000 hráčů, kteří pouze stojí, je úplně jiný workload než 1 000 hráčů v destruktibilním městě s fyzikou, vozidly a AI.
9.3 Výpočetní kapacita
Definujme:
\[
C_{required}=\sum_{i=1}^{n}N_iF_iT_i
\]

Kde:
- \(N_i\) = počet entit daného typu.
- \(F_i\) = frekvence aktualizace.
- \(T_i\) = průměrná CPU doba jedné aktualizace.
Výsledkem je požadovaný počet CPU sekund práce za jednu simulační sekundu.

Pro plánování je vhodné používat také:
\[
U=\frac{C_{required}}{C_{available}}
\]

Kde \(U\) vyjadřuje využití dostupné výpočetní kapacity.

Doporučuji cílit na provozní využití pod 70 % dlouhodobé kapacity pro reprezentativní workload. Zbývající rezerva je určena pro špičky, migrace, GC-free background práce, obnovu regionů a náhlé interakce. Hodnota 70 % je návrhový cíl, nikoliv univerzální optimum.
9.4 Memory model
Při odhadování RAM je nutné rozlišovat počet entit od počtu aktivních entit.

Příklad plánovacího modelu:
Paměťová kategorie	Počítá se podle
Active state	Počet L0/L1 entit
Dormant state	Počet rezidentních L3 entit
Physics	Colliders, constraints, broadphase
ECS	Komponenty, archetypy, indexy
Replication	Počet hráčů × zájmové entity
History	Snapshot history a event journal
Runtime	Scheduler, buffery, allocator

Pro rozhodování je důležitější paměť na entitu konkrétního modelu než univerzální odhad typu „jedna entita = 1 KB“.
9.5 Doporučená roadmapa škálování
1. Stage 1 — Single machine
   První validační cíl
   1 hráč, 10 000–100 000 jednoduchých entit, lokální physics a profiling.
   
   Cíl: ověřit ECS, SoA, physics islands a scheduling.
2. Stage 2 — Single node multiplayer
   
   100 hráčů.
   
   Cíl: interest management, replication, bandwidth a server authority.
3. Stage 3 — Distributed regions
   
   1 000 hráčů.
   
   Cíl: region ownership, boundary communication, migration a recovery.
4. Stage 4 — Cluster scaling
   
   5 000 hráčů.
   
   Cíl: dynamické rozdělení zátěže, hot-region splitting a fault tolerance.
5. Stage 5 — Persistent universe
   
   10 000+ hráčů.
   
   Cíl: dlouhodobý provoz, globální persistentní stav, automatické škálování a provozní observabilita.

Důležitá poznámka: 100 000 jednoduchých entit v lokálním benchmarku není důkazem schopnosti provozovat 100 000 fyzikálně interaktivních objektů. Každá benchmarková entita musí mít přesně definovaný workload.

10. Proposed module structure
Doporučuji oddělit simulační runtime od clusterové orchestrace.
10.1 Rust workspace

planetary-simulation/
│
├── crates/
│   │
│   ├── simulation-core/
│   │   ├── clock/
│   │   ├── scheduler/
│   │   ├── execution/
│   │   └── deterministic/
│   │
│   ├── entity-runtime/
│   │   ├── registry/
│   │   ├── components/
│   │   ├── archetypes/
│   │   └── lifecycle/
│   │
│   ├── physics-runtime/
│   │   ├── rigid-body/
│   │   ├── collision/
│   │   ├── constraints/
│   │   ├── physics-islands/
│   │   └── fidelity/
│   │
│   ├── spatial-runtime/
│   │   ├── indexing/
│   │   ├── partitioning/
│   │   ├── region-graph/
│   │   └── proximity/
│   │
│   ├── region-runtime/
│   │   ├── ownership/
│   │   ├── boundaries/
│   │   ├── migration/
│   │   └── recovery/
│   │
│   ├── fidelity-runtime/
│   │   ├── lod-manager/
│   │   ├── transitions/
│   │   ├── aggregation/
│   │   └── error-estimation/
│   │
│   ├── replication-runtime/
│   │   ├── interest-management/
│   │   ├── snapshots/
│   │   ├── prioritization/
│   │   ├── compression/
│   │   └── prediction-support/
│   │
│   ├── networking/
│   │   ├── transport/
│   │   ├── sessions/
│   │   ├── messages/
│   │   └── congestion/
│   │
│   ├── persistence-runtime/
│   │   ├── snapshots/
│   │   ├── event-journal/
│   │   ├── checkpoint/
│   │   └── recovery/
│   │
│   ├── observability/
│   │   ├── metrics/
│   │   ├── profiling/
│   │   └── tracing/
│   │
│   └── simulation-node/
│       ├── bootstrap/
│       ├── workers/
│       └── lifecycle/
│
├── benchmarks/
│   ├── entity-throughput/
│   ├── physics/
│   ├── networking/
│   ├── replication/
│   ├── migration/
│   └── persistence/
│
└── tests/
    ├── determinism/
    ├── distributed/
    ├── recovery/
    └── stress/



Toto je logická struktura. Nemusí se automaticky překlopit do jednoho crate na každý modul. Příliš mnoho samostatných crate by komplikovalo vývoj a verzování.
10.2 Node.js control plane
Node.js nemá provádět fyzikální výpočty.

Jeho odpovědností bude:
- Server discovery.
- Region allocation.
- Matchmaking.
- Player sessions.
- Simulation node registry.
- Cluster orchestration.
- Administrative API.
- Monitoring aggregation.
- World configuration.
- Persistentní metadata.
10.3 Python scientific tooling
Python bude sloužit pro:
- Profilování a analýzu benchmarků.
- Generování testovacích workloadů.
- Numerickou validaci fyzikálních modelů.
- Analýzu distribuované zátěže.
- Modelování kapacity clusteru.
- Monte Carlo simulace.
- Offline reprodukci simulačních scénářů.
10.4 Unity
Unity zůstane prezentačním a interakčním klientem:
- Rendering.
- Input.
- Local prediction.
- Interpolation.
- Client-side audio.
- User interface.
- Streaming vizualizace světa.
Nemá být zdrojem pravdy o persistentním světě.

11. API and data contracts
Nejdůležitější je definovat stabilní kontrakty mezi subsystémy dříve, než se jednotlivé runtime komponenty začnou vyvíjet.
11.1 Entity state
Návrh logického kontraktu:
{
  "entity_id": "128-bit-id",
  "entity_type": "vehicle",
  "world_position": {
    "frame_id": "planet_earth",
    "x": 0.0,
    "y": 0.0,
    "z": 0.0
  },
  "orientation": [0.0, 0.0, 0.0, 1.0],
  "linear_velocity": [0.0, 0.0, 0.0],
  "angular_velocity": [0.0, 0.0, 0.0],
  "simulation_level": 0,
  "simulation_tick": 12000,
  "owner_region": "region-001",
  "owner_epoch": 42,
  "state_version": 17
}


Jde o ilustraci logického schématu, nikoliv finální serializační formát.

Pro produkci doporučuji binární reprezentaci s explicitní verzí schématu.
11.2 Region ownership contract
{
  "region_id": "region-001",
  "owner_node": "sim-node-04",
  "owner_epoch": 42,
  "simulation_tick": 12000,
  "simulation_time": 400.0,
  "status": "active",
  "last_checkpoint": 11800
}


11.3 Region migration contract
{
  "migration_id": "migration-123",
  "entity_ids": ["entity-001"],
  "source_region": "region-001",
  "target_region": "region-002",
  "source_epoch": 42,
  "target_epoch": 43,
  "state_version": 17,
  "migration_status": "prepared"
}


11.4 Replication contract
{
  "recipient_id": "player-001",
  "server_tick": 12000,
  "snapshot_sequence": 900,
  "entity_updates": [
    {
      "entity_id": "entity-001",
      "state_version": 17,
      "update_type": "delta",
      "priority": 100
    }
  ]
}


11.5 Komunikační hranice
Komunikace	Doporučený mechanismus
Unity ↔ Rust	Binary network protocol
Node.js ↔ Rust	Versioned RPC / binary messages
Rust ↔ Rust	Internal binary protocol
Rust ↔ Persistence	Asynchronous event/snapshot interface
Python ↔ Benchmark runtime	Dataset / RPC / trace export

Transport je potřeba vybrat podle požadované sémantiky. Pro gameplay data může být vhodné UDP-based transport s vlastním řízením spolehlivosti; pro control plane a správu je vhodný spolehlivý transport.

Nedoporučuji zavádět jednu univerzální komunikační cestu pro všechny typy zpráv.

12. Benchmark suite specification
Tato část je kritická. Výkonnostní architektura musí být postavená na měřitelných výsledcích.
12.1 Physics throughput
Měřit:
- Rigid bodies / second.
- Collision pairs / second.
- Constraint solving time.
- Physics step p50/p95/p99.
- Broadphase time.
- Narrowphase time.
- Memory per rigid body.
- Physics island scaling.
Testovací profily:
Test	Workload
P1	1 000 rigid bodies
P2	10 000 rigid bodies
P3	100 000 jednoduchých bodies
P4	10 000 bodies s kolizemi
P5	Hustý fyzikální pile
P6	Velká síť navázaných constraintů
P7	Více oddělených physics islands

Všechny testy musí definovat geometrii, počet kolizí, constrainty, timestep a počet skutečně aktivních objektů.
12.2 ECS throughput
Měřit:
- Entity iteration throughput.
- Component access throughput.
- Archetype migration.
- Spawn/despawn rate.
- Parallel scheduling overhead.
- Memory consumption.
- Cache misses.
Srovnat:
- Bevy ECS.
- Flecs.
- Custom SoA.
Výsledkem má být rozhodnutí založené na reálných datech, nikoliv na popularitě frameworku.
12.3 Network throughput
Testovat:
- 100 sessions.
- 1 000 sessions.
- 5 000 sessions.
- 10 000 sessions.
Měřit:
- Packets/sec.
- Bytes/sec.
- CPU per connection.
- Packet loss.
- Jitter.
- RTT.
- Replication latency.
- Serialization cost.
- Connection churn.
12.4 Tick stability
Nejdůležitější metrika:
\[
T_{tick}=T_{physics}+T_{ECS}+T_{AI}+T_{coordination}
\]

Sledovat:
- p50.
- p95.
- p99.
- p99.9.
- Maximum.
- Deadline misses.
- Tick backlog.
- Queue depth.
Pro 30Hz profil je deadline 33,33 ms. Nejde však jen o průměr. Pokud p99 pravidelně překračuje deadline, simulace nebude stabilní.
12.5 Region migration
Testovací scénáře:
1. Migrace jedné entity.
2. Migrace 100 entit.
3. Migrace 10 000 entit.
4. Migrace regionu při aktivní fyzice.
5. Migrace při vysoké síťové zátěži.
6. Výpadek source node během transferu.
7. Výpadek target node během transferu.
8. Zastaralé zprávy po ownership transferu.
9. Současné migrace sousedních regionů.
Měřit:
- Migration duration.
- State transfer bytes.
- Entity downtime.
- Lost events.
- Duplicate authority.
- Recovery time.
- State divergence.
12.6 Persistence benchmark
Testovat:
- Snapshot creation.
- Snapshot restore.
- Event journal append.
- Event replay.
- Crash recovery.
- Concurrent region checkpoints.
- Storage backlog.
Důležitý parametr: kolik simulačního času lze ztratit při náhlém výpadku uzlu.

Musí existovat definovaná recovery point objective (RPO) a recovery time objective (RTO).
12.7 Benchmark hardware
Doporučuji tři referenční třídy:
Třída	Hardware profil	Účel
Development	6–8 CPU cores, 16 GB RAM	Vývoj a lokální simulace
Production baseline	16–32 CPU cores, 64 GB RAM	Region runtime
High density	32–64 CPU cores, 128–256 GB RAM	Náročné simulační regiony

Jde o výchozí testovací profily, nikoliv požadavky na finální hardware.

Pro všechny testy evidovat přesný model CPU, počet fyzických a logických jader, RAM, NUMA, OS, compiler flags a verze runtime.

13. Risk register
Předběžné hodnocení architektonických rizik.
Riziko	Dopad	Priorita
Region boundary physics	Fyzikální nekonzistence	Critical
Hot-region overload	Tick instability	Critical
Ownership split-brain	Dvojí autorita	Critical
LOD state loss	Ztráta fyzikálního stavu	Critical
Network replication overload	Bandwidth exhaustion	High
Global synchronization	Výkonnostní bottleneck	High
ECS memory fragmentation	Výkon a RAM	High
Persistence backlog	Ztráta dat při výpadku	High
Cross-platform nondeterminism	Rozdílné výsledky	High
Excessive abstraction	Vývojová komplexita	High

13.1 Nejnebezpečnější problém: Hot regions
Představme si 2 000 hráčů na jednom náměstí.

Rozdělení regionu na více serverů nepomůže automaticky.

Všichni hráči interagují se stejnými objekty a vytvářejí vzájemně závislé fyzikální operace.

Tady existuje zásadní limit:

Distribuovat lze výpočetní práci, ale nelze bez nákladů distribuovat těsně provázanou fyzikální interakci.

Možná řešení:
- Rozdělit region na fyzikální islands.
- Paralelizovat nezávislé skupiny.
- Použít lokální autoritativní physics worker.
- Oddělit fyziku od vizuální replikace.
- Dynamicky snižovat fidelity nekritických objektů.
- Rozdělit oblast na menší simulační authority pouze tehdy, pokud mají dostatečně oddělené interakce.
Pro extrémně husté oblasti musí existovat možnost samostatného výpočetního poolu.
13.2 Největší architektonická past: příliš mnoho distribuovaných závislostí
Nedoporučuji, aby každý subsystém komunikoval přímo se všemi ostatními.

Například:
- Physics nesmí přímo spravovat databázi.
- Unity nesmí vlastnit persistentní stav.
- Node.js nesmí počítat fyziku.
- Replication nesmí měnit autoritativní physics state.
- LOD manager nesmí bez koordinace mazat entity.
Všechny operace musí procházet definovanými rozhraními.

14. Open architectural decisions
Před zahájením implementace je nutné uzavřít následující rozhodnutí.
ID	Rozhodnutí	Doporučení
PERF-001	ECS framework	Benchmark Bevy ECS vs Flecs
PERF-002	Physics engine	Samostatná deep-dive analýza
PERF-003	Region granularity	Hierarchické a adaptivní dělení
PERF-004	Simulation tick	30 Hz výchozí profil
PERF-005	Physics timestep	Fixed timestep, nezávislý na renderu
PERF-006	Worker architecture	Hybrid Rayon + Tokio + dedicated workers
PERF-007	Entity fidelity	Čtyřúrovňový model
PERF-008	Network replication	Interest + priority-based replication
PERF-009	Ownership transfer	Epoch-based authority
PERF-010	Persistence	Event journal + snapshots
PERF-011	Distributed time	Region-local clocks
PERF-012	Hot regions	Physics islands + adaptive scheduling

Další otázky, které vyžadují společné rozhodnutí
1. Má být region fyzikální jednotkou, nebo pouze jednotkou serverového vlastnictví?

Doporučuji druhou variantu. Jeden serverový region může obsahovat více nezávislých physics islands.

2. Mají všechny regiony běžet v reálném čase?

Ne. Dormant a abstraktní regiony mohou používat event-driven nebo časově rekonstruovanou simulaci.

3. Máme používat distribuovanou fyziku?

Ano, ale pouze tam, kde je skutečně nutná. Výchozí model by měl minimalizovat počet fyzikálních interakcí přes hranice serverových regionů.

4. Má mít klient lokální fyziku?

Ano, pro prediction a vizualizaci. Autoritativní persistentní stav však zůstává na serveru.

5. Má být simulace deterministická?

Reprodukovatelnost musí být architektonický požadavek. Bitová cross-platform deterministika má být požadována pouze pro konkrétní subsystémy, které ji potřebují a dokáží garantovat.

15. Recommended next steps
Pro další architektonický výzkum doporučuji rozdělit práci do pěti navazujících deep dives.
Priority 1
WORKER 10 — Physics Runtime & Distributed Physics

Porovnat Rapier, PhysX, Jolt, Bullet a vlastní fyzikální subsystémy. Zkoumat physics islands, collision boundaries, numerical stability a distribuované fyzikální interakce.


Priority 2
WORKER 11 — ECS, Memory & Parallel Execution

Bevy ECS, Flecs, SoA, SIMD, NUMA, work stealing, scheduling dependencies a memory allocation.


Priority 3
WORKER 12 — Distributed World & Region Ownership

Hierarchické prostorové dělení, ownership transfer, distributed clocks, region splitting, load balancing a fault recovery.


Priority 4
WORKER 13 — Multiplayer Networking & Replication

QUIC/UDP, authoritative networking, interest management, prediction, snapshots, congestion control a 10k connections.


Priority 5
WORKER 14 — Persistent Simulation & Recovery

Event sourcing, checkpointing, simulation replay, persistence consistency, crash recovery a dlouhodobá kontinuita světa.


16. Primary references and technical sources
Následující zdroje doporučuji zachovat jako základní technickou literaturu pro orchestrátora.
Oblast	Zdroj
Distributed simulation	IEEE HLA 1516-2025
ECS concurrency	Flecs systems and staging
Rust parallelism	Rayon documentation
SIMD	Rust std::simd
Network physics	Gaffer – State Synchronization
Snapshot replication	Gaffer – Snapshot Interpolation
Physics determinism	Rapier determinism
Fixed timestep	Rapier integration parameters
Server orchestration	Agones scheduling and autoscaling
Multiplayer architecture	Open 3D Engine Multiplayer


17. Final architectural recommendation
Worker 09 conclusion
Planetary Engine by měl používat hierarchický, regionálně distribuovaný simulační runtime s adaptivní fidelitou, autoritativním vlastnictvím a nezávislými simulačními hodinami.

Považuji za důležité prosadit následujících deset principů:
1. Jeden logický svět, mnoho fyzických simulačních uzlů.
2. Region ownership oddělený od ECS a fyzikálního rozdělení.
3. Adaptivní fidelity jako základní vlastnost každé simulovatelné entity.
4. Fixed timestep pro fyzikální subsystémy.
5. Hybridní paralelizace Rust runtime.
6. Autoritativní server s distribuovanou replikací.
7. Žádná globální synchronizace každého ticku.
8. Event journal a snapshot recovery.
9. Měřitelná fyzikální chyba při změně fidelity.
10. Benchmarky jako povinná podmínka architektonických rozhodnutí.
Nejdůležitější závěr pro celý Planetary Engine
Nesnažíme se simulovat každý atom světa se stejnou přesností. Snažíme se zachovat správné chování celého světa při dostupném výpočetním rozpočtu.

To je základní rozdíl mezi klasickým herním serverem a univerzální simulační platformou.

Zvlášť bych chtěl zdůraznit jednu věc pro další workery: LOD nesmí být pouze optimalizace vykreslování nebo početních detailů. Musí být součástí samotné fyzikální a simulační architektury.

Tím získáme možnost provozovat jeden svět, ve kterém se současně odehrává pěší gameplay, automobilová fyzika, letecká simulace, ekonomika měst, planetární procesy i astronomické děje – bez požadavku, aby každý z těchto systémů běžel na maximální přesnosti neustále.

Stav WORKER 09: Architektonický návrh dokončen. Implementace neprovedena.

Výkonnostní cíle 1–10 000+ hráčů zůstávají validačními cíli. Jejich dosažení musí potvrdit benchmarky na konkrétním hardwaru a reprezentativních simulačních scénářích.