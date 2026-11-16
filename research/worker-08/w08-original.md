PLANETARY ENGINE — WORKER 08
Scientific & Environmental Simulation Architecture
Deep research
Architecture specification
No implementation
Rust / Node.js / Python / Unity

Mission: Navrhnout environmentální simulační vrstvu, která propojí astronomii, planetární fyziku, počasí, oceány, terén a fyzickou interakci všech objektů ve společném světě.
1. Executive summary
Zásadní architektonické rozhodnutí: Planetary Engine by neměl simulovat celou planetu na jedné prostorové a časové přesnosti.

Navrhuji Hierarchical Multi-Fidelity Environmental Simulation – hierarchickou simulaci prostředí s několika úrovněmi přesnosti.

Jeden fyzikálně konzistentní svět bude mít několik výpočetních reprezentací:
1. Global Environment: celoplanetární klima, rotace, sluneční záření, tlakové a teplotní profily, oceánská cirkulace.
2. Regional Environment: počasí, vítr, srážky, proudy, regionální teploty a vodní bilance.
3. Local Environment: detailní aerodynamické podmínky, turbulence, lokální proudění, vlny a interakce s terénem.
4. Interaction Environment: okamžité fyzikální podmínky aplikované na vozidla, postavy, letadla, lodě a další objekty.
Tyto úrovně nesmí představovat čtyři nezávislé simulace. Musí být propojeny přes definované datové kontrakty, konzervaci fyzikálních veličin a řízený přenos výsledků mezi prostorovými měřítky.

Důležitý princip: Simulace nemusí být všude stejně přesná, ale její jednotlivé úrovně musí být vzájemně kompatibilní.
2. Scientific foundations and reference systems
2.1 Coordinate system and physical reference frame
Environmentální simulace musí pracovat v souřadnicovém systému, který odpovídá skutečné planetě, nikoliv pouze Unity world coordinates.

Doporučený model:
Vrstva	Souřadnicový systém
Astronomická	Inertial reference frame, ICRF-compatible
Planetární	ECEF – Earth-Centered, Earth-Fixed
Geografická	Geodetic latitude, longitude, altitude
Regionální	ENU – East, North, Up
Lokální fyzika	Floating origin / local Cartesian
Unity rendering	Lokální transformace relativní k aktivnímu originu

Pro planetu podobnou Zemi:
- Geoid / referenční elipsoid WGS84.
- Rotace planety definovaná astronomickou vrstvou.
- Gravitace získaná z planetárního gravitačního modelu.
- Atmosférické veličiny definované relativně k lokální geopotenciální výšce.
- Oceánská hloubka relativně k definovanému vertikálnímu datumu.
Proč je to důležité: Atmosféra, počasí a oceány jsou definovány v rotující soustavě planety, zatímco astronomická mechanika může pracovat v inerciální soustavě. Míchání těchto systémů bez explicitních transformací způsobí chyby ve větru, Coriolisově síle, rychlosti vozidel i poloze objektů.
2.2 Základní fyzikální veličiny
Každý environmentální subsystém musí používat jednotný systém SI.
Veličina	Symbol	Jednotka
Tlak	\(p\)	Pa
Hustota	\(\rho\)	kg/m³
Teplota	\(T\)	K
Rychlost proudění	\(\vec{u}\)	m/s
Gravitační zrychlení	\(\vec{g}\)	m/s²
Vlhkost	\(q\)	kg/kg
Tepelná energie	\(E\)	J
Tepelný tok	\(Q\)	W/m²
Salinita	\(S\)	PSU / definovaná hmotnostní frakce
Hloubka	\(h\)	m

Doporučení: interně nepoužívat °C, km/h, hPa ani jiné prezentační jednotky. Převody provádět na hranici API a UI.
2.3 Referenční vědecké modely
Pro vývoj doporučuji vycházet z těchto zavedených systémů:
- ECMWF Integrated Forecasting System – dynamika atmosféry, fyzikální parametrizace a numerické metody. 
  
  ECMWF
- U.S. Standard Atmosphere 1976 – referenční atmosférické profily. 
  
  ntrs.nasa.gov+1
- MITgcm – oceánská a atmosférická cirkulace.
- GEBCO – globální výšková a hloubková data. 
  
  GEBCO+1
Tyto modely nejsou návrhem na přímé portování celých meteorologických nebo oceánografických systémů do herního enginu. Slouží jako vědecká reference a zdroj validačních dat.

3. A. Atmosphere — atmosférický model
3.1 Atmosférický stav
Atmosféra bude reprezentována stavovým polem:
\[
A(\mathbf{x},t)=
\begin{bmatrix}
p & \rho & T & \mathbf{u} & q & \chi
\end{bmatrix}
\]

Kde:
- \(p\): statický tlak.
- \(\rho\): hustota vzduchu.
- \(T\): absolutní teplota.
- \(\mathbf{u}\): vektor rychlosti vzduchu.
- \(q\): měrná vlhkost.
- \(\chi\): složení atmosféry, například molární frakce plynů.
Pro jednoduchou atmosféru lze použít stavovou rovnici ideálního plynu:
\[
p=\rho R_s T
\]

Pro suchý vzduch je \(R_s\) přibližně 287,05 J/(kg·K).

Pro vlhký vzduch je vhodné použít virtuální teplotu:
\[
T_v \approx T(1+0.61q)
\]

Přesnější model musí rozlišovat vodní páru, kapalnou vodu a led v atmosféře.
3.2 Vertikální atmosférický profil
Základním modelem pro počáteční planetu bude hydrostatická rovnováha:
\[
\frac{dp}{dz}=-\rho g
\]

V kombinaci se stavovou rovnicí získáme vertikální průběh tlaku a hustoty.

Pro vrstvu s konstantním teplotním gradientem:
\[
T(z)=T_0+L(z-z_0)
\]

Pro izotermickou vrstvu:
\[
p(z)=p_0
\exp\left(-\frac{g(z-z_0)}{R_sT}\right)
\]

