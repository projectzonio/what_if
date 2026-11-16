#!/usr/bin/env python
"""RG-116 street topology metrics for a road-dataset v1 folder (stdlib only). One code path for a generated export and for the OSM window.

  python -X utf8 -I tools/worldgen/street_stats.py --dir artifacts/worldgen/gen-1 --label gen-1 --city 0,0,1700
  python -X utf8 -I tools/worldgen/street_stats.py --dir artifacts/worldgen/osm-ustinad --label osm --centre-window 1500

Writes <dir>/street-stats.json and prints a table. Scopes (each is a set of local-class nodes/edges):
  all     every local edge of the folder
  city    local edges whose midpoint lies within --city x,z,r  (generated: the city disk plus its noise; default none)
  centre  local edges whose midpoint lies inside the square |x|,|z| <= --centre-window around the origin (OSM: the window origin is the Usti centre,
          50.66 N 14.04 E, see provenance.json)
Metrics per scope:
  t_share        deg-3 nodes / nodes with deg >= 3 among nodes touching a local edge (degree = the folder's node degree, all classes)
  spacing_p50    median length of local-edge chains (degree-2 nodes of the same class contracted) between two nodes of deg >= 3
  phi            street orientation order (Boeing 2019): 36 bins of 10 deg, length-weighted, each edge counted in both directions
                 (bearing and bearing + 180); phi = 1 - ((H - Hg)/(Hmax - Hg))^2, Hmax = ln 36, Hg = ln 4 (a perfect grid). 1.0 = grid, 0 = uniform.
                 Bearings come from the edge centreline segments (so curves count by their pieces, not by the chord).
  dead_end_share nodes with degree 1 / nodes touching a local edge
"""
import argparse
import json
import math
import os
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import graph  # noqa: E402

BINS = 36


def load(d, name):
    with open(os.path.join(d, name + ".json"), encoding="utf-8") as f:
        return json.load(f)["items"]


def orientation_phi(edges):
    """Boeing 2019 orientation order from centreline pieces of the given edges. Returns None without any length."""
    w = [0.0] * BINS
    for e in edges:
        c = e["centreline"]
        for i in range(0, len(c) - 3, 3):
            dx, dz = c[i + 3] - c[i], c[i + 5] - c[i + 2]
            ln = math.hypot(dx, dz)
            if ln < 1e-6:
                continue
            b = math.degrees(math.atan2(dx, dz)) % 360.0   # bearing from north
            for bb in (b, (b + 180.0) % 360.0):
                w[int(bb // (360.0 / BINS)) % BINS] += ln
    tot = sum(w)
    if tot <= 0:
        return None
    h = -sum(x / tot * math.log(x / tot) for x in w if x > 0)
    hmax, hg = math.log(BINS), math.log(4)
    return 1.0 - ((h - hg) / (hmax - hg)) ** 2


def pct(v, q):
    if not v:
        return None
    s = sorted(v)
    return s[min(len(s) - 1, int(q * len(s)))]


def midpoint(e):
    c = e["centreline"]
    n = len(c) // 3
    k = (n // 2) * 3
    return c[k], c[k + 2]


def scope_metrics(nodes, local, in_scope, chains):
    sel = [e for e in local if in_scope(e)]
    touch = set()
    for e in sel:
        touch.add(e["a"])
        touch.add(e["b"])
    deg = {n: nodes[n]["degree"] for n in touch}
    j = [n for n in touch if deg[n] >= 3]
    d3 = sum(1 for n in j if deg[n] == 3)
    hist = defaultdict(int)
    for n in j:
        hist[str(deg[n])] += 1
    sp = [c["length"] for c in chains if nodes[c["a"]]["degree"] >= 3 and nodes[c["b"]]["degree"] >= 3 and c["a"] != c["b"]
          and c["a"] in touch and c["b"] in touch]
    phi = orientation_phi(sel)
    return {
        "local_edges": len(sel), "local_km": round(sum(e["length_m"] for e in sel) / 1000.0, 2), "junction_nodes": len(j),
        "degree_hist": dict(sorted(hist.items())),
        "t_share": round(d3 / len(j), 4) if j else None,
        "spacing_n": len(sp), "spacing_p05": pct(sp, 0.05), "spacing_p50": pct(sp, 0.5), "spacing_p95": pct(sp, 0.95),
        "phi": None if phi is None else round(phi, 4),
        "dead_end_share": round(sum(1 for n in touch if deg[n] == 1) / len(touch), 4) if touch else None,
    }


def compute(nodes, edges, city=None, centre=None):
    local = [e for e in edges if e["class"] == "local"]
    # graph.chains indexes edges by id: hand it only the local ones, re-indexed
    remap = [dict(e, id=i) for i, e in enumerate(local)]
    chains = graph.chains(remap, lambda e: e["class"])
    out = {"all": scope_metrics(nodes, local, lambda e: True, chains)}
    if city:
        cx, cz, cr = city
        out["city"] = scope_metrics(nodes, local, lambda e: math.hypot(midpoint(e)[0] - cx, midpoint(e)[1] - cz) <= cr, chains)
    if centre:
        out["centre"] = scope_metrics(nodes, local, lambda e: abs(midpoint(e)[0]) <= centre and abs(midpoint(e)[1]) <= centre, chains)
    return out


def fmt(v, p=3):
    return "-" if v is None else ("%.*f" % (p, v) if isinstance(v, float) else str(v))


def table(label, res):
    lines = ["street-stats %s" % label,
             "| scope | local edges | km | junctions | T share | spacing p50 (m) | phi | dead ends | degree hist |", "|---|---|---|---|---|---|---|---|---|"]
    for k, m in res.items():
        lines.append("| %s | %d | %s | %d | %s | %s | %s | %s | %s |" % (k, m["local_edges"], m["local_km"], m["junction_nodes"], fmt(m["t_share"]),
                                                                      fmt(m["spacing_p50"], 1), fmt(m["phi"]), fmt(m["dead_end_share"]), m["degree_hist"]))
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", required=True)
    ap.add_argument("--label", default=None)
    ap.add_argument("--city", help="x,z,radius_m of the generated city disk")
    ap.add_argument("--centre-window", type=float, help="half-size in metres of the centre square (OSM)")
    a = ap.parse_args(argv)
    nodes = load(a.dir, "nodes")
    edges = load(a.dir, "edges")
    city = tuple(float(x) for x in a.city.split(",")) if a.city else None
    res = compute(nodes, edges, city, a.centre_window)
    label = a.label or os.path.basename(os.path.normpath(a.dir))
    print(table(label, res))
    with open(os.path.join(a.dir, "street-stats.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"schema": "nightracer.worldgen.street-stats.v1", "label": label, "scopes": res}, f, indent=2)
        f.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
