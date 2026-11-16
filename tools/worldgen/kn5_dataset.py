#!/usr/bin/env python
"""RG-108 (W1): raw KN5 road measurements (Unity, --kn5-worldgen-export) -> dataset nightracer.worldgen.road-dataset.v1 + roadside.json (stdlib only).

Unity exports raw numbers per edge sample (Kn5WorldGenExport.cs); everything that decides the v1 content is here, so the OSM extractor's functions are reused
and the v1 logic exists once: graph.profiles / finish_nodes / junction_records / span_records / assign_node_attrs and dataset_stats.compute. graph.py and
dataset_stats.py are read-only for this ticket.

  python -X utf8 -I tools/worldgen/kn5_dataset.py build --map gunsai [--dir artifacts/worldgen/kn5-gunsai]     # raw.json -> v1 files + roadside.json + stats + report
  python -X utf8 -I tools/worldgen/kn5_dataset.py all   --map gunsai                                           # build, validate, stats

Rules (all parameters are written to provenance.parameters):
- Class from the median measured width (class_source "width_rule_v1"): >= 13 m motorway, >= 9 primary, >= 6.5 secondary, >= 4.5 local, else track; no `link` class.
  Lanes = clamp(round(width / 3.5), 1, 6) (lanes_source "width_estimate"). Both are ESTIMATES: nothing in a KN5 map says what a road is called or how many lanes it has.
- Tunnel: the up-ray from 0.5 m above the deck hits geometry (terrain, or a decoration that is not tree / lamp / sign) within 15 m, continuously over >= 50 m.
  Bridge: the highest ground 2 m outside BOTH road edges (terrain / other road collider, or a decoration that is not tree / lamp / sign / rail, >= 1.5 m below the deck)
  is more than 4 m below the deck (at least one side measured), continuously over >= 30 m. One missed sample inside a run is bridged (a ray can slip between two roof triangles).
- Edges are cut at span changes (portal nodes), so edge["span"] holds like in the OSM dataset. cover_depth_m / clearance_m come from graph.span_records via the
  per-sample `_dem` (tunnel: deck + cover, bridge: deck - drop to the ground).
- Roadside bins: 100 m of edge, left and right separately: occupancy of the first decoration hit by a top-down ray at 2 / 5 / 10 / 20 / 30 m beyond the road edge (per class
  and offset), small objects (footprint <= 3 m) as one instance with the distance to the edge, share of the bin with a wall collider within 3 m of the edge.
"""
import argparse
import bisect
import json
import math
import os
import subprocess
import sys
import time
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import graph  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
VERSION = "rg108-1"
OFFSETS = (2, 5, 10, 20, 30)
BIN_M = 100.0
CLASS_BANDS = ((13.0, "motorway"), (9.0, "primary"), (6.5, "secondary"), (4.5, "local"))  # else track
TUNNEL_COVER_MAX_M, TUNNEL_MIN_M = 15.0, 50.0
BRIDGE_MIN_DROP_M, BRIDGE_MIN_M = 4.0, 30.0
NO_GROUND = 999.0
DROP_CAP_M = 100.0
WIDTH_CAP_M = 60.0
SMALL_REACH_M = 40.0
CLASS_NAMES = ("tree", "lamp", "sign", "guardrail", "building", "other")

# archive provenance: copied from the accepted ADRs (never invented); None = not recorded there
ARCHIVES = {
    "gunsai": {"adr": "ADR-018", "archive": "gunsai-touge_modland.7z (Gunsai Touge 1.2, ModLand re-upload)", "sha256": "c550688e0f6bf0817c58dd7d0fd91aee58de15f7da239e37e00baa9688578871", "bytes": None},
    "sierra": {"adr": "ADR-023", "archive": "sierra_de_cadiz.7z (Sierra de Cadiz v0.954)", "sha256": "42ca5242795835a8e8b43df48c667b469265f5dae1a4a9230007db1af362fe77", "bytes": 1462249010},
    "kanazawa": {"adr": "ADR-027", "archive": "kanazawa_0.5.0.zip (Kanazawa Free Roam 0.5.0)", "sha256": "68b706f0fd78894723765c0d162b63b998750fe7e13c0cfaeba40a15c94ae745", "bytes": 413816630},
}


