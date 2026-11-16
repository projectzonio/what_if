PLANETARY ENGINE
WORKER 05 – AUTHORITATIVE SERVER RUNTIME

Deep research
 Architecture proposal
 Implementation prohibited

Téma: L2J-inspired MMO server architecture implemented in Rust and Node.js.

Cíl: Navrhnout serverovou infrastrukturu, která dokáže dlouhodobě provozovat jeden persistentní fyzikálně konzistentní svět, nikoliv pouze oddělené herní instance.

1. Executive Summary
1.1 Hlavní závěr
Planetary Engine by měl používat authoritative distributed simulation architecture with hierarchical world ownership.

Nejde o klasickou mikroservisní architekturu, kde každý typ entity nebo herní systém běží jako samostatná služba.

Doporučuji následující princip:
- Jeden globální světový identifikátor.
- Jeden logický světový stav.
- Prostorové rozdělení světa na regiony.
- Každý aktivní simulační region má právě jednoho autoritativního vlastníka.
- Vlastníkem je konkrétní Rust simulation process.
- Node.js koordinuje hráče, sessions, gameplay služby a přidělování regionů.
- Persistence je nezávislá na životním cyklu jednotlivých simulačních procesů.
- Unity dostává pouze replikovaný stav a posílá požadavky na změny.
Doporučený model autority
Global World Identity

Jedno univerzum · jedna logická časová osa · globální identita entit


Region A

Rust Process 01

Authoritative owner


Region B

Rust Process 02

Authoritative owner


Region C

Rust Process 03

Authoritative owner


Region N

Rust Process N

Authoritative owner




Node.js – World Control Plane

Ownership registry · Session management · Gameplay orchestration · Administration


Durable Persistence Layer

World snapshots · Entity state · Transactional records · Recovery data


Důležitá vlastnost: region není nutně proces. Jeden Rust proces může vlastnit více regionů a jeden region může být interně rozdělen do simulačních buněk.

Tím oddělíme logické prostorové rozdělení od fyzického rozdělení výpočetního výkonu.
1.2 Co si bereme z L2J
Z L2J přebíráme především:
- Centrální správu hráčů a jejich lifecycle.
- World object registry.
- Oddělení world manageru a jednotlivých herních subsystémů.
- Regionální prostorovou organizaci.
- Event-driven gameplay logiku.
- Centralizované rozhodování o autoritě herního stavu.
- Správu NPC, spawnů, questů a herních událostí.
Co nepřebíráme:
- Java-specific threading model.
- Historické limity paměťového modelu.
- Předpoklad, že svět je tvořen relativně statickými herními objekty.
- Předpoklad, že region je pouze oblast pro hledání okolních hráčů.
- Předpoklad, že veškeré herní objekty lze obsloužit stejnou frekvencí.
Planetary Engine potřebuje navíc fyzikální simulaci, pohybující se objekty, vozidla, atmosféru, astronomické objekty a případně kontinuální simulaci mimo přítomnost hráčů.

2. Detailed Technical Investigation
2.1 L2J – reverse engineering architektonických principů




















7










L2J je zajímavý především jako dlouhodobě rozvíjená implementace MMO serveru.

Jeho architektonická filozofie je vhodná pro pochopení těchto problémů:
Systém	Převzatý princip	Nutná změna
World	Centrální registr objektů	Distribuovaný globální registry
Player	Lifecycle hráče	Oddělení identity a fyzické entity
Region	Prostorové skupiny	Dynamické simulační partitioning
NPC	Server-authoritative AI	Samostatné simulační frekvence
Spawn	Správa existence objektů	Procedurální a persistentní spawn
Event	Událostní gameplay	Deterministické simulační události
Scheduler	Časované úlohy	Multirate simulation scheduler
Session	Připojení hráče	Oddělení session od world ownership

Klíčový rozdíl: L2J primárně spravuje herní svět. Planetary Engine musí spravovat fyzickou realitu, ve které se herní svět odehrává.
Doporučené rozdělení odpovědností
1. WorldManager – globální metadata a koordinace světa.
2. RegionManager – prostorová struktura a přidělování regionů.
3. EntityManager – registrace a životní cyklus entit.
4. PlayerManager – persistentní identita a herní profil.
5. SessionManager – síťová připojení.
6. EventManager – herní a simulační události.
7. SpawnManager – vznik a despawn entit.
8. SimulationScheduler – časování simulačních úloh.
Tyto názvy označují logické moduly, nikoliv nutně samostatné procesy.
2.2 Server authority – co přesně znamená
Server authority není pouze pravidlo, že server rozhoduje o tom, kolik má hráč HP.

Je to pravidlo vlastnictví stavu.

Příklad:

Hráč ovládá automobil. Unity odešle požadavek na akceleraci.
Unity:
    Input: throttle = 0.8
    Input: steering = 0.2

Node.js:
    Ověří session
    Předá příkaz příslušnému simulation owneru

Rust:
    Ověří oprávnění entity
    Zpracuje příkaz
    Provede fyzikální simulaci
    Aktualizuje stav automobilu
    Vytvoří nový authoritative snapshot

Node.js / Network Gateway:
    Distribuuje výsledek zainteresovaným klientům

Unity:
    Interpoluje a vykreslí stav


Klient nikdy neposílá autoritativní pozici automobilu.

Posílá vstupní příkazy.

Výjimkou mohou být prezentační a predikční informace, které server následně ověří.

Tento princip odpovídá server-authoritative modelu používanému například v Nakama. Jeho dokumentace výslovně popisuje serverové zpracování vstupů, pevný tick rate a držení autoritativního stavu na jednom hostitelském uzlu dané instance. 

Heroic Labs Documentation+1

2.3 Proč nedělat jeden globální simulační thread
Jedna globální smyčka je jednoduchá, ale pro Planetary Engine představuje významné omezení.

Představme si:
- 10 000 NPC.
- 2 000 vozidel.
- 500 hráčů.
- 100 000 dalších fyzikálních objektů.
- Velké množství statických objektů.
Kdyby každá entita vyžadovala aktualizaci 60krát za sekundu, dostáváme:
\[
112\,500 \times 60 = 6\,750\,000
\]

Aktualizací entit za sekundu.

To není automaticky nemožné, ale náklady na aktualizaci se dramaticky liší podle typu objektu.

Doporučuji multirate simulation:
Typ entity	Příklad cílové frekvence
Lokální hráč a interaktivní vozidlo	60 Hz
Aktivní NPC	10–30 Hz
Vzdálené NPC	1–5 Hz
Statické objekty	Bez pravidelné aktualizace
Ekonomické procesy	0,1–1 Hz
Vzdálené astronomické procesy	Podle modelu a požadované přesnosti

