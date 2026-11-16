---
type: concept
sources:
  - .planning/what-if/docs/worldgen-vegetation-buildings.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-149.md
  - src/WhatIf.WorldGen/Scatter.cs
  - tools/worldgen/cli/ScatterReport.cs
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-151.md
  - .planning/what-if/docs/vegetation-cc0-models.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-157.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-154.md
updated: 2026-11-15
last_verified: 2026-11-15
status: implemented
aliases:
  - vegetace worldgen
  - stromy a domy
  - scatter
  - rozmístění vegetace
  - procedurální stromy
  - vesnice domy
  - les okraj
  - cc0 stromy
  - cc0 modely vegetace
---

# Vegetace a budovy pro WorldGen v rozpočtu UHD (RG-149, V1 RG-151)

Rešerše (ADR-035, bez Unity), schválená 2026-11-10. Návrh, zatím bez implementačních ticketů. Dokument: `.planning/race-game/docs/worldgen-vegetation-buildings.md`.

## Rozhodnutí (návrh)
- **Rozmístění** = nová Unity-free CPU fáze `Scatter` v `src/WhatIf.WorldGen`, po sektorech 256 m, čistá funkce (seed, sektor, terén, silniční graf, sídla); vlastní RNG stage na sektor, hash silničního datasetu se nemění. Kontrola přes CLI `--scatter-dump` (navazuje na [[worldgen-sweep]] a `test-tile` (race_game)).
- **Geometrie v1** = vlastní procedurální low-poly meshe (strom LOD0/1/2 220/60/12 trojúhelníků, dům krabice + sedlová střecha 24). CC0 modely až po rozhodnutí uživatele (ADR-037 kryje jen textury, viz `thirdparty-assets` (race_game)).
- **Kreslení** = `Graphics.RenderMeshInstanced` po sektor x LOD x druh, žádný GameObject na rostlinu (vzor `Kn5GrassField`). Neprůhledné koruny, žádný alpha test na Low/Medium.
- **Stíny**: WorldGen dnes nevrhá žádné stíny (`WorldGenSectorSource.cs` dává `ShadowCastingMode.Off` zemi, silnici i stavbám — ověřeno v review). Vegetace by byla první vrhač; Low bez stínů, Medium/High jen LOD1 do 60 m. Zjištění z Kanazawy „stíny ~70 % trojúhelníků“ (`cryengine-optimization-study` (race_game)) se na WorldGen nepřenáší.
- **Streaming**: instance patří do `Content` sektoru, žádné globální pole (`streaming-sectors` (race_game), pravidlo chunked loading).

## V1 hotovo (RG-151, schváleno 2026-11-11)
- `src/WhatIf.WorldGen/Scatter.cs`: `ScatterModel(spec, GenResult)`, `Sector(sx, sz)` (čistá funkce, RNG stage `scatter<sx>,<sz>`), `Hash`, `AuditSector` (kontroly a-c). Od RG-169 instanční `TreeLine` / `WaterLine` (výchozí = konstanty 120 / -3 m, bitově shodné) a diagnostické `TreeRejected[8]`; What If je otevírá přes `RegionWorld.Scatter()` ([[what-if-planet-engine]]). Druhy: tree, bush, house, street_tree. `GenResult.Settlements` (jen čtení, mimo hash datasetu).
- CLI: `--scatter-dump <csv> [--window x0,z0,size,step] [--mask-offset v]` -> `scatter.csv` + `scatter-hash.txt`, vypíše (a)-(d), domy na vesnici, podíl masky. Overlay: `render_net.py --scatter <csv>` (záporný střed jako `--centre=-3350,900`; Python bez `-I`, Pillow v user site).
- Seed 1, okno `-3950,300,1200,4`: hash silnic `c03c3dc7...` beze změny, scatter hash `b4c1d501...` ve dvou bězích stejný, 3714 instancí (3123 stromů, 520 keřů, 55 domů, 16 stromů u ulic), 0 porušení, 0 vyřazených; 55 domů ve vesnici 3; nejbližší strom 131 m od rovinky.
- Čas na sektor (dev běh, ne měřicí protokol): medián ~0,35 ms, lesní sektory až 4 ms, první vesnický sektor 5-7 ms (staví parcely) -> V2 musí počítat s dvoupumpovým rozdělením (návrh §5), zatím nepostaveno.
- Odchylky: maska lesa 24 % okna místo ~40 % (offset +0,15 by dal ~40 %); město bez domů (rozhodnutí V1.5/V2); keře mají strop 1500 jako stromy; cache parcel `_lots` není thread-safe (V2 volá `Sector()` z jednoho vlákna nebo zamkne).

