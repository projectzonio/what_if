---
type: concept
sources:
  - tools/knowledge/lint-wiki.js
  - tools/knowledge/build-rag-index.js
  - tools/knowledge/_lib/simple-frontmatter.js
updated: 2026-10-19
status: implemented
aliases:
  - obsidian
  - obsidian principy
  - wiki conventions
  - jak psát wiki stránku
---

# Obsidian konvence race_game wiki

`wiki/` je platný Obsidian vault (otevři složku), ale **Obsidian není zdroj
pravdy ani závislost**: vše kontroluje headless `lint-wiki.js` a čte
`build-rag-index.js`. Převzato ze Zonio auditu `kepano/obsidian-skills`
(MIT, 2026-07): *obsidian-markdown* ano, *Bases / JSON Canvas* odloženo (vyplatí
se až od desítek stránek), *obsidian-cli* zamítnuto.

**Otevřené (uživatel 2026-10-19):** Zonio uplatňuje Obsidian principy šířeji
(např. PID, monitoring nástrojů); popis je ve Zonio skillu `second-brain`, Zonio
wiki a Zonio changelozích. Pro race_game zatím neprověřeno — kandidát na další
krok, převzít princip, ne kód.

## Pravidla

- **Tři typy stránek** v adresářích `wiki/entity/` (konkrétní subsystém nebo
  nástroj s domovem v kódu), `wiki/concept/` (vzor napříč místy),
  `wiki/decision/` (rozhodnutí; vždy sekce `## Proč (Why)`).
- **Frontmatter minimum:** `type`, neprázdné `sources` (repo-relativní cesty,
  které lint ověří), `updated: YYYY-MM-DD`. Volitelně jen s funkcí: `aliases`
  (rozlišení wikilinků + boost v RAG), `status`
  (`implemented|historical|planned|provisional|superseded`), `last_verified`,
  `source_fingerprint`, `supersedes` / `superseded_by`.
- **Vztahy = ``wikilink` (race_game)`**, fakta = `sources`. Wikilinky tvoří graf, ze
  kterého `rag-query.js` bere 1-hop sousedy; citace nepřepisovat na wikilinky.
- **Index:** každá stránka má v `wiki/index.md` právě jeden řádek
  `- [type] slug — popis`; stránka bez řádku je orphan.
- **Log:** `wiki/log.md` je append-only (datum, co a proč).
- **Jeden fakt na jednom místě.** Stav ticketů nepatří do wiki (je v
  `task_pool.json`), naměřená čísla patří do `benchmarks/` a review; wiki nese
  závěr a ukazatel.
- **Wiki jen tam, kde fakt nejde dát do nástroje** — pasti, rozhodnutí,
  hranice. Opakovanou ztrátu řeší nástroj, ne odstavec.
- Do wiki patří i nálezy, omyly a `provisional` věci: zelený report bez
  kontextu je past pro dalšího agenta.

## Po zápisu

```powershell
node tools\knowledge\lint-wiki.js --write-report   # 0 fatal
node tools\knowledge\rag-query.js --query "<ověřovací dotaz>"   # změněnou wiki přeindexuje sám
```

Související: [[rag-knowledge-tools]], [[knowledge-layer-separate-from-zonio]].