Jde o návrhové výchozí hodnoty pro testování, nikoliv naměřené benchmarky.

To umožní výrazně snížit množství práce bez ztráty vnímané kontinuity světa.
2.4 Rust simulation runtime







blog.csdn.net


Pro doporučenou technologii je zásadní Tokio.

Tokio poskytuje:
- Multithreaded runtime.
- Work-stealing scheduler.
- Asynchronous TCP a UDP.
- Timers.
- Cancellation.
- Asynchronous task management.
Jeho multithreaded scheduler standardně používá worker thread pro každé dostupné CPU jádro. Důležité je ale rozlišovat asynchronní I/O scheduler od fyzikálního scheduleru. Tokio samo o sobě nezaručuje deterministické pořadí simulačních operací. 

Rust+1

Doporučená interní struktura Rust procesu
Simulation Process
│
├── Network Runtime
│   ├── TCP / QUIC
│   ├── Incoming Commands
│   └── Outgoing Snapshots
│
├── World Runtime
│   ├── World Registry
│   ├── Region Ownership
│   └── Entity Registry
│
├── Simulation Runtime
│   ├── Fixed Tick Scheduler
│   ├── Physics Systems
│   ├── AI Systems
│   └── Gameplay Systems
│
├── Spatial Runtime
│   ├── Spatial Index
│   ├── AOI
│   ├── Collision Queries
│   └── Region Boundaries
│
├── Replication Runtime
│   ├── Snapshot Builder
│   ├── Delta Compression
│   └── Interest Filtering
│
└── Persistence Runtime
    ├── Snapshot Writer
    ├── Event Journal
    └── Recovery


Doporučení: oddělit I/O a simulation workers
Fyzikální simulace nesmí být závislá na tom, zda Tokio právě zpracovává síťové požadavky.

Navrhuji:
- Tokio pro síťové I/O a asynchronní komunikaci.
- Vyhrazené simulation workers pro fyzikální tick.
- Explicitní fronty příkazů mezi síťovou a simulační vrstvou.
- Omezené fronty s backpressure.
- Žádné blokující databázové operace v simulation ticku.
To je důležité zejména při zatížení serveru. Pomalý klient nesmí zpomalit simulační smyčku celého regionu.
2.5 Spatial partitioning – rozdělení planety
Toto je jeden z nejdůležitějších problémů celé architektury.

Klasické MMO pracuje s relativně malým herním světem. Planetary Engine musí pracovat s celou planetou a později s astronomickým prostředím.

Nemůžeme proto použít jedinou prostorovou reprezentaci pro všechny účely.
Alternativy









A. Quadtree

Rekurzivní dělení prostoru na čtyři části.
- Výhoda: adaptivní prostorové rozdělení.
- Nevýhoda: komplikace na sférickém povrchu.
- Vhodnost: lokální planetární a herní oblasti.












B. H3

Hexagonální hierarchická globální prostorová síť.
- Výhoda: globální indexování a sousedství.
- Nevýhoda: hierarchická buňka není vždy přesně geometricky obsažena v rodičovské buňce.
- Vhodnost: globální index, regionální metadata, AOI kandidáti.












C. S2 Geometry

Hierarchická kvadrilaterální síť na sféře.
- Výhoda: sférická geometrie a hierarchie.
- Nevýhoda: není přímo fyzikálním simulačním modelem.
- Vhodnost: globální planetární partitioning a geografické dotazy.



Primární technické zdroje:
- Uber H3 – Technical overview.
- S2 Geometry – Cell hierarchy.
H3 používá hierarchickou hexagonální síť vytvořenou projekcí přes icosahedron. S2 používá šest stěn krychle projektovaných na jednotkovou sféru a rekurzivně je rozděluje na čtyři části. 

GitHub+1

Doporučené řešení: hybridní prostorová hierarchie
Doporučená varianta
Nepoužíval bych H3 jako samotný simulační prostor.

Navrhuji:
Úroveň	Reprezentace	Účel
Universe	Hierarchický astronomický graf	Galaxie, hvězdy, systémy
Planet	Sférická geometrie	Planeta
Global index	S2 nebo H3	Globální vyhledávání
Simulation region	Hierarchický prostorový partitioning	Vlastnictví
Local physics	Lokální Cartesian coordinates	Fyzika
AOI	Spatial hash / BVH / grid	Okolní entity

Důvod: globální planetární souřadnice a lokální fyzikální souřadnice mají rozdílné numerické požadavky.

Při simulaci lokálního vozidla nechceme počítat kolize v souřadnicích vzdálených miliony kilometrů od počátku systému.

Použijeme lokální referenční soustavu, jejíž transformace je definována vůči globální planetární soustavě.
AOI – Area of Interest
AOI určuje, které entity musí klient znát.

Například:
- Hráč vidí ostatní hráče ve svém okolí.
- Řidič vidí okolní vozidla.
- Letadlo vidí objekty ve větším rozsahu.
- Astronomická observatoř vidí objekty podle jiného prostorového a informačního modelu.
AOI nesmí být pouze kruh s pevným poloměrem.

Doporučuji kombinovat:
1. Geometrickou vzdálenost.
2. Viditelnost.
3. Typ entity.
4. Rychlost a předpokládanou trajektorii.
5. Herní relevanci.
6. Specifická pravidla daného simulačního subsystému.
Například letadlo může potřebovat znát jiný rozsah objektů než hráč na ulici.
Výpočetní složitost
Naivní kontrola všech dvojic entit:
\[
O(N^2)
\]

Při 100 000 entitách je to přibližně 10 miliard dvojic.

Prostorové indexování umožňuje omezit hledání na relevantní sousedy.

Spatial hash má při vhodném rozložení dat průměrnou praktickou složitost blízkou:
\[
O(N + K)
\]

kde \(K\) je počet nalezených kandidátů.

Nejde o univerzální garanci. Při extrémním shlukování může výkon výrazně degradovat.

Doporučení: použít více prostorových indexů podle účelu, nikoliv jeden univerzální index pro všechny operace.
2.6 Region ownership a migrace
Tady vzniká největší rozdíl mezi jednoduchým MMO a distribuovaným fyzikálním univerzem.

Představme si automobil, který jede přes hranici dvou regionů.

Automobil musí mít vždy jednoho autoritativního vlastníka.

Nesmí nastat situace, kdy dva Rust procesy současně simulují stejný automobil.
Doporučený ownership model
Každý region bude mít:
region_id
owner_process_id
owner_epoch
lease_expiration
simulation_tick
region_state_version


