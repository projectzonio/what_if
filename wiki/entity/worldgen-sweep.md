---
type: entity
sources:
  - .planning/what-if/docs/worldgen-drive.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-122.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-128.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-138.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-139.md
  - .planning/what-if/docs/race_game-lane-history/reviews/RG-180.md
updated: 2026-11-16
last_verified: 2026-11-16
status: implemented
aliases:
  - rain + flood
  - test deštěm a záplavou
  - flood test
  - stream-run sweep
  - WorldGenSweep
  - sweep-inject
  - brána sweepu
  - sweep gate
  - SkipSweep
  - WidthStepJoint
---

# Test deštěm a záplavou (`--stream-run sweep`, RG-122)

Fyzikální kontrola generovaného světa po blocích. Prochází svět přes `SectorStreamer` (`streaming-sectors` (race_game)) v okně 3×3, testuje
prostřední sektor a nikdy nenačte celou mapu (pravidlo chunked loading). Svět staví `worldgen-unity-adapter` (race_game), graf a terén
dodává [[worldgen]]. Příkazy, typy vad a injecty popisuje `.planning/race-game/docs/worldgen-drive.md`, oddíl „Rain + flood test (RG-122)“.

## Co dělá
- **Déšť** (`WorldGenSweepRain.cs`) pouští paprsky shora: do 1 m od okraje silnice po 1 m, jinde po 4 m. Porovnává první zásah
  s návrhem: uvnitř obrysu silnice (`WorldGenSweepRoads.cs`) má být silnice, mimo něj navržená dlaždice. Seam pass hustě
  prochází hrany u uzlů.
- **Záplava** (`WorldGenSweepFlood.cs`) šíří sondy velikosti auta od silnice. Hlídá sklon, schod, světlou výšku a pád. Najde
  pasti, pády z hran tunelů a mostů a `ground_in_tunnel`.
- **Injecty** (`WorldGenSweepInject.cs`, `--sweep-inject all`) podstrčí pět vad: `step`, `collider`, `wall`, `seam`, `hole`.
  Důkaz detekce běží na `--sweep-area node:935:600`, terrain seed 1. Šestý inject `barrier` (jen jménem, není v `all`, RG-139) vypne collider zábradlí
  viaduktu; na `--sweep-area node:945:300` dá neznámý `bridge_edge` a exit 1.
- **Výstup:** `sweep-<s>.json/.txt` a snímky 5 nejhorších buněk. Exit ≠ 0 znamená, že je přítomná neznámá vada. Známé třídy
  (`tunnel_edge`, `portal_*`) uzavřel RG-120; `portal_lid` je od RG-128 0. **`bridge_edge` je od RG-139 vada** (na seedech 1–3 0 výskytů; jako známý by schoval chybějící zábradlí).
  **Od RG-142 (2026-11-12) žádná známá třída:** `WidthStepJoint` (`ground_over_road` u uzlu stupně 2 se skokem šířky 6 → 7 m, seed 1 uzel 728) je smazaný, protože generátor dává řetězci hran přes uzly stupně 2 jednu šířku ([[worldgen]]). Celá mapa seed 1/2/3: unknown 0, known 0, exit 0 (orchestrátor seed 1 znovu).
- **Konstrukce (RG-120):** déšť prochází collidery `Tunnel`/`Viaduct` (`IsStructure`); zásah podlahy portálové jámy není
  `portal_hole`; maska děr = `GroundData.Omit`. Záplava: prázdná sousední buňka za stěnou/zábradlím je `Barrier`, ne pád;
  `Covered` vyžaduje skutečný collider nad buňkou. RG-128 opravil obrys silnic a diagonálu dlaždic v detektorech; `--sweep-inject all` pak našel 5 z 5.