def median(v):
    s = sorted(v)
    return (s[len(s) // 2] + s[(len(s) - 1) // 2]) / 2.0 if s else None


def class_of_width(w):
    for lo, c in CLASS_BANDS:
        if w >= lo:
            return c
    return "track"


def lanes_of_width(w):
    return int(max(1, min(6, round(w / 3.5))))


class NameClasses:
    """Roadside class of a name id from the regex table (kn5-roadside-classes.json)."""

    def __init__(self, table, names):
        import re
        self.table, self.names = table, names
        self.rx = [(c, re.compile("|".join(table["classes"][c]), re.I)) for c in table["order"]]
        self.default = table.get("default", "other")
        self.cache = {}

    def of(self, nid):
        if nid is None or nid < 0:
            return None
        c = self.cache.get(nid)
        if c is None:
            s = self.names[nid] if nid < len(self.names) else ""
            c = next((cn for cn, rx in self.rx if rx.search(s)), self.default)
            self.cache[nid] = c
        return c


# ---- spans -------------------------------------------------------------------------------------------------------------------

def _runs(flags, min_len_m, step):
    """Mark runs of True of at least min_len_m (samples * step), tolerating one False between two True. Returns the new flag list."""
    f = list(flags)
    for i in range(1, len(f) - 1):
        if not f[i] and f[i - 1] and f[i + 1]:
            f[i] = True
    out = [False] * len(f)
    i = 0
    while i < len(f):
        if not f[i]:
            i += 1
            continue
        j = i
        while j + 1 < len(f) and f[j + 1]:
            j += 1
        if (j - i + 1) * step >= min_len_m:
            for k in range(i, j + 1):
                out[k] = True
        i = j + 1
    return out


def sample_labels(samples, cls, ignore_cover, ignore_ground):
    """Per sample: ('tunnel'|'bridge'|'ground', cover_m or None, drop_m or None). Needs the 'step' of the edge."""
    n = len(samples)
    cum = [0.0]
    for i in range(1, n):
        cum.append(cum[-1] + math.hypot(samples[i]["x"] - samples[i - 1]["x"], samples[i]["z"] - samples[i - 1]["z"]))
    step = cum[-1] / (n - 1) if n > 1 and cum[-1] > 0 else graph.STEP_M
    cover, drop = [None] * n, [None] * n
    for i, s in enumerate(samples):
        up = s.get("up") or []
        for k in range(0, len(up) - 2, 3):
            d, kind, nid = up[k], up[k + 1], up[k + 2]
            if d <= TUNNEL_COVER_MAX_M and (kind == 1 or cls.of(nid) not in ignore_cover):
                cover[i] = d
                break
        sides = []
        for c, v, nv in ((s.get("dcl", NO_GROUND), s.get("dvl", NO_GROUND), s.get("nvl", -1)), (s.get("dcr", NO_GROUND), s.get("dvr", NO_GROUND), s.get("nvr", -1))):
            d = c
            if v < d and cls.of(nv) not in ignore_ground:
                d = v
            sides.append(d)
        drop[i] = sides
    tun = _runs([c is not None for c in cover], TUNNEL_MIN_M, step)
    # a bridge needs measured ground: ground more than 4 m below on both sides, and at least one side actually measured (no ground collider within 200 m on either side is
    # a map without terrain there, not a viaduct; Gunsai's outer ends gave 100 m "bridges" before this rule)
    bri = _runs([(not tun[i]) and min(drop[i]) > BRIDGE_MIN_DROP_M and min(drop[i]) < NO_GROUND for i in range(n)], BRIDGE_MIN_M, step)
    labels = ["tunnel" if tun[i] else ("bridge" if bri[i] else "ground") for i in range(n)]
    dropv = []
    for i in range(n):
        m = [d for d in drop[i] if d < NO_GROUND]
        dropv.append(min(DROP_CAP_M, sum(m) / len(m)) if m else DROP_CAP_M)
    return labels, cover, dropv, cum


def _fill(vals, labels, want):
    """vals with None replaced by the nearest value of a sample with the same label; None stays when the label has none."""
    out = list(vals)
    idx = [i for i, v in enumerate(vals) if v is not None and labels[i] == want]
    for i in range(len(vals)):
        if out[i] is None and labels[i] == want and idx:
            out[i] = vals[min(idx, key=lambda j: abs(j - i))]
    return out


# ---- conversion --------------------------------------------------------------------------------------------------------------

def convert(raw, names, small, table, info, log=print):
    """raw.json + names.json + small.json -> ({file: object}, local) ; info = {map, adr, archive, sha256, bytes, commit, dirty, date}."""
    meta = raw["meta"]
    R = meta["raster"]
    ox, oz = R["x0"] + R["span_x"] / 2.0, R["z0"] + R["span_z"] / 2.0
    cls = NameClasses(table, names)
    ign_cover, ign_ground = set(table["ignore_for_tunnel_cover"]), set(table["ignore_for_bridge_ground"])
    verts = raw["vertices"]
    nodes, node_of_vertex = [], {}

    def new_node(x, y, z):
        nodes.append({"id": len(nodes), "osm_id": None, "pos": [round(x - ox, 2), round(y, 2), round(z - oz, 2)], "degree": 0, "kind": "joint", "layer": 0, "boundary": False,
                      "roundabout_radius_m": None})
        return len(nodes) - 1

    def vnode(vid):
        if vid not in node_of_vertex:
            v = verts[vid]
            node_of_vertex[vid] = new_node(v[0], v[1], v[2])
        return node_of_vertex[vid]

    edges, esamples, edge_dropped = [], [], 0
    for re_ in raw["edges"]:
        ss = re_["samples"]
        if len(ss) < 2:
            edge_dropped += 1
            continue
        labels, cover, dropv, cum = sample_labels(ss, cls, ign_cover, ign_ground)
        n = len(ss)
        if n >= 2 and labels[-1] != labels[-2] and (n < 3 or labels[-2] != labels[-1]):
            labels[-1] = labels[-2]  # a final piece of one sample would have no length: it joins the previous piece
        pieces, i = [], 0
        while i < n:
            j = i
            while j + 1 < n and labels[j + 1] == labels[i]:
                j += 1
            pieces.append((i, j, labels[i]))
            i = j + 1
        prev_node = vnode(re_["a"])
        for pi, (i0, j0, lab) in enumerate(pieces):
            last = pi == len(pieces) - 1
            j1 = n - 1 if last else min(n - 1, j0 + 1)  # the next piece starts at j0 + 1: the boundary sample belongs to both
            pts = ss[i0:j1 + 1]
            if len(pts) < 2:
                continue
            if last:
                end_node = vnode(re_["b"])
            else:
                end_node = new_node(ss[j1]["x"], ss[j1]["y"], ss[j1]["z"])
            sub_cum = cum[i0:j1 + 1]
            length = sub_cum[-1] - sub_cum[0]
            if length < 1e-3:
                prev_node = end_node
                continue
            ws = [min(WIDTH_CAP_M, s["w"]) for s in pts if s["w"] > 0]
            width = median(ws)
            cov = _fill(cover, labels, "tunnel")[i0:j1 + 1]
            drp = dropv[i0:j1 + 1]
            surf = Counter(s["sf"].lower() for s in pts if s.get("sf"))
            banks = [s["bk"] for s in pts if "bk" in s]
            ys = [s["y"] for s in pts]
            if lab == "tunnel":
                known = [c for c in cov if c is not None]
                dem = [y + (c if c is not None else (median(known) if known else 0.0)) for y, c in zip(ys, cov)]
            elif lab == "bridge":
                dem = [y - d for y, d in zip(ys, drp)]
            else:
                dem = list(ys)
            e = {"id": len(edges), "a": prev_node, "b": end_node, "osm_way": None,
                 "class": class_of_width(width) if width is not None else "local", "highway": None,
                 "lanes": lanes_of_width(width) if width is not None else None, "width_m": round(width, 2) if width is not None else None,
                 "surface": surf.most_common(1)[0][0] if surf else None, "oneway": False, "maxspeed": None, "roundabout": False,
                 "layer": {"bridge": 1, "tunnel": -1}.get(lab, 0), "span": lab, "length_m": round(length, 2),
                 "class_source": "width_rule_v1" if width is not None else "default_no_width", "lanes_source": "width_estimate" if width is not None else "none",
                 "quality": {"samples": len(pts), "frame": sum(1 for s in pts if s["src"] == "frame"), "cross": sum(1 for s in pts if s["src"] == "cross"),
                             "none": sum(1 for s in pts if s["src"] == "none"), "surface_missing": sum(1 for s in pts if not s.get("sf")),
                             "open": sum(1 for s in pts if s.get("open"))}}
            xz = [(s["x"] - ox, s["z"] - oz) for s in pts]
            rs, rss, ds = graph.resample(xz, graph.STEP_M)
            if graph.polyline_length(rs) < 0.9 * length:   # an out-and-back or zigzag piece (cluster artefact): resampling would cut it, keep the measured samples
                rs, rss, ds = xz, [c - sub_cum[0] for c in sub_cum], length / (len(xz) - 1)
            e["pts"] = xz
            e["_xz"], e["_s"], e["step_m"] = rs, rss, ds
            total = sub_cum[-1] - sub_cum[0]
            rel = [c - sub_cum[0] for c in sub_cum]

            def at(arr, sv):
                if total <= 0:
                    return arr[0]
                k = min(max(bisect.bisect_right(rel, sv) - 1, 0), len(rel) - 2)
                seg = rel[k + 1] - rel[k]
                t = 0.0 if seg <= 0 else min(1.0, max(0.0, (sv - rel[k]) / seg))
                return arr[k] + (arr[k + 1] - arr[k]) * t

            e["_y"] = [at(ys, sv * (total / (rss[-1] if rss[-1] > 0 else 1.0))) for sv in rss]
            e["_dem"] = [at(dem, sv * (total / (rss[-1] if rss[-1] > 0 else 1.0))) for sv in rss]
            e["centreline"] = [v for (x, z), y in zip(rs, e["_y"]) for v in (round(x, 1), round(y, 1), round(z, 1))]
            e["bank_deg"] = round(median(banks), 2) if banks else None
            edges.append(e)
            esamples.append((pts, sub_cum, i0 > 0))
            prev_node = end_node
    # the first sample of a continuing piece is the last sample of the previous one: roadside counts it once
    log("kn5_dataset: %d edges (%d raw edges without samples dropped)" % (len(edges), edge_dropped))
    used = set()
    for e in edges:
        used.add(e["a"])
        used.add(e["b"])
    keep = [n for n in nodes if n["id"] in used]
    remap = {n["id"]: i for i, n in enumerate(keep)}
    for n in keep:
        n["id"] = remap[n["id"]]
    for e in edges:
        e["a"], e["b"] = remap[e["a"]], remap[e["b"]]
    nodes = keep
    graph.assign_node_attrs(nodes, edges)
    graph.finish_nodes(nodes, edges)
    profs = graph.profiles(edges)
    juncs = graph.junction_records(nodes, edges)
    spans = graph.span_records(nodes, edges)
    roadside, local = build_roadside(edges, esamples, small, cls, names, ox, oz)
    for e in edges:
        for k in ("pts", "_xz", "_s", "_y", "_dem"):
            del e[k]
    sch = graph.SCHEMA
    half = max(R["span_x"], R["span_z"]) / 2.0
    size_km = round(2.0 * half / 1000.0 + 0.1, 3)
    prov = {"schema": sch, "source_kind": "kn5", "adr": "ADR-030/031/032 (accepted 2026-10-29); map: %s" % info["adr"],
            "extractor": {"script": "tools/worldgen/kn5_dataset.py", "version": VERSION, "commit": info.get("commit"), "dirty": info.get("dirty"),
                          "unity_exporter": "engine-unity/NightRacer/Assets/Scripts/Kn5/WorldGen/Kn5WorldGenExport.cs"},
            "date": info["date"], "purpose": "learn road cross-section and roadside rules only (ADR-032); no mesh, texture or KN5 name leaves artifacts/",
            "window": {"name": "kn5-" + info["map"], "size_km": size_km, "centre_unity_xz": [round(ox, 1), round(oz, 1)],
                       "frame": "Unity world (ADR-015 KN5 mapping: x mirrored) minus the raster centre, metres, x right, y up, z forward; y is absolute"},
            "parameters": {"class_source": "width_rule_v1", "lanes_source": "width_estimate", "class_bands_m": {"motorway": 13.0, "primary": 9.0, "secondary": 6.5, "local": 4.5, "track": 0.0},
                           "lanes_rule": "clamp(round(width / 3.5 m), 1, 6)", "width_rule": "median of the measured samples (frame or cross) of the edge, capped at 60 m",
                           "step_m": graph.STEP_M, "raster_mpp": R["mpp"], "skeleton": meta["skeleton"], "sampling": meta["sampling"],
                           "tunnel_rule": {"up_ray_max_m": TUNNEL_COVER_MAX_M, "min_len_m": TUNNEL_MIN_M, "ignored_classes": sorted(ign_cover), "gap_tolerance_samples": 1},
                           "bridge_rule": {"min_drop_m": BRIDGE_MIN_DROP_M, "min_len_m": BRIDGE_MIN_M, "side_offset_m": 2.0, "ignored_classes": sorted(ign_ground), "gap_tolerance_samples": 1},
                           "roadside": {"bin_m": BIN_M, "offsets_m": list(OFFSETS), "small_object_footprint_m": 3.0, "small_reach_m": SMALL_REACH_M, "wall_within_m": 3.0},
                           "bank_deg": "median of the per-sample height difference of the two road edges (0.5 m inside), + = left edge higher",
                           "edge_layer": "derived from the span: bridge 1, tunnel -1, else 0 (not measured)", "oneway": "false everywhere (a KN5 map has no direction data)"},
            "counts": {"nodes": len(nodes), "edges": len(edges), "junctions": len(juncs), "spans": len(spans), "raw_samples": meta["sampling"]["samples"]},
            "sources": [{"kind": "kn5-archive", "adr": info["adr"], "file": info["archive"], "sha256": info["sha256"], "bytes": info["bytes"],
                         "licence": "none known; private local test data, no licence granted (%s, ADR-014/023)" % info["adr"], "date": info["date"]}]}
    files = {"provenance": prov, "nodes": nodes, "edges": edges, "profiles": profs, "junctions": juncs, "spans": spans, "roadside": roadside}
    return files, local


# ---- roadside ----------------------------------------------------------------------------------------------------------------

def build_roadside(edges, esamples, small, cls, names, ox, oz):
    items, other_names = [], Counter()
    grid = defaultdict(list)  # 20 m cells -> (edge idx, bin, x, z, tx, tz, l, r, y)
    bins_of = []
    tot_hits, tot_other, tot_cells = 0, 0, 0
    for ei, (e, (pts, cum, cont)) in enumerate(zip(edges, esamples)):
        L = e["length_m"]
        nb = max(1, int(math.ceil(L / BIN_M - 1e-9)))
        tunnel = e["span"] == "tunnel"
        bins = []
        for k in range(nb):
            side = lambda: {"cells": 0, "occupied": 0, "occupancy": defaultdict(lambda: [0] * len(OFFSETS)), "small": defaultdict(list), "walls": 0}  # noqa: E731
            bins.append({"bin": k, "s0": round(k * BIN_M, 1), "s1": round(min(L, (k + 1) * BIN_M), 1), "samples": 0, "tunnel": tunnel, "L": side(), "R": side()})
        bins_of.append(bins)
        if tunnel:
            continue
        base = cum[0]
        for si, s in enumerate(pts):
            if cont and si == 0:
                continue
            k = min(nb - 1, int((cum[si] - base) / BIN_M))
            b = bins[k]
            b["samples"] += 1
            for key, ol, wl in (("L", "ol", "wl"), ("R", "or", "wr")):
                sd = b[key]
                sd["walls"] += s.get(wl, 0)
                for oi, nid in enumerate(s.get(ol, [])):
                    sd["cells"] += 1
                    tot_cells += 1
                    c = cls.of(nid)
                    if c is None or c == "ignore":
                        continue
                    sd["occupied"] += 1
                    sd["occupancy"][c][oi] += 1
                    tot_hits += 1
                    if c == "other":
                        tot_other += 1
                        other_names[nid] += 1
            lext = s["l"] if s["l"] >= 0 else (s["w"] / 2.0 if s["w"] > 0 else 3.0)
            rext = s["r"] if s["r"] >= 0 else (s["w"] / 2.0 if s["w"] > 0 else 3.0)
            grid[(int(s["x"] // 20), int(s["z"] // 20))].append((ei, k, s["x"] - ox, s["z"] - oz, s["tx"], s["tz"], lext, rext, s["y"]))
    # small objects: one instance, assigned to the nearest sample with the object beside the road edge (|along| <= 6 m, <= 8 m height difference)
    sm_assigned, sm_total = 0, 0
    sm_other = Counter()
    for i in range(0, len(small) - 3, 4):
        x, z, y, nid = small[i], small[i + 1], small[i + 2], int(small[i + 3])
        sm_total += 1
        c = cls.of(nid)
        if c is None or c == "ignore":
            continue
        best = None
        for gx in range(int((x) // 20) - 1, int((x) // 20) + 2):
            for gz in range(int((z) // 20) - 1, int((z) // 20) + 2):
                for (ei, k, sx, sz, tx, tz, lext, rext, sy) in grid.get((gx, gz), ()):
                    dx, dz = (x - ox) - sx, (z - oz) - sz
                    along = dx * tx + dz * tz
                    if abs(along) > 6.0 or abs(y - sy) > 8.0:
                        continue
                    lat = -dx * tz + dz * tx
                    side, d = ("L", lat - lext) if lat >= 0 else ("R", -lat - rext)
                    if d < -0.5 or d > SMALL_REACH_M:
                        continue
                    if best is None or d < best[0]:
                        best = (d, ei, k, side)
        if best is None:
            continue
        sm_assigned += 1
        d, ei, k, side = best
        bins_of[ei][k][side]["small"][c].append(round(max(0.0, d), 1))
        if c == "other":
            sm_other[nid] += 1
    for ei, bins in enumerate(bins_of):
        out = []
        for b in bins:
            ob = {"bin": b["bin"], "s0": b["s0"], "s1": b["s1"], "samples": b["samples"], "tunnel": b["tunnel"]}
            for key, name in (("L", "left"), ("R", "right")):
                sd = b[key]
                ob[name] = {"cells": sd["cells"], "occupied": sd["occupied"], "occupancy": {c: v for c, v in sorted(sd["occupancy"].items())},
                            "small": {c: {"n": len(d), "dist_m": d[:60]} for c, d in sorted(sd["small"].items())},
                            "wall_share": round(sd["walls"] / b["samples"], 3) if b["samples"] else None}
            out.append(ob)
        items.append({"edge": edges[ei]["id"], "span": edges[ei]["span"], "length_m": edges[ei]["length_m"], "bins": out})
    all_other = Counter(other_names)
    all_other.update(sm_other)
    local = {"occupancy_hits": tot_hits, "occupancy_other": tot_other, "occupancy_cells": tot_cells,
             "other_share_of_hits": round(tot_other / tot_hits, 4) if tot_hits else None,
             "small_total": sm_total, "small_assigned": sm_assigned, "small_other": sum(sm_other.values()),
             "other_share_incl_small": round((tot_other + sum(sm_other.values())) / (tot_hits + sm_assigned), 4) if (tot_hits + sm_assigned) else None,
             "top_other": [(names[nid] if nid < len(names) else "?", n) for nid, n in all_other.most_common(30)]}
    return {"schema": graph.SCHEMA, "items": items}, local


# ---- I/O ---------------------------------------------------------------------------------------------------------------------

def _dump(path, obj):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, separators=(",", ":"), allow_nan=False)


def _git(*args):
    try:
        return subprocess.run(["git", "-C", REPO] + list(args), capture_output=True, text=True, timeout=30).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        return ""


def write_files(files, out_dir):
    sch = graph.SCHEMA
    os.makedirs(out_dir, exist_ok=True)
    for name in ("nodes", "edges", "profiles", "junctions", "spans", "roadside"):
        obj = files[name]
        _dump(os.path.join(out_dir, name + ".json"), obj if name == "roadside" else {"schema": sch, "items": obj})
    _dump(os.path.join(out_dir, "provenance.json"), files["provenance"])


def build(map_name, out_dir, table_path=None, log=print):
    arch = ARCHIVES.get(map_name, {"adr": "unknown", "archive": "unknown", "sha256": "unknown", "bytes": None})
    with open(os.path.join(out_dir, "raw.json"), encoding="utf-8") as f:
        raw = json.load(f)
    with open(os.path.join(out_dir, "names.json"), encoding="utf-8") as f:
        names = json.load(f)
    with open(os.path.join(out_dir, "small.json"), encoding="utf-8") as f:
        small = json.load(f)
    with open(table_path or os.path.join(os.path.dirname(os.path.abspath(__file__)), "kn5-roadside-classes.json"), encoding="utf-8") as f:
        table = json.load(f)
    info = dict(arch, map=map_name, date=time.strftime("%Y-%m-%d", time.gmtime()), commit=_git("rev-parse", "HEAD") or None,
                dirty=bool(_git("status", "--porcelain", "--", "tools/worldgen", "engine-unity/NightRacer/Assets/Scripts/Kn5")))
    files, local = convert(raw, names, small, table, info, log)
    write_files(files, out_dir)
    report = make_report(raw, files, local)
    _dump(os.path.join(out_dir, "kn5-report.json"), report)
    return files, local, report


def make_report(raw, files, local):
    meta = raw["meta"]
    edges, nodes, spans, juncs = files["edges"], files["nodes"], files["spans"], files["junctions"]
    km = sum(e["length_m"] for e in edges) / 1000.0
    widths = [e["width_m"] for e in edges for _ in range(max(1, int(e["length_m"] // 10))) if e["width_m"]]
    medw = median(widths)
    area = meta["raster"]["area_m2"]
    by_class = defaultdict(float)
    for e in edges:
        by_class[e["class"]] += e["length_m"] / 1000.0
    span_n = Counter((s["kind"], s["class"]) for s in spans)
    q = Counter()
    for e in edges:
        for k, v in e["quality"].items():
            q[k] += v
    ns = max(1, q["samples"])
    deg = Counter(n["degree"] for n in nodes)
    return {"graph_km": round(km, 2), "edges": len(edges), "nodes": len(nodes), "junctions": len(juncs), "degree_mix": {str(k): v for k, v in sorted(deg.items())},
            "components_raw_graph": meta["skeleton"]["components"], "component_lengths_m": meta["component_lengths_m"],
            "raster_area_m2": area, "median_width_m": round(medw, 2) if medw else None,
            "expected_km_area_over_width": round(area / medw / 1000.0, 2) if medw else None,
            "graph_vs_expected": round(km / (area / medw / 1000.0), 3) if medw else None,
            "km_by_class": {k: round(v, 2) for k, v in sorted(by_class.items())},
            "spans": {"%s/%s" % k: v for k, v in sorted(span_n.items())},
            "span_km": {kind: round(sum(s["length_m"] for s in spans if s["kind"] == kind) / 1000.0, 3) for kind in ("tunnel", "bridge")},
            "samples": q["samples"], "share_frame": round(q["frame"] / ns, 4), "share_cross": round(q["cross"] / ns, 4), "share_width_none": round(q["none"] / ns, 4),
            "share_surface_missing": round(q["surface_missing"] / ns, 4), "share_open": round(q["open"] / ns, 4),
            "edges_without_width": sum(1 for e in edges if e["width_m"] is None),
            "roadside": {k: v for k, v in local.items() if k != "top_other"}, "timing_s": meta["timing_s"], "peak_commit_mib": meta.get("peak_commit_mib")}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["build", "all"])
    ap.add_argument("--map", required=True)
    ap.add_argument("--dir", default=None, help="raw export folder = output folder (default artifacts/worldgen/kn5-<map>)")
    ap.add_argument("--other-names", default=None, help="local list of the top 30 'other' names (default artifacts/rg108/kn5-<map>-other-names.txt, never committed)")
    a = ap.parse_args(argv)
    out_dir = a.dir or os.path.join(REPO, "artifacts", "worldgen", "kn5-" + a.map)
    files, local, report = build(a.map, out_dir)
    p = a.other_names or os.path.join(REPO, "artifacts", "rg108", "kn5-%s-other-names.txt" % a.map)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write("# top 30 'other' roadside names of %s (node|material|shader, hits); local only, never committed (ADR-032)\n" % a.map)
        for nm, n in local["top_other"]:
            f.write("%6d  %s\n" % (n, nm))
    print(json.dumps(report, indent=1))
    if a.cmd == "all":
        import dataset_stats
        import validate_dataset
        rc = validate_dataset.main(["--dir", out_dir])
        if rc != 0:
            return rc
        rc = dataset_stats.main(["--dir", out_dir])
        if rc == 0:  # the stats must say that class and lanes are estimates
            sp = os.path.join(out_dir, "stats.json")
            with open(sp, encoding="utf-8") as f:
                st = json.load(f)
            st["estimates"] = {"class_source": "width_rule_v1", "lanes_source": "width_estimate", "note": "class and lanes come from the measured width, not from names or tags"}
            with open(sp, "w", encoding="utf-8", newline="\n") as f:
                json.dump(st, f, indent=1, allow_nan=False)
        return rc
    return 0


if __name__ == "__main__":
    sys.exit(main())
