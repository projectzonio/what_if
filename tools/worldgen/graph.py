#!/usr/bin/env python
"""RG-107 road graph from OSM ways (pure functions, stdlib only, no I/O) -> schema nightracer.worldgen.road-dataset.v1.

Input : OSM nodes {id: (lat, lon)} and ways [{id, nds, tags}], a Window (local equirectangular frame, metres, x east, z north, y up).
Output: nodes / edges (+ per-edge profiles, junction legs, spans). Everything here is deterministic; the order follows way id.
"""
import math
import re
from collections import Counter, defaultdict

SCHEMA = "nightracer.worldgen.road-dataset.v1"
M_PER_DEG = 111194.93  # metres per degree of latitude (R = 6371008.8 m); longitude is scaled with cos(lat0)

# highway tag -> class. Differences from plan section 4: ramps get their own class "link" (they would pollute the motorway radius statistics),
# trunk joins the motorway class (Czech "silnice pro motorova vozidla"), tertiary joins secondary, "service" is not fetched at all.
CLASS_OF = {
    "motorway": "motorway", "trunk": "motorway",
    "motorway_link": "link", "trunk_link": "link", "primary_link": "link", "secondary_link": "link", "tertiary_link": "link",
    "primary": "primary", "secondary": "secondary", "tertiary": "secondary",
    "unclassified": "local", "residential": "local", "living_street": "local", "track": "track",
}
CLASSES = ("motorway", "primary", "secondary", "local", "track", "link")
SPANS = ("ground", "bridge", "tunnel")
NODE_KINDS = ("end", "joint", "junction", "interchange", "portal")
CZ_MAXSPEED = {"CZ:urban": 50, "CZ:rural": 90, "CZ:trunk": 110, "CZ:motorway": 130}

CURV_HALF_M = 30.0  # 3-point circumradius: points at s-30, s, s+30 m
GRADE_HALF_M = 30.0  # grade = height difference over s-30 .. s+30 m
STEP_M = 10.0


class Window:
    def __init__(self, name, lat0, lon0, size_km):
        self.name, self.lat0, self.lon0, self.size_km = name, lat0, lon0, size_km
        self.half = size_km * 500.0
        self.cos = math.cos(math.radians(lat0))
        self.bbox = (lat0 - self.half / M_PER_DEG, lon0 - self.half / (M_PER_DEG * self.cos),
                     lat0 + self.half / M_PER_DEG, lon0 + self.half / (M_PER_DEG * self.cos))  # south, west, north, east

    def project(self, lat, lon):
        return (lon - self.lon0) * self.cos * M_PER_DEG, (lat - self.lat0) * M_PER_DEG

    def unproject(self, x, z):
        return self.lat0 + z / M_PER_DEG, self.lon0 + x / (M_PER_DEG * self.cos)

    def inside(self, x, z):
        return abs(x) <= self.half and abs(z) <= self.half

    def tiles(self, n):
        """n x n bboxes (south, west, north, east), row-major from the south-west."""
        s, w, nn, e = self.bbox
        out = []
        for r in range(n):
            for c in range(n):
                out.append((s + (nn - s) * r / n, w + (e - w) * c / n, s + (nn - s) * (r + 1) / n, w + (e - w) * (c + 1) / n))
        return out


# ---- tags -------------------------------------------------------------------------------------------------------------

def _first_number(v):
    if v is None:
        return None
    m = re.match(r"\s*(\d+(?:[.,]\d+)?)", v)
    return float(m.group(1).replace(",", ".")) if m else None


def parse_lanes(v):
    n = _first_number(v)
    return int(n) if n is not None and 1 <= n <= 12 else None


def parse_width(v):
    if v is None:
        return None
    m = re.fullmatch(r"\s*(\d+(?:[.,]\d+)?)\s*(m)?\s*", v)
    if not m:
        return None  # feet, inches, free text
    w = float(m.group(1).replace(",", "."))
    return round(w, 2) if 0 < w <= 60 else None