owner_epoch je monotónně rostoucí fencing token.

Příklad:
Stav	Proces	Epoch
Region A	Rust 01	15
Předání	Rust 01 → Rust 02	16
Nový vlastník	Rust 02	16

Pokud starý proces po migraci pokračuje v činnosti, jeho požadavky s epochou 15 musí být odmítnuty.

To chrání před situací známou jako split-brain.
Doporučený migrační protokol
1. Prepare
   
   Nový proces připraví cílový region a jeho simulační prostředí.
2. Freeze boundary transfer
   
   Starý proces vytvoří konzistentní migrační bod a připraví předání.
3. Serialize
   
   Přenesou se entity, jejich stav, potřebné interní reference a časová metadata.
4. Ownership commit
   
   Koordinátor atomicky změní vlastníka a zvýší owner_epoch.
5. Activate
   
   Nový proces pokračuje v simulaci.
6. Redirect
   
   Síťová vrstva začne směrovat příkazy a aktualizace na nového vlastníka.
7. Cleanup
   
   Starý proces odstraní již nepoužívaný lokální stav.
Důležité: migrace musí mít definovanou obnovu při pádu v každém jednotlivém kroku.

Nelze spoléhat na to, že síťová komunikace mezi procesy proběhne právě jednou.

Použijeme idempotentní příkazy, epochy, potvrzování a trvalý záznam o převodu vlastnictví.
Cross-region interakce
Co když automobil přejede hranici, ale jeho fyzikální interakce zahrnuje objekt na sousedním regionu?

Navrhuji tři režimy:
- Read-only ghost entities: sousední proces má kopii relevantního stavu pro výpočty.
- Cross-region command: změna stavu se předává skutečnému vlastníkovi.
- Ownership migration: objekt se převede pod jiného vlastníka.
Ghost entity nikdy nesmí být autoritativní.

U složitých fyzikálních interakcí přes hranici regionu musí být navíc definováno pořadí zpracování. Jinak může vzniknout rozdílný výsledek simulace podle toho, který proces zpracuje požadavek první.

Pro první verzi bych se vyhnul libovolnému distribuovanému řešení fyzikálních constraintů. Preferoval bych dostatečně velké simulační regiony, lokální výpočet a explicitní hranice odpovědnosti.

3. Architecture Proposal
3.1 Celková topologie procesů
Planetary Engine server topology
Node.js Control Plane

API · Auth · Sessions · Gameplay · Region Registry


Network Gateway

Connection termination · Protocol routing · Replication delivery


Rust Simulation A

Authoritative regions


Rust Simulation B

Authoritative regions


Rust Simulation C

Authoritative regions


Rust Simulation N

Authoritative regions




Persistence and Durable Storage

Database · Snapshot storage · Event journal


Network Gateway může být součástí Node.js control plane nebo samostatný proces. Doporučuji jej logicky oddělit už v návrhu, ale nevyžadovat jeho samostatné nasazení v první verzi.
3.2 Procesní rozdělení podle technologie
Technologie	Autoritativní odpovědnost
Rust	Simulační stav, fyzika, entity, region ownership
Node.js	Identity, sessions, API, gameplay orchestrace
Python	Offline výpočty, analýzy, generování dat
Unity	Rendering, input, lokální predikce

Rust
Rust musí být jediným místem, kde se mění autoritativní fyzikální stav.

Důležité: Node.js nesmí přímo upravovat interní stav fyzikální entity.

Pokud gameplay služba potřebuje změnit stav objektu, pošle příkaz Rust procesu.
Node.js
Node.js nemá být druhý fyzikální engine.

Má být zodpovědný za:
- Authentication.
- Player identity.
- Session lifecycle.
- Permission checks.
- Inventory a ekonomické transakce.
- Quest orchestration.
- Gameplay services.
- Region directory.
- Admin API.
- Matchmaking a server allocation.
- Koordinaci persistentních operací.
Node.js oficiálně doporučuje nezatěžovat Event Loop dlouhými CPU operacemi. Worker threads jsou vhodné pro CPU-intensive JavaScript, nikoliv jako náhrada Rust simulation runtime. 

Node.js Learn

Python
Python nesmí být synchronní závislostí simulačního ticku.

Je vhodný pro:
- Offline ekonomické simulace.
- Procedurální generování světa.
- Astronomické výpočty.
- Analýzu simulačních dat.
- Trénování modelů.
- Batch processing.
- Testovací prostředí.
3.3 Komunikační protokoly
Navrhuji oddělit tři typy komunikace:
Komunikace	Protokol	Důvod
Unity → Gateway	QUIC	Moderní zabezpečený transport
Gateway → Rust	Interní QUIC nebo TCP	Stabilní komunikace
Node.js → Rust control	gRPC / Protobuf přes HTTP/2	Typované RPC
Rust → Persistence	Asynchronní interní rozhraní	Oddělení simulačního runtime
Python → Services	HTTP / gRPC	Batch a vědecké nástroje

Tato tabulka představuje návrh, nikoliv požadavek použít všechny protokoly současně.

4. Mathematical and Computational Models
4.1 Simulation tick
Doporučuji fixed timestep pro autoritativní simulační subsystémy.
\[
\Delta t = \frac{1}{f}
\]

Při 60 Hz:
\[
\Delta t = 0.0166667\,s
\]

Základní aktualizace:
\[
S_{t+1}=F(S_t,I_t,\Delta t)
\]

Kde:
- \(S_t\) je stav světa.
- \(I_t\) jsou vstupní příkazy.
- \(F\) je simulační funkce.
- \(\Delta t\) je simulační krok.
Fixed timestep pomáhá stabilitě fyziky, ale automaticky nezaručuje determinismus.

Ten vyžaduje také kontrolu pořadí operací, náhodných generátorů, numeriky a externích vstupů.
4.2 Multirate simulation
Různé entity mohou mít odlišné aktualizační frekvence.

Při plánování je nutné rozlišit:
- Simulační frekvenci.
- Frekvenci replikace.
- Frekvenci persistence.
- Frekvenci AI rozhodování.
Například automobil může být fyzikálně aktualizován na 60 Hz, ale vzdálenému klientovi se jeho stav může odesílat na 10 Hz.

To neznamená, že fyzika automobilu běží na 10 Hz.
4.3 Network replication
Základní snapshot může obsahovat:
entity_id
entity_type
simulation_tick
position
rotation
linear_velocity
angular_velocity
state_version


Pro každý klient se vytvoří výběr entit podle AOI.

Při změně stavu se odešle delta vůči předchozímu známému snapshotu.
\[
D_t=S_t-S_{t-1}
\]

