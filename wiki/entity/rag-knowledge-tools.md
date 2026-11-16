---
type: entity
sources:
  - tools/knowledge/README.md
  - tools/knowledge/build-rag-index.js
  - tools/knowledge/rag-query.js
  - tools/knowledge/code-graph-cs.js
  - tools/knowledge/lint-wiki.js
  - tools/knowledge/evaluate_rag.py
  - tools/knowledge/rag-evaluation.v1.json
  - tools/knowledge/_lib/project-root.js
updated: 2026-11-03
last_verified: 2026-11-03
status: implemented
aliases:
  - rag
  - graphrag lite
  - second brain
  - rag-query
  - jak hledat v projektu
---

# RAG / GraphRAG-lite (`tools/knowledge`)

Lexikální vyhledávání nad dokumentací, briefy, review, pamětí agentů, wiki,
skilly a kódem. Bez npm závislostí, výstup v `artifacts/knowledge/` (gitignored,
**každý worktree má vlastní index**).

```powershell
node tools\knowledge\rag-query.js --query "<dotaz>" --top 8   # první krok místo čtení stromu
node tools\knowledge\code-graph-cs.js <Symbol>                # volající/volaní C# symbolu
node tools\knowledge\lint-wiki.js --write-report              # integrita wiki
python -X utf8 tools\knowledge\evaluate_rag.py                # regrese vyhledávání
```

## Co vrací a co ne

- **Chunk je navigace, ne důkaz** — otevři `path:start-end` a čti zdroj.
- **Stav ticketu** (dotaz `RG-0xx stav`) vrací chunk `main:.planning/race-game/task_pool.json`:
  jeden na ticket, čtený **živě z main checkoutu**, kde `pool.py` pool mutuje.
  `tasks/RG-0xx.md` ve worktree nese stav z okamžiku odbočení větve — pro stav
  mu nevěř. Výpis všech ticketů podle stavu není otázka pro RAG:
  `python -X utf8 agent_pool/scripts/pool.py status` (v main checkoutu).
- **C# karty** (`Code graph`) jsou lexikální: přetížení a dynamické volání
  nerozliší.
- **Dovednostní karty** (`Skill cards`, od 2026-10-24) stojí hned pod hlavičkou: max. 2
  skilly z `.claude/skills` se zdůvodněním — chunk skillu ve výsledcích, wiki stránka ve
  výsledcích cituje skill v `sources` (hrana grafu; stránka citující > 2 skilly je rozcestník
  a nepočítá se), nebo ≥ 2 společné termy dotazu s popisem skillu. Registr: [[agent-skills]].
  Změna skillu přestaví index stejně jako změna wiki.

## Čerstvost indexu

`rag-query.js` index před dotazem sám přestaví (~5 s), když se změnil Git HEAD
worktree, hash `task_pool.json` v main checkoutu, některá `wiki/*.md` je novější
než index, nebo je starší než 24 h. `--no-rebuild` to vypne. Do 2026-10-19
jen varoval po 24 h: index second-brain worktree z 2026-10-11 hlásil RG-077/078
jako `todo` osm dní po jejich dokončení.

## Měřítka (2026-10-19, worktree na `main` 0ec19e9)

2 821 chunků / 591 zdrojů, C# graf 189 souborů / 1 545 metod / 15 947 hran,
build ~4 s. Ranking konstanty jsou zatím zonio ladění, ne race_game měření —
`evaluate_rag.py` je baseline pro jejich změnu.

Souvisí: [[obsidian-wiki-conventions]], [[external-tools-zonio]],
[[knowledge-layer-separate-from-zonio]].

## Graf závislostí (RG-121, 2026-11-02)

- Směr A rozbalí i `sources:` ve tvaru **adresáře** (`src/NightRacer.Streaming/`) na nejlépe odpovídající indexovaný soubor; dřív se hlásil jako „NOT INDEXED“, přestože soubory pod ním v indexu byly. Případy `graph-*` v `rag-evaluation.v1.json` používají `expectedNeighbourAny`.
- Díra v grafu je oblast kódu bez stránky: `audit-graph-areas.py` (audit), `brief-graph-check.py` (při psaní briefu), `measure-ticket-coverage.py` (podíl cest ticketů s wiki stránkou + sousedem). Před opravou 6 %, po 100 % (dotaz = cesta) / 94 % (dotaz = jen název souboru), viz outbox RG-121.

## Runtime graf scény (RG-125, 2026-11-03)

```powershell
python -X utf8 -I tools\knowledge\runtime-graph.py snapshot --report-dir <agent-port dir> --label <label>   # živý player přes `agent-port-runtime` (race_game)
python -X utf8 -I tools\knowledge\runtime-graph.py build --label <label>
node tools\knowledge\build-rag-index.js
```

- Snapshot `tree` běžícího playeru → **vzory** (sbalená jména sektorů/chunků) s komponentami. Typ je first-party C# (z C# grafu),
  Unity built-in (explicitní seznam `UNITY_BUILTIN`) nebo `unresolved` — žádný „ostatní“ koš; nový built-in se ukáže jako unresolved.
- Hrana **„kdo to postavil“** s polem `evidence`: `new-gameobject-literal` (silná), `name-literal` (u obecných jmen může minout),
  `parent-creator` (zděděná od rodiče, např. KN5 meshe z `Kn5Track.cs` — odvozená, **ne důkaz**).
- Index: jeden chunk na vzor, virtuální cesta `runtime:<label>:<vzor>`; `rag-query.js` k zásahu přidá builder `.cs` do Graph neighbors
  a stránku, která builder cituje v `sources:`. Snapshoty `artifacts/knowledge/runtime/` jsou git-ignored a **per worktree** (bez nich
  runtime lane mlčí, eval projde i tak).
- Snapshot vidí jen načtené sektory u spawnu. Viadukt nemá samostatné pilíře (jeden mesh `Viaduct` na sektor).
- Čísla: worldgen seed 1 tunel 574 uzlů / 21 vzorů, Gunsai 1570 / 36; build indexu 1,8 → 2,1 s se dvěma snapshoty.
- Otevřené: `evaluate_rag.py` nemá `requiresRuntime` (runtime případy jsou proto volné); `rag-query.js` bere jen 2 zdroje na stránku.