def parse_maxspeed(v):
    if v is None:
        return None
    if v in CZ_MAXSPEED:
        return CZ_MAXSPEED[v]
    m = re.fullmatch(r"\s*(\d+)\s*(km/h|mph)?\s*", v)
    if not m:
        return None
    n = int(m.group(1)) * (1.609 if m.group(2) == "mph" else 1.0)
    return int(round(n)) if 5 <= n <= 200 else None


def parse_layer(v):
    try:
        return max(-5, min(5, int(v)))
    except (TypeError, ValueError):
        return 0


def way_attrs(tags):
    """Edge attributes from OSM tags, or None when the way is not a drivable road class."""
    hw = tags.get("highway")
    cls = CLASS_OF.get(hw)
    if cls is None:
        return None
    ow = tags.get("oneway")
    roundabout = tags.get("junction") in ("roundabout", "circular")
    if ow in ("yes", "true", "1", "-1"):
        oneway = True
    elif ow in ("no", "false", "0"):
        oneway = False
    else:
        oneway = hw in ("motorway", "motorway_link") or roundabout
    tunnel = tags.get("tunnel") not in (None, "no", "culvert")
    bridge = tags.get("bridge") not in (None, "no")
    return {"class": cls, "highway": hw, "lanes": parse_lanes(tags.get("lanes")), "width_m": parse_width(tags.get("width")),
            "maxspeed": parse_maxspeed(tags.get("maxspeed")), "oneway": oneway, "reverse": ow == "-1", "roundabout": roundabout,
            "surface": tags.get("surface"), "layer": parse_layer(tags.get("layer")),
            "span": "tunnel" if tunnel else ("bridge" if bridge else "ground")}


# ---- geometry ---------------------------------------------------------------------------------------------------------

def polyline_length(pts):
    return sum(math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1))


def resample(pts, step=STEP_M):
    """Equally spaced samples (spacing close to step, both ends included). Returns (samples [(x, z)], s [m along], spacing)."""
    cum = [0.0]
    for i in range(len(pts) - 1):
        cum.append(cum[-1] + math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]))
    total = cum[-1]
    closed = len(pts) > 2 and pts[0] == pts[-1]
    n = max(8 if closed else 2, int(round(total / step)))  # a loop needs enough points to keep its shape
    ds = total / n
    out, ss = [], []
    j = 0
    for i in range(n + 1):
        s = i * ds
        while j < len(pts) - 2 and cum[j + 1] < s:
            j += 1
        seg = cum[j + 1] - cum[j]
        t = 0.0 if seg <= 0 else min(1.0, max(0.0, (s - cum[j]) / seg))
        out.append((pts[j][0] + (pts[j + 1][0] - pts[j][0]) * t, pts[j][1] + (pts[j + 1][1] - pts[j][1]) * t))
        ss.append(s)
    return out, ss, ds


def curvature_series(samples, ds, half_m=CURV_HALF_M):
    """Signed curvature 1/m per sample (left turn positive, x east / z north) from the circumradius of the points half_m before, at and after
    the sample (k = round(half_m / ds) samples). Only samples with k <= i <= n-k are real; the ends are filled with the nearest real value
    (0.0 when the edge is shorter than 2*half_m). Returns (kappa list, (first, last) valid index range or None)."""
    n = len(samples)
    k = max(1, int(round(half_m / ds)))
    kap = [0.0] * n
    if n < 2 * k + 1:
        return kap, None
    for i in range(k, n - k):
        a, b, c = samples[i - k], samples[i], samples[i + k]
        ab = (b[0] - a[0], b[1] - a[1])
        bc = (c[0] - b[0], c[1] - b[1])
        cross = ab[0] * bc[1] - ab[1] * bc[0]
        den = math.hypot(*ab) * math.hypot(*bc) * math.hypot(c[0] - a[0], c[1] - a[1])
        kap[i] = 0.0 if den <= 1e-9 else 2.0 * cross / den
    for i in range(k):
        kap[i] = kap[k]
        kap[n - 1 - i] = kap[n - 1 - k]
    return kap, (k, n - 1 - k)