Tento model poskytuje levnou analytickou aproximaci, vhodnou pro výškové profily atmosféry a základní aerodynamiku.































Důležitá hranice modelu: Standard Atmosphere není počasí. Neobsahuje skutečnou lokální meteorologickou situaci, fronty, denní vývoj počasí ani regionální anomálie.

Doporučuji kombinovat:
1. Standardní atmosférický profil.
2. Regionální teplotní a tlakové odchylky.
3. Lokální meteorologické podmínky.
4. Dynamický model proudění.
Referenční profil U.S. Standard Atmosphere 1976 sahá až do 1000 km, ale běžná interaktivní atmosférická simulace bude potřebovat jiné modely pro jednotlivé výškové režimy. 

*(zdroj: ntrs.nasa.gov +1)*

3.3 Aerodynamika
Každé fyzické těleso musí dostávat vlastnosti okolního vzduchu.

Základní odpor:
\[
F_D=\frac{1}{2}\rho v_{rel}^2 C_D A
\]

Kde:
- \(\rho\): lokální hustota vzduchu.
- \(v_{rel}\): rychlost objektu vůči vzduchu.
- \(C_D\): součinitel odporu.
- \(A\): referenční plocha.
Relativní rychlost:
\[
\mathbf{v}_{rel}=\mathbf{v}_{object}-\mathbf{u}_{air}
\]

Toto je zásadní: letadlo letící rychlostí 100 m/s proti větru 20 m/s má jinou aerodynamickou situaci než letadlo letící stejnou rychlostí po větru.

Pro pokročilý model je potřeba zahrnout:
- Reynoldsovo číslo.
- Machovo číslo.
- Dynamickou viskozitu.
- Vztlak.
- Indukovaný odpor.
- Stlačitelnost.
- Teplotní vliv na rychlost zvuku.
\[
Re=\frac{\rho vL}{\mu}
\]

\[
Ma=\frac{v}{\sqrt{\gamma R_sT}}
\]

Doporučená implementační hranice: environmentální engine poskytuje lokální fyzikální vlastnosti vzduchu. Model letadla, dronu nebo automobilu vypočítává vlastní aerodynamické síly.

Tím se zabrání tomu, aby environmentální vrstva obsahovala specifickou logiku jednotlivých gameplay žánrů.

4. B. Atmospheric Dynamics — dynamika atmosféry
4.1 Základní rovnice
Pro globální a regionální atmosférickou simulaci vycházíme z rovnic zachování hmoty, hybnosti a energie.

Kontinuita:
\[
\frac{\partial \rho}{\partial t}
+\nabla\cdot(\rho\mathbf{u})=0
\]

Zachování hybnosti v rotující soustavě:
\[
\frac{D\mathbf{u}}{Dt}
+2\boldsymbol{\Omega}\times\mathbf{u}
=
-\frac{1}{\rho}\nabla p
+\mathbf{g}
+\mathbf{F}
\]

Kde \(\mathbf{F}\) zahrnuje další síly, například tření a parametrizované turbulentní procesy.

Energetická rovnice musí zahrnovat:
- Tepelnou výměnu.
- Sluneční a dlouhovlnné záření.
- Latentní teplo kondenzace.
- Transport tepla prouděním.
- Interakci s povrchem.
4.2 Coriolisova síla
\[
\mathbf{a}_{cor}=-2\boldsymbol{\Omega}\times\mathbf{v}
\]

Coriolisův parametr:
\[
f=2\Omega\sin(\varphi)
\]

Jeho vliv závisí na zeměpisné šířce.

To má zásadní dopad na:
- Směr rotace velkých atmosférických systémů.
- Oceánské proudy.
- Dlouhodobý transport vzduchových hmot.
- Regionální meteorologii.
Na lokální úrovni automobilu nebo postavy bude obvykle zanedbatelný. Na planetární úrovni je nezbytný.
4.3 Turbulence
Plnohodnotné DNS (Direct Numerical Simulation) není vhodné pro celoplanetární simulaci.

Doporučené alternativy:
Metoda	Použití	Omezení
DNS	Vědecká lokální simulace	Extrémní výpočetní náročnost
LES	Detailní lokální proudění	Stále vysoké nároky
RANS	Technické proudění	Závislost na modelu turbulence
K-epsilon	Levná turbulence	Omezení v některých typech proudění
K-omega SST	Aerodynamické aplikace	Náročnější než jednoduché aproximace
Procedurální turbulence	Vizuální a gameplay efekty	Bez garantované fyzikální přesnosti

Doporučení: Regionální počasí řešit parametrizovanou turbulencí. Detailní lokální simulaci aktivovat pouze tam, kde skutečně ovlivňuje gameplay.
4.4 Počasí a meteorologické systémy
Počasí nemá být pouze vizuální efekt s náhodně měněnými parametry.

Navrhuji stavový model meteorologických systémů:
- Tlakové výše a níže.
- Teplé a studené fronty.
- Oblaka.
- Srážkové systémy.
- Bouřky.
- Vítr a nárazy.
- Vlhkost.
- Viditelnost.
- Teplotní inverze.
Každý region bude mít časově proměnný meteorologický stav.




















6










Pro produkční referenci doporučuji architekturu a numerické postupy ECMWF IFS. Jeho dokumentace odděluje dynamiku, numerické metody, fyzikální procesy, asimilaci dat a vlnový model, což je vhodný vzor i pro modulární návrh Planetary Engine. 

ECMWF

5. C. Oceans and water simulation
5.1 Architektura vodního systému
Oceán není jen vodní plocha s kolizní geometrií.

Musí obsahovat několik fyzikálně odlišných vrstev:




















4










Subsystém	Fyzikální odpovědnost
Bathymetry	Hloubka a podmořský terén
Ocean State	Teplota, salinita, hustota
Ocean Currents	Horizontální a vertikální proudění
Surface Waves	Vlny a jejich spektrum
Tides	Astronomicky vynucené změny hladiny
Coastal Dynamics	Pobřežní interakce
Water Interaction	Síly na lodě, vozidla a objekty

