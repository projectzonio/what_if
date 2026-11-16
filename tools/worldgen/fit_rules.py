#!/usr/bin/env python
"""RG-109 rules fit: nightracer.worldgen.road-dataset.v1 folders -> world-rules.json (nightracer.worldgen.world-rules.v1), stdlib only.

  python -X utf8 -I tools/worldgen/fit_rules.py --dir artifacts/worldgen/osm-ustinad [--dir <more datasets>] --out artifacts/worldgen/world-rules.json [--md rules.md]

Fitted statistics per road class, never over all edges (track is 62 % of the km). Every rule block carries n, the source windows and
"provisional": true when n < 30; the generator may not use a provisional rule without an override. Methods:
- radius / grade: same samples and caps as dataset_stats.py (30 m three-point radius per ~10 m sample, grade over +-30 m, ground edges only).
  min_radius_m = p05 of the curve samples (radius < 1000 m); max_grade_pct = p99.
- segments: along chains of one class (degree-2 nodes contracted) a sample is a curve when radius < 1000 m, otherwise straight. Runs of equal
  kind are measured in metres. Runs end at junctions and edges shorter than 60 m, so they are biased short. curve_to_straight = curve m / straight m.
- junction filter: raw junction nodes (degree >= 3) within 25 m form one cluster = one intersection. Legs of `link` edges (ramps) and legs of
  a oneway pair of the same class with opposite flow that leave within 30 deg of each other (dual carriageway, Y-split) are dropped / merged
  into one leg. Edges that join two nodes of one cluster are not legs. A cluster with < 3 legs left is not an intersection. Real Y-forks of
  two-way roads stay (no 70 deg forced). A junction counts for every class that has a leg left.
- junction spacing: chain length between two different intersections; nodes that are no intersection are contracted when exactly two edges of
  the class meet there (ramps do not interrupt a motorway chain).
- spans: tunnel and bridge length; tunnel trigger = cover depth (DEM minus centreline, median over the span), bridge trigger = clearance
  (centreline minus DEM, median over the span). Both come from spans.json (graph.span_records).
Output is deterministic: sorted keys, values rounded to 0.01, no dates or paths, so the same input gives the same sha256.
"""
import argparse
import hashlib
import json
import math
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dataset_stats  # noqa: E402
import graph  # noqa: E402

SCHEMA = "nightracer.worldgen.world-rules.v1"
MIN_N = dataset_stats.MIN_N
CLUSTER_M = 25.0
SPLIT_DEG = 30.0
STRAIGHT_RADIUS_M = 1000.0
LEG_LIMIT_DEG = 70.0
FILES = ("provenance", "nodes", "edges", "profiles", "junctions", "spans")


# ---- numbers ----------------------------------------------------------------------------------------------------------

def block(per_source, qs=(0.05, 0.50, 0.95)):
    """Rule block from {source window: [values]}: n, n_by_source, sources, provisional and the quantiles pNN (pooled values)."""
    vals = [v for w in sorted(per_source) for v in per_source[w]]
    out = {"n": len(vals), "n_by_source": {w: len(per_source[w]) for w in sorted(per_source) if per_source[w]},
           "sources": [w for w in sorted(per_source) if per_source[w]], "provisional": len(vals) < MIN_N}
    for q in qs:
        out["p%02d" % round(q * 100)] = dataset_stats.pct(vals, q)
    return out


def run_lengths(flags, steps):
    """[(flag, metres)] runs of equal consecutive flags; each sample stands for its step."""
    runs = []
    for f, s in zip(flags, steps):
        if runs and runs[-1][0] == f:
            runs[-1][1] += s
        else:
            runs.append([f, s])
    return [(f, m) for f, m in runs]


def angle_gaps(bearings):
    b = sorted(bearings)
    return [(b[(i + 1) % len(b)] - b[i]) % 360.0 for i in range(len(b))] if len(b) >= 2 else []


