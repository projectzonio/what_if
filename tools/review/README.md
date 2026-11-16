# tools/review — orchestrátorské pomůcky

Python 3 stdlib (jen čtení), PowerShell pro čekání. Převzato z race_game `tools/review` (2026-11-16); `manual-test.py` a `recompute-s1.py` (ruční jízdy, Unity měření) se nepřenášely.

| nástroj | k čemu |
| --- | --- |
| `branch-scope.py` | rozsah větve ticketu proti `### Exact write ownership` v briefu; padá na dotčené read-only cestě, binárce, souboru nad 1 MB, poolových souborech na větvi |
| `make-prompt.py` | z „Prepared handoff“ briefu vyrobí startovací/rework/review prompt (`agent_pool/shared/prompts/`) |
| `trim-handover.py` | přesune z `orchestrator-state.md` staré/uzavřené bloky do archivu; exit 1 nad 12 kB |
| `run-bg.ps1`, `wait-run.ps1` | dlouhý běh na pozadí s `.exit` markerem a čekání s jedním výstupním řádkem (žádné `until grep`) |
| `test_wait_run.ps1` | self-test čekacích skriptů (součást `scripts/test.ps1`) |

```powershell
python -X utf8 -I tools\review\branch-scope.py --worktree <worktree> --brief agent_pool\shared\briefs\WI-0xx.md [--base <rev>] [--head <rev>]
python -X utf8 -I tools\review\make-prompt.py start WI-0xx --print
python -X utf8 -I tools\review\trim-handover.py
```

Výstup `branch-scope.py` ukládej jako evidence review (`agent_pool/agents/<agent>/logs/WI-0xx-review-check.txt`).

## Dlouhý běh: `run-bg.ps1` + `wait-run.ps1`

Nekončící čekací smyčka je to nejdražší (race_game RG-119: 12-14 h, `until grep "^done" out.txt` nikdy nesedělo). `run-bg.ps1` spustí jeden příkaz, zapíše stdout+stderr do `<OutDir>/<Name>.log` a VŽDY `<OutDir>/<Name>.exit` s návratovým kódem. `wait-run.ps1 -ExitFile <...>.exit -TimeoutS 2700` čeká a skončí jedním řádkem (exit 0 hotovo, 2 timeout: čekání zopakuj). Obojí spouštěj s `run_in_background: true`; žádné čekání nad ~45 min.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\review\run-bg.ps1 -Name build -OutDir artifacts\tmp -- scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\review\wait-run.ps1 -ExitFile artifacts\tmp\build.exit -TimeoutS 2700
```
