#!/usr/bin/env python
"""RG-107 statistics per road class from a nightracer.worldgen.road-dataset.v1 folder (stdlib only), in the style of road_stats.py.

Output: <dir>/stats.json and <dir>/stats.md (markdown tables for docs/worldgen-dataset.md). One command reproduces them:
  python -X utf8 -I tools/worldgen/dataset_stats.py --dir artifacts/worldgen/osm-ustinad

Methods (the numbers are only as good as these):
- Radius: 3-point circumradius of resampled points 30 m before / at / after each ~10 m sample (profiles.json "curvature", valid range curv_valid);
  edges shorter than 60 m give no sample. radius = 1/|curvature|, capped at 10000 m (straight). Each sample stands for ~10 m of road, so the
  percentiles are length-weighted. OSM vertex jitter of sigma metres gives a noise floor of about 30^2/(2*sigma) m (sigma 0.5 m -> ~900 m),
  so large radii are upper-noise, small radii (< 300 m) are real.
- Grade: height difference over +-30 m (60 m baseline) from the 30 m DEM, absolute value per sample, ground edges only (bridge/tunnel heights are
  interpolated between their ends, not measured).
- Junction spacing: length of chains of edges of one class between two nodes of degree >= 3 (degree-2 nodes of the same class are contracted).
- Degree: node degree of nodes with degree >= 3 that touch the class, one count per class.
- Spans: tunnel/bridge chains (consecutive edges of one span kind and class); a motorway with two carriageways gives two chains.
- A class/metric with n < 30 is flagged "n<30" and must not be used as a rule.
"""
import argparse
import json
import math
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import graph  # noqa: E402

RADIUS_CAP_M = 10000.0
MIN_N = 30


def pct(vals, q):
    if not vals:
        return None
    s = sorted(vals)
    i = (len(s) - 1) * q
    lo, hi = int(math.floor(i)), int(math.ceil(i))
    return round(s[lo] + (s[hi] - s[lo]) * (i - lo), 2)


def dist(vals, qs=(0.05, 0.50, 0.95)):
    out = {"n": len(vals)}
    if vals:
        out.update({"p%02d" % round(q * 100): pct(vals, q) for q in qs})
        out["mean"] = round(sum(vals) / len(vals), 2)
        out["max"] = round(max(vals), 2)
    if len(vals) < MIN_N:
        out["flag"] = "n<30"
    return out


def share(vals, pred):
    return round(sum(1 for v in vals if pred(v)) / len(vals), 4) if vals else None


def compute(nodes, edges, profs, juncs, spans, prov=None):
    by_class = defaultdict(lambda: {"edges": 0, "length_m": 0.0, "no_lanes": 0, "no_width": 0, "no_maxspeed": 0, "radius": [], "radius_edges": 0,
                                    "grade": [], "spacing": [], "degree": Counter(), "spans": defaultdict(list)})
    eid = {e["id"]: e for e in edges}
    for e in edges:
        c = by_class[e["class"]]
        c["edges"] += 1
        c["length_m"] += e["length_m"]
        c["no_lanes"] += e["lanes"] is None
        c["no_width"] += e["width_m"] is None
        c["no_maxspeed"] += e["maxspeed"] is None
    for p in profs:
        e = eid[p["edge"]]
        c = by_class[e["class"]]
        if p["curv_valid"]:
            i0, i1 = p["curv_valid"]
            c["radius"].extend(RADIUS_CAP_M if abs(k) < 1.0 / RADIUS_CAP_M else 1.0 / abs(k) for k in p["curvature"][i0:i1 + 1])
            c["radius_edges"] += 1
        if e["span"] == "ground":
            c["grade"].extend(abs(g) for g in p["grade_pct"])
    for ch in graph.chains(edges, lambda e: e["class"]):
        a, b = nodes[ch["a"]], nodes[ch["b"]]
        if a["degree"] >= 3 and b["degree"] >= 3 and ch["a"] != ch["b"]:
            by_class[edges[ch["edges"][0][0]]["class"]]["spacing"].append(ch["length"])
    inc = defaultdict(set)
    for e in edges:
        inc[e["a"]].add(e["class"])
        inc[e["b"]].add(e["class"])
    for n in nodes:
        if n["degree"] >= 3:
            for cls in inc[n["id"]]:
                by_class[cls]["degree"][n["degree"]] += 1
    for s in spans:
        by_class[s["class"]]["spans"][s["kind"]].append(s["length_m"])
    classes = {}
    for cls in graph.CLASSES:
        c = by_class.get(cls)
        if c is None:
            continue
        curves = [r for r in c["radius"] if r < 1000.0]
        classes[cls] = {
            "edges": c["edges"], "length_km": round(c["length_m"] / 1000.0, 1),
            "share_edges_no_lanes": round(c["no_lanes"] / c["edges"], 3), "share_edges_no_width": round(c["no_width"] / c["edges"], 3),
            "share_edges_no_maxspeed": round(c["no_maxspeed"] / c["edges"], 3),
            "radius_m": dict(dist(c["radius"]), edges_used=c["radius_edges"], share_lt_100=share(c["radius"], lambda r: r < 100),
                             share_lt_200=share(c["radius"], lambda r: r < 200), share_gt_1000=share(c["radius"], lambda r: r >= 1000)),
            "radius_curves_lt_1000m": dist(curves),
            "grade_abs_pct": dict(dist(c["grade"], (0.5, 0.95, 0.99)), share_gt_8=share(c["grade"], lambda g: g > 8)),
            "junction_spacing_m": dist(c["spacing"]),
            "junction_degree": {str(k): v for k, v in sorted(c["degree"].items())},
            "tunnels": dict(dist(c["spans"].get("tunnel", [])), total_km=round(sum(c["spans"].get("tunnel", [])) / 1000.0, 2)),
            "bridges": dict(dist(c["spans"].get("bridge", [])), total_km=round(sum(c["spans"].get("bridge", [])) / 1000.0, 2)),
        }
    legs = [j["min_leg_angle_deg"] for j in juncs]
    kinds = Counter(n["kind"] for n in nodes)
    out = {"schema": "nightracer.worldgen.road-dataset-stats.v1", "min_n": MIN_N,
           "window": (prov or {}).get("window", {}).get("name"), "extractor": (prov or {}).get("extractor"),
           "totals": {"nodes": len(nodes), "edges": len(edges), "length_km": round(sum(e["length_m"] for e in edges) / 1000.0, 1),
                      "node_kinds": dict(kinds), "edges_by_span": dict(Counter(e["span"] for e in edges))},
           "junction_min_leg_angle_deg": dict(dist(legs), share_lt_70=share(legs, lambda a: a < 70)),
           "classes": classes}
    return out