5.2 Hloubka a bathymetry
Pro planetu doporučuji jednotnou topograficko-bathymetrickou reprezentaci.

Data:
\[
H(\varphi,\lambda)
\]

- Kladné hodnoty: nadmořská výška.
- Záporné hodnoty: hloubka pod referenční hladinou.
- Nulová hodnota: referenční hladina moře.
Důležitá produkční reference je GEBCO.

Reálná data
 GEBCO_2026
- Globální pokrytí.
- Rozlišení 15 úhlových vteřin.
- 43 200 × 86 400 buněk.
- Přibližně 3,73 miliardy hodnot.
- Globální NetCDF dataset přibližně 7 GB nekomprimovaný.
- Součástí je i Type Identifier Grid, který popisuje původ podkladových dat.
Zdroj: GEBCO_2026 technical specification. 

*(zdroj: GEBCO +1)*

Rozlišení 15 arcseconds odpovídá přibližně:
- 463 m severojižně.
- Na rovníku přibližně 463 m východozápadně.
- S rostoucí zeměpisnou šířkou se východozápadní rozměr zmenšuje.
To je dobrý globální základ, ale není to dostatečné pro detailní přístavy, pobřeží, řeky ani lokální vodní fyziku.

Doporučená hierarchie dat:
Úroveň	Rozlišení	Použití
Planetary	400–500 m	Globální oceán
Regional	30–100 m	Pobřeží, zátoky, jezera
Local	1–10 m	Přístavy, řeky, vodní vozidla
Interaction	Sub-metre podle potřeby	Kontaktní fyzika

Jde o návrhové cíle, nikoliv o tvrzení, že všechna data budou dostupná v těchto rozlišeních.
5.3 Hustota a vztlak
Hustota sladké vody při běžných teplotách je přibližně 1000 kg/m³. Mořská voda má typicky vyšší hustotu, přibližně 1025 kg/m³.

Přesná hustota musí záviset na:
- Teplotě.
- Salinitě.
- Tlakové hloubce.
Základní vztlaková síla:
\[
F_B=\rho_{water}gV_{displaced}
\]

Výsledná hydrostatická síla:
\[
F_{net}=F_B-mg
\]

Pro lodě je nutné počítat skutečně ponořený objem, nikoliv aplikovat konstantní vztlak na celé těleso.

Doporučuji rozlišovat:
1. Hydrostatický vztlak.
2. Hydrodynamický odpor.
3. Dynamický tlak.
4. Přídavnou hmotnost vody.
5. Tlumení pohybu.
6. Vlnami způsobené síly.
Pro jednoduchá vozidla lze začít diskrétními buoyancy volumes. Pro lodě je vhodnější rozdělení trupu na více hydrostatických segmentů.
5.4 Oceánské proudy
Proudění oceánu je ovlivněno:
- Větrem.
- Rotací planety.
- Rozdíly hustoty.
- Teplotou.
- Salinitou.
- Bathymetrií.
- Přílivem a odlivem.
Základní rovnice hybnosti je analogická atmosférické dynamice:
\[
\frac{D\mathbf{u}}{Dt}
+2\boldsymbol{\Omega}\times\mathbf{u}
=
-\frac{1}{\rho}\nabla p
+\mathbf{g}
+\mathbf{F}_{friction}
\]

Pro velkoprostorové oceánské proudy lze použít hydrostatickou Boussinesqovu aproximaci.

Ta předpokládá přibližně konstantní referenční hustotu v inerciálních členech, přičemž změny hustoty se zachovávají v gravitačním členu (buoyancy).

Alternativy:
Model	Výhoda	Omezení
Shallow Water Equations	Levný, regionální proudění	Nezachytí plnou 3D strukturu
Hydrostatic primitive equations	Vhodné pro velké oceány	Hydrostatická aproximace
Non-hydrostatic model	Detailní vertikální dynamika	Vyšší výpočetní náročnost
Procedurální proudy	Velmi levné	Bez fyzikální konzistence

Pro první produkční verzi doporučuji globální aproximaci proudů a detailnější shallow-water simulaci v aktivních oblastech.
5.5 Vlny
Pro povrchové vlny je potřeba odlišit:
- Wind waves.
- Swell.
- Interference vln.
- Odraz od pobřeží.
- Lom vln.
- Mělkou vodu.
- Rozbíjení vln.
Pro globální model je vhodný spektrální přístup.

Spektrum:
\[
E(f,\theta)
\]

Popisuje distribuci energie podle frekvence a směru.

Pro interaktivní rendering lze použít kombinaci:
- Gerstnerových vln.
- FFT-based ocean simulation.
- Spektrálních modelů typu Phillips/JONSWAP.
- Lokální aproximace mělké vody.
Důležité rozlišení: vizuální výška vlny není automaticky dostatečná pro výpočet síly působící na trup lodi. Fyzikální interakce musí vycházet z lokálního povrchu, rychlosti vody a relativního pohybu objektu.
5.6 Příliv a odliv
Příliv je astronomicky vynucený jev.

Zdroje:
- Gravitační působení Měsíce.
- Gravitační působení Slunce.
- Rotace systému Země–Měsíc.
- Geometrie oceánských pánví.
- Rezonance a tření.
Pro jednoduchý model lze hladinu aproximovat harmonickým součtem:
\[
\eta(t)=
\sum_{i=1}^{N}
A_i\cos(\omega_i t+\phi_i)
\]

Kde jednotlivé složky reprezentují astronomické harmonické komponenty.

Tento model je vhodný pro základní simulaci hladiny, ale skutečné lokální amplitudy a fáze jsou silně závislé na geometrii oceánu.

Pro referenční data doporučuji harmonické přílivové modely, například TPXO od University of Oregon.