V praxi nejde o prosté odčítání všech polí. Použije se bitové maskování změn, kvantizace, komprese a specifické kódování jednotlivých typů dat.
Snapshot interpolation
Pro ostatní entity doporučuji snapshot interpolation.

Klient přijímá stavové snapshoty a vykresluje interpolovaný stav mezi dvěma časovými body.

Výhody:
- Není nutná identická fyzikální simulace klienta.
- Odolnost proti jitteru.
- Menší závislost na determinismu mezi platformami.
Nevýhody:
- Interpolační zpoždění.
- Vyšší nároky na bandwidth.
- Nutnost řešit ztracené a opožděné pakety.
Glenn Fiedler podrobně rozebírá snapshot interpolation a kompresi síťových fyzikálních stavů. 

Gaffer On Games+1

Client prediction
Pro lokálně ovládaného hráče:
1. Klient okamžitě aplikuje vstup.
2. Odešle vstup serveru.
3. Server zpracuje vstup autoritativně.
4. Server vrátí potvrzený tick.
5. Klient porovná svůj predikovaný stav.
6. Případně provede reconciliation.
Predikce je pouze prezentační a ovládací optimalizace. Nikdy nesmí změnit serverovou autoritu.
4.4 Výpočet síťové zátěže
Pro přibližný odhad lze použít:
\[
B=P \times E \times R \times S
\]

Kde:
- \(P\) = počet klientů.
- \(E\) = počet replikovaných entit na klienta.
- \(R\) = počet aktualizací za sekundu.
- \(S\) = velikost aktualizace v bytech.
Ilustrační scénář:
- 1 000 hráčů.
- 100 viditelných entit na hráče.
- 10 aktualizací za sekundu.
- 24 byte na jednu komprimovanou aktualizaci.
\[
B=1000\times100\times10\times24
\]

\[
B=24\,000\,000\,B/s
\]

Ilustrační model
To odpovídá přibližně 192 Mbit/s aplikačních dat při předpokladu desítkové jednotky MB/s. Nezahrnuje hlavičky, šifrování, retransmise, transportní režii ani nerovnoměrné rozložení klientů.

Právě proto je AOI a prioritizace replikace zásadní.
4.5 Regionální výpočetní budget
Pro 60Hz simulační region:
\[
T_{budget}=16.67\,ms
\]

V tomto čase musí proběhnout:
- Přijetí příkazů.
- Entity update.
- Physics.
- AI.
- Cross-region synchronizace.
- Stavové změny.
- Příprava výsledku.
Nedoporučuji plánovat využití 100 % dostupného tick budgetu. Rezerva je potřebná pro špičky, shlukování entit a nečekané interakce.

Doporučený počáteční cíl je 70–80 % p95 tick budgetu, tedy přibližně 11,7–13,3 ms. Jde o návrhový limit, který musí potvrdit benchmarky.

5. Networking Deep Dive
5.1 TCP versus UDP versus QUIC
Vlastnost	TCP	UDP	QUIC
Reliable delivery	Ano	Ne	Podle typu dat
Ordered delivery	Ano	Ne	Per stream
Multiplexing	Omezený	V aplikaci	Nativní
Encryption	Samostatně	Samostatně	TLS 1.3
Head-of-line blocking	Na spojení	Ne	Omezeno na jednotlivé streamy
Datagramy bez retransmise	Ne	Ano	RFC 9221
Connection migration	Ne	Aplikačně	Podporováno

QUIC je zvláště zajímavý kvůli kombinaci spolehlivých streamů a nezávislých datagramů.

RFC 9000 definuje QUIC jako multiplexovaný zabezpečený transport nad UDP. RFC 9221 přidává unreliable datagrams bez transportních retransmisí. 

rfc-editor.org+1

Doporučení
Preferovaná varianta
QUIC jako hlavní klientský transport.

Rozdělení:
Data	Transport
Authentication	Reliable stream
Player commands	Reliable nebo datagram podle semantiky
Inventory transactions	Reliable
Entity snapshots	Unreliable datagrams
Position updates	Unreliable datagrams
Chat	Reliable
World administration	Reliable
Session lifecycle	Reliable

Pozor na důležitou věc: ne každý input je vhodný pro unreliable transport.

U průběžného vstupu, například držení plynu, lze posílat aktuální stav vstupu a sekvenční číslo. U jednorázového příkazu, například použití předmětu, je nutná spolehlivá a idempotentní semantika.
5.2 Lag compensation
Pro bojové a rychlé interakce bude nutné implementovat server-side lag compensation.

Základní princip:
- Server uchovává krátkou historii stavů.
- Přijde požadavek hráče s časovým a sekvenčním kontextem.
- Server vyhodnotí, jaký stav byl relevantní v okamžiku interakce.
- Výsledek ověří podle serverových pravidel.
U Planetary Engine je ale nutné rozlišovat bojovou lag compensation od obecné fyzikální reality.

Nemůžeme vracet celý svět v čase při každém opožděném vstupu.

Lag compensation musí být specifická pro danou interakci a omezená časovým oknem.
5.3 Backpressure
Každá komunikační fronta musí mít definovaný limit.

Při přetížení:
- Staré nepotřebné snapshoty se zahodí.
- Novější snapshot nahradí starší stav.
- Kritické příkazy se nezahazují bez potvrzeného výsledku.
- Klienti s extrémně vysokou latencí mohou dostat nižší replikační prioritu.
- Simulation tick nesmí čekat na síťového klienta.
To je jeden z nejdůležitějších produkčních požadavků.

6. Persistence and Recovery
6.1 Problém
Server nesmí při restartu ztratit:
- Postavení hráčů.
- Vlastnictví objektů.
- Stav vozidel.
- Ekonomiku.
- Konstrukce vytvořené hráči.
- NPC a persistentní entity.
- Historii důležitých světových událostí.
Zároveň není možné zapisovat kompletní stav všech entit do databáze při každém 60Hz ticku.

Při 100 000 aktivních entitách by to znamenalo šest milionů aktualizací entit za sekundu.

To je důvod pro oddělení simulační paměti od durable persistence.
6.2 Doporučená hybridní strategie
Persistence pipeline
Authoritative Simulation State


Event Journal

Důležité změny a transakce


Snapshots

Konzistentní stav v čase




Recovery

Načtení snapshotu + replay událostí


A. Event journal
Ukládá významné změny:
event_id
world_id
region_id
entity_id
owner_epoch
simulation_tick
event_type
payload
created_at


