# roadfeel — what a tyre feels on the worldgen road (RG-128)

Runs the generator and `WorldGenMap` (the same classes as the player) **without Unity**. Walks every road edge at 1 m stations on five lateral lines
(-0.9, -0.5, 0, 0.5, 0.9 of the half width), takes the highest collider surface (road mesh or ground tile) between design height - 1 m and + 2 m
and reports

1. where something stands above the design surface by more than `--over` (default 2 cm), road mesh or GROUND;
2. **lips**: the driven surface jumps by at least 5 cm between two stations 1 m apart (a speed bump for the tyre). Exit 1 when one is >= `--lip-limit` (default 20 cm).

Origin: the RG-128 user verdict "retardér před a za viaduktem": two ribbons of one junction differed by up to 0.98 m where a ramp leaves the motorway.
The sweep (rain / flood) does not see this, it looks for holes; the tyre meets edges.

## Build and run

```
dotnet build tools/worldgen/roadfeel -c Release        # output: artifacts/bin/roadfeel/release/roadfeel.dll
dotnet artifacts/bin/roadfeel/release/roadfeel.dll --seed 1 [--spec tools/worldgen/specs/default-10km.json] [--csv artifacts/tmp/rf1.csv] [--edge 553 --dump-edges]
python -X utf8 tools/worldgen/roadfeel/census.py artifacts/tmp/rf1.csv      # per-metre census from the CSV: ground above design, lips by size, worst edge each
```

Same references and traps as `tools/worldgen/pitcheck`: the build copies `check.dll`, `NightRacer.Streaming.dll`, `WhatIf.WorldGen.dll` from
`D:/workspace/dev-cache/compile-check/<worktree>/`. After a source change run `tools/game-unity/compile-check.ps1` **and rebuild roadfeel**, otherwise it measures the old copy
in `artifacts/bin/roadfeel/release/` (RG-128: the numbers did not move for that reason). Needs `artifacts/worldgen/world-rules.json` (git-ignored).

| Argument | Meaning |
|---|---|
| `--seed N`, `--spec` | default 1, `terrain-10km.json` (a flat spec works too) |
| `--over m` | threshold for "above design", default 0.02 |
| `--lip-limit m` | exit 1 from this lip size, default 0.20 |
| `--csv file` | every sample `edge,s,lat,x,z,design,top,road` (raw CSVs > 1 MB stay local) |
| `--edge id` (repeatable), `--dump-edges` | only these edges; print their ends |

Exit codes: **0** no lip >= `--lip-limit`, **1** lips, **2** input error.

## Edge 0 does not count

Edge 0 starts at the map border (x = -5000, node degree 1). The sector beyond the border does not exist, so its first metres have a ground-only surface and show a
1-station jump that no driver can meet (the car cannot be there). It is printed separately as `edge 0 not counted`.

## Baseline (RG-128 rework 1, terrain seeds 1/2/3)

lips >= 20 cm: 0 / 0 / 0. lips >= 5 cm: 46 / 30 / 37 (before the ramp pin and the strip overlay: hundreds, with 0.98 m between ramp and motorway).
The remaining ones sit at 4 m from a node (the cut line) at lat 0: a sibling leg's road at node height ends there while the ribbon on its own profile already fell 5-6 cm.

## What it cannot check

Anything that is not geometry in plan and height: friction, the physics material of a patch, the Unity mesh collider cooking, tunnels (skipped), bridges' rails.