Významná architektonická hranice: astronomická vrstva dodává polohy a gravitační vlivy nebeských těles, oceánská vrstva vypočítává jejich hydrodynamickou odezvu. Není správné simulovat příliv pouze jako jednoduchou funkci vzdálenosti Měsíce.

6. D. Climate and Planetary Environment
6.1 Energetická bilance planety
Základní klimatický model musí zachovávat energetickou bilanci.

Zjednodušeně:
\[
C\frac{dT}{dt}
=
Q_{solar}-Q_{outgoing}
\]

Příchozí sluneční záření:
\[
Q_{solar}=
\frac{S_0}{4}(1-\alpha)
\]

Kde:
- \(S_0\): solární konstanta.
- \(\alpha\): planetární albedo.
- Faktor 4 vychází z poměru plochy koule a jejího průřezu.
Pro jednoduchý radiační model:
\[
Q_{outgoing}=\epsilon\sigma T^4
\]

Kde \(\epsilon\) je efektivní emisivita a \(\sigma\) Stefan-Boltzmannova konstanta.

Model skleníkového efektu lze rozšířit o radiační vrstvy atmosféry, absorpci a emisi dlouhovlnného záření.

Pozor: Jednovrstvý greenhouse model je vhodný pro globální průměrnou teplotu, nikoliv pro přesné regionální počasí.
6.2 Insolace
Dopadající energie závisí na:
- Vzdálenosti od hvězdy.
- Úhlu dopadu.
- Zeměpisné šířce.
- Rotaci planety.
- Sklonu rotační osy.
- Ročním období.
- Zastínění terénem a objekty.
- Atmosférickém útlumu.
Základní vztah:
\[
I= S_0\left(\frac{r_0}{r}\right)^2
\max(0,\cos\theta)
\]

Kde \(\theta\) je úhel mezi normálou povrchu a směrem ke hvězdě.

Tím se propojuje astronomický systém s environmentálním modelem.
6.3 Tepelná výměna povrchu
Povrch musí mít vlastní tepelný stav.

Doporučuji materiálové parametry:
- Tepelná kapacita.
- Tepelná vodivost.
- Emisivita.
- Albedo.
- Absorpce slunečního záření.
- Vlhkost.
- Tepelná difuzivita.
Rovnice vedení tepla:
\[
\rho c_p\frac{\partial T}{\partial t}
=
\nabla\cdot(k\nabla T)+Q
\]

Pro první verzi lze použít jednorozměrný vícevrstvý model půdy, vody a stavebních materiálů.

Tento systém je důležitý pro:
- Teplotu vozovek.
- Sníh a led.
- Vysychání půdy.
- Lokální mikroklima.
- Tepelné zatížení postav.
- Poškození a vlastnosti materiálů.
6.4 Roční období a planetární parametry
Roční období musí vznikat z geometrie planety a jejího pohybu, nikoliv z náhodného přepínání sezon.

Vstupy:
- Orbitální parametry.
- Sklon rotační osy.
- Excentricita orbity.
- Délka rotace.
- Oběžná doba.
- Albedo.
- Atmosférické vlastnosti.
To umožní budoucí podporu planet s odlišnými klimatickými režimy, nikoliv pouze simulaci Země.

7. E. Environmental coupling
Toto je jedna z nejdůležitějších částí celého návrhu.

Environmentální systémy nesmí být izolované moduly. Musí tvořit propojený fyzikální systém.
7.1 Coupling matrix
Zdroj	Příjemce	Přenášené veličiny
Star / Astronomy	Atmosphere	Solar irradiance, incidence angle
Planet Rotation	Atmosphere	Angular velocity, Coriolis
Atmosphere	Aircraft	Density, pressure, temperature, wind
Atmosphere	Vehicles	Air density, temperature, wind
Atmosphere	Terrain	Precipitation, heat flux
Atmosphere	Ocean	Wind stress, heat, evaporation
Moon / Astronomy	Ocean	Tidal forcing
Ocean	Ships	Current, waves, buoyancy
Ocean	Coastal Terrain	Water level, flooding
Terrain	Atmosphere	Surface roughness, heat, moisture
Terrain	Ocean	Bathymetry, coast boundaries
Weather	Characters	Temperature, precipitation, wind, visibility
Climate	Materials	Thermal conditions, freezing, weathering

7.2 Příklad: letadlo
Letadlo musí dostávat prostředí v jeho aktuální poloze a čase.
Aircraft Position
       |
       v
Environmental Query
       |
       +--> Atmospheric Density
       +--> Temperature
       +--> Pressure
       +--> Wind Vector
       +--> Humidity
       +--> Gravity
       |
       v
Aircraft Aerodynamics
       |
       v
Lift / Drag / Control Forces
       |
       v
Physics Integration


Environmentální engine nepočítá, jak letadlo zatáčí. Poskytuje prostředí, ve kterém se jeho vlastní fyzika odehrává.
7.3 Příklad: automobil za deště
Déšť musí ovlivnit povrch, nikoliv pouze zobrazit částice.
Atmospheric Precipitation
          |
          v
Surface Water Accumulation
          |
          v
Road Surface State
          |
          +--> Wetness
          +--> Water Film Depth
          +--> Surface Temperature
          +--> Ice Formation
          |
          v
Vehicle-Tire Interaction
          |
          +--> Friction
          +--> Aquaplaning
          +--> Braking Distance
          +--> Lateral Grip


Důležité: tření pneumatiky nesmí být určeno pouze počasím. Záleží také na materiálu povrchu, rychlosti, zatížení pneumatiky, geometrii dezénu a vodním filmu.
7.4 Příklad: voda a pobřežní oblast
Příliv, srážky, terén a oceán musí ovlivňovat stejnou vodní bilanci.
\[
\frac{\partial h}{\partial t}
+
\nabla\cdot(h\mathbf{u})=P-E+R
\]

Kde:
- \(h\): výška vodní vrstvy.
- \(\mathbf{u}\): horizontální rychlost vody.
- \(P\): srážky.
- \(E\): evaporace.
- \(R\): další přítok nebo odtok.
Pro mělkou vodu se přidá rovnice hybnosti, tření a tlakový gradient.