def bearing_diff(a, b):
    d = abs(a - b) % 360.0
    return min(d, 360.0 - d)


# ---- junction filter --------------------------------------------------------------------------------------------------

def cluster_points(points, radius=CLUSTER_M):
    """points [(id, x, z)] -> list of sorted id lists; single linkage on distance <= radius (grid of cell size radius)."""
    parent = {p[0]: p[0] for p in points}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    grid = defaultdict(list)
    for pid, x, z in points:
        grid[(int(math.floor(x / radius)), int(math.floor(z / radius)))].append((pid, x, z))
    for (cx, cz), cell in grid.items():
        for dx in (-1, 0, 1):
            for dz in (-1, 0, 1):
                for pid, x, z in cell:
                    for qid, qx, qz in grid.get((cx + dx, cz + dz), ()):
                        if qid > pid and math.hypot(qx - x, qz - z) <= radius:
                            parent[find(qid)] = find(pid)
    groups = defaultdict(list)
    for pid in sorted(parent):
        groups[find(pid)].append(pid)
    return sorted(groups.values())


def merge_dual_legs(legs, split_deg=SPLIT_DEG):
    """legs [(bearing, edge id, class, oneway, leaving)]: pairs of oneway legs of one class with opposite flow and bearings within split_deg
    are one carriageway pair -> one leg (circular mean bearing). Closest pairs first. Returns (legs, merged pair count)."""
    cand = sorted((bearing_diff(a[0], b[0]), i, j) for i, a in enumerate(legs) for j, b in enumerate(legs)
                  if i < j and a[3] and b[3] and a[2] == b[2] and a[4] != b[4] and bearing_diff(a[0], b[0]) < split_deg)
    used, out, n = set(), [], 0
    for _, i, j in cand:
        if i in used or j in used:
            continue
        used.update((i, j))
        a, b = legs[i], legs[j]
        x = math.sin(math.radians(a[0])) + math.sin(math.radians(b[0]))
        y = math.cos(math.radians(a[0])) + math.cos(math.radians(b[0]))
        out.append((round(math.degrees(math.atan2(x, y)) % 360.0, 1), min(a[1], b[1]), a[2], True, a[4]))
        n += 1
    out += [l for i, l in enumerate(legs) if i not in used]
    return sorted(out, key=lambda l: (l[0], l[1])), n


def filter_junctions(nodes, edges, juncs, cluster_m=CLUSTER_M, split_deg=SPLIT_DEG):
    """Raw junction records -> intersections. Returns (list of {nodes, legs, classes, min_leg_angle_deg}, counts of what was dropped).
    A cluster that keeps fewer than 3 legs is not an intersection (it is listed in counts['clusters'] only)."""
    eid = {e["id"]: e for e in edges}
    by_node = {j["node"]: j for j in juncs}
    pos = {n["id"]: n["pos"] for n in nodes}
    clusters = cluster_points([(j["node"], pos[j["node"]][0], pos[j["node"]][2]) for j in juncs], cluster_m)
    out = []
    cnt = Counter(raw_nodes=len(juncs), clusters=len(clusters))
    for members in clusters:
        ms = set(members)
        legs = []
        for n in members:
            j = by_node[n]
            for b, ei in zip(j["bearings"], j["leg_edges"]):
                e = eid[ei]
                if e["a"] in ms and e["b"] in ms:
                    cnt["internal_legs"] += 1
                elif e["class"] == "link":
                    cnt["link_legs"] += 1
                else:
                    legs.append((b, ei, e["class"], e["oneway"], e["a"] == n))
        legs, merged = merge_dual_legs(legs, split_deg)
        cnt["merged_pairs"] += merged
        out.append({"nodes": members, "legs": legs, "classes": sorted({l[2] for l in legs}),
                    "min_leg_angle_deg": round(min(angle_gaps([l[0] for l in legs])), 1) if len(legs) >= 3 else None})
    real = [c for c in out if c["min_leg_angle_deg"] is not None]
    cnt["intersections"] = len(real)
    return out, dict(cnt)