def markdown(st):
    f = lambda v: "-" if v is None else ("%g" % v)
    flag = lambda d: " (n<30)" if d.get("flag") else ""
    lines = ["Window %s: %d nodes, %d edges, %.1f km of road." % (st["window"], st["totals"]["nodes"], st["totals"]["edges"], st["totals"]["length_km"]), ""]
    lines += ["| class | edges | km | no lanes | no width | no maxspeed |", "|---|---|---|---|---|---|"]
    for k, c in st["classes"].items():
        lines.append("| %s | %d | %s | %.0f %% | %.0f %% | %.0f %% |" % (k, c["edges"], f(c["length_km"]), 100 * c["share_edges_no_lanes"], 100 * c["share_edges_no_width"], 100 * c["share_edges_no_maxspeed"]))
    lines += ["", "| class | radius samples n | radius p05 / p50 / p95 (m) | share < 100 m | share < 200 m | curves < 1000 m: n, p05 / p50 (m) |", "|---|---|---|---|---|---|"]
    for k, c in st["classes"].items():
        r, cu = c["radius_m"], c["radius_curves_lt_1000m"]
        lines.append("| %s | %d%s | %s / %s / %s | %s | %s | %d, %s / %s%s |" % (k, r["n"], flag(r), f(r.get("p05")), f(r.get("p50")), f(r.get("p95")),
                     f(r["share_lt_100"]), f(r["share_lt_200"]), cu["n"], f(cu.get("p05")), f(cu.get("p50")), flag(cu)))
    lines += ["", "| class | grade samples n | grade abs p50 / p95 / p99 (%) | share > 8 % |", "|---|---|---|---|"]
    for k, c in st["classes"].items():
        g = c["grade_abs_pct"]
        lines.append("| %s | %d%s | %s / %s / %s | %s |" % (k, g["n"], flag(g), f(g.get("p50")), f(g.get("p95")), f(g.get("p99")), f(g["share_gt_8"])))
    lines += ["", "| class | junction spacing n | spacing p05 / p50 / p95 (m) | degree 3 / 4 / 5+ (nodes) |", "|---|---|---|---|"]
    for k, c in st["classes"].items():
        s, d = c["junction_spacing_m"], c["junction_degree"]
        d5 = sum(v for dk, v in d.items() if int(dk) >= 5)
        lines.append("| %s | %d%s | %s / %s / %s | %d / %d / %d |" % (k, s["n"], flag(s), f(s.get("p05")), f(s.get("p50")), f(s.get("p95")), d.get("3", 0), d.get("4", 0), d5))
    lines += ["", "| class | tunnels n | km | length p50 / p95 / max (m) | bridges n | km | length p50 / p95 / max (m) |", "|---|---|---|---|---|---|---|"]
    for k, c in st["classes"].items():
        t, b = c["tunnels"], c["bridges"]
        lines.append("| %s | %d%s | %s | %s / %s / %s | %d%s | %s | %s / %s / %s |" % (k, t["n"], flag(t) if t["n"] else "", f(t["total_km"]), f(t.get("p50")), f(t.get("p95")), f(t.get("max")),
                     b["n"], flag(b) if b["n"] else "", f(b["total_km"]), f(b.get("p50")), f(b.get("p95")), f(b.get("max"))))
    a = st["junction_min_leg_angle_deg"]
    lines += ["", "Junction min leg angle (n=%d): p05 %s / p50 %s deg, share < 70 deg: %s." % (a["n"], f(a.get("p05")), f(a.get("p50")), f(a["share_lt_70"]))]
    return "\n".join(lines) + "\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", required=True)
    a = ap.parse_args(argv)

    def ld(name):
        with open(os.path.join(a.dir, name + ".json"), encoding="utf-8") as f:
            return json.load(f)

    st = compute(ld("nodes")["items"], ld("edges")["items"], ld("profiles")["items"], ld("junctions")["items"], ld("spans")["items"], ld("provenance"))
    with open(os.path.join(a.dir, "stats.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(st, f, indent=1, allow_nan=False)
    md = markdown(st)
    with open(os.path.join(a.dir, "stats.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(md)
    print(md)
    return 0


if __name__ == "__main__":
    sys.exit(main())