Tak lze modelovat záplavy, rozliv řek, přílivové oblasti i pobřežní změny.

8. F. Computational Strategy — výpočetní architektura
8.1 Čtyři úrovně simulace
L0 — Planetary
Global

100–500 km
Globální energetická bilance, klimatické pásy, rotace, cirkulace, dlouhodobé proudy.

Časová aktualizace: minuty až hodiny simulačního času.


L1 — Regional
Regional

1–50 km
Počasí, fronty, srážky, regionální vítr, říční systémy, teplotní pole.

Časová aktualizace: sekundy až minuty.


L2 — Local
Interactive

1–1000 m
Lokální proudění, turbulence, mikroklima, vlny, detailní voda, interakce s terénem.

Časová aktualizace: desítky milisekund až sekundy.


L3 — Interaction
Physics

Centimetry až metry
Aerodynamické síly, vztlak, odpor, mokrý povrch, lokální podmínky kontaktu.

Aktualizace podle fyzikálního ticku interagujících objektů.


Uvedené hodnoty jsou návrhové rozsahy. Nejde o pevné rozlišení každého subsystému.
8.2 Prostorová diskretizace
Nedoporučuji používat jednu globální pravidelnou 3D voxelovou mřížku.

Problémy:
- Extrémní počet buněk.
- Neefektivní reprezentace atmosféry.
- Rozdílné požadavky oceánu a atmosféry.
- Paměťová náročnost.
- Obtížná reprezentace zakřivené planety.
- Zbytečné výpočty v neaktivních oblastech.
Doporučuji kombinovat několik metod:
Subsystém	Preferovaná metoda
Globální atmosféra	Sférická nebo cubed-sphere mřížka
Regionální atmosféra	Nested grid / adaptive refinement
Oceán	Structured curvilinear grid
Lokální proudění	Adaptive Cartesian grid
Povrchová voda	Shallow-water grid
Vlny	Spectral representation
Terrain	Hierarchical tiled heightfields
Interakce objektů	Lokální analytické dotazy

Cubed-sphere je zajímavá alternativa k běžné lat/lon mřížce, protože omezuje geometrické problémy u pólů.
8.3 Prostorové rozlišení a paměť
Příklad pro globální atmosférické pole s rozlišením 0,25°:
\[
N=1440\times720=1\,036\,800
\]

Při 10 veličinách typu float32:
\[
M=N\times10\times4
\]

\[
M\approx39.5\ MB
\]

To je pouze jedna výšková vrstva.

Pokud přidáme 37 vertikálních úrovní:
\[
M\approx1.46\ GB
\]

Pro jedinou kompletní kopii těchto polí.

Při několika časových snímcích, pomocných polích a dalších fyzikálních proměnných se paměť rychle zvyšuje.

Výpočet ukazuje, proč musí být oddělená:
- Simulační mřížka.
- Ukládání historických dat.
- Interpolační cache.
- Lokální detailní simulace.
8.4 Prostorové a časové LOD
LOD nesmí znamenat jen vizuální detail.

Environmentální LOD musí řídit:
- Prostorové rozlišení.
- Časový krok.
- Počet fyzikálních proměnných.
- Metodu numerického řešení.
- Frekvenci synchronizace.
- Přesnost interpolace.
Například letadlo může vyžadovat vysokou přesnost atmosférického profilu ve výšce 10 km, ale jeho okolí nemusí potřebovat detailní 3D turbulence.

Loď v přístavu naopak potřebuje lokální vlny a vodní hladinu, zatímco globální oceán může běžet v mnohem nižším rozlišení.
8.5 Precomputed scientific datasets
Pro globální simulaci doporučuji maximálně využívat vědecká data jako počáteční podmínky a referenční pole.
Doporučené zdroje dat









ERA5

Globální meteorologická reanalýza, hodinová data, 0,25° atmosférická mřížka.
Copernicus ERA5











GEBCO_2026

Globální výška terénu a bathymetrie.
Dataset specification











TPXO

Harmonické přílivové konstanty a transporty.
OSU TPXO











NOAA GFS

Analýzy a předpovědi počasí, užitečné pro srovnání alternativních meteorologických zdrojů.
NOAA GFS


ERA5 poskytuje hodinová povrchová pole s rozlišením 0,25° a samostatná vlnová data s rozlišením 0,5°. Jde o reanalýzu kombinující model a pozorování, nikoliv o surové naměřené hodnoty v každém bodě planety. 

*(zdroj: cds.climate.copernicus.eu +1)*

Důležitá architektonická poznámka:

Dataset nesmí být samotným simulačním enginem.

Data slouží jako:
- Initial conditions.
- Boundary conditions.
- Historical reconstruction.
- Calibration.
- Validation.
- Procedural generation reference.
Pokud hráč změní klima, odstraní les, postaví přehradu nebo vytvoří nový vodní zdroj, simulace musí být schopná pokračovat mimo původní dataset.

9. Navrhovaná implementační architektura
9.1 Rozdělení technologií
Technologie	Odpovědnost
Rust	Environment simulation runtime
Node.js	Control plane, API, orchestrace, persistence
Python	Scientific tooling, import, kalibrace, offline výpočty
Unity / C#	Rendering, klientská interpolace, gameplay presentation

Rust bude autoritativní výpočetní vrstva.

Node.js nebude počítat atmosférickou dynamiku. Bude spravovat konfiguraci, požadavky, persistence a komunikaci mezi subsystémy.

Python bude sloužit pro vědecké nástroje, zpracování NetCDF/GRIB, přípravu datasetů a offline analýzu.

Unity bude zobrazovat výsledky a lokálně interpolovat environmentální stav. Nebude rozhodovat o autoritativním počasí nebo globální fyzice.
9.2 Navrhované moduly

