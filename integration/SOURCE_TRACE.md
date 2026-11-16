# SOURCE_TRACE — stopa zdrojů odzadu (WI-001)

Index: pro každé PE-001..020, INT-01..12, C-01..09 a čtyři blokátory z `research/gpt/final_for_orchestrator.md` §9.2 jsou vypsány zdroje po vrstvách. Vytvořil WI-001 (2026-11-16). Nic z `research/**` ani `audit/**` nebylo změněno.

## Jak číst

- Vrstvy odzadu (user, 2026-11-16; `AGENTS.md`): **F** = `research/gpt/final_for_orchestrator.md` -> **G** = `research/gpt/gpt0N.md` -> **S** = `research/worker-0N/w0N-supplement-01.md` -> **O** = `research/worker-0N/w0N-original.md` -> **A** = `audit/` (W10 review, CONFLICTS, OPEN_QUESTIONS).
- Odkaz `soubor:řádek` je číslo řádku souboru (u tabulek řádek tabulky, u nadpisů řádek nadpisu). Zkratky cest: `gptNN` = `research/gpt/gptNN.md`; `wNN-sup` = `research/worker-NN/wNN-supplement-01.md`; `wNN-orig` = `research/worker-NN/wNN-original.md`; `CONF` = `audit/CONFLICTS.md`; `OQ` = `audit/OPEN_QUESTIONS.md`; `W10` = `audit/WORKER_10_REVIEW.md`; `FIN` = `research/gpt/final_for_orchestrator.md`.
- Stav shody: **Shoda** (vrstvy říkají totéž), **Shoda s výhradou** (jádro sedí, detail ne), **ROZPOR** (dvě vrstvy říkají různé věci, viz sekce Rozpory), **Bez opory** (v dané vrstvě nenalezeno).
- „Nenalezeno“ znamená: nenalezeno grepem klíčových termínů (uvedeny v poznámce), ne důkaz absence. Detailní čtení celých dokumentů tento ticket neprováděl (dokumenty mají 17–88 kB, čteno po sekcích).
- Všechny hodnoty (q, 2^15 m, frekvence, RPO) jsou „návrh, nezměřeno“, dokud je benchmark neověří (`AGENTS.md`). ADR nejsou `Accepted` (FIN:806 sloupec Status = Conditional / Needs research; ADR schvaluje uživatel).

## 1. Integrovaný registr PE-001..020 (FIN:802-826)