# ---- collection per dataset -------------------------------------------------------------------------------------------

def blank():
    return {k: [] for k in ("radius", "grade", "straight", "curve", "spacing", "degree", "leg_angle", "leg_before", "tunnel_len", "tunnel_cover",
                            "bridge_len", "bridge_clear")}


def segment_runs(edges, profs):
    """Straight / curve runs along chains of one class: {class: [(is_curve, metres)]}."""
    runs_by = defaultdict(list)
    pm = {p["edge"]: p for p in profs}
    for ch in graph.chains(edges, lambda e: e["class"]):
        runs = runs_by[edges[ch["edges"][0][0]]["class"]]
        flags, steps = [], []

        def flush():
            if flags:
                runs.extend(run_lengths(flags, steps))
            flags.clear()
            steps.clear()

        for ei, fwd in ch["edges"]:
            p = pm.get(ei)
            if p is None or not p["curv_valid"]:
                flush()  # shorter than 60 m: no curvature, the run is cut
                continue
            kap = p["curvature"] if fwd else p["curvature"][::-1]
            if flags:
                kap = kap[1:]  # the end sample of the previous edge is the same point
            flags.extend(abs(k) * STRAIGHT_RADIUS_M > 1.0 for k in kap)
            steps.extend([p["step_m"]] * len(kap))
        flush()
    return runs_by


def spacing_lengths(edges, intersections):
    """Chain lengths per class between two different intersections (see module doc). Returns {class: [m]}."""
    node_x = {}
    for i, c in enumerate(intersections):
        if c["min_leg_angle_deg"] is not None:
            for n in c["nodes"]:
                node_x[n] = i
    out = defaultdict(list)
    for cls in graph.CLASSES:
        es = [e for e in edges if e["class"] == cls and e["a"] != e["b"]]
        inc = defaultdict(list)
        for e in es:
            inc[e["a"]].append(e["id"])
            inc[e["b"]].append(e["id"])
        parent = {e["id"]: e["id"] for e in es}

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        through = {n for n, l in inc.items() if n not in node_x and len(l) == 2}
        for n in through:
            parent[find(inc[n][0])] = find(inc[n][1])
        comp = defaultdict(lambda: [0.0, []])
        for e in es:
            r = comp[find(e["id"])]
            r[0] += e["length_m"]
            r[1].extend(n for n in (e["a"], e["b"]) if n not in through)
        for _, (length, ends) in sorted(comp.items()):
            if len(ends) == 2 and all(n in node_x for n in ends) and node_x[ends[0]] != node_x[ends[1]]:
                out[cls].append(length)
    return out


def collect(data):
    """One dataset -> ({class: blank() lists}, filter summary)."""
    nodes, edges, profs = data["nodes"]["items"], data["edges"]["items"], data["profiles"]["items"]
    juncs, spans = data["junctions"]["items"], data["spans"]["items"]
    out = defaultdict(blank)
    eid = {e["id"]: e for e in edges}
    for p in profs:
        e = eid[p["edge"]]
        c = out[e["class"]]
        if p["curv_valid"]:
            i0, i1 = p["curv_valid"]
            c["radius"].extend(dataset_stats.RADIUS_CAP_M if abs(k) < 1.0 / dataset_stats.RADIUS_CAP_M else 1.0 / abs(k) for k in p["curvature"][i0:i1 + 1])
        if e["span"] == "ground":
            c["grade"].extend(abs(g) for g in p["grade_pct"])
    for cls, runs in segment_runs(edges, profs).items():
        for is_curve, m in runs:
            out[cls]["curve" if is_curve else "straight"].append(m)
    inter, cnt = filter_junctions(nodes, edges, juncs)
    for c in inter:
        if c["min_leg_angle_deg"] is None:
            continue
        deg = len(c["legs"])
        for cls in c["classes"]:
            out[cls]["leg_angle"].append(c["min_leg_angle_deg"])
            out[cls]["degree"].append(deg)
    for j in juncs:
        for cls in sorted(set(j["leg_classes"])):
            out[cls]["leg_before"].append(j["min_leg_angle_deg"])
    for cls, vals in spacing_lengths(edges, inter).items():
        out[cls]["spacing"].extend(vals)
    for s in spans:
        c = out[s["class"]]
        if s["kind"] == "tunnel":
            c["tunnel_len"].append(s["length_m"])
            if s.get("cover_depth_m") is not None:
                c["tunnel_cover"].append(s["cover_depth_m"])
        elif s["kind"] == "bridge":
            c["bridge_len"].append(s["length_m"])
            if s.get("clearance_m") is not None:
                c["bridge_clear"].append(s["clearance_m"])
    allbefore = [j["min_leg_angle_deg"] for j in juncs]
    allafter = [c["min_leg_angle_deg"] for c in inter if c["min_leg_angle_deg"] is not None]
    summ = {"counts": cnt, "before": allbefore, "after": allafter, "max_cluster_nodes": max((len(c["nodes"]) for c in inter), default=0)}
    return out, summ