- **Zbylé neznámé třídy** (proto sweep zatím končí exit 1), RG-128, celá mapa terrain t1 / t2 / t3: `step` 15 / 5 / 11, `seam_gap` 0 / 1 / 6,
  `surface_mismatch` 0 / 5 / 1, `ground_over_road` 3 / 1 / 2 (před RG-128 138 / 119 / 122), `low_clearance` t3 4, flat 1 30.
  **Po RG-130 (2026-11-08):** `step` 0 / 0 / 0; `seam_gap` a `surface_mismatch` beze změny a všechny na koncích mostních polí (krajnice pozemního pásu vs. mostovka bez krajnice, 954/955 přechod L0/L1). RG-128 je přisuzoval jámě, to neplatí. Sweep proto dál končí exit 1 a **není automatickou bránou** `run-kn5.ps1 -WorldGen`. Brána přijde po opravě konců mostů.
  **Po RG-138 (2026-11-08):** `wall_in_lane` 11 / 15 / 24 → 0 (pruh = silnice ve výšce sondy, `Lane`); `seam_gap` a `surface_mismatch` 0 / 0 / 0. Model silnic (`WorldGenSweepRoads`) teď sleduje generátor: na mitrovaném uzlu stupně 2 končí hrana na mitre přímce (`WorldGenMap.MitreEnd`), ne na kolmé rovině. Dřív klín na vnější straně ohybu nepatřil žádné hraně a na mostovce dával falešný `surface_mismatch`. Obrys mostu na prostém konci pole zahrnuje rozšíření (`PlainSpanEnd`, `SpanEndWiden`). Zbývá: t1 `ground_over_road` 1 (hrana 1175) + `trap` 3 buňky, t3 `low_clearance` 4 (hrana 1267); t2 exit 0.
  **Past záplavy (RG-138):** `Barrier()` se volá jen u prázdného souseda (pád). Platná buňka níž o ≤ `MaxDown` 1,0 m projde `Allowed()` přes zábradlí, proto t1 `trap` vedle rozšířené mostovky (falešný). Oprava patří do ticketu brány sweepu (review RG-138).
  **Po RG-139 (2026-11-09): celá mapa t1 / t2 / t3 exit 0 / 0 / 0**, neznámé 0, známé 1 / 0 / 0. Záplava: krok dolů na platnou buňku (pokles nad `MaxUp` po korekci sklonu, `StepDown`) zastaví `Barrier()` stejně jako pád; `trap` má pozici. t3 `low_clearance` byla vada adaptéru: mostovka 0,6 m místo 0,3 m, které generátor rezervuje (`DeckThicknessAndClearance` 5,3 m), viz `worldgen-unity-adapter` (race_game).
  Sweep hledá díry, ne hrboly: schod v pruhu z překryvu ramp neviděl, ten měří `roadfeel` ([[worldgen]]).
- **Průchod `edge_step` (RG-180, 2026-11-16, `WorldGenSweepFlood.cs` `EdgeSteps`):** každé 2 m na obou obrysech každé ne-tunelové silnice paprsek dolů 0,3 m uvnitř obrysu a 0,5 / 1,0 / 2,0 m vně; vada = `hrana − země(0,5 m)` nad **0,08 m** nebo plocha s `normal.y < 0,7`. Druhy `ground`, `junction` (Trim + 6 m), `bridge_approach` (24 m), `deck`, `tunnel`; přeskakuje jinou silnici a palubu. Histogram podle třídy a druhu, CSV `edge-<série>.csv` (sloupce `edge,class,kind,s,side,x,z,edgeY,step05,step10,step20,ny05,ol,outx,outz`).
  - Příčina schodu (obrubník při kolmém nájezdu z trávy, hlášení RG-053) byla v `WorldGenTerrain.Flatten`, viz `worldgen-unity-adapter` (race_game): lavice brala nejnižší výšku ze všech segmentů v dosahu, oprava = nejbližší bod každé silnice, pak nejnižší silnice. Seed 1 29 543 -> 3 828, seed 2 28 755 -> 4 452; ztráta rychlosti při kolmém nájezdu 11–16 -> 0–2 km/h; jízda uživatele ve městě OK.
  - **`edge_step` je zatím neznámá třída**, takže sweep i brána končí exit 1 (jízdy RG-180 přes `-SkipSweep`). Zbytky: lem 8–12 cm u místních křižovatek (Lift + FlatBiasM + tětiva), svislá římsa u mostních nájezdů (116 stanic, `Cap`), regrese `seam_gap` uzel 611 seed 1. Řeší navazující ticket (`agent_pool/shared/reviews/RG-180.md`).

## Použití
- **Brána (RG-139):** `run-kn5.ps1 -WorldGen` s `--stream-run manual` v `-PlayerExtra` nejdřív spustí sweep `--sweep-area spawn:600` na stejném specu, seedu a spawnu (série `<s>-gate`, `artifacts/sweep/<s>-gate`). Exit ≠ 0: `SWEEP GATE FAILED`, výpis neznámých vad s hranou a pozicí, cesta k reportu, exit 3, jízda nestartuje. `-SkipSweep` jízdu spustí a vypíše, že kontrola neproběhla. Měřicí běhy (`sweep/measure/fly/drive`) brána nehlídá.
- Celá mapa trvá 38–66 s na i3-1215U. Baseline počtů je v reportu RG-122 a ve sloupci „now“ v `RG-119-rework2.md`.

## Pasti
- Chybějící zábradlí nad terénem 1–20 m pod mostovkou sweep nevidí: soused je platná buňka a `MaxDown` ho odmítne jako krok, ne jako pád. `bridge_edge` vzniká až nad pádem > 20 m (limit detektoru, RG-139, bez ticketu).
- Sweep porovnává postavené s navrženým. **Nevidí vadu samotného návrhu**: svislý profil bez oblouků (hrboly a vzlety u viaduktu,
  jízda 3 RG-119) prošel čistý. Kontrola zakřivení profilu patří do validátoru generátoru ([[worldgen]]).
- Snímky jsou jen u nalezené vady. Libovolnou pózu (`--sweep-shot-at`) neumí.
- `ground_over_road` ve výchozích datech: 119–140 clusterů na seed, medián 8 cm. Jsou to zářezy na hranách záplav křižovatek
  z RG-117, takže je má i plochý spec; nejde o šum detektoru.

Související: `features` (race_game), [[workflow-tools]], `unity-notebook-runs` (race_game).