| ID | Rozhodnutí (FIN) | F | G | S | O | A | Shoda | Poznámka |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| PE-001 | Jedna logická časová osa na větev (Conditional) | FIN:807, :143-164 | gpt01:21 (§A.1), :1144 (ADR-TIME-001), :1146 | w01-sup:13 (D-01); w09-sup:34 (platí W10 baseline) | w09-orig: nenalezeno ("WorldInstant", "watermark") | W10:196 (CONFLICT-005); CONF:15 | Shoda | Zdroj původu je W10 baseline, ne originály. FIN:162 zamítá nezávislé časové souřadnice islandu. |
| PE-002 | `i128` WorldInstant (Conditional) | FIN:808, :415-431 | gpt01:691 (128bit **nanosekundy**), :1147 (ADR-TIME-004: signed integer ns) | w01-sup:13 (D-01), :47-48 (`i128`, `q = 1/1.44e9 s`); w07-sup:281 (Q5: návrh i64); w09-sup:390 (O-6) | w01-orig: nenalezeno ("i128", "i64") | CONF:26 (NC-01), :86 (C-02); OQ:14 (OQ-04) | **ROZPOR** | R-01, R-02. Šířka (i128 vs i64) i jednotka (ns vs q = 1/1,44·10⁹ s). Kvantum i epocha jsou návrh nezměřeno (FIN:429). |
| PE-003 | Nezávislé committed watermarky regionů (Conditional) | FIN:809 | gpt01:42, :441, :1151 (ADR-TIME-008) | w09-sup:15 (D3), :16 (D4 GVT), :187 (§4.3) | w09-orig: nenalezeno ("watermark", "GVT") | W10:196 (CONFLICT-005); CONF:15, :71 (NC-46) | Shoda | Watermark žije až od W10/doplňků, originál ho nemá. |
| PE-004 | Hierarchický rámec/cell/local (Conditional) | FIN:810, :432-446 | gpt04:91 (shrnuje W01 §2.7), :1220 (ADR-ENTITY-006) | w01-sup:18 (D-06), :116 (§3.4), :345 (N-06: velikost buňky není definována) | w01-orig:293 (§2.7), :332 (floating origin nesmí měnit autoritativní souřadnice) | CONF:32 (NC-07); OQ:15 (OQ-05) | Shoda s výhradou | `2^15 m` je jen ve w01-sup:18 a OQ:15; G ho nepotvrzuje (grep "2^15", "32768" v gpt01..08: nenalezeno) a O ji nemá. Viz T-03. |
| PE-005 | Specializovaní provideři gravitace, jedno skládání (Conditional) | FIN:811, :165-189 | gpt02:41, :73, :89, :649-652 (ADR-GRAV-001..004) | w04-sup:56 (§3.1), :351 (N-1), :359 (N-9) | w01-orig:246 (gravitace odděleně od zdánlivého zrychlení) | W10:88 (CONFLICT-002); CONF:12, :36-39 (NC-11..14), :91 (C-07) | Shoda | |
| PE-006 | Nezávislá autorita physics islandu (Conditional) | FIN:812, :190-223 | gpt03:20-21 (PIAM), :57-60 | w02-sup:13 (D-201), :19 (D-207), :15 (D-203) | w02-orig:66-71 (constraint island ≠ region), :107 | W10:158 (CONFLICT-004); CONF:14, :41 (NC-16) | Shoda | FIN:215 mění čtení "island timeline" na "execution sequence", viz INT-01. |
| PE-007 | Trvalá identita entity nezávislá na reprezentaci (Conditional) | FIN:813, :224-258 | gpt04:41, :660-686 (128bit EntityID), :1215 (ADR-ENTITY-001) | w09-sup:58 (§3.1), :41 (CONFLICT-003); w01-sup:36 | w02-orig:1489 | W10:117 (CONFLICT-003); CONF:13 | Shoda | 128bit EntityID je G (gpt04:660); FIN ho nepřebírá výslovně. |
| PE-008 | Verzovaný stav s vlastněnými komponentami (Conditional) | FIN:814, :241-254 | gpt04:137 (state_version), :1219 (ADR-ENTITY-005), :1226 (ADR-ENTITY-012) | w02-sup:117 (§3.2); w09-sup:25 (D13); w06-sup:315 (N-03) | w05-orig, w09-orig: nehledáno do hloubky | W10:535 (Kanonický Entity State); CONF:59 (NC-34) | Shoda s výhradou | "Component-level authority" je rozšíření FIN (INT-06); G mluví o capability composition, ne o autoritě po komponentách. |
| PE-009 | Hierarchický multirate scheduler (Conditional) | FIN:815, :293-329 | gpt06:15, :26-27, :530, :1239 | w01-sup:15 (D-03 kvantum vs dt), :22 (D-10); w02-sup:23 (D-211); w03-sup:22 (D3-10) | w02-orig:143-147 (fixní krok default) | W10:53 (CONFLICT-001); CONF:27 (NC-02), :54 (NC-29) | Shoda | Zamítnutí univerzálního ticku (FIN:327) sedí i s ADR-TIME-015 (gpt01:1158). |
| PE-010 | Tříúrovňový determinismus D1/D2/D3 (Conditional) | FIN:816, :330-363 | gpt07:27-29, :35, :110 | w09-sup:24 (D12), :47 (CONFLICT-008); w06-sup:319 (N-07) | w06-orig:956-960 (§7.4) | W10:305 (CONFLICT-008); CONF:18, :62 (NC-37) | Shoda | |
| PE-011 | Journal + aktivace řízená závislostmi (Conditional) | FIN:817, :259-292, :610-631 | gpt05:29-35, :134, :159, :224 | w06-sup:94 (§3.3), :120 (§4.1), :240 (§4.7); w08-sup:305 (§4.8) | w06-orig:1283 (příklad `world_revision`) | W10:229 (CONFLICT-006); CONF:16, :64-65 (NC-39, NC-40) | Shoda | Příklad mostu je v gpt05:228, :255 i v FIN:613. Blokátor 3. |
| PE-012 | Oddělené RegionHandoff / EntityTransfer / DomainHandoff (Conditional) | FIN:818, :482-493 | gpt03:57-60 | w05-sup:19 (D-07), :158 (§3.4), :298 (§4.3); w02-sup:182 (§4.3); w09-sup:22 (D10); w03-sup:166 (§4.1) | w05-orig:432, :455 | CONF:42 (NC-17), :89 (C-05) | Shoda | Atomický cross-region převod islandu zůstává otevřený (FIN:569). |
| PE-013 | Konzistentní causal-cut recovery (**Needs research**) | FIN:819, :506-520, :945-947 | gpt07:293, :419, :546 (jen náznak); gpt01: nenalezeno | w05-sup:21 (D-09), :330 (§4.5), :472 (N-09); w09-sup:26 (D14), :303 (§4.9), :371 (R-3), :387 (O-3) | w05-orig:765 (§6 Persistence and Recovery) | CONF:71 (NC-46), :93 (C-09) | Shoda s výhradou | Termín "causal cut" v G není (grep "causal cut", "consistent cut": nenalezeno v gpt07/05/01); pojem "konzistentní řez" je v S/A (w05-sup:472, CONF:71). Blokátor 2. |
| PE-014 | Fidelity transitions s ohledem na zachování (Conditional) | FIN:820, :364-409 | gpt08:33, :40, :159, :417 | w03-sup:150 (§3.3), :416 (NC-302); w08-sup:161 (§3.6); w09-sup:375 (R-7) | w03-orig:256 | CONF:53 (NC-28), :92 (C-08) | Shoda s výhradou | `FidelityLevel = (SimTier, ModelVariant)` je v FIN:383, CONF:53, :92 a jen zkráceně ve w03-sup:416; v gpt08 není (viz T-02). |
| PE-015 | Autoritativní snapshoty s časem a revizí (Conditional) | FIN:821, :521-530 | gpt04:1227 (ADR-ENTITY-013); gpt07:539 (jen zmínka) | w07-sup:19 (D7 floating origin), :21 (D9 ReplicationSnapshot), :83 (§3.3) | w07-orig:507, :557 | CONF:74-76 (NC-49..51) | Shoda s výhradou | G pokrývá replikaci okrajově; těžiště je w07-sup. |
| PE-016 | Verzované časové vzorky prostředí (Conditional) | FIN:822 | gpt02:656 (ADR-GRAV-008); gpt06:695 (`sampleTime`); gpt04:177, :334 | w08-sup:94 (§3.3), :408 (NEW-803) | w08-orig:599-600 (tabulka atmosféra -> letadlo/vozidlo) | W10:265 (CONFLICT-007), :582 (Environment Sample); CONF:67 (NC-42) | Shoda | |
| PE-017 | Protokol autority cross-island interakcí (**Needs research**) | FIN:823, :637-659 | gpt03:59, :253, :562, :783, :829 | w02-sup:19 (D-207), :210 (§4.4); w09-sup:23 (D11), :292 (§4.8) | w02-orig:509-524 (CCD jen lokálně) | CONF:90 (C-06), :50 (NC-25) | Shoda s výhradou | FIN:640 tvrdí, že GPT-03 to neřeší; gpt03:59, :562 arbitráž výslovně navrhuje (obecně, ne protokolem). Blokátor 1. |
| PE-018 | Transakce / sága napříč doménami (**Needs research**) | FIN:824, :713-725 | gpt05:102, :265, :1129 | w06-sup:316 (N-04); w05-sup:23 (D-11) | w06-orig:29 | CONF:60 (NC-35); OQ:16 (OQ-07) | Shoda s výhradou | Slovo "saga" v gpt01..08 nenalezeno; je v OQ:16 (OQ-07). Rozhodnutí patří uživateli (USER-004). |
| PE-019 | Větve (identita, historie, replay) (**Needs research**) | FIN:825, :699-712 | gpt01:46, :1153-1154 (ADR-TIME-010/011); gpt04:1229 (ADR-ENTITY-015); gpt05:610, :1153; gpt07:55, :312 | w06-sup:320 (N-08) | w06-orig:1043, :1061 | CONF:63 (NC-38) | Shoda s výhradou | `BranchId` jako pojem je ve w06-sup:320 a FIN:152; v gpt01 pojem "BranchId" nenalezen (viz T-01). |
| PE-020 | Politika přetížení a degradace (**Needs research**) | FIN:826, :753-763 | gpt06:890, :1239 (ADR-SCHED-009), :1319; gpt01:1354 | w09-sup:21 (D9 strop warpu) | w09-orig:1139-1142; w05-orig:287 | OQ: žádná přímá položka | Shoda s výhradou | FIN:763 říká, že to nikdo nedefinoval důkladně, ale gpt06:1239 má explicitní ADR. Viz R-06. |

