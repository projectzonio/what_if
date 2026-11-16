#!/usr/bin/env python
"""RG-106 data sample: road-geometry statistics from the RG-085 reset-points json (stdlib only).

Input : tools/kn5-unity/reset-points/<map>.json  (schema nightracer.reset-points.v1; one checkpoint about every 100 m of the road network,
        Kn5RoadNetwork.SpacingM; "node" = snapped raster node (Unity x, y, z), "yaw" = growth direction in degrees, "heading" = yaw is reliable).
Output: artifacts/worldgen/<map>.road-stats.json (gitignored, ADR-018: derived from third-party maps, never committed).

The json holds the checkpoints only, not the graph. The graph is rebuilt here as a Gabriel graph over the checkpoint nodes (edges <= --max-edge m, layers kept
apart by a grade gate), which is good enough for statistics but NOT a surveyed road graph. Resolution limit: with ~100 m between nodes a three-point
circumradius cannot see curves tighter than about 50 m radius and smooths short bends; see "limits" in the output.

Usage: python -X utf8 -I tools/worldgen/road_stats.py --map sierra [--map gunsai ...] [--out artifacts/worldgen]
"""
import argparse
import json
import math
import os
import sys
from collections import defaultdict

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SPACING_M = 100.0


def load(map_name):
    path = os.path.join(REPO, "tools", "kn5-unity", "reset-points", map_name + ".json")
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    if d.get("schema") != "nightracer.reset-points.v1":
        raise SystemExit("unexpected schema %r in %s" % (d.get("schema"), path))
    return path, d["points"]


def pct(vals, q):
    if not vals:
        return None
    s = sorted(vals)
    i = (len(s) - 1) * q
    lo, hi = int(math.floor(i)), int(math.ceil(i))
    return round(s[lo] + (s[hi] - s[lo]) * (i - lo), 3)


def dist_summary(vals, qs=(0.05, 0.25, 0.5, 0.75, 0.95, 0.99)):
    if not vals:
        return {"n": 0}
    out = {"n": len(vals), "min": round(min(vals), 3), "max": round(max(vals), 3), "mean": round(sum(vals) / len(vals), 3)}
    for q in qs:
        out["p%02d" % round(q * 100)] = pct(vals, q)
    return out


def histogram(vals, edges):
    """counts for [e0,e1), [e1,e2) ... and the open last bin"""
    counts = [0] * len(edges)
    for v in vals:
        k = 0
        for i, e in enumerate(edges):
            if v >= e:
                k = i
        counts[k] += 1
    labels = ["%g-%g" % (edges[i], edges[i + 1]) for i in range(len(edges) - 1)] + [">=%g" % edges[-1]]
    return dict(zip(labels, counts))


def axis_diff(bearing, yaw_deg):
    """angle between an undirected edge bearing and a growth direction (Unity yaw: 0 = +z, 90 = +x), radians in 0..pi/2"""
    d = abs((bearing - math.radians(yaw_deg) + math.pi) % (2 * math.pi) - math.pi)
    return min(d, math.pi - d)


