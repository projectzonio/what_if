#!/usr/bin/env python3
"""RG-129 geometry probe, Python side: case list for the Unity probe, and all analysis of the measured grids.

    cases   --map gunsai|kanazawa|sierra --out cases.json     (from artifacts/worldgen/kn5-<map>/{nodes,edges,spans,raw}.json)
    analyse --cases cases.json --grid grid.json --out report.json

Unity (Kn5GeometryProbe) only casts vertical rays on a 1 m grid and keeps every hit on a road collider (all heights per cell). Everything here is arithmetic on those grids,
so `analyse` reruns without Unity and is deterministic (no randomness; `cases` uses a seeded random.Random).

Method in one paragraph: a leg of a node is followed on the grid from the node outwards at 1 m, taking in each cell the hit nearest the previous height. A jump >= LAYER (1.5 m) in one
metre is another layer (overpass), counted separately and never a lip. A cell with no road hit is a gap; the line then looks +-1/+-2 m sideways (counted as `recentred`). A lip is the height
change between two consecutive followed points; across-lips are the same along a line perpendicular to the leg inside the road half width. Nothing here uses the dataset heights except to
start the walk and in the frame control. Limits are listed in agent_pool/agents/claude/outbox/RG-129-states.md.
"""
import argparse, json, math, os, random, statistics, sys

LAYER = 1.5          # m per 1 m of travel: another layer, not a lip
LIP_LEVELS = (0.10, 0.20, 0.30)
FORK_DEG = 40.0
SIZES = {"fork": 120, "junction": 80, "span_end": 80, "plain": 60, "frame": 4}
LEG_M = {"fork": 56, "junction": 36, "span_end": 36, "plain": 28}
PLAIN_N, FRAME_N = 30, 100


# ------------------------------------------------------------------------------------------------------------------------------ cases
def load_map(mapname, dirpath=None):
    """dataset files of a KN5 map (world = dataset + raster centre from raw.json) or of a --dir written by the generator harness (already world frame, offset 0)."""
    d = dirpath or os.path.join("artifacts", "worldgen", "kn5-" + mapname)
    j = lambda f: json.load(open(os.path.join(d, f + ".json"), encoding="utf-8"))
    if dirpath: ox, oz = 0.0, 0.0
    else:
        raw_meta = j("raw")["meta"]["raster"]
        ox, oz = raw_meta["x0"] + raw_meta["span_x"] / 2.0, raw_meta["z0"] + raw_meta["span_z"] / 2.0
    return j("nodes")["items"], j("edges")["items"], j("spans")["items"], (ox, oz)


def resample(flat, off, step=1.0, maxlen=None):
    """flat centreline [x,y,z,...] (dataset frame) -> [(x,y,z)] at `step` metres along the arc, in world coordinates."""
    pts = [(flat[i] + off[0], flat[i + 1], flat[i + 2] + off[1]) for i in range(0, len(flat), 3)]
    out, acc, nxt = [pts[0]], 0.0, step
    for a, b in zip(pts, pts[1:]):
        seg = math.dist((a[0], a[2]), (b[0], b[2]))
        while seg > 0 and acc + seg >= nxt - 1e-9:
            t = (nxt - acc) / seg
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t))
            nxt += step
            if maxlen and nxt > maxlen + 1e-9: return out
        acc += seg
    return out


def heading(pts, d=15):
    b = pts[min(len(pts) - 1, d)]
    a = pts[0]
    return math.degrees(math.atan2(b[2] - a[2], b[0] - a[0]))


def angdiff(a, b):
    return abs((a - b + 180.0) % 360.0 - 180.0)


def node_legs(nid, edges, off, length):
    legs = []
    for e in edges:
        if e["a"] != nid and e["b"] != nid: continue
        flat = e["centreline"]
        if e["a"] != nid:
            flat = [v for tri in reversed([flat[i:i + 3] for i in range(0, len(flat), 3)]) for v in tri]
        pts = resample(flat, off, 1.0, length)
        if len(pts) < 6: continue
        legs.append({"edge": e["id"], "cls": e["class"], "width": e["width_m"], "span": e["span"], "other": e["b"] if e["a"] == nid else e["a"],
                     "pts": [[round(v, 2) for v in p] for p in pts]})
    for lg in legs: lg["hdg"] = round(heading([tuple(p) for p in lg["pts"]]), 1)
    return legs