## 2. Nové integrační rozpory INT-01..12 (FIN:594-608)

INT-xx vznikly až v F; v A neexistují. „Opora v nižších vrstvách“ ukazuje, kde už konflikt popisuje vrstva pod F.

| ID | Téma (FIN) | F | G | S | A | Shoda | Poznámka |
| --- | --- | --- | --- | --- | --- | --- | --- |
| INT-01 | Čas islandu vs. globální timeline (Critical) | FIN:597, :154-162, :215-217 | gpt03:120 ("island has its own timeline") | w02-sup:23 (D-211), :311 (N-202) | CONF:47 (NC-22) | Shoda | F vědomě opravuje gpt03:120 (viz R-03). |
| INT-02 | Fyzikální vs. regionální autorita (Critical) | FIN:598, :205-213 | gpt03:20, :57 | w02-sup:310 (N-201), :316 (N-207); w05-sup:474 (N-11) | CONF:41 (NC-16), :46 (NC-21), :85 (C-01) | Shoda | |
| INT-03 | Mutace vs. aktivace odvozeného stavu (Critical) | FIN:599, :610-631 | gpt05:29-35, :159, :228, :255 | w06-sup:321-322 (N-09, N-10), :240 (§4.7) | CONF:64 (NC-39); W10:229 | Shoda | Blokátor 3. |
| INT-04 | Determinismus vs. adaptivní integrace (High) | FIN:600 | gpt07:254 (§C.5) | w03-sup:417 (NC-303) | CONF:54 (NC-29) | Shoda | |
| INT-05 | Fidelity vs. zachování stavu (High) | FIN:601, :391-406 | gpt08:40, :362 (§C5 State loss) | w08-sup:195 (§4.2), :213 (§4.3); w03-sup:209 (§4.3) | W10:265 (CONFLICT-007) | Shoda | |
| INT-06 | Stav entity vs. komponenty s vlastní autoritou (High) | FIN:602, :241-254 | gpt04:1219, :1226 | w06-sup:321 (N-09); w02-sup:117 | CONF:64 (NC-39) | Shoda s výhradou | "Component-level authority" jako formulace jen ve F. |
| INT-07 | Recovery vs. What-If branching (High) | FIN:603, :699-712 | gpt07:312, :315; gpt05:1153 | w06-sup:320 (N-08); w05-sup:22 (D-10) | CONF:63 (NC-38), :71 (NC-46) | Bez přímé opory | Obě strany existují odděleně; jejich interakci nepopisuje žádná nižší vrstva (viz T-05). |
| INT-08 | Cross-island kolize vs. vlastnictví (Critical) | FIN:604, :637-659 | gpt03:59, :562 | w02-sup:19 (D-207); w09-sup:23 (D11) | CONF:90 (C-06) | Shoda s výhradou | Totéž jako PE-017. |
| INT-09 | Vzorkování prostředí vs. exekuce domény (High) | FIN:605 | gpt06:695 | w08-sup:408 (NEW-803) | CONF:67 (NC-42) | Shoda | |
| INT-10 | Globální time warp vs. limity domén (High) | FIN:606 | gpt01:1155 (ADR-TIME-012), :1354 | w09-sup:20 (D8), :21 (D9), :389 (O-5); w03-sup:22 (D3-10 `max_warp`); w07-sup:15 (D3) | CONF:87 (C-03), :28 (NC-03) | Shoda | |
| INT-11 | Migrace islandu vs. vnitřní stav solveru (High) | FIN:607, :493 | gpt03:60 | w02-sup:312 (N-203), :22 (D-210) | CONF:48 (NC-23), :105 (V-05) | Shoda | Rapier/Jolt jsou "k ověření" (V-05). |
| INT-12 | Mutace a zachování hmoty/energie (High) | FIN:608 | gpt05:348 (jen pro nebeská tělesa) | w08-sup:136 (§3.5 flux accumulator), :305 (§4.8) | CONF:66 (NC-41) | Shoda s výhradou | Prezentace "napříč projekcemi" ve F nemá přímou oporu v G (viz T-04). |