# ---- fit ---------------------------------------------------------------------------------------------------------------

def fit(datasets):
    """datasets [(window name, data dict)] -> rules dict (not yet rounded for the file; all values already come from dataset_stats.pct = 0.01)."""
    per = {}
    summaries = {}
    for name, data in datasets:
        per[name], summaries[name] = collect(data)
    classes = [c for c in graph.CLASSES if any(c in per[w] for w in per)]
    rules = {}
    prov = []

    def src(metric, cls):
        return {w: per[w][cls][metric] for w in sorted(per) if cls in per[w]}

    for cls in classes:
        radius = block(src("radius", cls))
        curves = block({w: [r for r in v if r < STRAIGHT_RADIUS_M] for w, v in src("radius", cls).items()})
        radius["min_radius_m"] = curves["p05"]
        radius["curve_samples"] = {k: curves[k] for k in ("n", "p05", "p50", "provisional")}
        grade = block(src("grade", cls), (0.5, 0.95, 0.99))
        grade["max_grade_pct"] = grade["p99"]
        st, cu = block(src("straight", cls)), block(src("curve", cls))
        tot = lambda m: sum(sum(v) for v in src(m, cls).values())
        ratio_n = min(st["n"], cu["n"])
        segments = {"straight_radius_m": STRAIGHT_RADIUS_M, "straight_run_m": st, "curve_run_m": cu,
                    "curve_to_straight": {"value": round(tot("curve") / tot("straight"), 2) if tot("straight") > 0 else None, "n": ratio_n,
                                          "sources": sorted(set(st["sources"]) & set(cu["sources"])), "provisional": ratio_n < MIN_N}}
        before = block(src("leg_before", cls))
        after = block(src("leg_angle", cls))
        lt = lambda b: round(sum(1 for v in b if v < LEG_LIMIT_DEG) / len(b), 2) if b else None
        after["share_lt_70"] = lt([v for w in sorted(src("leg_angle", cls)) for v in src("leg_angle", cls)[w]])
        after["before"] = {"n": before["n"], "p05": before["p05"], "p50": before["p50"],
                           "share_lt_70": lt([v for w in sorted(src("leg_before", cls)) for v in src("leg_before", cls)[w]])}
        degs = [d for w in sorted(src("degree", cls)) for d in src("degree", cls)[w]]
        dsh = {"n": len(degs), "sources": block(src("degree", cls))["sources"], "provisional": len(degs) < MIN_N,
               "share_3": round(degs.count(3) / len(degs), 2) if degs else None, "share_4": round(degs.count(4) / len(degs), 2) if degs else None,
               "share_5plus": round(sum(1 for d in degs if d >= 5) / len(degs), 2) if degs else None}
        rules[cls] = {
            "radius": radius, "grade": grade, "segments": segments,
            "junctions": {"spacing_m": block(src("spacing", cls)), "degree": dsh, "leg_angle_deg": after},
            "spans": {"tunnel": {"length_m": block(src("tunnel_len", cls)), "cover_depth_m": block(src("tunnel_cover", cls))},
                      "bridge": {"length_m": block(src("bridge_len", cls)), "clearance_m": block(src("bridge_clear", cls))}}}

    def walk(node, path):
        if isinstance(node, dict):
            if node.get("provisional") is True:
                prov.append(".".join(path))
            for k in sorted(node):
                if isinstance(node[k], dict):
                    walk(node[k], path + [k])
    for cls in classes:
        walk(rules[cls], [cls])
    flt = {}
    for w in sorted(summaries):
        s = summaries[w]
        b, a = s["before"], s["after"]
        lt = lambda v: round(sum(1 for x in v if x < LEG_LIMIT_DEG) / len(v), 2) if v else None
        flt[w] = {"counts": s["counts"], "max_cluster_nodes": s["max_cluster_nodes"],
                  "before": {"n": len(b), "p05": dataset_stats.pct(b, 0.05), "p50": dataset_stats.pct(b, 0.5), "share_lt_70": lt(b)},
                  "after": {"n": len(a), "p05": dataset_stats.pct(a, 0.05), "p50": dataset_stats.pct(a, 0.5), "share_lt_70": lt(a)}}
    return {"schema": SCHEMA, "min_n": MIN_N, "sources": sorted(per),
            "parameters": {"cluster_m": CLUSTER_M, "dual_split_deg": SPLIT_DEG, "straight_radius_m": STRAIGHT_RADIUS_M, "leg_limit_deg": LEG_LIMIT_DEG,
                           "rounding": 0.01},
            "junction_filter": flt, "provisional_rules": sorted(prov), "classes": rules}