def make_cases(mapname, seed=129, dirpath=None):
    nodes, edges, spans, off = load_map(mapname, dirpath)
    cases = []
    ends = {}
    for s in spans:
        for nid in (s["start_node"], s["end_node"]): ends.setdefault(nid, []).append(s["kind"])
    for n in nodes:
        pos = (n["pos"][0] + off[0], n["pos"][1], n["pos"][2] + off[1])
        legs = None
        if n["degree"] >= 3:
            legs = node_legs(n["id"], edges, off, LEG_M["fork"])
            best = min((angdiff(a["hdg"], b["hdg"]) for i, a in enumerate(legs) for b in legs[i + 1:]), default=999)
            cls = "fork" if best <= FORK_DEG else "junction"
            if cls == "junction": legs = node_legs(n["id"], edges, off, LEG_M["junction"])
            cases.append({"id": "%s-n%d" % (cls, n["id"]), "cls": cls, "cx": round(pos[0], 2), "cz": round(pos[2], 2), "size": SIZES[cls], "y": round(pos[1], 2),
                          "node": n["id"], "min_leg_deg": round(best, 1), "legs": legs})
        if n["id"] in ends:
            legs = node_legs(n["id"], edges, off, LEG_M["span_end"])
            cases.append({"id": "span_end-n%d" % n["id"], "cls": "span_end", "cx": round(pos[0], 2), "cz": round(pos[2], 2), "size": SIZES["span_end"], "y": round(pos[1], 2),
                          "node": n["id"], "kinds": sorted(set(ends[n["id"]])), "legs": legs})
    rnd = random.Random(seed)
    node_xz = [(n["pos"][0] + off[0], n["pos"][2] + off[1]) for n in nodes]
    cand = []
    for e in edges:
        if e["span"] != "ground": continue
        pts = resample(e["centreline"], off, 1.0)
        for i in range(101, len(pts) - 101, 10):
            x, y, z = pts[i]
            if min(math.dist((x, z), q) for q in node_xz) > 100: cand.append((e["id"], i))
    rnd.shuffle(cand)
    by_edge = {e["id"]: e for e in edges}
    chosen, used = [], []
    for eid, i in cand:
        pts = resample(by_edge[eid]["centreline"], off, 1.0)
        x, y, z = pts[i]
        if any(math.dist((x, z), u) < 70 for u in used): continue
        used.append((x, z)); chosen.append((eid, i, pts))
        if len(chosen) >= PLAIN_N: break
    for k, (eid, i, pts) in enumerate(sorted(chosen, key=lambda c: (c[0], c[1]))):
        x, y, z = pts[i]
        fw = [list(p) for p in pts[i:i + LEG_M["plain"] + 1]]
        bw = [list(p) for p in pts[max(0, i - LEG_M["plain"]):i + 1][::-1]]
        legs = [{"edge": eid, "cls": by_edge[eid]["class"], "width": by_edge[eid]["width_m"], "span": "ground", "pts": [[round(v, 2) for v in p] for p in L]}
                for L in (fw, bw)]
        cases.append({"id": "plain-e%d-%d" % (eid, i), "cls": "plain", "cx": round(x, 2), "cz": round(z, 2), "size": SIZES["plain"], "y": round(y, 2), "edge": eid, "legs": legs})
    allsamples = [(e["id"], i) for e in edges for i in range(0, len(e["centreline"]) // 3)]
    rnd2 = random.Random(seed + 1)
    for eid, i in sorted(rnd2.sample(allsamples, min(FRAME_N, len(allsamples)))):
        c = by_edge[eid]["centreline"]
        x, y, z = c[3 * i] + off[0], c[3 * i + 1], c[3 * i + 2] + off[1]
        cases.append({"id": "frame-e%d-%d" % (eid, i), "cls": "frame", "cx": round(x, 2), "cz": round(z, 2), "size": SIZES["frame"], "y": round(y, 2), "edge": eid})
    return {"map": mapname, "frame_offset": list(off), "cases": cases}


# ------------------------------------------------------------------------------------------------------------------------------ grid
class Grid:
    def __init__(self, g):
        self.id, self.n, self.x0, self.z0 = g["id"], g["n"], g["x0"], g["z0"]
        self.cells = [[v / 100.0 for v in c] for c in g["cells"]]

    def hits(self, x, z):
        """road heights at (x,z): per layer of the cell's own hits, bilinear between the 4 cell centres (a nearest-cell lookup reads slope x 1 m as a lip on a banked surface). Only corner values within LAYER of the own hit join; otherwise the own hit stays. Limit: a real step is spread over 1 m."""
        fx, fz = x - self.x0 - 0.5, z - self.z0 - 0.5
        i, j = math.floor(fx), math.floor(fz)
        ci, cj = math.floor(x - self.x0), math.floor(z - self.z0)
        if not (0 <= ci < self.n and 0 <= cj < self.n): return []
        tx, tz = fx - i, fz - j
        out = []
        for h in self.cells[cj * self.n + ci]:
            val = 0.0
            for ii, jj, w in ((i, j, (1 - tx) * (1 - tz)), (i + 1, j, tx * (1 - tz)), (i, j + 1, (1 - tx) * tz), (i + 1, j + 1, tx * tz)):
                near = [v for v in self.cells[jj * self.n + ii] if abs(v - h) < LAYER] if 0 <= ii < self.n and 0 <= jj < self.n else []
                if not near: val = None; break
                val += w * min(near, key=lambda v: abs(v - h))
            out.append(h if val is None else val)
        return out


def follow(grid, pts, h0):
    """Follow the surface along pts [(x,y,z)] (1 m apart). -> list of (h|None, flag), flag in ok|recentred|gap|layer|out. h0: start height (None: nearest to pts[0].y)."""
    out, prev = [], h0
    for k, p in enumerate(pts):
        x, z = p[0], p[2]
        c = grid.hits(x, z)
        flag = "ok"
        if not c:
            a, b = pts[max(0, k - 1)], pts[min(len(pts) - 1, k + 1)]
            tx, tz = b[0] - a[0], b[2] - a[2]
            tl = math.hypot(tx, tz) or 1.0
            nx, nz = -tz / tl, tx / tl
            for o in (1, -1, 2, -2):
                c = grid.hits(x + nx * o, z + nz * o)
                if c: flag = "recentred"; break
        if not c:
            out.append((None, "gap" if grid.hits(x, z) == [] and 0 <= math.floor(x - grid.x0) < grid.n and 0 <= math.floor(z - grid.z0) < grid.n else "out"))
            continue
        ref = prev if prev is not None else p[1]
        b = min(c, key=lambda h: abs(h - ref))
        if prev is None and abs(b - ref) > LAYER:
            out.append((None, "layer")); continue
        if prev is not None and abs(b - prev) >= LAYER:
            out.append((None, "layer")); continue
        prev = b
        out.append((b, flag))
    return out


def steps_of(fl):
    """lips along a followed line: |dh| between consecutive ok points (no gap or layer jump in between)."""
    st, last = [], None
    for k, (h, f) in enumerate(fl):
        if h is None: last = None; continue
        if last is not None: st.append((k, h - last))
        last = h
    return st


def detrend(steps, w=3):
    """lip = the step minus the local grade (median of the neighbouring steps within +-w m, same unbroken run): a plain slope gives no lip, a 30 cm step does."""
    byk = {k: d for k, d in steps}
    out = []
    for k, d in steps:
        nb = [byk[j] for j in range(k - w, k + w + 1) if j != k and j in byk]
        out.append((k, d - (statistics.median(nb) if nb else d)) if nb else (k, 0.0))
    return out


def lip_summary(steps):
    raw = max((abs(d) for _, d in steps), default=0.0)
    ds = detrend(steps)
    m = max((abs(d) for _, d in ds), default=0.0)
    return {"max": round(m, 3), "raw_max": round(raw, 3), "n": len(steps), **{"ge%02d" % round(t * 100): sum(1 for _, d in ds if abs(d) >= t) for t in LIP_LEVELS}}


def across_steps(grid, pts, fl, half):
    """lips across: at each followed station, walk sideways in 1 m steps up to `half` m on both sides, following the surface; the largest step per station."""
    res = []
    for k in range(1, len(pts) - 1):
        h = fl[k][0]
        if h is None: continue
        a, b = pts[k - 1], pts[k + 1]
        tx, tz = b[0] - a[0], b[2] - a[2]
        tl = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / tl, tx / tl
        prof, sd = {0: h}, []
        for sgn in (1, -1):
            prev = h
            for o in range(1, int(half) + 1):
                c = grid.hits(pts[k][0] + nx * o * sgn, pts[k][2] + nz * o * sgn)
                if not c: break
                bb = min(c, key=lambda v: abs(v - prev))
                if abs(bb - prev) >= LAYER: break
                sd.append((bb - prev) * sgn); prev = bb; prof[o * sgn] = bb   # signed step in the +offset direction
        tr = statistics.median(sd) if len(sd) >= 2 else 0.0                  # cross slope per metre
        res.append((k, max((abs(x - tr) for x in sd), default=0.0), prof))
    return res


def grid_adjacent_max(grid):
    """largest second difference |h[i+1] - 2 h[i] + h[i-1]| along either axis (raw cells, same layer guard): a plane gives 0, a step stays; where in the whole grid is a sharp edge."""
    n, best, at = grid.n, 0.0, None
    C = grid.cells
    for j in range(n):
        for i in range(n):
            b = C[j * n + i]
            if not b: continue
            for di, dj in ((1, 0), (0, 1)):
                if not (0 <= i - di and i + di < n and 0 <= j - dj and j + dj < n): continue
                a, c = C[(j - dj) * n + i - di], C[(j + dj) * n + i + di]
                for y in b:
                    xa, xc = [v for v in a if abs(v - y) < LAYER], [v for v in c if abs(v - y) < LAYER]
                    if not xa or not xc: continue
                    d = abs(min(xa, key=lambda v: abs(v - y)) + min(xc, key=lambda v: abs(v - y)) - 2 * y)
                    if d > best: best, at = d, (grid.x0 + i + .5, grid.z0 + j + .5)
    return round(best, 3), at


def surface_slope(grid):
    """median gradient magnitude (m/m) over single-layer 2x2 blocks of the grid: how tilted is the sheet."""
    n, C, g = grid.n, grid.cells, []
    for j in range(n - 1):
        for i in range(n - 1):
            a, b, c = C[j * n + i], C[j * n + i + 1], C[(j + 1) * n + i]
            if len(a) == 1 and len(b) == 1 and len(c) == 1 and abs(b[0] - a[0]) < LAYER and abs(c[0] - a[0]) < LAYER: g.append(math.hypot(b[0] - a[0], c[0] - a[0]))
    return round(statistics.median(g), 4) if g else None


# ------------------------------------------------------------------------------------------------------------------------------ per class analysis
def leg_follow(grid, case, leg):
    pts = [tuple(p) for p in leg["pts"]]
    fl = follow(grid, pts, None)
    return pts, fl


def hlist(fl): return [h for h, _ in fl]


def slope(h, a, b):
    ha, hb = h[a] if a < len(h) else None, h[b] if b < len(h) else None
    return None if ha is None or hb is None or b <= a else (hb - ha) / (b - a)


def stat_pair(vals):
    v = [x for x in vals if x is not None]
    return None if not v else round(statistics.median(v), 4)


def path_metrics(grid, pts, fl, width):
    half = max(1.0, min(4.0, (width or 6.0) / 2.0 - 0.75))
    along = lip_summary(steps_of(fl))
    ac = across_steps(grid, pts, fl, half)
    aw = max((w for _, w, _ in ac), default=0.0)
    cnt = {"ge%02d" % round(t * 100): sum(1 for _, w, _ in ac if w >= t) for t in LIP_LEVELS}
    flags = {k: sum(1 for _, f in fl if f == k) for k in ("ok", "recentred", "gap", "layer", "out")}
    return {"along": along, "across": {"max": round(aw, 3), "n": len(ac), **cnt}, "flags": flags, "half_m": half}, ac


def analyse_fork(grid, case):
    legs = case["legs"]
    best = min(((angdiff(a["hdg"], b["hdg"]), i, j) for i, a in enumerate(legs) for j, b in enumerate(legs) if i < j), default=None)
    if best is None: return {"unmeasured": "fewer than two legs"}
    _, ia, ib = best
    fa, fb = leg_follow(grid, case, legs[ia]), leg_follow(grid, case, legs[ib])
    (pa, la), (pb, lb) = fa, fb
    ha, hb = hlist(la), hlist(lb)
    L = min(len(pa), len(pb))
    sep_d, sep_type, sep_gap, kerb_d = None, None, 0, None
    cross = []
    for d in range(L):
        if ha[d] is None or hb[d] is None:
            if sep_d is None: sep_d, sep_type = d, "leg_off_road"
            continue
        s = math.dist((pa[d][0], pa[d][2]), (pb[d][0], pb[d][2]))
        if s < 1.5:      # legs inside one or two cells: still one sheet, nothing to test between them
            if s >= 3: cross.append((hb[d] - ha[d]) / s)
            continue
        nseg = max(1, math.ceil(s))
        seg = [(pa[d][0] + (pb[d][0] - pa[d][0]) * t / nseg, ha[d], pa[d][2] + (pb[d][2] - pa[d][2]) * t / nseg) for t in range(nseg + 1)]
        sf = follow(grid, seg, ha[d])
        gaps = sum(1 for h, _ in sf if h is None)
        mx = max((abs(x) for _, x in detrend(steps_of(sf))), default=0.0)   # a tilted sheet is not a step
        if sep_d is None and (gaps or mx > 0.15):
            sep_d, sep_type, sep_gap = d, ("gap" if gaps else "step"), gaps
            sep_step = mx
        if sep_d is None and s >= 3: cross.append((hb[d] - ha[d]) / s)
        if kerb_d is None and mx > 0.15: kerb_d = d
    sep_at = None
    if sep_d is not None and sep_d < L:
        sep_at = {"d_m": sep_d, "dist_between_legs_m": round(math.dist((pa[sep_d][0], pa[sep_d][2]), (pb[sep_d][0], pb[sep_d][2])), 2), "type": sep_type, "gap_cells": sep_gap}
        k = [h for h in (ha[sep_d], hb[sep_d]) if h is not None]
        sep_at["dh_m"] = round(abs(ha[sep_d] - hb[sep_d]), 3) if len(k) == 2 else None
    end_dh = None
    if ha[L - 1] is not None and hb[L - 1] is not None: end_dh = round(abs(ha[L - 1] - hb[L - 1]), 3)
    cover = round(sum(1 for c in grid.cells if c) / len(grid.cells), 3)
    sl = surface_slope(grid)
    apart = round(math.dist((pa[L - 1][0], pa[L - 1][2]), (pb[L - 1][0], pb[L - 1][2])), 1)
    drops = [k for k, bad in (("slope_ge15", sl is None or sl >= 0.15), ("coverage_ge70", cover >= 0.70), ("angle_lt3", best[0] < 3.0), ("legs_lt20m_apart", apart < 20.0)) if bad]
    return {"angle_deg": round(best[0], 1), "surface_slope_median": sl, "grid_coverage": cover, "legs_apart_end_m": apart, "candidate": not drops, "dropped_by": drops,
            "shared_len_m": sep_d if sep_d is not None else L, "never_separates_in_probe": sep_d is None, "cross_slope_shared_median": stat_pair(cross),
            "cross_slope_n": len(cross), "separation": sep_at, "dh_at_end_m": end_dh, "probe_len_m": L,
            "paths": [path_metrics(grid, pa, la, legs[ia]["width"])[0], path_metrics(grid, pb, lb, legs[ib]["width"])[0]]}


def through_pair(legs):
    return max(((angdiff(a["hdg"], b["hdg"]), i, j) for i, a in enumerate(legs) for j, b in enumerate(legs) if i < j), default=None)


def join_path(g, ia, ib, h0=None):
    """reversed leg ia + leg ib as one driving line (station 0 = node at index len(a)-1)."""
    pa = [tuple(p) for p in g["legs"][ia]["pts"]][::-1]
    pb = [tuple(p) for p in g["legs"][ib]["pts"]][1:]
    return pa + pb, len(pa) - 1


def analyse_junction(grid, case):
    legs = case["legs"]
    tp = through_pair(legs)
    if tp is None: return {"unmeasured": "fewer than two legs"}
    _, ia, ib = tp
    pts, k0 = join_path(case, ia, ib)
    fl = follow(grid, pts, None)
    # seed at the node so the walk is consistent in both directions
    h = hlist(fl)
    st = k0
    res = {"through_angle_deg": round(tp[0], 1), "through_cls": [legs[ia]["cls"], legs[ib]["cls"]]}
    # cross slope and grade by zone along the through line (stations relative to the node)
    ac = across_steps(grid, pts, fl, 2)
    cs_by_k = {k: (prof.get(2, None), prof.get(-2, None)) for k, _, prof in ac}
    def zcross(lo, hi):
        v = [(a - b) / 4.0 for k, (a, b) in cs_by_k.items() if lo <= k - st <= hi and a is not None and b is not None]
        return stat_pair(v), len(v)
    def zgrade(lo, hi):
        v = [slope(h, k, k + 4) for k in range(st + lo, st + hi) if 0 <= k and k + 4 < len(h)]
        return stat_pair(v)
    res["major"] = {"cross_slope": {z: zcross(*r)[0] for z, r in (("before", (-32, -15)), ("node", (-4, 4)), ("after", (15, 32)))},
                    "grade": {z: zgrade(*r) for z, r in (("before", (-32, -15)), ("node", (-4, 4)), ("after", (15, 32)))}}
    inside = [abs(d) for k, d in steps_of(fl) if abs(k - st) <= 10]
    outside = [abs(d) for k, d in steps_of(fl) if abs(k - st) > 10]
    res["major"]["lip_max_within10m"], res["major"]["lip_max_outside"] = round(max(inside, default=0), 3), round(max(outside, default=0), 3)
    res["major"]["path"] = path_metrics(grid, pts, fl, max(legs[ia]["width"] or 0, legs[ib]["width"] or 0))[0]
    minors = []
    for m, leg in enumerate(legs):
        if m in (ia, ib): continue
        mp, mf = leg_follow(grid, case, leg)
        mh = hlist(mf)
        g_node = slope(mh, 0, 8)
        g_far = slope(mh, 20, min(35, len(mh) - 1))
        adapt = None
        if g_far is not None:
            adapt = 0
            for d in range(0, 30):
                s = slope(mh, d, d + 4) if d + 4 < len(mh) else None
                if s is None or abs(s - g_far) > 0.03: adapt = d + 1
            if adapt > 30: adapt = None
        minors.append({"cls": leg["cls"], "grade_node_0_8m": None if g_node is None else round(g_node, 4), "grade_far_20_35m": None if g_far is None else round(g_far, 4),
                       "adapt_dist_m": adapt, "path": path_metrics(grid, mp, mf, leg["width"])[0]})
    res["minors"] = minors
    return res


def analyse_span_end(grid, case):
    legs = case["legs"]
    sp = [i for i, l in enumerate(legs) if l["span"] != "ground"]
    out = [i for i, l in enumerate(legs) if l["span"] == "ground"]
    kind = case["kinds"][0] if len(case["kinds"]) == 1 else "+".join(case["kinds"])
    if not sp: return {"unmeasured": "no span leg at the node", "kind": kind}
    if not out: return {"unmeasured": "dead end: no leg outside the span", "kind": kind}
    ia = sp[0]
    ib = max(out, key=lambda j: angdiff(legs[ia]["hdg"], legs[j]["hdg"]))
    pts, k0 = join_path(case, ib, ia)
    fl = follow(grid, pts, None)
    h = hlist(fl)
    joint = [abs(d) for k, d in detrend(steps_of(fl)) if abs(k - k0) <= 2]
    gb = stat_pair([slope(h, k, k + 4) for k in range(k0 - 30, k0 - 10) if 0 <= k and k + 4 < len(h)])
    ga = stat_pair([slope(h, k, k + 4) for k in range(k0 + 10, k0 + 30) if 0 <= k and k + 4 < len(h)])
    pm = path_metrics(grid, pts, fl, legs[ia]["width"])[0]
    return {"kind": kind, "lip_at_joint_m": round(max(joint, default=0), 3), "grade_before": gb, "grade_after": ga,
            "grade_change": None if gb is None or ga is None else round(ga - gb, 4), "path": pm}


def analyse_plain(grid, case):
    pa = [tuple(p) for p in case["legs"][1]["pts"]][::-1]
    pb = [tuple(p) for p in case["legs"][0]["pts"]][1:]
    pts = pa + pb
    fl = follow(grid, pts, None)
    pm = path_metrics(grid, pts, fl, case["legs"][0]["width"])[0]
    return {"path": pm}


def analyse_frame(grid, case):
    n = grid.n
    c = [grid.cells[(n // 2 - 1 + dj) * n + n // 2 - 1 + di] for di in (0, 1) for dj in (0, 1)]
    d = [abs(h - case["y"]) for cell in c for h in cell]
    return {"hit": bool(d), "best_dy_m": None if not d else round(min(d), 3), "ok": bool(d) and min(d) <= 0.2}


ANALYSE = {"fork": analyse_fork, "junction": analyse_junction, "span_end": analyse_span_end, "plain": analyse_plain, "frame": analyse_frame}


def path_of(res):
    """all path_metrics of a case result."""
    out = []
    def walk(o):
        if isinstance(o, dict):
            if "along" in o and "across" in o: out.append(o)
            else:
                for v in o.values(): walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(res)
    return out


def analyse(cases_doc, grid_doc):
    grids = {g["id"]: Grid(g) for g in grid_doc["cases"]}
    results, unmeasured = [], []
    for c in cases_doc["cases"]:
        g = grids.get(c["id"])
        if g is None: unmeasured.append({"id": c["id"], "cls": c["cls"], "reason": "no grid"}); continue
        r = ANALYSE[c["cls"]](g, c)
        r.update({"id": c["id"], "cls": c["cls"], "x": c["cx"], "z": c["cz"]})
        if c["cls"] != "frame": r["grid_max_adjacent_step_m"], r["grid_max_adjacent_at"] = grid_adjacent_max(g)
        if "unmeasured" in r:
            unmeasured.append({"id": c["id"], "cls": c["cls"], "reason": r["unmeasured"]})
        else:
            pm = path_of(r)
            if c["cls"] not in ("frame",) and pm:
                r["lip_along_max"] = max(p["along"]["max"] for p in pm)
                r["lip_across_max"] = max(p["across"]["max"] for p in pm)
                bad = sum(p["flags"]["gap"] + p["flags"]["out"] for p in pm); tot = sum(sum(p["flags"].values()) for p in pm)
                r["gap_share"] = round(bad / tot, 3) if tot else None
                if tot and bad / tot > 0.5: unmeasured.append({"id": c["id"], "cls": c["cls"], "reason": "centreline off the road collider on %.0f %% of the stations" % (100 * bad / tot)})
            if c["cls"] == "frame" and not r["hit"]: unmeasured.append({"id": c["id"], "cls": "frame", "reason": "no road hit"})
        results.append(r)
    return {"map": cases_doc["map"], "results": results, "unmeasured": unmeasured, "rays": grid_doc.get("rays"), "ray_buffer_full": grid_doc.get("ray_buffer_full")}


def summary(rep):
    """class counts, frame control, lip rates per class vs the plain noise floor; printed and stored in the report."""
    by = {}
    for r in rep["results"]: by.setdefault(r["cls"], []).append(r)
    s = {"frame_control": None, "classes": {}}
    fr = by.get("frame", [])
    if fr: s["frame_control"] = {"n": len(fr), "ok": sum(1 for r in fr if r["ok"]), "share": round(sum(1 for r in fr if r["ok"]) / len(fr), 3)}
    by["fork_candidate"] = [r for r in by.get("fork", []) if r.get("candidate")]
    for cls, rs in by.items():
        if cls == "frame" or not rs: continue
        n_all = len(rs)
        um = sum(1 for u in rep["unmeasured"] if u["cls"] == cls)
        la = sorted(r["lip_along_max"] for r in rs if "lip_along_max" in r)
        lc = sorted(r["lip_across_max"] for r in rs if "lip_across_max" in r)
        def rate(v, t): return round(sum(1 for x in v if x >= t) / len(v), 3) if v else None
        s["classes"][cls] = {"cases": n_all, "unmeasured": um, "unmeasured_share": round(um / n_all, 3) if n_all else None,
                             "along_max_median": None if not la else round(statistics.median(la), 3), "across_max_median": None if not lc else round(statistics.median(lc), 3),
                             **{"along_ge%02d" % round(t * 100): rate(la, t) for t in LIP_LEVELS}, **{"across_ge%02d" % round(t * 100): rate(lc, t) for t in LIP_LEVELS}}
    return s


def _q(v, p):
    v = sorted(x for x in v if x is not None)
    return None if not v else round(v[min(len(v) - 1, int(p * len(v)))], 3)


def states(rep):
    """the numbers of the questions 1-4 per class, straight from the results (reproducible: no hand arithmetic in the report)."""
    R = [r for r in rep["results"] if "unmeasured" not in r]
    out = {}
    allf = [r for r in R if r["cls"] == "fork"]
    drop = {k: sum(1 for r in allf if k in r["dropped_by"]) for k in ("slope_ge15", "coverage_ge70", "angle_lt3", "legs_lt20m_apart")}
    out["fork_dropped"] = {"n": len(allf), "candidates": sum(1 for r in allf if r["candidate"]), **drop}
    for name, sel in (("fork_all", lambda x: True), ("fork_cand", lambda x: x["candidate"])):
        F = [r for r in allf if sel(r)]
        sep = [r for r in F if r["separation"]]
        dh = [r["separation"]["dh_m"] for r in sep]
        out[name] = {"n": len(F), "separates": len(sep), "never_in_probe": len(F) - len(sep),
                     "sep_type": {t: sum(1 for r in sep if r["separation"]["type"] == t) for t in ("step", "gap", "leg_off_road")},
                     "shared_len_p50": _q([r["shared_len_m"] for r in F], .5), "shared_len_p90": _q([r["shared_len_m"] for r in F], .9),
                     "cross_slope_median": _q([r["cross_slope_shared_median"] for r in F], .5), "cross_slope_n": sum(1 for r in F if r["cross_slope_shared_median"] is not None),
                     "dh_at_sep_p50": _q(dh, .5), "dh_at_sep_p90": _q(dh, .9), "dh_at_sep_n": sum(1 for x in dh if x is not None),
                     "dh_at_end_p50": _q([r["dh_at_end_m"] for r in F], .5), "lip_along_p50": _q([r.get("lip_along_max") for r in F], .5)}
    J = [r for r in R if r["cls"] == "junction"]
    mi = [m for r in J for m in r.get("minors", [])]
    maj = [r["major"] for r in J if "major" in r]

    def dd(a, b): return [abs(m["cross_slope"][a] - m["cross_slope"][b]) for m in maj if m["cross_slope"][a] is not None and m["cross_slope"][b] is not None]
    out["junction"] = {"n": len(J), "minor_legs": len(mi), "minor_grade_node_abs_p50": _q([abs(m["grade_node_0_8m"]) for m in mi if m["grade_node_0_8m"] is not None], .5),
                       "minor_grade_far_abs_p50": _q([abs(m["grade_far_20_35m"]) for m in mi if m["grade_far_20_35m"] is not None], .5),
                       "adapt_dist_p50": _q([m["adapt_dist_m"] for m in mi], .5), "adapt_dist_p90": _q([m["adapt_dist_m"] for m in mi], .9), "adapt_dist_n": sum(1 for m in mi if m["adapt_dist_m"] is not None),
                       "major_cross_slope_abs_before_p50": _q([abs(m["cross_slope"]["before"]) for m in maj if m["cross_slope"]["before"] is not None], .5),
                       "major_cross_slope_abs_node_p50": _q([abs(m["cross_slope"]["node"]) for m in maj if m["cross_slope"]["node"] is not None], .5),
                       "major_cross_slope_node_vs_before_p50": _q(dd("node", "before"), .5), "major_cross_slope_node_vs_before_p90": _q(dd("node", "before"), .9),
                       "major_lip_within10_p50": _q([m["lip_max_within10m"] for m in maj], .5), "major_lip_within10_p90": _q([m["lip_max_within10m"] for m in maj], .9),
                       "major_lip_outside_p50": _q([m["lip_max_outside"] for m in maj], .5), "major_lip_outside_p90": _q([m["lip_max_outside"] for m in maj], .9)}
    for kind in ("bridge", "tunnel"):
        S = [r for r in R if r["cls"] == "span_end" and r.get("kind") == kind]
        j = [r["lip_at_joint_m"] for r in S]
        gc = [abs(r["grade_change"]) for r in S if r["grade_change"] is not None]
        out["span_end_" + kind] = {"n": len(S), "lip_p50": _q(j, .5), "lip_p90": _q(j, .9), "lip_max": _q(j, 1.0), **{"lip_ge%02d" % round(t * 100): sum(1 for x in j if x >= t) for t in LIP_LEVELS},
                                   "grade_change_abs_p50": _q(gc, .5), "grade_change_abs_p90": _q(gc, .9), "grade_change_n": len(gc)}
    return out


def render(grid_doc, case, out, scale=5):
    """height map of one case grid (lowest road layer; colour = height above the grid minimum, 0..3 m; white = second layer), legs as black dots. Needs Pillow (not under -I)."""
    from PIL import Image
    g = Grid(next(c for c in grid_doc["cases"] if c["id"] == case["id"]))
    lo = min(min(c) for c in g.cells if c)
    img = Image.new("RGB", (g.n * scale, g.n * scale), (24, 24, 28))
    px = img.load()
    for j in range(g.n):
        for i in range(g.n):
            c = g.cells[j * g.n + i]
            if not c: continue
            t = min(1.0, (min(c) - lo) / 3.0)
            col = (255, 255, 255) if len(c) > 1 else (int(40 + 215 * t), int(60 + 160 * (1 - abs(2 * t - 1))), int(220 * (1 - t) + 30))
            for dy in range(scale):
                for dx in range(scale): px[i * scale + dx, (g.n - 1 - j) * scale + dy] = col
    for leg in case.get("legs", []):
        for x, _, z in leg["pts"]:
            i, j = int((x - g.x0) * scale), int((z - g.z0) * scale)
            if 0 <= i < g.n * scale and 0 <= j < g.n * scale: px[i, g.n * scale - 1 - j] = (0, 0, 0)
    img.save(out)


def typical_fork(rep):
    F = [r for r in rep["results"] if r["cls"] == "fork" and r.get("candidate") and r["separation"] and r["separation"]["dh_m"] is not None]
    if not F: raise SystemExit("no fork candidate with a separation in this report")
    ms, md = statistics.median(r["shared_len_m"] for r in F), statistics.median(r["separation"]["dh_m"] for r in F)
    return min(F, key=lambda r: (abs(r["shared_len_m"] - ms) / max(ms, 1) + abs(r["separation"]["dh_m"] - md) / max(md, .1), r["id"]))["id"]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("cases"); c.add_argument("--map", required=True); c.add_argument("--out", required=True); c.add_argument("--seed", type=int, default=129); c.add_argument("--dir", default=None, help="dataset folder in world frame (generator harness dump)")
    pc = sub.add_parser("picture"); pc.add_argument("--cases", required=True); pc.add_argument("--grid", required=True); pc.add_argument("--report", required=True); pc.add_argument("--id", default=None); pc.add_argument("--out", required=True)
    a = sub.add_parser("analyse"); a.add_argument("--cases", required=True); a.add_argument("--grid", required=True); a.add_argument("--out", required=True)
    args = ap.parse_args(argv)
    if args.cmd == "cases":
        doc = make_cases(args.map, args.seed, args.dir)
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        json.dump(doc, open(args.out, "w", encoding="utf-8", newline="\n"), separators=(",", ":"))
        cnt = {}
        for x in doc["cases"]: cnt[x["cls"]] = cnt.get(x["cls"], 0) + 1
        print(args.map, "cases", cnt)
        return 0
    cases_doc = json.load(open(args.cases, encoding="utf-8"))
    grid_doc = json.load(open(args.grid, encoding="utf-8"))
    if args.cmd == "picture":
        prep = json.load(open(args.report, encoding="utf-8"))
        cid = args.id or typical_fork(prep)
        render(grid_doc, next(c for c in cases_doc["cases"] if c["id"] == cid), args.out)
        print("picture", cid, "->", args.out)
        return 0
    rep = analyse(cases_doc, grid_doc)
    rep["summary"] = summary(rep)
    rep["states"] = states(rep)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    json.dump(rep, open(args.out, "w", encoding="utf-8", newline="\n"), indent=1, sort_keys=True)
    print(json.dumps(rep["summary"], sort_keys=True))
    print("unmeasured:", len(rep["unmeasured"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