def bearing_deg(dx, dz):
    return math.degrees(math.atan2(dx, dz)) % 360.0  # clockwise from +z (north)


# ---- topology ---------------------------------------------------------------------------------------------------------

def build_graph(osm_nodes, ways, window, min_edge_m=0.5):
    """OSM -> (nodes, edges). Edges are cut at every node used by two or more way pieces and at way ends; pieces outside the window
    are dropped (their cut ends are flagged boundary). Edge dict keeps its projected polyline in 'pts' (removed before writing)."""
    segs = []
    ring_radius = {}  # osm node id -> roundabout radius
    for w in sorted(ways, key=lambda w: w["id"]):
        at = way_attrs(w["tags"])
        if at is None:
            continue
        ids = [n for n in w["nds"] if n in osm_nodes]
        ids = [n for i, n in enumerate(ids) if i == 0 or n != ids[i - 1]]
        if at["reverse"]:
            ids.reverse()
        xz = [window.project(*osm_nodes[n]) for n in ids]
        if at["roundabout"] and len(ids) >= 4:
            cx = sum(p[0] for p in xz) / len(xz)
            cz = sum(p[1] for p in xz) / len(xz)
            r = sum(math.hypot(p[0] - cx, p[1] - cz) for p in xz) / len(xz)
            for n in ids:
                ring_radius[n] = round(r, 1)
        inside = [window.inside(*p) for p in xz]
        i = 0
        while i < len(ids):
            if not inside[i]:
                i += 1
                continue
            j = i
            while j + 1 < len(ids) and inside[j + 1]:
                j += 1
            if j > i:
                segs.append({"way": w["id"], "attrs": at, "ids": ids[i:j + 1], "xz": xz[i:j + 1], "cut0": i > 0, "cut1": j < len(ids) - 1})
            i = j + 1
    uses = Counter()
    for sg in segs:
        for n in sg["ids"]:
            uses[n] += 1
    pieces = []  # (way, attrs, ids, xz)
    boundary = set()
    for sg in segs:
        ids, xz = sg["ids"], sg["xz"]
        if sg["cut0"]:
            boundary.add(ids[0])
        if sg["cut1"]:
            boundary.add(ids[-1])
        start = 0
        for i in range(1, len(ids)):
            if uses[ids[i]] >= 2 or i == len(ids) - 1:
                pieces.append((sg["way"], sg["attrs"], ids[start:i + 1], xz[start:i + 1]))
                start = i
    node_ids = sorted({p[2][0] for p in pieces} | {p[2][-1] for p in pieces})
    nidx = {osm: i for i, osm in enumerate(node_ids)}
    pos = {}
    edges = []
    for way, at, ids, xz in pieces:
        length = polyline_length(xz)
        if length < 1e-6 or (length < min_edge_m and ids[0] == ids[-1]):
            continue
        e = {"id": len(edges), "a": nidx[ids[0]], "b": nidx[ids[-1]], "osm_way": way, "class": at["class"], "highway": at["highway"],
             "lanes": at["lanes"], "width_m": at["width_m"], "surface": at["surface"], "oneway": at["oneway"], "maxspeed": at["maxspeed"],
             "roundabout": at["roundabout"], "layer": at["layer"], "span": at["span"], "length_m": round(length, 2), "pts": xz}
        edges.append(e)
        pos[ids[0]] = xz[0]
        pos[ids[-1]] = xz[-1]
    used = Counter()
    for e in edges:
        used[e["a"]] += 1
        used[e["b"]] += 1
    nodes = []
    for osm in node_ids:
        i = nidx[osm]
        if used[i] == 0:
            continue
        nodes.append({"id": i, "osm_id": osm, "pos": [round(pos[osm][0], 2), 0.0, round(pos[osm][1], 2)], "degree": used[i], "kind": "joint",
                      "layer": 0, "boundary": osm in boundary, "roundabout_radius_m": ring_radius.get(osm)})
    # ids must stay dense for the validator and the stats: renumber
    remap = {n["id"]: i for i, n in enumerate(nodes)}
    for n in nodes:
        n["id"] = remap[n["id"]]
    for e in edges:
        e["a"], e["b"] = remap[e["a"]], remap[e["b"]]
    assign_node_attrs(nodes, edges)
    return nodes, edges