planetary_engine/
│
├── simulation_runtime/
│   ├── simulation_clock/
│   ├── scheduler/
│   ├── deterministic_execution/
│   └── state_management/
│
├── environment/
│   ├── atmosphere/
│   │   ├── thermodynamics/
│   │   ├── atmospheric_dynamics/
│   │   ├── wind/
│   │   ├── humidity/
│   │   ├── precipitation/
│   │   └── weather/
│   │
│   ├── ocean/
│   │   ├── bathymetry/
│   │   ├── hydrostatics/
│   │   ├── currents/
│   │   ├── waves/
│   │   ├── tides/
│   │   └── coastal/
│   │
│   ├── climate/
│   │   ├── solar_radiation/
│   │   ├── energy_balance/
│   │   ├── greenhouse/
│   │   ├── seasonal_cycles/
│   │   └── heat_transfer/
│   │
│   ├── surface/
│   │   ├── soil/
│   │   ├── water_balance/
│   │   ├── snow_ice/
│   │   └── material_thermal/
│   │
│   ├── coupling/
│   │   ├── atmosphere_ocean/
│   │   ├── atmosphere_terrain/
│   │   ├── ocean_coastal/
│   │   └── astronomy_environment/
│   │
│   └── spatial/
│       ├── planetary_grid/
│       ├── regional_grid/
│       ├── local_grid/
│       ├── interpolation/
│       └── lod/
│
├── environmental_api/
│   ├── state_queries/
│   ├── force_inputs/
│   └── subscriptions/
│
├── data/
│   ├── importers/
│   ├── datasets/
│   ├── cache/
│   └── validation/
│
└── tools/
    ├── scientific_validation/
    ├── visualization/
    └── benchmarks/



Toto je logická modulární struktura. Nemusí znamenat, že každý modul bude samostatný proces nebo Rust crate.
9.3 Execution model
Doporučuji oddělit simulační frekvence od frekvence herního physics ticku.

Příklad plánování:
Subsystém	Navržená frekvence
Global climate	1/60 Hz až 1/3600 Hz
Regional weather	0,1–1 Hz
Local wind	1–20 Hz
Local water	5–30 Hz
Interaction queries	30–120 Hz
Unity rendering	60+ FPS

Jde o počáteční rozpočty, nikoliv o změřené požadavky nebo pevné konstanty.

Výsledky musí být časově označené a interpolovatelné.
9.4 Environment Query API
Všechny gameplay systémy musí používat jednotné rozhraní.

Konceptuální kontrakt:
{
  "query_id": "environment-query-001",
  "world_id": "planet-001",
  "position": {
    "latitude": 49.1951,
    "longitude": 16.6068,
    "altitude_m": 250.0
  },
  "simulation_time": 125000.0,
  "requested_fields": [
    "air_temperature",
    "air_pressure",
    "air_density",
    "wind_velocity",
    "humidity",
    "gravity",
    "precipitation"
  ],
  "required_accuracy": "local"
}


Výsledný stav:
{
  "simulation_time": 125000.0,
  "sample_time": 125000.0,
  "position": {
    "latitude": 49.1951,
    "longitude": 16.6068,
    "altitude_m": 250.0
  },
  "atmosphere": {
    "temperature_k": 288.15,
    "pressure_pa": 98400.0,
    "density_kg_m3": 1.19,
    "wind_velocity_enu_m_s": [2.0, 4.0, 0.0],
    "specific_humidity": 0.006,
    "precipitation_rate_kg_m2_s": 0.0
  },
  "gravity_enu_m_s2": [0.0, 0.0, -9.81],
  "quality": {
    "resolution_m": 1000.0,
    "interpolation": "trilinear",
    "source": "regional_simulation",
    "confidence": 0.95
  }
}


Ukázkové hodnoty a schéma návrhu, nikoliv skutečná meteorologická měření nebo finální datový standard.

Důležité požadavky:
- Každá veličina musí mít definované SI jednotky.
- Souřadnicový referenční systém musí být explicitní.
- Vektorové veličiny musí uvádět orientaci os.
- Čas musí být simulační čas, nikoliv pouze systémový UTC timestamp.
- Výsledek musí obsahovat zdroj, stáří a prostorové rozlišení.
- Neznámá hodnota nesmí být zaměněna za nulu.
- Musí být možné vyžádat pouze konkrétní proměnné.
9.5 Datový formát a komunikace
Doporučení:
Účel	Technologie
Scientific datasets	NetCDF / GRIB
Standardizovaná metadata	CF Conventions
Interní výpočty	Rust-native numeric arrays
Runtime queries	Binary RPC
Control plane	Node.js API
Client replication	Snapshot + interpolation
Historical data	Chunked time-series storage

Pro vědecká data je vhodné dodržovat CF Conventions. Standard definuje význam proměnných, jednotky, souřadnice, čas a metadata, což je důležité pro kompatibilitu datových zdrojů. 

cfconventions.org


10. Performance considerations
10.1 Největší výpočetní náklady
Subsystém	Relativní náročnost	Riziko
Global climate	Nízká až střední	Dlouhodobá numerická stabilita
Global atmospheric dynamics	Velmi vysoká	Výpočetní rozsah
Regional weather	Vysoká	Adaptivní rozlišení
Local turbulence	Velmi vysoká	Extrémní nároky
Ocean circulation	Vysoká	3D prostorová reprezentace
Wave simulation	Střední až vysoká	Spektrální výpočty
Tides	Nízká při precomputingu	Datová přesnost
Environmental queries	Nízká až střední	Vysoká frekvence dotazů

10.2 CPU versus GPU
Doporučené rozdělení:

CPU / Rust
- Autoritativní stav.
- Globální a regionální simulační scheduler.
- Interpolace.
- Lokální fyzikální dotazy.
- Numerické řešení podle potřeby.
- Persistence a synchronizace.
GPU / Unity
- Vizuální vlny.
- Mraky a atmosférické efekty.
- Renderování srážek.
- Vizuální turbulence.
- Povrchové shadery.
- Částicové efekty.
GPU akcelerace vědeckých solverů může být později důležitá. Nedoporučuji ji ale zavádět jako architektonickou povinnost první verze.
10.3 Server scaling
Pro tisíce hráčů není rozumné počítat samostatnou atmosféru pro každého hráče.