Příklady:
- Player entered vehicle.
- Vehicle ownership transferred.
- Building constructed.
- Item consumed.
- Player inventory modified.
- NPC spawned.
- Region ownership changed.
Ne každý pohybový tick musí být uložen jako event.
B. Snapshot
Snapshot obsahuje kompletní obnovitelný stav daného regionu k určitému okamžiku.
snapshot_id
world_id
region_id
simulation_tick
region_epoch
entity_count
schema_version
payload_location
checksum
created_at


C. Recovery
Při restartu:
1. Načíst poslední platný snapshot.
2. Ověřit integritu.
3. Načíst eventy od snapshot ticku.
4. Ověřit jejich pořadí a identitu.
5. Provést replay.
6. Obnovit ownership.
7. Znovu otevřít region pro příkazy.
6.3 Databázová technologie
Doporučuji nerozhodovat nyní o jediné databázi pro všechny účely.

Potřebujeme oddělit:
Data	Charakter
Player accounts	Transakční
Inventory	Transakční
Economy	Transakční
World entities	Velkoobjemová stavová data
Event journal	Sekvenční zápis
Snapshots	Velké binární objekty
Telemetry	Analytická data

Jako referenční databázový systém doporučuji prozkoumat PostgreSQL.

Jeho Write-Ahead Logging poskytuje crash recovery pomocí replay logů a podporuje také point-in-time recovery z archivovaných WAL segmentů. 

PostgreSQL

To je vhodný základ pro transakční část systému.

Neznamená to ale, že PostgreSQL musí být současně úložištěm každého simulačního snapshotu.
Kritické pravidlo: transakční konzistence
Příklad:

Hráč prodá vozidlo.

Musí se atomicky změnit:
- Vlastnictví vozidla.
- Inventář kupujícího.
- Peníze prodávajícího.
- Peníze kupujícího.
- Transakční historie.
Nelze to řešit několika nezávislými síťovými příkazy.

Doporučuji transakční gameplay službu s idempotency key a jednoznačným potvrzením výsledku.

Fyzikální stav vozidla se následně aktualizuje prostřednictvím autoritativního příkazu do Rust runtime.

Tím se oddělí finanční konzistence od simulační konzistence.
6.4 Recovery po pádu procesu
Zde doporučuji tři úrovně:
Typ selhání	Obnova
Pád Node.js	Rust procesy pokračují
Pád Rust regionu	Snapshot + event replay
Pád celého server clusteru	Globální recovery a ownership reconciliation

Node.js control plane nesmí být single point of failure pro pokračování již běžící fyzikální simulace.

Pro koordinaci ownershipu však musí existovat nezávislý, konzistentní mechanismus, který zabrání dvěma procesům získat současně autoritu.

7. Scalability Model
7.1 Varianta A – Single process
Jeden Rust proces vlastní celý aktivní svět.
Doporučeno pro první funkční server
Výhody:
- Nejjednodušší konzistence.
- Žádná cross-process fyzika.
- Snadný debugging.
- Jednoduché ownership.
- Nižší komunikační režie.
Nevýhody:
- Omezený výpočetní výkon jednoho stroje.
- Jediný bod selhání simulace.
- Omezená horizontální škálovatelnost.
7.2 Varianta B – Multi-threaded single process
Jeden proces vlastní více simulačních regionů a rozděluje práci mezi CPU cores.

Výhody:
- Sdílená paměť.
- Rychlé předávání dat.
- Menší síťová režie.
- Jednodušší správa vlastnictví.
Nevýhody:
- Složitější synchronizace.
- Riziko contention.
- Nezávislé regiony se mohou vzájemně blokovat.
- Omezená odolnost proti pádu procesu.
To je podle mě nejvhodnější první škálovací krok.
7.3 Varianta C – Multiple Rust processes
Každý proces vlastní skupinu regionů.

Výhody:
- Horizontální škálování.
- Izolace problémů.
- Větší výpočetní kapacita.
- Možnost přesouvat zatížené oblasti.
Nevýhody:
- Cross-region komunikace.
- Ownership migration.
- Distributed persistence.
- Složitější monitoring a recovery.
7.4 Varianta D – Distributed world
Celé univerzum je rozděleno mezi více serverových uzlů a případně geograficky distribuované datacentra.

To je dlouhodobý cíl.

Důležité je, že distribuovaný svět neznamená, že všechny regiony musí simulovat stejnou rychlostí nebo být dostupné na stejném fyzickém serveru.

Každý region může mít vlastní výpočetní budget a simulační frekvenci.
Doporučená evoluce
1. Stage 1 – Single Rust process
   
   Kompletní autoritativní server, více regionů v jednom procesu.
2. Stage 2 – Multithreaded simulation
   
   Regionální paralelizace a optimalizace CPU.
3. Stage 3 – Multiple processes
   
   Explicitní region ownership a migrace.
4. Stage 4 – Distributed world
   
   Více serverových uzlů, dynamické přidělování výpočetních zdrojů.
5. Stage 5 – Geographic distribution
   
   Globální infrastruktura, regionální datacentra a optimalizace latence.
Tento vývoj minimalizuje riziko, že budeme muset předělávat základní simulační model při každém kroku.
7.5 Orchestrace serverů
Jako referenční systém doporučuji prozkoumat Agones.

Agones řeší:
- Game server lifecycle.
- Allocation.
- Fleets.
- Scheduling.
- Autoscaling.
Podporuje strategii Packed pro koncentrování serverů na menší počet uzlů a Distributed pro rozprostření serverů mezi uzly. 

Agones+1

Pro Planetary Engine bych ale nepoužíval model, kdy každá regionální buňka automaticky znamená jeden GameServer.

Agones může později pomoci s orchestrace procesů, ale samotné přidělování simulační autority musí zůstat součástí našeho world ownership systému.

8. Proposed Module Structure
Následující struktura je návrh logického rozdělení. Nejde o závaznou strukturu Rust workspace ani o implementační plán jednotlivých souborů.
8.1 Rust – Simulation Runtime
simulation_runtime/
│
├── core/
│   ├── world/
│   ├── region/
│   ├── entity/
│   ├── identity/
│   └── simulation_clock/
│
├── simulation/
│   ├── scheduler/
│   ├── physics/
│   ├── ai/
│   ├── gameplay/
│   └── event_system/
│
├── spatial/
│   ├── global_index/
│   ├── local_coordinates/
│   ├── spatial_hash/
│   ├── aoi/
│   └── collision_queries/
│
├── ownership/
│   ├── registry/
│   ├── lease/
│   ├── migration/
│   └── fencing/
│
├── networking/
│   ├── transport/
│   ├── commands/
│   ├── replication/
│   ├── snapshots/
│   └── compression/
│
├── persistence/
│   ├── event_journal/
│   ├── snapshots/
│   ├── recovery/
│   └── checkpoint/
│
└── observability/
    ├── metrics/
    ├── tracing/
    └── diagnostics/


