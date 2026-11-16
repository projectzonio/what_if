# Prompty pro GPT (nový chat, bez vkládání dat)

Repo `projectzonio/what_if` je samonosné: GPT si všechno přečte z GitHubu. V novém chatu zapni GitHub konektor s přístupem k repu a vlož jeden z promptů níže. Výstup ulož jako soubor a nahraj ho do repa (nebo ho předej Claude, ten ho commitne).

---

## Prompt A: Worker 10, druhý audit

```
Jsi Worker 10 – cross-system validátor projektu Planetary Engine (What If).
Repo: github.com/projectzonio/what_if, větev main. Čti přímo z repa.

Tvůj předchozí výstup je audit/WORKER_10_REVIEW.md (audit v0.1).
Na tvou žádost z §12 vznikly doplňující výzkumy research/worker-0N/w0N-supplement-01.md (N = 1..9).
Zadání doplňků: audit/SUPPLEMENT_BRIEF.md.
Správce repa je sloučil do:
- audit/CONFLICTS.md – oddíl A (tvé CONFLICT-001..008), B (52 nových rozporů NC-01..NC-52),
  C (9 rozporů mezi doplňky navzájem C-01..C-09), D (tvrzení k ověření V-01..V-10)
- audit/OPEN_QUESTIONS.md – otázky OQ-01..OQ-33 (U = uživatel, A = architektura)
- integration/INTERFACE_CONTRACTS.md, integration/DEPENDENCY_GRAPH.md, integration/INTEGRATION_ROADMAP.md
- decisions/ADR/ – ADR-001..015 ve stavu Proposed

Úkol – druhý audit:
1. Rozhodni C-01..C-09 (rozpory mezi doplňky). U každého: rozhodnutí, odůvodnění, dopad na kontrakty.
2. Projdi NC-01..NC-52: přijmout navržené řešení / upravit / zamítnout (tabulka).
3. Pro každé ADR-001..015 napiš finální text (Kontext, Rozhodnutí, Důsledky, Alternativy) a stav
   Accepted / Proposed (pokud závisí na OQ typu U nebo na měření). Navrhni nová ADR, pokud chybí
   (např. Time Authority / TimeScale, EntityTransfer protokol, DomainHandoff, glosář).
4. Napiš Master Architecture Baseline jako soubory architecture/MASTER_ARCHITECTURE.md,
   COORDINATE_SYSTEM.md, TIME_SYSTEM.md, ENTITY_MODEL.md, PHYSICS_MODEL.md, NETWORK_MODEL.md,
   PERSISTENCE_MODEL.md (obsah viz architecture/README.md). Finální kontrakty jako informační model.
5. Ověř zrekonstruovaný graf závislostí (integration/DEPENDENCY_GRAPH.md – hrany doplnil správce
   repa z tvého textu, originál se ztratil při kopírování) a oprav ho.
6. Implementační gates: které VAL testy musí projít před Stage 1, 2, 3.
7. Seznam otázek, které musí rozhodnout uživatel, než se uzavře baseline (odkaz na OQ-xx).

Pravidla:
- Původní výzkumy research/worker-0N/w0N-original.md jsou historie, neměň je. Rozpory mezi nimi
  vznikly paralelní prací bez koordinace, ne chybou autora.
- Rozlišuj ověřeno / návrh / odhad. Čísla z doplňků jsou většinou odhad.
- Diagramy piš jako ```mermaid bloky (textově, ne obrázkem).
- Výstup dávej po souborech: nadpis "=== FILE: <cesta v repu> ===" a pak celý obsah souboru.
  Začni souborem audit/WORKER_10_REVIEW_v0.2.md (shrnutí auditu), pak decisions/ADR/*, pak architecture/*.
- Česky, identifikátory anglicky.
```

---

## Prompt B: ověření tvrzení z primárních zdrojů

```
Repo: github.com/projectzonio/what_if (main). Přečti audit/CONFLICTS.md, oddíl D (V-01..V-10).

Pro každé tvrzení V-xx najdi primární zdroj (normu, oficiální dokumentaci, původní článek,
zdrojový kód) a napiš:
- verdikt: potvrzeno / vyvráceno / částečně / nenalezeno
- přesné znění nebo hodnoty ze zdroje (u konstant čísla s jednotkami)
- URL primárního zdroje (ne blog, ne fórum, pokud existuje primární)
- dopad na repo: který doplněk / kontrakt se musí opravit

Výstup jako jeden soubor:
=== FILE: audit/VERIFICATION_V01-V10.md ===
Česky. Tabulka + detail po bodech.
```

---

## Prompt C: další doplňující výzkum (šablona)

```
Repo: github.com/projectzonio/what_if (main). Jsi Worker 0N projektu Planetary Engine.
Přečti audit/SUPPLEMENT_BRIEF.md (pravidla a struktura výstupu), audit/CONFLICTS.md
a svůj research/worker-0N/w0N-supplement-01.md.
Úkol: <téma>.
Výstup: === FILE: research/worker-0N/w0N-supplement-02.md === (struktura 1–9 podle briefu).
```
