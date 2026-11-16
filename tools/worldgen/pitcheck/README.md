# pitcheck — headless mesh check of the worldgen ground (RG-127)

Runs the generator and `WorldGenMap` (the same classes as the player) **without Unity**, builds the 3x3 sectors around a node or a point and checks that the
ground / portal-pit mesh has no open edge. Origin: the RG-120 harness that found the hidden `InTunnel` wedge bug no Unity run had shown.

## Build and run

```
dotnet build tools/worldgen/pitcheck -c Release        # output: artifacts/bin/pitcheck/release/pitcheck.dll (central Directory.Build.props)
dotnet artifacts/bin/pitcheck/release/pitcheck.dll --node 945 --seed 1 [--range 30] [--map]
dotnet artifacts/bin/pitcheck/release/pitcheck.dll --at -2670,-3787
dotnet artifacts/bin/pitcheck/release/pitcheck.dll --portals --seed 2 [--max-list 10]
```

References: the game scripts as `check.dll`, `NightRacer.Streaming.dll`, `WhatIf.WorldGen.dll` from `D:/workspace/dev-cache/compile-check/<worktree>/` and the
Unity 6000.3.26f1 `UnityEngine.CoreModule.dll` stub. If `check.dll` is missing the build runs `tools/game-unity/compile-check.ps1` first (it needs a
`Library/ScriptAssemblies` of any worktree, see that script). After changing `src/WhatIf.WorldGen` or `Assets/Scripts` rerun
`compile-check.ps1` (the csproj reuses an existing `check.dll`, it does not rebuild it). No Unity process, no notebook slot.

| Argument | Meaning |
|---|---|
| `--root` | repo root (default: the folder with `AGENTS.md` above the current directory) |
| `--spec`, `--rules` | default `tools/worldgen/specs/terrain-10km.json`, `artifacts/worldgen/world-rules.json` (git-ignored, copy it into a fresh worktree) |
| `--seed N` | default 1 |
| `--node <id>` / `--at x,z` / `--portals` | exactly one: a graph node (also lists its legs: edge, span, width, length, end; RG-138), a world position, or every tunnel portal node of the world (table node / closed / open edges) |
| `--range m` | half size of the checked square around the target, default 30 |
| `--map` | ASCII top view, north up, 1 m cells: digit = ground metres above the centre height (`+` ≥ 10, `-` below), `R` road, `T` tube, `B` bridge, `F`/`f` pit floor above/below, `.` nothing |
| `--max-list N` | how many open edges to print (default 25) |
| `--rails` | RG-136: viaduct railing report: every deck segment side without a barrier (edge, k, side, x, z, deck y, reason `junctionEnd`/`roadBelow`, other edge, other is bridge, deck minus other road height) + totals per reason. `--at x,z --range m` limits the list, `--rail-tol m` sets the height gate (default 0.5, negative = pre-RG-136 behaviour). Example: `pitcheck.dll --rails --seed 1 --max-list 50` |
| `--edges-near m` | RG-139: every road edge with a sample within m of the target: span, layer, class, width, nearest segment, lateral offset (left +), height interpolated at the closest point, its span clearance. For "which edge / which deck is this finding on". |
| `--drop-one` | self-check (RG-130): removes the pit wall triangle (area > 0.05 m²) nearest the target that no road/tube/bridge covers and prints where; the place must then report open (exit 1) |

Exit codes: **0** closed, **1** open edges (listed as `OPEN edge (x,y,z) -> (x,y,z)`), **2** input error (unknown argument, missing file, node not in the graph).

## What it checks

- Every edge of the ground triangles (tile grid, omitted-triangle pit floor, pit wall columns) must be used by exactly two triangles, except on a sector border
  (the neighbour sector closes it). A tile edge meets the wall columns as a T-junction (watertight, not welded), so every ground/pit edge is split at each ground/pit vertex lying on it within 1 cm, at any fraction (RG-130; before: whole metres only, so a wall column at a fractional position, such as the door-boundary column, read as open).
- An edge used once that lies against a road / tube / bridge surface (same height ±1.5 m, within 0.3 m) is counted as sealed by that surface and only
  reported as a number (`sealed by a road/tube/bridge surface`). This is a heuristic: it hides a gap smaller than 0.3 m and a gap under a surface of the same height.

## What it cannot check

Runtime (streaming order, unload while in contact), whether colliders are actually built from the meshes, the rain/PathBot sweep results (`worldgen-sweep`),
lighting, materials, the Unity mesh import (welding, 16-bit index limits). A closed mesh here is necessary, not sufficient.

## Example (before RG-130, terrain seed 1)

```
node 945 at -2670.0, -3786.9 y 101.7 kind portal
... sealed by a road/tube/bridge surface within 0.3 m (not counted): 54
OPEN edge (-2684.0,101.5,-3773.0) -> (-2684.0,105.3,-3774.0)
OPEN edge (-2684.0,101.5,-3801.0) -> (-2684.0,105.3,-3800.0)
boundary edges not on a tile border within 30 m: 2  -> OPEN          (exit 1)
```

Example from RG-127/128: the two edges are the wall bottom running as a wedge from the floor to the tube wall top at the door boundary. Since RG-130 (door-boundary column pair in `PitWall`) every portal of terrain seeds 1-3 is closed (0 / 0 / 0); `--drop-one` at node 964 (seed 2) reports 2 open edges, exit 1. Older portal tables for
seeds 1-3: `agent_pool/agents/claude/outbox/RG-127_portal-table.md`. Open edges are findings for the generator/adapter owner; the tool does not fix them.