8.2 Node.js – Application Server
application_server/
│
├── api/
├── authentication/
├── authorization/
├── sessions/
├── players/
├── world/
│   ├── world_registry/
│   ├── region_directory/
│   ├── ownership_coordinator/
│   └── world_lifecycle/
│
├── gameplay/
│   ├── inventory/
│   ├── economy/
│   ├── quests/
│   ├── crafting/
│   ├── trading/
│   └── social/
│
├── networking/
│   ├── gateway/
│   ├── routing/
│   └── protocol/
│
├── persistence/
├── administration/
└── monitoring/


Důležité architektonické pravidlo
application_server/gameplay/inventory může vlastnit inventářovou transakci.

Nemůže ale přímo změnit fyzikální pozici objektu v Rust procesu.

Každá změna autoritativního simulačního stavu musí projít definovaným rozhraním.
8.3 Python – Scientific Tooling
scientific_tooling/
│
├── world_analysis/
├── astronomical_models/
├── simulation_analysis/
├── procedural_generation/
├── economy_models/
├── data_processing/
└── validation/


Python zůstává mimo vysokofrekvenční simulační cestu.

9. API and Data Contracts
Tato část je kritická. Procesy musí mít stabilní kontrakty ještě před implementací.

Doporučuji Protobuf jako první kandidát na společný datový formát pro interní komunikaci.
9.1 Globální identita entity
Každá entita musí mít identitu nezávislou na procesu, který ji právě simuluje.

Navrhovaný kontrakt:
EntityIdentity {
    world_id: UUID
    entity_id: UUID
    entity_type: uint32
    creation_version: uint64
}


Regionální umístění není součástí trvalé identity.

Entita může změnit region, ale její globální identita zůstává stejná.
9.2 Region ownership
RegionOwnership {
    world_id: UUID
    region_id: UUID

    owner_process_id: UUID
    owner_epoch: uint64

    lease_expiration: timestamp

    state_version: uint64
    simulation_tick: uint64
}


owner_epoch musí být kontrolován při každé autoritativní operaci, která mění stav regionu.
9.3 Player command
PlayerCommand {
    command_id: UUID
    player_id: UUID
    entity_id: UUID

    session_id: UUID

    sequence_number: uint64
    client_timestamp: timestamp

    target_region_id: UUID
    expected_owner_epoch: uint64

    command_type: uint32
    payload: bytes
}


Důležité:
- command_id umožňuje idempotentní zpracování.
- sequence_number pomáhá řešit pořadí vstupů.
- expected_owner_epoch zabraňuje příkazům určeným starému vlastníkovi.
- client_timestamp není autoritativní čas.
- payload musí podléhat validaci podle typu příkazu.
9.4 Simulation snapshot
EntitySnapshot {
    world_id: UUID
    region_id: UUID
    entity_id: UUID

    owner_epoch: uint64
    simulation_tick: uint64

    state_version: uint64

    position: Vector3
    rotation: Quaternion

    linear_velocity: Vector3
    angular_velocity: Vector3

    component_mask: uint64
    component_data: bytes
}


Snapshot musí podporovat evoluci schématu.

Přidání nového typu entity nesmí vyžadovat změnu základního síťového protokolu pro všechny ostatní entity.
9.5 Region migration
RegionMigration {
    migration_id: UUID

    world_id: UUID
    region_id: UUID

    source_process_id: UUID
    target_process_id: UUID

    previous_epoch: uint64
    next_epoch: uint64

    checkpoint_tick: uint64
    checkpoint_hash: bytes

    migration_state: enum
}


Stavy:
PREPARING
FROZEN
TRANSFERRED
COMMITTED
ACTIVATED
COMPLETED
ABORTED


Je potřeba přesně definovat přechody mezi stavy a chování při restartu.
9.6 Interní API
Volání	Směr	Účel
CreateWorld	Node → Rust	Inicializace světa
RegisterRegion	Rust → Node	Registrace regionu
AssignRegion	Node → Rust	Přidělení vlastnictví
SubmitCommand	Gateway → Rust	Vstupní příkaz
GetRegionState	Node → Rust	Diagnostika
BeginMigration	Node → Rust	Zahájení migrace
CommitMigration	Node → Rust	Potvrzení převodu
SaveCheckpoint	Rust → Persistence	Uložení stavu
RestoreRegion	Persistence → Rust	Obnova regionu
PublishSnapshot	Rust → Gateway	Distribuce stavu

Protokol musí obsahovat také timeouty, retry pravidla, chybové kódy a verzi schématu.

10. Integration Dependencies
Serverová architektura není izolovaný subsystém.

Závisí na dalších částech Planetary Engine.
Subsystem	Závislost na serveru	Požadovaný kontrakt
Universe simulation	Astronomické entity	Global identity
Planetary system	Planety a jejich souřadnice	Spatial reference
Physics	Authoritative state	Simulation tick
Entity physics	Vozidla a objekty	Entity components
AI	NPC a autonomní systémy	Scheduler interface
Networking	Přenos stavu	Snapshot protocol
Persistence	Trvalý stav	Snapshot/event schema
Economy	Transakční svět	Gameplay commands
Unity client	Replikace	Client protocol
World generation	Inicializace regionů	Region descriptors

Zásadní závislost: Worker 03
Entity Physics Systems musí definovat, jaké části fyzikálního stavu jsou:
- Autoritativní.
- Přenositelné mezi procesy.
- Persistované.
- Znovu inicializovatelné.
- Predikovatelné klientem.
Bez toho nelze dokončit region migration.
Zásadní závislost: Worker 04
Celestial & Universe Simulation musí definovat:
- Globální souřadnicový systém.
- Hierarchii astronomických objektů.
- Referenční rámce.
- Časovou reprezentaci.
- Frekvenci simulace jednotlivých astronomických měřítek.
Serverová architektura nesmí předpokládat, že všechny entity mají souřadnice v jediném lokálním Cartesian prostoru.

11. Performance Considerations
11.1 Výkonové metriky
Počet připojených hráčů je pouze jedna z metrik.