def assign_node_attrs(nodes, edges):
    inc = defaultdict(list)
    for e in edges:
        inc[e["a"]].append(e)
        inc[e["b"]].append(e)
    for n in nodes:
        es = inc[n["id"]]
        n["degree"] = len(es)
        layers = {e["layer"] for e in es}
        n["layer"] = layers.pop() if len(layers) == 1 else 0
        classes = {e["class"] for e in es}
        if n["degree"] >= 3:
            n["kind"] = "interchange" if ("link" in classes and "motorway" in classes) else "junction"
        elif n["degree"] == 1:
            n["kind"] = "end"
        else:
            n["kind"] = "portal" if len({e["span"] for e in es}) > 1 else "joint"


def chains(edges, key):
    """Contract nodes of exactly two distinct edges with equal non-None key(edge). Returns [{'edges': [(eid, forward)], 'a', 'b', 'length'}]."""
    adj = defaultdict(list)
    for e in edges:
        adj[e["a"]].append(e["id"])
        adj[e["b"]].append(e["id"])
    parent = list(range(len(edges)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for es in adj.values():
        if len(es) == 2 and es[0] != es[1]:
            k0, k1 = key(edges[es[0]]), key(edges[es[1]])
            if k0 is not None and k0 == k1:
                parent[find(es[0])] = find(es[1])
    groups = defaultdict(list)
    for e in edges:
        groups[find(e["id"])].append(e["id"])
    out = []
    for gid in sorted(groups, key=lambda g: groups[g][0]):
        members = groups[gid]
        ends = Counter()
        for i in members:
            ends[edges[i]["a"]] += 1
            ends[edges[i]["b"]] += 1
        start = next((n for n in sorted(ends) if ends[n] == 1), edges[members[0]]["a"])
        remaining = set(members)
        cur, seq, length = start, [], 0.0
        while remaining:
            nxt = next((i for i in adj[cur] if i in remaining), None)
            if nxt is None:  # should not happen for a path or a cycle; fall back to the smallest remaining edge
                nxt = min(remaining)
                cur = edges[nxt]["a"]
            e = edges[nxt]
            fwd = e["a"] == cur
            seq.append((nxt, fwd))
            length += e["length_m"]
            cur = e["b"] if fwd else e["a"]
            remaining.discard(nxt)
        out.append({"edges": seq, "a": start, "b": cur, "length": length})
    return out


def apply_heights(edges, height_fn, step=STEP_M):
    """Resample every edge, drape it on the DEM (height_fn(x, z) -> y) and replace the heights of tunnel/bridge chains by a straight line between
    their end heights (the DEM is a surface model: it shows the hill over a tunnel or the valley under a bridge, not the deck)."""
    for e in edges:
        e["_xz"], e["_s"], e["step_m"] = resample(e["pts"], step)
        e["_y"] = [height_fn(x, z) for x, z in e["_xz"]]
        e["_dem"] = list(e["_y"])  # raw surface height, kept for the span cover depth / clearance
    for ch in chains(edges, lambda e: None if e["span"] == "ground" else (e["span"], e["class"])):
        first = edges[ch["edges"][0][0]]
        last = edges[ch["edges"][-1][0]]
        y0 = first["_y"][0] if ch["edges"][0][1] else first["_y"][-1]
        y1 = last["_y"][-1] if ch["edges"][-1][1] else last["_y"][0]
        total = ch["length"]
        off = 0.0
        for eid, fwd in ch["edges"]:
            e = edges[eid]
            L = e["_s"][-1]
            e["_y"] = [y0 + (y1 - y0) * ((off + (s if fwd else L - s)) / total if total > 0 else 0.0) for s in e["_s"]]
            off += L
    for e in edges:
        e["centreline"] = [v for (x, z), y in zip(e["_xz"], e["_y"]) for v in (round(x, 1), round(y, 1), round(z, 1))]
        e["bank_deg"] = None  # OSM carries no banking
    return edges


def profiles(edges):
    """Per edge: curvature (1/m) and grade (%) series with the valid curvature index range."""
    from dem import grade_series
    out = []
    for e in edges:
        kap, valid = curvature_series(e["_xz"], e["step_m"])
        out.append({"edge": e["id"], "step_m": round(e["step_m"], 3), "curvature": [round(k, 5) for k in kap],
                    "curv_valid": list(valid) if valid else None, "grade_pct": [round(g, 2) for g in grade_series(e["_s"], e["_y"], GRADE_HALF_M)]})
    return out


def finish_nodes(nodes, edges):
    """Node heights from the incident edge samples (after the span override) so that edges and nodes agree."""
    for e in edges:
        nodes[e["a"]]["pos"][1] = round(e["_y"][0], 1)
        nodes[e["b"]]["pos"][1] = round(e["_y"][-1], 1)


def leg_bearing(e, at_a, reach_m=30.0):
    """Bearing (deg) of the edge leaving its end a (at_a) or b, measured to the sample about reach_m away."""
    xz, s = e["_xz"], e["_s"]
    if at_a:
        j = next((i for i in range(1, len(s)) if s[i] >= reach_m), len(s) - 1)
        dx, dz = xz[j][0] - xz[0][0], xz[j][1] - xz[0][1]
    else:
        L = s[-1]
        j = next((i for i in range(len(s) - 2, -1, -1) if L - s[i] >= reach_m), 0)
        dx, dz = xz[j][0] - xz[-1][0], xz[j][1] - xz[-1][1]
    return bearing_deg(dx, dz)


def junction_records(nodes, edges):
    inc = defaultdict(list)
    for e in edges:
        inc[e["a"]].append((e, True))
        inc[e["b"]].append((e, False))
    out = []
    for n in nodes:
        if n["degree"] < 3:
            continue
        legs = sorted(((round(leg_bearing(e, at_a), 1) % 360.0, e["id"], e["class"]) for e, at_a in inc[n["id"]]), key=lambda t: (t[0], t[1]))
        b = [t[0] for t in legs]
        gaps = [(b[(i + 1) % len(b)] - b[i]) % 360.0 for i in range(len(b))]
        out.append({"node": n["id"], "legs": len(legs), "bearings": b, "leg_edges": [t[1] for t in legs], "leg_classes": [t[2] for t in legs],
                    "min_leg_angle_deg": round(min(gaps), 1), "roundabout_radius_m": n.get("roundabout_radius_m"),
                    "interchange_kind": "motorway_ramp" if n["kind"] == "interchange" else None})
    return out


def span_records(nodes, edges):
    out = []
    for ch in chains(edges, lambda e: None if e["span"] == "ground" else (e["span"], e["class"])):
        e0 = edges[ch["edges"][0][0]]
        if e0["span"] == "ground":
            continue
        first, last = edges[ch["edges"][0][0]], edges[ch["edges"][-1][0]]
        # centreline minus DEM over every sample of the chain: tunnel cover depth = -median (DEM above the road), bridge clearance = +median
        diffs = sorted(y - d for eid, _ in ch["edges"] for y, d in zip(edges[eid]["_y"], edges[eid]["_dem"]))
        mid = (diffs[len(diffs) // 2] + diffs[(len(diffs) - 1) // 2]) / 2.0
        out.append({"id": len(out), "kind": e0["span"], "class": e0["class"], "edges": [i for i, _ in ch["edges"]], "start_node": ch["a"],
                    "end_node": ch["b"], "length_m": round(ch["length"], 1),
                    "cover_depth_m": round(-mid, 1) if e0["span"] == "tunnel" else None,
                    "clearance_m": round(mid, 1) if e0["span"] == "bridge" else None,
                    "portal_heights_m": [round(first["_y"][0] if ch["edges"][0][1] else first["_y"][-1], 1),
                                         round(last["_y"][-1] if ch["edges"][-1][1] else last["_y"][0], 1)],
                    "layer": e0["layer"]})
    return out