## 3. Konflikty C-01..09 (CONF:83-93; FIN:539-593)

| ID | Rozpor | F | G | S | O | A | Shoda | Poznámka |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C-01 | Epocha vlastnictví | FIN:540-545 | gpt03:57-60; gpt04:1225 (ADR-ENTITY-011) | w02-sup:13 (D-201); w05-sup:14 (D-02), :15 (D-03); w06-sup:313 (N-01) | w05-orig:426-432 | CONF:85 | Shoda | F rozděluje na tři pole; shoduje se s doporučením správce. |
| C-02 | Šířka `WorldInstant` | FIN:547-553 | gpt01:691, :1147 | w01-sup:13, :47-48; w07-sup:281; w09-sup:390 | — | CONF:86; OQ:14 | **ROZPOR** | R-01, R-02. |
| C-03 | Rychlost času (nepovinné; mimo acceptance) | — (nezmíněno) | gpt01:1155 | w09-sup:20-21; w07-sup:15; w03-sup:22 | — | CONF:87 | Shoda | F konflikt C-03 do kritických neuvádí (FIN:539-593). |
| C-04 | Revize světa | FIN:555-561 | gpt05:134, :224 | w06-sup:94 (§3.3), :313 (N-01); w02-sup:317 (N-208); w08-sup:94 | w06-orig:1283 | CONF:88 | Shoda | |
| C-05 | Čtyři "handoffy" | FIN:563-569 | gpt03:57-60 | w05-sup:19 (D-07), :158; w02-sup:182; w03-sup:166; w09-sup:22 (D10) | — | CONF:89 | Shoda | |
| C-06 | Mezi-regionální CCD | FIN:571-577 | gpt03:59 | w02-sup:21 (D-209), :245 (§4.7); w09-sup:23 (D11), :292 (§4.8) | w02-orig:509-524 | CONF:90, :50 | Shoda | |
| C-07 | Neinerciální zrychlení | FIN:579-585 | gpt02:73, :89, :652 | w04-sup:359 (N-9), :186 (§4.2) | w01-orig:246 | CONF:91 | Shoda | Znaménkové konvence ω: CONF:33 (NC-08). |
| C-08 | Fidelity (mimo acceptance) | — (jen FIN:383) | gpt08: nenalezeno | w03-sup:416 | — | CONF:92 | Shoda | F konflikt C-08 nevede jako samostatný, využívá ho v §2.8. |
| C-09 | Obnova a sousedé | FIN:587-593 | gpt07:293, :419 | w05-sup:21 (D-09), :467 (N-04), :472 (N-09); w09-sup:26 (D14), :371 (R-3), :387 (O-3) | — | CONF:93 | Shoda | Blokátor 2. |