Doporučený model:
Planet Environment
       |
       v
Regional Environment States
       |
       v
Shared Local Simulation Cells
       |
       v
Cached Environmental Snapshots
       |
       +------ Player A
       +------ Player B
       +------ Player C
       +------ Aircraft
       +------ Vehicles


Hráči ve stejné regionální oblasti sdílejí stejný autoritativní environmentální stav.

Dotazy se agregují, cachují a mohou využívat společné interpolované výsledky.

Pro tisíce hráčů je zásadní počet aktivních simulačních oblastí, nikoliv samotný počet připojených klientů.
10.4 Determinismus
Doporučuji rozlišovat dva režimy:
1. Authoritative physical state: výsledek určuje server.
2. Scientific reproducibility: stejné počáteční podmínky, konfigurace, verze modelu a numerické parametry produkují reprodukovatelné výsledky.
Bitově identický výsledek napříč různým hardwarem nelze automaticky předpokládat, zejména u paralelních floating-point výpočtů.

Pro důležité události je potřeba zaznamenávat:
- Model version.
- Initial state.
- Simulation timestamp.
- Random seed.
- Numerical configuration.
- Input forcing.
- State transitions.

11. Testing and validation plan
Vědecká validace musí být oddělena od gameplay testování.
11.1 Atmosphere
Test	Očekávaný výsledek
Hydrostatic equilibrium	Správný tlakový profil
Ideal gas law	Konzistentní tlak, hustota a teplota
Standard atmosphere	Shoda s COESA76 tabulkami
Wind transport	Správný směr a rychlost
Coriolis test	Správná hemisférická orientace
Energy conservation	Kontrola energetické bilance

11.2 Ocean
Test	Očekávaný výsledek
Hydrostatic pressure	Správný tlak podle hloubky
Buoyancy	Správné ponoření těles
Shallow water	Stabilní šíření vlny
Tidal harmonics	Shoda s TPXO
Wave propagation	Zachování energie v ideálním testu
Coastal flooding	Správná vodní bilance

11.3 Climate
- Globální radiační rovnováha.
- Reakce na změnu albeda.
- Reakce na změnu skleníkových plynů.
- Denní tepelný cyklus.
- Sezonní změny insolace.
- Tepelná akumulace vody a půdy.
11.4 Integration tests

TEST 01
Planet rotates
    -> Solar incidence changes
    -> Surface heating changes
    -> Atmospheric temperature responds
    -> Wind field evolves

TEST 02
Moon orbital position changes
    -> Tidal forcing changes
    -> Ocean surface elevation responds
    -> Coastal water level changes

TEST 03
Rainfall increases
    -> Surface water accumulates
    -> Terrain water state changes
    -> Vehicle tire interaction changes

TEST 04
Aircraft climbs
    -> Atmospheric density decreases
    -> Aerodynamic forces change
    -> Aircraft trajectory responds

TEST 05
Global dataset is replaced
    -> Import validation
    -> Coordinate alignment
    -> Unit validation
    -> Runtime sampling comparison



11.5 Validation datasets
Model	Referenční zdroj
Atmosphere	COESA76
Weather	ERA5 / GFS
Bathymetry	GEBCO
Tides	TPXO
Waves	WAVEWATCH III
Ocean circulation	MITgcm benchmark experiments
Climate energy	NASA Earth energy budget

MITgcm dokumentace obsahuje matematické rovnice, numerické metody a testovací experimenty pro oceánskou i atmosférickou dynamiku. Je vhodným zdrojem pro vytvoření validační sady. 

*(zdroj: mitgcm.org +1)*

Pro vlnové modelování je důležitým referenčním systémem NOAA WAVEWATCH III, který řeší spektrální rovnici zachování akční hustoty v prostoru, frekvenci a směru. 

*(zdroj: weather.gov +1)*

Pro příliv je TPXO vhodný i jako numerická validační reference, protože obsahuje harmonické konstanty hladiny a transportů. 

*(zdroj: tpxo.net +1)*


12. Risk register
ID	Riziko	Závažnost	Doporučené řešení
ENV-01	Globální 3D simulace příliš náročná	Critical	Multi-fidelity model
ENV-02	Nesoulad různých časových kroků	Critical	Central simulation clock
ENV-03	Nesprávná interpolace polí	High	Validované interpolátory
ENV-04	Numerická nestabilita	High	Konzervativní numerické metody
ENV-05	Nesoulad datových souřadnic	High	Jednotný geodetický standard
ENV-06	Nesprávné SI jednotky	High	Typed quantities / metadata
ENV-07	Duplicitní simulace pro hráče	High	Shared regional state
ENV-08	Neudržitelná závislost na externích datech	Medium	Procedural fallback
ENV-09	Nesoulad server/client fyziky	High	Server authority
ENV-10	Přenos chyb mezi LOD úrovněmi	High	Conservation-aware coupling

12.1 Zvláštní riziko: falešná fyzikální přesnost
Je potřeba zabránit situaci, kdy systém poskytuje výsledek s vysokým počtem desetinných míst, ale podkladový model má nízkou přesnost.

Například:
- Vítr 12,384 m/s může být numericky přesně interpolovaný.
- Ale jeho původní regionální pole může mít rozlišení 10 km.
- Výsledek proto nemá automaticky lokální meteorologickou přesnost.
Proto musí každá environmentální hodnota nést metadata o prostorové a časové kvalitě.
12.2 Konzervace mezi LOD
Zásadní problém bude přechod mezi globální, regionální a lokální simulací.

Například pokud regionální model přijme více vody z globálního modelu, musí být správně zachována vodní bilance.

Stejně tak při předávání tepla mezi atmosférou, oceánem a terénem.