## V2 hotovo (RG-154, schváleno 2026-11-15)
Kreslení v Unity za flagem `--worldgen-scatter 1` (výchozí off). Detail a čísla: doc §12 a §12.1, review `reviews/RG-154.md`.
- `WorldGenScatter.cs`: jedna rodina batchů na druh x LOD pro celý aktivní prstenec sektorů, přestavba po 16 m / 8° / změně sektoru, `Graphics.RenderMeshInstanced` po 1000, odeslání z `beginCameraRendering` (agent port renderuje snímky extra `Camera.Render()`). `WorldGenScatterMeshes.cs` = vlastní procedurální meshe (placeholder, rozhodnutí uživatele 2026-11-15: geometrie zůstává, vzhled přinesou textury; otázka CC0 modelů uzavřena „teď ne“).
- **Vzdálenosti podle úrovně kvality** (`DistTable`, klíč = název presetu graphics-lab, bez něj Low): Low 60/250/50/250, worldgen-max 200/1200/120/1200 m (strom LOD1 / strom daleko / keř / dům). `--worldgen-scatter-dist a,b,c,d` přepíše (měření). Medium/High řádek nemají (= Low).
- **Dither cross-fade** v `Resources/WorldGenScatter.shader`: LOD1 -> LOD2 v pásmu 12 % se komplementárním šumem, každý druh mizí posledních 10 % dosahu; bez blendingu a bez nového passu.
- **Past:** materiál vytvořený jen za běhu ztratí variantu `INSTANCING_ON` a `RenderMeshInstanced` nekreslí nic bez chyby -> materiál v Resources s `m_EnableInstancingVariants: 1` (viz `worldgen-unity-adapter` (race_game)).
- Cena: Low tile +0,43 ms P95 (první kolo); worldgen-max 200/1200 +0,02 ms P95 proti 60/250, 0 hitchů (`perf-matrix` (race_game) report `2026-11-15-08722a24-full`). Žádný vrhač stínu.
- Jízda uživatele 2026-11-15 (rework 1): přeskakování detailu „ne (křoviny mají opožděný spawn)“, konec lesa vidět „ano“, cukání „ne“. Otevřené: dosah lesa omezuje načtený prstenec sektorů (150/900 = 200/1200), keře naskakují pozdě (dosah 120 m, placement 6-10 ms/sektor nad `BudgetMs` 3) -> follow-up.

## CC0 modely: rešerše V4 (RG-157, schváleno 2026-11-12)
Dokument `.planning/race-game/docs/vegetation-cc0-models.md` (nic nestaženo, licence V znovu ověřeny orchestrátorem).
- **Poly Haven** (třída A, viz `texture-sources` (race_game)): stromy jsou filmové, 0,3-17,4 M trojúhelníků na strom; per-LOD počty žádná přečtená stránka neukazuje. V rozpočtu jen `fern_02` (6 K) a `shrub_03` (17 K) pro pár kusů zblízka. Listy = alpha cutout -> overdraw a alpha-clip ve stínovém průchodu (odhad O, neměřeno).
- **Quaternius**: `license.html` je od 2026-08-28 „QAL v1.0“, ne CC0 (zákaz redistribuce assetů samotných); stránky balíčků a Poly Pizza stále píšou CC0 -> třída B / vyloučit do rozhodnutí.
- **Kenney Nature Kit**: CC0 site-wide (V), bez čísel trisů/formátů -> per-asset B, kandidát na pilot.
- ambientCG má typ „3D Model“, strom nepotvrzen; Poly Pizza/Sketchfab C; OpenGameArt 403.
- Schema manifestu v2 pro modely (mesh hash, LOD seznam, tris, osa/jednotky, materiály s `alpha_mode`, autor a provenience per soubor, `qa.mesh`): dokument §5, navazuje na `thirdparty-assets` (race_game).
- Doporučení (O): procedurální v1 zůstává; při „řada kuželů“ nejdřív bohatší procedurální LOD0, CC0 pilot až po rozhodnutích D1-D5 (po jízdě RG-154), editor import, ne runtime glTF.

## Rozpočty (odhad O, neměřeno)
Low +1,0 ms (čekáno 0,4–0,8, dominují draw cally, ne trojúhelníky), Medium +2,5 ms, High +4,0 ms nad řádkem `worldgen-<level>` v `perf-matrix` (race_game). Low s vegetací i texturami ([[worldgen-materials]]) ~5,1 ms.

## Navržené tickety
V1 `Scatter` + CLI dump (CPU, M) -> V2 Unity kreslení minimálního kroku na Low (GPU, M) -> V3 Medium/High extra: LOD0, stíny blízkých stromů, billboardy, tráva (M) -> V4 volitelně CC0 stromy (rozhodnutí uživatele; podklad RG-157 výše).

## Otevřené mezery
- `RenderMeshInstanced`: limit instancí na volání (V2 používá 1000 bez ověření z dokumentace), kompatibilita se SRP Batcherem a stíny nejsou doslovně ověřené.
- Stránka LOD Group 6000.3 nedosažena; návrh si přepíná LOD sám podle vzdálenosti s hysterezí.
- Okraj lesa na dlaždici seed 1: splněno maskou bez změny parametrů (RG-151, 131 m).