## 4. Čtyři blokátory (FIN:938-957)

| Blokátor | F | Souvisí s | Opora v nižších vrstvách | Poznámka |
| --- | --- | --- | --- | --- |
| 1. Autorita cross-island interakce | FIN:941-943, :637-659 | PE-017, INT-08, C-06 | gpt03:59, :562; w02-sup:19, :210; w09-sup:23; CONF:90 | Částečná opora (obecná arbitráž), protokol chybí. |
| 2. Kauzální recovery | FIN:945-947, :506-520 | PE-013, C-09 | w05-sup:472; w09-sup:371, :303; CONF:71, :93 | Opora v S/A, v G jen okrajově. |
| 3. Aktivace mutace | FIN:949-951, :610-631 | PE-011, INT-03 | gpt05:29-35, :159; w06-sup:240; CONF:64-65 | Dobrá opora; časový kontrakt chybí. |
| 4. Přechody napříč doménami | FIN:953-955 | PE-014, PE-018, PE-019 | gpt08:40, :417; gpt05:1129; gpt01:1153 | Souhrnný blokátor; jednotlivé části viz řádky PE. |

## 5. Tvrzení bez opory (F tvrdí, nižší vrstva to neobsahuje)

| ID | Tvrzení ve F | Co jsem našel | Hledané termíny |
| --- | --- | --- | --- |
| T-01 | Čtvrtý časový pojem `BranchId` jako součást GPT-01 (FIN:152) | gpt01 používá větve (gpt01:46, :1153-1154), pojem `BranchId` v gpt01..08 ne; `BranchId` jako prefix revize je ve w06-sup:320 | "BranchId" v gpt01..08 |
| T-02 | `FidelityLevel = (SimTier, ModelVariant)` jako doporučení nad GPT-08 (FIN:381-383) | gpt08 ho neobsahuje; je v CONF:53, :92 a ve zkrácené podobě w03-sup:416 | "SimTier", "ModelVariant", "FidelityLevel" v `research/` a `audit/` (zbývá jen FIN:383, CONF:53, :92) |
| T-03 | Velikost buňky `2^15 m` jako doporučená (FIN:444) | jen w01-sup:18 (D-06) a OQ:15 (OQ-05); G ani O ji nepotvrzují; w01-sup:345 uvádí, že velikost v originálech chybí | "2^15", "32768" v gpt01..08 |
| T-04 | INT-12 a F §3.3 o zachování hmoty/energie napříč projekcemi mutace (FIN:608) | G nemá ekvivalent (gpt05:348 se týká nebeských těles); opora je jen w08-sup:136, :305 | "mass", "energy", "conservation" v gpt05 |
| T-05 | INT-07 "recovery vs. branching" (FIN:603) | Žádná nižší vrstva interakci nepopisuje, jen každou část zvlášť (viz řádek INT-07) | "branch" v gpt07, gpt05, w05-sup |
| T-06 | "Causal cut" terminologie (FIN:512, :945-947) | V G není; pojem "konzistentní řez" je ve w05-sup:472 a CONF:71 | "causal cut", "consistent cut" |
| T-07 | "GPT-03 neřeší kolize mezi nezávislými islandy" (FIN:640) | gpt03:59, :562, :783, :829 arbitráž popisují, byť bez protokolu (viz R-05) | "arbitrat" v gpt03 |
| T-08 | Termín "saga" jako doporučení (FIN:725) | V G nenalezeno; je jen v OQ:16 (OQ-07) | "saga" v gpt01..08 |