Doporučuji explicitní conservation budgets:
- Mass budget.
- Momentum budget.
- Energy budget.
- Water budget.
Tyto rozpočty budou součástí automatických validačních testů.

13. Open architectural decisions
Následující rozhodnutí je potřeba uzavřít společně s astronomy, terrain, physics a simulation runtime workery.
ID	Rozhodnutí	Doporučený směr
ENV-ADR-01	Globální grid	Cubed-sphere / hybrid
ENV-ADR-02	Regionální grid	Adaptive nested grids
ENV-ADR-03	Atmosférická dynamika	Hydrostatic primitive equations
ENV-ADR-04	Lokální turbulence	RANS / LES podle potřeby
ENV-ADR-05	Oceánská dynamika	Hydrostatic ocean model
ENV-ADR-06	Lokální voda	Shallow-water equations
ENV-ADR-07	Globální příliv	Harmonic constituents
ENV-ADR-08	Vlny	Spectral model + lokální FFT
ENV-ADR-09	Climate	Energy balance + heat transfer
ENV-ADR-10	Environment API	Centralized typed query interface
ENV-ADR-11	Synchronizace	Multi-rate simulation scheduler
ENV-ADR-12	Authority	Rust authoritative runtime

Zvlášť důležitá rozhodnutí
ADR-01: Cubed-sphere versus lat/lon

Pro planetární model preferuji cubed-sphere, ale bude nutné ověřit kompatibilitu s globálními dataset importéry a regionálními gridy.

ADR-02: Jak ambiciózní bude skutečná meteorologie?

Jsou tři možnosti:
1. Pouze procedurální počasí.
2. Fyzikálně konzistentní regionální počasí s globálními okrajovými podmínkami.
3. Plnohodnotný numerický model počasí.
Doporučuji variantu 2 jako cílový kompromis.

ADR-03: Real-world Earth versus generické planety

Environmentální systém nesmí mít napevno zakódovanou Zemi.

Parametry jako gravitační konstanta, rotační rychlost, složení atmosféry, sklon osy, albedo, hvězdné spektrum a tepelné vlastnosti musí být konfigurovatelné pro jednotlivé planetární instance.

ADR-04: Autoritativní versus lokální simulace

Doporučuji serverovou autoritu nad environmentálním stavem a lokální klientskou interpolaci.

Unity nesmí samostatně generovat počasí, které by měnilo fyzikální výsledek vozidla nebo letadla.

14. Recommended next steps
Navrhuji následující pořadí dalšího architektonického výzkumu:
1. Planetary Physics Integration
   
   Uzavřít gravitační pole, rotaci, referenční soustavy a planetární parametry.
2. Global Environmental Data Layer
   
   Definovat jednotný systém importu, verzování, geografického indexování a ukládání vědeckých dat.
3. Environmental Simulation Scheduler
   
   Navrhnout multi-rate scheduler, interpolaci a předávání stavu mezi úrovněmi.
4. Atmosphere and Ocean MVP Architecture
   
   Vybrat minimální numerické modely pro první funkční planetu.
5. Physics Coupling
   
   Navázat environmentální API na vozidla, letadla, lodě, postavy a materiály.
6. Scalability Research
   
   Ověřit sdílení environmentálních oblastí mezi tisíci hráči.
15. Final architectural recommendation
Planetary Environmental Engine
Navrhovaná architektura celoplanetárního prostředí
Astronomical System

Sun · Moon · Orbit · Rotation · Insolation


Atmosphere

Pressure · Wind · Weather · Heat


Ocean

Currents · Waves · Tides · Water


Climate

Radiation · Seasons · Energy


Surface

Terrain · Soil · Snow · Ice




Environmental Coupling Layer

Mass · Energy · Momentum · Water conservation


Environmental Query & State API

Spatial queries · Temporal queries · LOD · Interpolation


Vehicles

Aerodynamics · Grip


Characters

Survival · Exposure


Aircraft / Ships

Flight · Buoyancy




Konečné rozhodnutí
Pro Planetary Engine doporučuji vybudovat vlastní environmentální runtime v Rustu, ale neopakovat výzkum a numerickou práci meteorologických a oceánografických institucí.

Použít jejich vědecké modely, datasety, matematické principy a validační experimenty jako základ. Vlastní engine bude řešit hierarchii simulace, adaptivní přesnost, herní interakce, synchronizaci a serverovou autoritu.

Nejdůležitější architektonický princip: Hráč nesmí vstoupit do jiného fyzikálního světa jen proto, že se přesunul z jednoho gameplay žánru do druhého.

Stejný vítr musí působit na letadlo, plachetnici, strom i postavu. Stejný déšť musí ovlivňovat terén, vodní bilanci i přilnavost pneumatik. Stejné sluneční záření musí pohánět tepelný stav povrchu, atmosféru i oceán.

To je základ univerzálního fyzikálně koherentního světa.

16. Primary research references
Oblast	Primární zdroj
Atmosférická dynamika	ECMWF IFS Documentation
Standardní atmosféra	NOAA/NASA U.S. Standard Atmosphere 1976
Atmosférické rovnice	MITgcm equations
Oceánská dynamika	MITgcm Manual
Příliv	OSU TPXO
Vlny	NOAA WAVEWATCH III
Bathymetrie	GEBCO 2026
Meteorologická reanalýza	Copernicus ERA5
Předpověď počasí	NOAA GFS
Klimatická energetická bilance	NASA Earth Energy Budget
Datová interoperabilita	CF Conventions

Stav Worker 08: Architektonický výzkum a návrh dokončen. Implementace nebyla zahájena. Číselné rozsahy výkonu a rozlišení jsou návrhové odhady, které vyžadují benchmarky.

Výstup pro orchestrátora: ENVIRONMENTAL_SIMULATION_ARCHITECTURE — návrh k porovnání s Planetary Physics, Terrain, Core Simulation Runtime a Multiplayer Architecture workery.