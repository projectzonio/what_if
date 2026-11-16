# RG-179 — souhrn ručního testu

## Odpovědi uživatele (doslova)

| Jízda | planet | holes | stutter | note | horizon | pops |
|---|---|---|---|---|---|---|
| P1 pobřežní město: jízda autem (planet-max, R = reset, Esc konec) | ne (zelená a šedá plocha, lehké náznaky modelu krajiny, chybí silnice, kopce....) | ne | ano (zaregistroval jsem 3 micro-lagy, jako v l2j při přechodu mezi sektory.) | - | - | - |
| P2 vrchol krychle (les, tři stěny se stýkají): jízda autem | - | ne | ne | - | - | - |
| P3 let 1000 m nad P2 (horizont a vzdálená vrstva, 120 s) | - | - | ne | - | ano | ne (moc detailů k posouzení tam není (okem člověka).) |

## Závěrečné otázky

- Celkově: je to dobrý základ pro další vývoj (poznámka k barvám, silnicím, vzdálené vrstvě)?: Celé je to placeholder, chybí silnice, celá scéna je zelená, šedá, modrá a písková (pobřeží) barva. Planetu jako celek z orbity nevidím. Tohle bylo jako test dlaždic při 1. testu.

## Průběh jízd

- P1 pobřežní město: jízda autem (planet-max, R = reset, Esc konec): 196.8 s, exit 0, report -
  - OS metriky: GPU 3D med/P95 52/54.3 %; CPU med/P95 12/19 %; private max 726.8 MiB; working set max 597.2 MiB; GPU ded+shared max 241.4 MiB; thermal max 69.9 °C; vzorků 42; CSV D:\workspace\race_game-worktrees\claude-RG-179\artifacts\manual-tests\RG-179\p1drive-os.csv
- P2 vrchol krychle (les, tři stěny se stýkají): jízda autem: 70.8 s, exit 0, report -
  - OS metriky: GPU 3D med/P95 54/62.6 %; CPU med/P95 12/20 %; private max 713.6 MiB; working set max 584.5 MiB; GPU ded+shared max 244.1 MiB; thermal max 72.9 °C; vzorků 15; CSV D:\workspace\race_game-worktrees\claude-RG-179\artifacts\manual-tests\RG-179\p2drive-os.csv
- P3 let 1000 m nad P2 (horizont a vzdálená vrstva, 120 s): 130.9 s, exit 0, report -
  - OS metriky: GPU 3D med/P95 98.9/99.5 %; CPU med/P95 25/64 %; private max 703.2 MiB; working set max 573.5 MiB; GPU ded+shared max 226.9 MiB; thermal max 82.9 °C; vzorků 28; CSV D:\workspace\race_game-worktrees\claude-RG-179\artifacts\manual-tests\RG-179\p3fly-os.csv

## Souhrn pro workera

Odjeto 3/3 jízd.
Odpovědi výše jsou uživatelovy doslova; stav ok/fail zapisuje orchestrátor (`record --verdict-file`).
