#!/usr/bin/env python3
"""RG-118: grade p50/p95 per road class of generated worlds against the fitted rule p50/p99 and Sierra.
RG-123: smallest vertical radius per class (crest / sag, from profiles.json) with the lowest lift-off speed sqrt(g R) and the design minimum.

Stdlib only. Reads edges.json centrelines (flat x,y,z triples) from one or more CLI output
directories, weights each segment grade by its horizontal length and prints one table.
The km shares of cut/fill/tunnel/viaduct and max cut/fill come from the CLI (--terrain-stats,
terrain-stats.json) and are not repeated here.

usage: terrain_stats.py --rules artifacts/worldgen/world-rules.json <out-dir> [<out-dir> ...]
"""
import argparse
import json
import math
import os

SIERRA = (4.6, 12.4)  # KN5 Sierra reference grade p50/p95 in % (private map, reference number only)
ORDER = ["motorway", "link", "primary", "secondary", "local"]


def weighted_pct(pairs, q):
    """pairs = [(value, weight)], q in 0..1 -> weighted quantile."""
    if not pairs:
        return None
    pairs = sorted(pairs)
    total = sum(w for _, w in pairs)
    acc = 0.0
    for v, w in pairs:
        acc += w
        if acc >= q * total:
            return v
    return pairs[-1][0]


def grades(out_dir):
    with open(os.path.join(out_dir, "edges.json"), encoding="utf-8") as f:
        items = json.load(f)["items"]
    by_cls = {}
    for e in items:
        c = e["centreline"]
        lst = by_cls.setdefault(e["class"], [])
        for i in range(0, len(c) - 3, 3):
            dx, dy, dz = c[i + 3] - c[i], c[i + 4] - c[i + 1], c[i + 5] - c[i + 2]
            h = math.hypot(dx, dz)
            if h > 1e-6:
                lst.append((abs(dy) / h * 100.0, h))
    return by_cls


VRADIUS_MIN = {"motorway": (6000, 3000), "link": (1000, 600), "primary": (2500, 1500), "secondary": (1200, 800), "local": (400, 300), "track": (200, 150)}


def vradii(out_dir):
    """class -> [min crest radius, min sag radius] over the edges of one dataset (None when the dataset has no vradius fields)."""
    try:
        with open(os.path.join(out_dir, "edges.json"), encoding="utf-8") as f:
            cls = {e["id"]: e["class"] for e in json.load(f)["items"]}
        with open(os.path.join(out_dir, "profiles.json"), encoding="utf-8") as f:
            profs = json.load(f)["items"]
    except OSError:
        return {}
    out = {}
    for p in profs:
        for j, k in enumerate(("vradius_crest_min_m", "vradius_sag_min_m")):
            v = p.get(k)
            if v is not None:
                cur = out.setdefault(cls[p["edge"]], [None, None])
                cur[j] = v if cur[j] is None else min(cur[j], v)
    return out


def liftoff_kmh(r):
    return None if r is None else math.sqrt(9.81 * r) * 3.6


def fmt(v):
    return "  -  " if v is None else "%5.1f" % v


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--rules", required=True)
    ap.add_argument("dirs", nargs="+")
    a = ap.parse_args()
    with open(a.rules, encoding="utf-8") as f:
        rules = json.load(f)["classes"]
    pooled = {}
    for d in a.dirs:
        for cls, lst in grades(d).items():
            pooled.setdefault(cls, []).extend(lst)
    print("grade %% (length-weighted, %d world(s)); rule = fitted p50/p99; Sierra = %.1f/%.1f" % ((len(a.dirs),) + SIERRA))
    print("%-10s %7s %6s %6s | %8s %8s" % ("class", "km", "p50", "p95", "rule p50", "rule p99"))
    for cls in ORDER + sorted(set(pooled) - set(ORDER)):
        lst = pooled.get(cls)
        if not lst:
            continue
        g = rules.get(cls, {}).get("grade", {})
        km = sum(w for _, w in lst) / 1000.0
        print("%-10s %7.1f %6s %6s | %8s %8s" % (cls, km, fmt(weighted_pct(lst, 0.5)), fmt(weighted_pct(lst, 0.95)),
                                                fmt(g.get("p50")), fmt(g.get("p99"))))
    vr = {}
    for d in a.dirs:
        for cls, (c, s) in vradii(d).items():
            cur = vr.setdefault(cls, [None, None])
            for j, v in enumerate((c, s)):
                if v is not None:
                    cur[j] = v if cur[j] is None else min(cur[j], v)
    if vr:
        print()
        print("vertical radius (min over %d world(s)); lift-off = sqrt(g R) of the crest" % len(a.dirs))
        print("%-10s %9s %9s %12s | %9s %9s" % ("class", "crest m", "sag m", "lift-off km/h", "min crest", "min sag"))
        for cls in ORDER + sorted(set(vr) - set(ORDER)):
            if cls in vr:
                c, s = vr[cls]
                m = VRADIUS_MIN.get(cls, (None, None))
                print("%-10s %9s %9s %12s | %9s %9s" % (cls, "-" if c is None else "%.0f" % c, "-" if s is None else "%.0f" % s,
                                                       fmt(liftoff_kmh(c)), m[0], m[1]))
    allg = [p for lst in pooled.values() for p in lst]
    print("%-10s %7.1f %6s %6s | Sierra   %5.1f %5.1f (p50/p95)" % (("all", sum(w for _, w in allg) / 1000.0,
                                                                    fmt(weighted_pct(allg, 0.5)), fmt(weighted_pct(allg, 0.95))) + SIERRA))


if __name__ == "__main__":
    main()