Doporučuji sledovat:
Metrika	Význam
Concurrent sessions	Počet připojených klientů
Active entities	Aktivní entity
Simulation ticks/sec	Rychlost simulace
Tick p50/p95/p99	Doba simulačního kroku
AOI candidates	Počet kandidátů AOI
AOI visible entities	Skutečně replikované entity
Network egress	Odchozí síťová zátěž
Snapshot size	Velikost snapshotů
Command queue depth	Fronta příkazů
Migration duration	Doba migrace
Persistence lag	Zpoždění ukládání
Recovery time	Doba obnovy
CPU per region	Výpočetní náklady regionu
Memory per entity	Paměťové náklady entity

11.2 Paměťové odhady
Pro 100 000 aktivních entit:
Průměrná paměť na entitu	Celková paměť
256 B	24,4 MiB
512 B	48,8 MiB
1 KiB	97,7 MiB
4 KiB	390,6 MiB
16 KiB	1,53 GiB

Toto je pouze základní výpočet velikosti datových struktur.

Nezahrnuje:
- Allocator overhead.
- Spatial index.
- Entity references.
- Physics broadphase.
- AI state.
- Replication buffers.
- Snapshot buffers.
- Network buffers.
- Temporary allocations.
Praktická spotřeba může být násobně vyšší.

Doporučuji měřit skutečnou paměťovou cenu podle kategorií entit, nikoliv používat jedinou konstantu.
11.3 CPU profil
CPU náklady budou tvořit zejména:
1. Physics.
2. Collision detection.
3. AI.
4. Spatial queries.
5. Replication.
6. Serialization.
7. Cross-region synchronization.
Doporučuji měřit jednotlivé subsystémy odděleně.

Nejdůležitější není maximální throughput v laboratorním testu, ale stabilní p99 při nerovnoměrném zatížení.
11.4 Kapacita 1 000 hráčů
Nelze nyní poctivě slíbit, že jeden server zvládne 1 000 hráčů.

Kapacita závisí na:
- Počtu fyzikálních entit v okolí.
- Počtu interakcí.
- AOI.
- Frekvenci replikace.
- Síťové topologii.
- Složitosti fyziky.
- AI.
- Rozložení hráčů ve světě.
1000 hráčů rovnoměrně rozprostřených po planetě může být výrazně jednodušší scénář než 1000 hráčů v jediném městě.

Pro škálovatelnost proto musíme testovat prostorové shlukování, ne pouze celkový počet klientů.

12. Testing and Validation Plan
Testovací plán musí ověřit architekturu dříve, než začneme implementovat kompletní herní systémy.
12.1 Ownership tests
- Dva procesy se pokusí získat jeden region.
- Starý proces pokračuje po vypršení lease.
- Proces spadne během migrace.
- Nový proces spadne po převzetí ownership.
- Příkazy dorazí na starého vlastníka.
- Zpožděné zprávy dorazí po změně epochy.
Požadovaný výsledek
V žádném okamžiku nesmí existovat dva platní autoritativní vlastníci stejného regionu.
12.2 Simulation tests
Test	Ověřovaná vlastnost
Fixed timestep	Stabilní simulační interval
Multirate	Správné frekvence
Entity lifecycle	Vznik a zánik
Region boundary	Kontinuita pohybu
Cross-region collision	Konzistence interakcí
Long-running test	Paměťové a časové úniky
Replay test	Reprodukovatelnost

12.3 Networking tests
Testovat:
- 0–5 % packet loss.
- 1–5 % packet loss.
- Variable latency.
- Jitter.
- Out-of-order delivery.
- Burst traffic.
- Client reconnect.
- Server reconnect.
- Přetížení front.
Měřit latenci od vstupu hráče až po autoritativní potvrzení.
12.4 Persistence tests
- Pád při zápisu eventu.
- Pád během snapshotu.
- Pád po vytvoření snapshotu.
- Obnova ze staršího snapshotu.
- Duplicitní event.
- Chybějící event.
- Poškozený snapshot.
- Změna verze schématu.
Ověřit, že žádná potvrzená ekonomická transakce není ztracena nebo provedena dvakrát.
12.5 Load testing
Navrhuji následující testovací stupnici:
Scénář	Klienti	Entity
Baseline	10	1 000
Small MMO	50	10 000
Medium MMO	250	25 000
Large MMO	1 000	100 000
Stress	2 000	250 000
Extreme	5 000	500 000

Jde o navržené testovací body, nikoliv o dosaženou kapacitu nebo odhad garantovaného výkonu.

Každý scénář musí mít dvě varianty:
- Rovnoměrně rozprostřené entity.
- Extrémní prostorové shlukování.
Dále samostatně testovat:
- 1000 klientů s nízkou aktivitou.
- 1000 klientů s vysokou aktivitou.
- 1000 klientů v jednom regionu.
- 1000 klientů rozdělených mezi více regionů.
12.6 Deterministic replay
Doporučuji vybudovat samostatný testovací nástroj:
Recorded Input Stream
        |
        v
Simulation Replay
        |
        v
State Checkpoints
        |
        v
State Comparison


Výsledkem bude možnost reprodukovat problematické události ze skutečného serverového provozu.

To bude mimořádně důležité při hledání chyb ve fyzice, region migration a cross-region interakcích.

13. Risk Register
Riziko	Závažnost	Doporučené řešení
Split-brain ownership	Kritická	Epoch fencing, leases
Cross-region physics	Kritická	Lokální autorita, ghost entities
Persistence lag	Vysoká	Journal + checkpoints
Hotspot region	Vysoká	Dynamické partitioning
AOI explosion	Vysoká	Hierarchické indexy
Network congestion	Vysoká	Backpressure, prioritizace
Node.js bottleneck	Střední	Oddělení control plane
Tick overruns	Vysoká	Multirate, profiling
Schema evolution	Vysoká	Verze protokolů
Migration failure	Kritická	Idempotentní stavový automat
Recovery inconsistency	Kritická	Journal replay, fencing
Global coordinate precision	Vysoká	Lokální referenční rámce

13.1 Největší architektonické nebezpečí
Největší riziko podle mě není výkon Rustu ani síťová komunikace.

Je to špatně navržené vlastnictví stavu.

Pokud bude mít každá služba vlastní kopii pravdy o entitě, později budeme řešit:
- Konfliktní aktualizace.
- Duplicitní simulaci.
- Ztracené změny.
- Nekonzistentní fyziku.
- Nejednoznačnou obnovu.
Proto musí být autorita nad stavem explicitní už v základních datových kontraktech.