def build_graph(nodes, pts, min_edge, max_edge, max_grade, max_align_deg):
    """Gabriel graph over (x, z); an edge is min_edge..max_edge long, |dy|/d <= max_grade, and follows the growth direction (yaw) of each
    heading-reliable endpoint within max_align_deg; a witness must be on the same layer (|dy| <= 4 m to both ends)."""
    cell = max_edge
    grid = defaultdict(list)
    for i, (x, y, z) in enumerate(nodes):
        grid[(int(x // cell), int(z // cell))].append(i)

    def near(i):
        x, _, z = nodes[i]
        cx, cz = int(x // cell), int(z // cell)
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for j in grid.get((cx + dx, cz + dz), ()):
                    if j != i:
                        yield j

    edges = []
    for i in range(len(nodes)):
        xi, yi, zi = nodes[i]
        for j in near(i):
            if j < i:
                continue
            xj, yj, zj = nodes[j]
            d = math.hypot(xj - xi, zj - zi)
            if d > max_edge or d < min_edge or abs(yj - yi) / d > max_grade:
                continue
            bearing = math.atan2(xj - xi, zj - zi)
            if any(pts[k].get("heading") and axis_diff(bearing, pts[k]["yaw"]) > math.radians(max_align_deg) for k in (i, j)):
                continue
            mx, mz, r2 = (xi + xj) / 2, (zi + zj) / 2, (d / 2) ** 2
            ok = True
            for k in near(i):
                if k == j:
                    continue
                xk, yk, zk = nodes[k]
                if abs(yk - yi) > 4 or abs(yk - yj) > 4:
                    continue
                if (xk - mx) ** 2 + (zk - mz) ** 2 < r2:
                    ok = False
                    break
            if ok:
                edges.append((i, j, d))
    return edges


def components(n, edges):
    parent = list(range(n))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for a, b, _ in edges:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb
    sizes = defaultdict(int)
    for i in range(n):
        sizes[find(i)] += 1
    return sorted(sizes.values(), reverse=True)


def circumradius(a, b, c):
    """horizontal circumradius of three (x, z) points; inf when collinear"""
    ax, az, bx, bz, cx, cz = a[0], a[2], b[0], b[2], c[0], c[2]
    ab, bc, ca = math.hypot(bx - ax, bz - az), math.hypot(cx - bx, cz - bz), math.hypot(ax - cx, az - cz)
    area2 = abs((bx - ax) * (cz - az) - (cx - ax) * (bz - az))   # twice the area
    if area2 < 1e-9:
        return float("inf")
    return ab * bc * ca / (2 * area2)


def analyse(map_name, args):
    path, pts = load(map_name)
    nodes = [tuple(p["node"]) for p in pts]
    n = len(nodes)
    edges = build_graph(nodes, pts, args.min_edge, args.max_edge, args.max_grade, args.max_align)
    adj = defaultdict(list)
    for a, b, d in edges:
        adj[a].append((b, d))
        adj[b].append((a, d))
    deg = [len(adj[i]) for i in range(n)]
    comp = components(n, edges)

    # grade per edge (signed value is meaningless for an undirected edge: use the absolute value, %)
    grades = [abs(nodes[b][1] - nodes[a][1]) / d * 100.0 for a, b, d in edges]

    # curvature: every degree-2 node with both neighbours 60..140 m away is the middle of a three-point arc
    radii, chain = [], 0
    for i in range(n):
        if deg[i] != 2:
            continue
        (a, da), (b, db) = adj[i]
        if not (60 <= da <= 140 and 60 <= db <= 140):
            continue
        chain += 1
        r = circumradius(nodes[a], nodes[i], nodes[b])
        radii.append(min(r, 5000.0))   # straight sections cap at 5 km
    curvy = [r for r in radii if r < 5000.0]

    # layers: a node has a stacked partner when another node lies within 25 m horizontally and 3..30 m above/below (bridge / viaduct / tunnel over road)
    cell = 25.0
    grid = defaultdict(list)
    for i, (x, y, z) in enumerate(nodes):
        grid[(int(x // cell), int(z // cell))].append(i)
    upper = lower = 0
    for i, (x, y, z) in enumerate(nodes):
        cx, cz = int(x // cell), int(z // cell)
        has_above = has_below = False
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for j in grid.get((cx + dx, cz + dz), ()):
                    if j == i or math.hypot(nodes[j][0] - x, nodes[j][2] - z) > cell:
                        continue
                    dy = nodes[j][1] - y
                    if 3.0 <= dy <= 30.0:
                        has_above = True
                    elif -30.0 <= dy <= -3.0:
                        has_below = True
        if has_below:
            upper += 1   # something lies below this node: it is the bridge / upper layer
        if has_above:
            lower += 1

    xs, ys, zs = [p[0] for p in nodes], [p[1] for p in nodes], [p[2] for p in nodes]
    total_edge_m = sum(d for _, _, d in edges)
    heading = sum(1 for p in pts if p.get("heading"))
    out = {
        "schema": "nightracer.worldgen.road-stats.v1",
        "map": map_name,
        "source": os.path.relpath(path, REPO).replace("\\", "/"),
        "parameters": {"gabriel_min_edge_m": args.min_edge, "gabriel_max_edge_m": args.max_edge, "max_align_deg": args.max_align, "max_edge_grade": args.max_grade, "checkpoint_spacing_m": SPACING_M},
        "extent_m": {"x": [round(min(xs), 1), round(max(xs), 1)], "z": [round(min(zs), 1), round(max(zs), 1)], "y": [round(min(ys), 1), round(max(ys), 1)],
                     "bbox_km2": round((max(xs) - min(xs)) * (max(zs) - min(zs)) / 1e6, 2),
                     "relief_m": round(max(ys) - min(ys), 1)},
        "network": {
            "checkpoints": n,
            "length_km_nominal": round(n * SPACING_M / 1000.0, 2),   # n x spacing: the definition of the sampling
            "length_km_graph_edges": round(total_edge_m / 1000.0, 2),  # sum of Gabriel edges, includes some cross links
            "edges": len(edges),
            "components": len(comp),
            "component_sizes_top5": comp[:5],
            "singleton_components": sum(1 for c in comp if c == 1),
            "degree_histogram": {str(k): deg.count(k) for k in sorted(set(deg))},
            "junction_nodes_deg3plus": sum(1 for d in deg if d >= 3),
            "dead_end_nodes_deg1": sum(1 for d in deg if d == 1),
            "heading_reliable_share": round(heading / n, 4),
            "edge_length_m": dist_summary([d for _, _, d in edges]),
        },
        "grade_pct_abs_per_edge": dist_summary(grades),
        "grade_histogram_pct": histogram(grades, [0, 2, 4, 6, 8, 10, 12]),
        "curvature": {
            "arc_samples": len(radii),
            "radius_m_all_capped_5km": dist_summary(radii),
            "radius_m_curves_only_lt_5km": dist_summary(curvy),
            "radius_histogram_m": histogram(radii, [0, 50, 100, 200, 400, 800, 1600, 5000]),
            "min_radius_m": round(min(radii), 1) if radii else None,
            "share_radius_lt_100m": round(sum(1 for r in radii if r < 100) / len(radii), 4) if radii else None,
        },
        "layers": {
            "upper_layer_nodes": upper,
            "upper_layer_share": round(upper / n, 4),
            "lower_layer_nodes_with_road_above": lower,
        },
        "limits": [
            "Graph is rebuilt from checkpoints every ~100 m (Gabriel graph); real junction topology, lane count, width, banking, tunnels are NOT in this json.",
            "Three-point radius at ~100 m spacing cannot resolve curves tighter than about 50 m radius (hairpins read too wide); shares of small radii are lower bounds.",
            "Grade is the absolute rise over ~100 m chords: short steep ramps are averaged out.",
            "Layer share counts stacked nodes within 25 m horizontally and 3..30 m vertically; layers 3+ collapse in Kn5RoadNetwork (known limit, RG-085).",
            "Derived from third-party map data (ADR-018/023/027): output stays in artifacts/, never in Git or a build.",
        ],
    }
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--map", action="append", required=True, help="gunsai | sierra | kanazawa (repeatable)")
    ap.add_argument("--out", default=os.path.join(REPO, "artifacts", "worldgen"))
    ap.add_argument("--min-edge", type=float, default=60.0, help="shortest edge in metres (shorter pairs are parallel cross-sections / raster noise)")
    ap.add_argument("--max-edge", type=float, default=150.0, help="longest Gabriel edge in metres")
    ap.add_argument("--max-align", type=float, default=50.0, help="largest angle between an edge and the road direction (yaw) of a heading-reliable end, degrees")
    ap.add_argument("--max-grade", type=float, default=0.20, help="steepest allowed edge (rise/run); steeper pairs are different layers")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)
    for m in args.map:
        res = analyse(m, args)
        target = os.path.join(args.out, m + ".road-stats.json")
        with open(target, "w", encoding="utf-8", newline="\n") as f:
            json.dump(res, f, indent=2)
            f.write("\n")
        net, cur = res["network"], res["curvature"]
        print("%s: %d cp, %.1f km nominal, %d edges, %d components, %d junction nodes, grade p50/p95 %s/%s %%, radius p50/p05 %s/%s m, upper layer %.1f %% -> %s" % (
            m, net["checkpoints"], net["length_km_nominal"], net["edges"], net["components"], net["junction_nodes_deg3plus"],
            res["grade_pct_abs_per_edge"].get("p50"), res["grade_pct_abs_per_edge"].get("p95"),
            cur["radius_m_all_capped_5km"].get("p50"), cur["radius_m_all_capped_5km"].get("p05"),
            res["layers"]["upper_layer_share"] * 100, os.path.relpath(target, REPO)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
