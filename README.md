# What If – Planetary Engine

Architektonický výzkum univerzální simulační platformy: jeden autoritativní fyzikální svět od povrchu planety po meziplanetární prostor. Postava, auto, letadlo, raketa a kosmická loď sdílí stejný čas, stejné souřadnice a stejnou identitu objektů.

**Fáze:** výzkum a konsolidace architektury. Implementace zatím zakázaná. Pracovní stack: Rust (autoritativní runtime), Node.js (control plane), Unity (jen klient), Python (vědecká a validační vrstva).

## Stav (2026-11-16)

| Krok | Stav |
| --- | --- |
| Výzkumy W01–W09 | hotovo, `research/worker-0N/w0N-original.md` |
| Audit W10 v0.1 (cross-system validace) | hotovo, `audit/WORKER_10_REVIEW.md` |
| Doplňující výzkumy W01–W09 podle W10 §12 | probíhá, `research/worker-0N/w0N-supplement-01.md` |
| Druhý audit W10 → Master Architecture Baseline | čeká |
| `architecture/` | čeká na druhý audit |

## Struktura

```
research/worker-01..09/   původní výzkumy (w0N-original.md) a doplňky (w0N-supplement-NN.md)
architecture/             konsolidovaná architektura (po druhém auditu W10)
integration/              graf závislostí, kontrakty rozhraní, integrační roadmapa
decisions/ADR/            rozhodnutí ADR-001..015 (zatím Proposed)
audit/                    audit W10, rejstřík rozporů, otevřené otázky, zdroje, zadání doplňků
```

## Workery

| Worker | Oblast | Doplňující úkol (W10 §12) |
| --- | --- | --- |
| W01 | Universal World Architecture: planetární geometrie, souřadnice, environment interface | Finální prostorový a časový kontrakt |
| W02 | Universal Physics Runtime | Physics islands přes regionální hranice |
| W03 | Entity Physics: postavy, vozidla, letadla, drony, rakety, lodě | Přechody mezi fyzikálními doménami |
| W04 | Celestial & Universe Simulation | Sjednocení gravitačních providerů |
| W05 | Authoritative Server Runtime | Fencing, ownership a fault recovery |
| W06 | Gameplay, strategie a stavba světa | World Mutation lifecycle a revision consistency |
| W07 | Unity klient a síťová synchronizace | Unity prediction při akcelerovaném čase |
| W08 | Vědecká a environmentální simulace | Conservation-aware LOD coupling |
| W09 | Výkon, škálování, distribuovaná simulace | Distributed time a boundary synchronization |
| W10 | Cross-system validátor | Druhý audit a Master Architecture Baseline |

## Pravidla repa

- `w0N-original.md` se obsahově nemění. Opravily se jen zbytky z kopírování z ChatGPT UI. Rozpory se řeší v `audit/CONFLICTS.md` a v doplňcích, ne přepisem originálu.
- Rozpory mezi W01–W09 vznikly paralelní prací naslepo. Nejde o chybu autora.
- Diagramy se píší jako ```` ```mermaid ```` bloky, GitHub je vykreslí.
- Nový výzkum: zadání do `audit/`, výstup do `research/worker-0N/w0N-supplement-NN.md`, nové rozpory do `audit/CONFLICTS.md`.