## 6. Rozpory mezi vrstvami

Oba zdroje jsou citovány, nerozhoduji (to je ADR, rozhoduje uživatel).

| ID | Vrstva A říká | Vrstva B říká | Dopad | Doporučení |
| --- | --- | --- | --- | --- |
| R-01 | **Šířka času:** W01 `i128` (w01-sup:13, :47-48), F ho přebírá (FIN:427, :551); W07 návrh `i64` (w07-sup:281), W09 O-6 nechává otevřené (w09-sup:390) | F tvrdí, že W07 navrhuje i64 (FIN:549); to potvrzují w07-sup:281, CONF:86 | Velikost každého snapshotu a wire protokolu | Zapsat do ADR "Time Authority": i128 kanonicky, delta na drátě (CONF:86 doporučení správce, OQ:14). Rozhodnutí = uživatel (OQ-04). |
| R-02 | **Jednotka času:** G doporučuje 128bitové **nanosekundy** (gpt01:691, :1147 ADR-TIME-004) | S doporučuje kvantum `q = 1/1,44·10⁹ s ≈ 0,694 ns` (w01-sup:13, :47-48), F ho přebírá s výhradou (FIN:429); w01-sup:32 navíc uvádí, že ns není dělitelná jednotka pro 1/60 s | Komenzurabilita kroků 60/50 Hz (CONF:26, NC-01) | Prověřit kvantum benchmarkem a výpočtem dělitelnosti; FIN:429 to už označuje za nepotvrzené. |
| R-03 | gpt03:120: island má vlastní timeline a synchronizační hranici | FIN:154-162, :215-217: island je jen execution sequence na sdílené časové ose | Interpretace PIAM | F má přednost (vyšší vrstva říká výslovně, že přepisuje); G zůstává beze změny. |
| R-04 | FIN:808 vs. gpt04:1222 (ADR-ENTITY-008: "Integer SI-based simulation timeline") | FIN:808 `i128 WorldInstant` | Neurčená jednotka v gpt04 | Doplnit při ADR; není to věcný konflikt, jen neúplnost G. |
| R-05 | FIN:640: GPT-03 kolizi nezávislých islandů úplně neřeší | gpt03:59, :562, :783: arbitráž je výslovně navržena | Rozsah blokátoru 1 | Při WI-004/PE-017 zohlednit, že G už má rámec. |
| R-06 | FIN:763: overload není definován důkladně | gpt06:1239 (ADR-SCHED-009), :890 | Označení PE-020 jako "Needs research" | Čerpat z gpt06 jako výchozí návrh. |

## 7. Výběr ověřených odkazů

Seznam 15 náhodně vybraných odkazů a skutečný text na řádku je v outboxu `agent_pool/agents/claude/outbox/` (výstup skriptu `artifacts/tmp/verify_refs.py`, vzorek seedovaný hodnotou 20261116).

## 8. Co tento ticket neřeší

- Nezapsal jsem nic do `architecture/`, `decisions/ADR/` ani `wiki/` (mimo write ownership).
- Originály v `research/**` a `audit/**` nejsou upraveny.
- USER-001..010 a OQ jsou jen odkazovány (FIN:828-842, `audit/OPEN_QUESTIONS.md`), nerozhoduji je.