14. Open Architectural Decisions
Některá rozhodnutí lze doporučit už nyní. Jiná musí počkat na výsledky Worker 03 a Worker 04.
ID	Rozhodnutí	Doporučený směr	Stav
SRV-001	Autorita entity	Jeden owner	Doporučeno
SRV-002	Simulation runtime	Rust	Doporučeno
SRV-003	Control plane	Node.js	Doporučeno
SRV-004	Simulační tick	Fixed timestep	Doporučeno
SRV-005	Spatial indexing	Hybrid S2/H3 + lokální index	Doporučeno
SRV-006	Networking	QUIC	Doporučeno
SRV-007	Replication	Snapshot + delta	Doporučeno
SRV-008	Persistence	Hybrid journal + snapshots	Doporučeno
SRV-009	Database	PostgreSQL jako kandidát	Ověřit
SRV-010	Internal RPC	Protobuf / gRPC	Ověřit
SRV-011	Region migration	Epoch fencing + checkpoint	Doporučeno
SRV-012	Multi-process	Až po single-process validaci	Doporučeno
SRV-013	Server orchestration	Agones jako kandidát	Později
SRV-014	Global world clock	Společný časový model	Závisí na W04
SRV-015	Entity serialization	Komponentový kontrakt	Závisí na W03

14.1 Otázka, kterou musíme vyřešit: jeden světový čas?
Toto je důležitější, než se na první pohled zdá.

Představme si:
- Hráč řídí automobil.
- Vzdálená ekonomika města zpracovává výrobu.
- NPC cestuje mezi městy.
- Planeta rotuje.
- Hvězdný systém se pohybuje po orbitě.
Všechny systémy musí mít definovaný vztah ke společnému času.

Doporučuji rozlišovat:
1. Universe Time – globální simulační čas.
2. Region Simulation Tick – lokální pořadí aktualizací.
3. Wall Clock – skutečný čas serveru.
4. Client Time – čas klienta, který není autoritativní.
Nemusí všechny subsystémy zpracovávat každý globální časový krok.

Musí ale být možné převést jejich časové stavy do společného referenčního rámce.
14.2 Druhá zásadní otázka: simulovat celý svět pořád?
Doporučuji tři režimy:
Režim	Chování
Active	Plná fyzická simulace
Reduced	Zjednodušená simulace
Dormant	Ukládání a analytické přepočítávání podle potřeby

Příklad:

Vozidlo, které hráč právě řídí, používá Active.

Vozidlo v sousední oblasti může používat Reduced.

Opuštěné vozidlo zaparkované na planetě může být Dormant.

Přechod mezi režimy musí zachovat relevantní fyzikální a gameplay vlastnosti.

Nesmíme ale zaměnit dormant režim za úplné ignorování fyzikálních zákonů. Například objekt, na který působí významné síly, nelze bez následků jednoduše přestat simulovat.

15. Recommended Next Steps
Doporučuji následující pořadí pro orchestrátora.
1. Dokončit návaznost Worker 03 a 04
   
   Získat definitivní entity kontrakty, globální souřadnice a časový model.
2. Definovat World Ownership Specification
   
   Jednoznačně definovat vlastnictví entity, regionu, procesů a jejich migraci.
3. Definovat Simulation Scheduling Specification
   
   Fixed timestep, multirate, priority scheduling a time budget.
4. Definovat Network Replication Specification
   
   QUIC, snapshoty, delta compression, prediction a AOI.
5. Definovat Persistence Specification
   
   Journal, snapshots, transakční gameplay a recovery.
6. Provést samostatný distribuovaný systémový deep dive
   
   Split-brain, leases, fencing, migration, fault tolerance a consistency.
7. Teprve potom připravit implementační architekturu
   
   Rust workspace, Node.js services, Protobuf kontrakty, storage a deployment.

16. Primární zdroje a technická literatura
Následující zdroje jsou důležité pro další práci. Odděluji standardy a oficiální dokumentaci od komunitních technických rozborů.
MMO a authoritative server
1. L2J Server – GitHub repository.
2. L2J Mobius – Official project.
3. Nakama – Authoritative multiplayer architecture.
Konkrétní příklad L2J regionálního ownership a known-list mechanismu lze vidět v L2Object source. Je tam vidět registrace objektu do WorldRegion, globálního world registru a známého seznamu objektů. To je užitečný architektonický vzor, nikoliv návrh distribuovaného ownershipu. 

GitHub+1

Poznámka: tento výzkum zatím představuje reverse engineering architektonických principů L2J, nikoliv kompletní audit všech tříd a thread poolů konkrétní verze L2J Mobius. Ten by měl být samostatným navazujícím úkolem.
Rust a Node.js
4. Tokio – Runtime documentation.
5. Tokio GitHub repository.
6. Node.js Event Loop architecture and performance.
Networking
7. RFC 9000 – QUIC.
8. RFC 9221 – QUIC Datagram Extension.
9. Snapshot Interpolation – Glenn Fiedler.
10. Snapshot Compression – Glenn Fiedler.
Spatial partitioning
11. H3 Technical Overview.
12. S2 Cell Hierarchy.
Persistence a orchestrace
13. PostgreSQL WAL and Point-in-Time Recovery.
14. Agones GameServer Allocation.
15. Agones Scheduling and Autoscaling.

17. Konečné architektonické doporučení
Recommended Architecture
Preferred direction
Authoritative Region-Based Simulation Runtime

Simulation

Rust + Tokio pro asynchronní I/O, vyhrazené simulační workery a fixed-timestep fyziku.

Control plane

Node.js pro sessions, identitu, gameplay, koordinaci a administraci.

Spatial

Hierarchické globální partitioning + lokální fyzikální indexy + dynamické AOI.

Networking

QUIC, reliable streams, unreliable datagrams, snapshot replication a client prediction.

Persistence

Durable event journal + region snapshots + transakční databáze.

Scalability

Single process → multithreaded regions → multiple processes → distributed world.

Nejdůležitější invariant

Jedna autoritativní entita, jeden platný vlastník jejího stavu, jednoznačné pořadí změn a obnovitelná historie.

Závěrečný pohled
Největší architektonickou chybou by bylo vytvořit samostatný MMO server, samostatný fyzikální server a samostatný planetární server, které si následně předávají kopie světa.

Tím bychom vytvořili tři různé reality.

Místo toho musí být fyzika, entity a persistentní svět součástí jedné autoritativní simulační architektury.

MMO systémy jsou nadstavbou této architektury.

A právě proto doporučuji L2J převzít jako referenci pro lifecycle a gameplay management, ale ne jako základní model samotného simulačního světa.

Výsledek Worker 05: Navržena procesní topologie, vlastnictví entit a regionů, síťová komunikace, persistence, recovery, škálování a kontrakty mezi technologiemi. Zůstávají otevřené konkrétní numerické a prostorové kontrakty Worker 03 a 04.

Výzkumná specifikace – bez implementace.