# RG-183 — souhrn ručního testu

## Odpovědi uživatele (doslova)

| Jízda | planet | descent | note | roads | colours | stutter | holes |
|---|---|---|---|---|---|---|---|
| Sestup z orbity (20 000 km) na P1 (planet-max, 120 s + 60 s visení, Esc konec) | ano | ano | - | - | - | - | - |
| P1 jízda autem po silnici pobřežním městem (planet-max, R = reset, Esc konec) | - | - | - | ano (silnice jsou rozmazané, jen to jen náznak. Ale k pobřeží jsem dojel, projel jsem vodu (auto se potopilo) a projel se po pobřeží.) | ano (Jsou hodně low res (placeholdery), ale jde rozpoznat tráva vs silnice vs písek na pláži. Voda je také jasná.) | ne | - |
| P2 jízda autem (roh krychle, les) - jen cukání a díry (planet-max, Esc konec) | - | - | - | - | - | ne | ne |

Komentář uživatele po jízdách (chat, 2026-11-16, doslova): „jízda odjetá, ve 3. jízdě jsem se spawnul na zelené ploše, bez lesa, trávy a silnic- nebylo co zkoumat.“

Orchestrátor: jízda 3 (P2) nic neodhalila. Les je ve v1 jen tmavší odstín, stromy ani textury nejsou (podle briefu jsou v samostatném lane). Spawn P2 tedy jízdu nevyužil, nejde o vadu v1, viz `follow-ups.md`.

## Průběh jízd

- Sestup z orbity (20 000 km) na P1 (planet-max, 120 s + 60 s visení, Esc konec): 159.6 s, exit 1, report -
  - OS metriky: GPU 3D med/P95 99.2/99.7 %; CPU med/P95 30/65 %; private max 662.2 MiB; working set max 536.4 MiB; GPU ded+shared max 224.9 MiB; thermal max 91.9 °C; vzorků 33; CSV D:\workspace\race_game-worktrees\claude-RG-183\artifacts\manual-tests\RG-183\orbit-descent-os.csv
- P1 jízda autem po silnici pobřežním městem (planet-max, R = reset, Esc konec): 159.9 s, exit 0, report -
  - OS metriky: GPU 3D med/P95 47.4/52.4 %; CPU med/P95 18/49 %; private max 719.2 MiB; working set max 591.9 MiB; GPU ded+shared max 247.1 MiB; thermal max 86.9 °C; vzorků 35; CSV D:\workspace\race_game-worktrees\claude-RG-183\artifacts\manual-tests\RG-183\p1-road-os.csv
- P2 jízda autem (roh krychle, les) - jen cukání a díry (planet-max, Esc konec): 52.4 s, exit 1, report -
  - OS metriky: GPU 3D med/P95 56.7/70.9 %; CPU med/P95 19/51 %; private max 698.9 MiB; working set max 567.9 MiB; GPU ded+shared max 241.6 MiB; thermal max 77.9 °C; vzorků 11; CSV D:\workspace\race_game-worktrees\claude-RG-183\artifacts\manual-tests\RG-183\p2-drive-os.csv

## Souhrn pro workera

Odjeto 3/3 jízd.
Odpovědi výše jsou uživatelovy doslova; stav ok/fail zapisuje orchestrátor (`record --verdict-file`).