def dumps(rules):
    return json.dumps(rules, indent=1, sort_keys=True, allow_nan=False) + "\n"


def markdown(rules):
    f = lambda v: "-" if v is None else ("%g" % v)
    mark = lambda b: " (prov.)" if b.get("provisional") else ""
    lines = ["Sources: %s. Provisional = n < %d (not usable by the generator without an override)." % (", ".join(rules["sources"]), rules["min_n"]), ""]
    lines += ["| class | radius n | min radius (p05 of curves < 1000 m) | radius p05 / p50 / p95 (m) | grade n | grade p50 / p95 / max=p99 (%) |", "|---|---|---|---|---|---|"]
    for k, c in rules["classes"].items():
        r, g = c["radius"], c["grade"]
        lines.append("| %s | %d%s | %s (curves n=%d%s) | %s / %s / %s | %d%s | %s / %s / %s |" % (k, r["n"], mark(r), f(r["min_radius_m"]), r["curve_samples"]["n"],
                     mark(r["curve_samples"]), f(r["p05"]), f(r["p50"]), f(r["p95"]), g["n"], mark(g), f(g["p50"]), f(g["p95"]), f(g["max_grade_pct"])))
    lines += ["", "| class | straight runs n, p05 / p50 / p95 (m) | curve runs n, p05 / p50 / p95 (m) | curve : straight (length) |", "|---|---|---|---|"]
    for k, c in rules["classes"].items():
        s = c["segments"]
        a, b, q = s["straight_run_m"], s["curve_run_m"], s["curve_to_straight"]
        lines.append("| %s | %d%s, %s / %s / %s | %d%s, %s / %s / %s | %s%s |" % (k, a["n"], mark(a), f(a["p05"]), f(a["p50"]), f(a["p95"]), b["n"], mark(b),
                     f(b["p05"]), f(b["p50"]), f(b["p95"]), f(q["value"]), mark(q)))
    lines += ["", "| class | spacing n, p05 / p50 / p95 (m) | intersections n, degree 3 / 4 / 5+ share | leg angle p05 / p50 (deg) after, share < 70 deg | before: p05, share < 70 deg |",
              "|---|---|---|---|---|"]
    for k, c in rules["classes"].items():
        j = c["junctions"]
        sp, dg, la = j["spacing_m"], j["degree"], j["leg_angle_deg"]
        lines.append("| %s | %d%s, %s / %s / %s | %d%s, %s / %s / %s | %s / %s, %s | %s, %s |" % (k, sp["n"], mark(sp), f(sp["p05"]), f(sp["p50"]), f(sp["p95"]), dg["n"], mark(dg),
                     f(dg["share_3"]), f(dg["share_4"]), f(dg["share_5plus"]), f(la["p05"]), f(la["p50"]), f(la["share_lt_70"]), f(la["before"]["p05"]), f(la["before"]["share_lt_70"])))
    lines += ["", "| class | tunnel length n, p05 / p50 / p95 (m) | tunnel cover depth n, p50 (m) | bridge length n, p05 / p50 / p95 (m) | bridge clearance n, p50 (m) |", "|---|---|---|---|---|"]
    for k, c in rules["classes"].items():
        t, b = c["spans"]["tunnel"], c["spans"]["bridge"]
        lines.append("| %s | %d%s, %s / %s / %s | %d%s, %s | %d%s, %s / %s / %s | %d%s, %s |" % (k, t["length_m"]["n"], mark(t["length_m"]), f(t["length_m"]["p05"]), f(t["length_m"]["p50"]),
                     f(t["length_m"]["p95"]), t["cover_depth_m"]["n"], mark(t["cover_depth_m"]), f(t["cover_depth_m"]["p50"]), b["length_m"]["n"], mark(b["length_m"]),
                     f(b["length_m"]["p05"]), f(b["length_m"]["p50"]), f(b["length_m"]["p95"]), b["clearance_m"]["n"], mark(b["clearance_m"]), f(b["clearance_m"]["p50"])))
    lines += ["", "Provisional rules (%d): %s" % (len(rules["provisional_rules"]), ", ".join(rules["provisional_rules"]) or "none"), "", "Junction filter:", "",
              "| window | raw nodes | clusters | intersections | internal legs | link legs dropped | pairs merged | leg angle p05 / p50 before | share < 70 before | p05 / p50 after | share < 70 after |", "|---|---|---|---|---|---|---|---|---|---|---|"]
    for w, s in rules["junction_filter"].items():
        c, b, a = s["counts"], s["before"], s["after"]
        lines.append("| %s | %d | %d | %d | %d | %d | %d | %s / %s | %s | %s / %s | %s |" % (w, c["raw_nodes"], c["clusters"], c["intersections"], c.get("internal_legs", 0), c.get("link_legs", 0),
                     c.get("merged_pairs", 0), f(b["p05"]), f(b["p50"]), f(b["share_lt_70"]), f(a["p05"]), f(a["p50"]), f(a["share_lt_70"])))
    return "\n".join(lines) + "\n"


def load(dir_):
    data = {}
    for name in FILES:
        with open(os.path.join(dir_, name + ".json"), encoding="utf-8") as fh:
            data[name] = json.load(fh)
    w = (data["provenance"].get("window") or {}).get("name") or os.path.basename(os.path.normpath(dir_))
    return w, data


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", action="append", required=True, help="dataset folder (repeat for more windows / KN5 maps)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--md", default=None, help="also write the rule tables as markdown")
    a = ap.parse_args(argv)
    datasets = [load(d) for d in a.dir]
    names = [n for n, _ in datasets]
    if len(set(names)) != len(names):
        print("fit_rules: duplicate window names %s" % names, file=sys.stderr)
        return 2
    rules = fit(datasets)
    text = dumps(rules)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    if a.md:
        with open(a.md, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(markdown(rules))
    print(markdown(rules))
    print("world-rules.json sha256 %s (%d bytes, %d provisional rules)" % (hashlib.sha256(text.encode("utf-8")).hexdigest(), len(text), len(rules["provisional_rules"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
