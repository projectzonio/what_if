#!/usr/bin/env python
"""RG-107 validator for nightracer.worldgen.road-dataset.v1 (stdlib only). Exit code != 0 on any error.

Checks: all files present with the schema id; field presence and types; units (metres, degrees, percent) and plausible ranges; no NaN/inf;
references (edge a/b, junction node, span edges, profile edge) resolve; derived values agree (node degree, edge length vs polyline, series lengths);
provenance complete (ODbL, DEM credit, sha256, bytes, URL per source). Reports (does not fail on) the share of edges without lanes / width,
which is a property of OSM data.

RG-108: provenance.source_kind selects the provenance checks. "osm" (default): the checks above. "kn5": ADR id, archive sha256 (64 hex, or the literal "unknown" which is
reported), extractor commit, parameters with class_source / lanes_source, no ODbL text anywhere; roadside.json is required (one item per edge, ceil(length / 100 m) bins, left and
right) whereas it is optional for osm. For kn5 the report adds the unknown shares by sample (width none, surface missing, cross, open sides) and the class / lanes sources; shares
are reported, not failed (the ticket acceptance bounds them).

RG-115: a provenance with a "generator" block (W3 export, no source_kind) is checked as source_kind "generator": seed, spec, rules sha256, ODbL / Copernicus texts; source tiles are optional.

Usage: python -X utf8 -I tools/worldgen/validate_dataset.py --dir artifacts/worldgen/osm-ustinad
"""
import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import graph  # noqa: E402

FILES = ("provenance", "nodes", "edges", "profiles", "junctions", "spans")
OPTIONAL = ("stats", "roadside")
HEX = set("0123456789abcdef")


def finite(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def is_int(v):
    return isinstance(v, int) and not isinstance(v, bool)


def load(dirpath, errs):
    data = {}
    for name in FILES + OPTIONAL:
        path = os.path.join(dirpath, name + ".json")
        if not os.path.exists(path):
            if name not in OPTIONAL:
                errs.append("missing file %s.json" % name)
            continue
        try:
            with open(path, encoding="utf-8") as f:
                data[name] = json.load(f, parse_constant=lambda c: (_ for _ in ()).throw(ValueError("non-finite constant " + c)))
        except (ValueError, OSError) as ex:
            errs.append("%s.json unreadable: %s" % (name, ex))
    return data


def _provenance_osm(prov, err):
    for k in ("adr", "extractor", "date", "window", "parameters", "osm", "dem", "sources"):
        if k not in prov:
            err("provenance: missing %s" % k)
    if "ODbL" not in str(prov.get("osm", {}).get("licence", "")):
        err("provenance: OSM licence (ODbL) missing")
    if "COPERNICUS" not in str(prov.get("dem", {}).get("credit", "")):
        err("provenance: Copernicus DEM credit text missing")
    srcs = prov.get("sources") or []
    if not srcs:
        err("provenance: no sources")
    kinds = set()
    for i, s in enumerate(srcs):
        kinds.add(s.get("kind"))
        sha = s.get("sha256", "")
        if not (isinstance(sha, str) and len(sha) == 64 and set(sha) <= HEX):
            err("provenance.sources[%d]: bad sha256" % i)
        if not (is_int(s.get("bytes")) and s["bytes"] > 0):
            err("provenance.sources[%d]: bad bytes" % i)
        for k in ("url", "date", "licence"):
            if not s.get(k):
                err("provenance.sources[%d]: missing %s" % (i, k))
    for k in ("overpass-tile", "dem-tile"):
        if k not in kinds:
            err("provenance: no %s source" % k)


def _provenance_generator(prov, err):
    """RG-115: a dataset exported by src/WhatIf.WorldGen (W3). No OSM geometry is inside, so no source tiles are required; what is required is the
    reproducibility block (seed, spec, sha256 of the rules) and the licence / credit texts of the open data the rules were learned from (ODbL, Copernicus).
    When the source provenance was supplied (--source-provenance), its sources are checked like an OSM dataset."""
    for k in ("adr", "extractor", "date", "purpose", "window", "parameters", "osm", "dem", "sources", "generator"):
        if k not in prov:
            err("provenance: missing %s" % k)
    gen = prov.get("generator") or {}
    sha = gen.get("rules_sha256", "")
    if not (isinstance(sha, str) and len(sha) == 64 and set(sha) <= HEX):
        err("provenance.generator: bad rules_sha256")
    if not is_int(gen.get("seed")):
        err("provenance.generator: seed missing or not an integer")
    if not isinstance(gen.get("spec"), dict):
        err("provenance.generator: spec missing")
    if "ODbL" not in str((prov.get("osm") or {}).get("licence", "")):
        err("provenance: OSM licence (ODbL) missing")
    if "copernicus" not in str((prov.get("dem") or {}).get("credit", "")).lower():
        err("provenance: Copernicus DEM credit text missing")
    for i, s in enumerate(prov.get("sources") or []):
        s_sha = s.get("sha256", "")
        if not (isinstance(s_sha, str) and len(s_sha) == 64 and set(s_sha) <= HEX):
            err("provenance.sources[%d]: bad sha256" % i)
        if not (is_int(s.get("bytes")) and s["bytes"] > 0):
            err("provenance.sources[%d]: bad bytes" % i)


def _provenance_kn5(prov, err, rep):
    """RG-108: a KN5 map dataset. Licence texts of OSM / DEM must not appear (the data is measured on a private local map, not derived from ODbL data)."""
    for k in ("adr", "extractor", "date", "window", "parameters", "sources"):
        if k not in prov:
            err("provenance: missing %s" % k)
    if "ODbL" in json.dumps(prov) or "osm" in prov or "dem" in prov:
        err("provenance: OSM/DEM/ODbL content in a kn5 dataset")
    ex = prov.get("extractor") or {}
    commit = ex.get("commit")
    if not (isinstance(commit, str) and 7 <= len(commit) <= 40 and set(commit.lower()) <= HEX):
        err("provenance.extractor: commit missing or not hex (%r)" % (commit,))
    par = prov.get("parameters") or {}
    for k in ("class_source", "lanes_source"):
        if not par.get(k):
            err("provenance.parameters: missing %s" % k)
    srcs = prov.get("sources") or []
    if not srcs:
        err("provenance: no sources")
    unknown = []
    for i, s in enumerate(srcs):
        sha = s.get("sha256", "")
        if sha == "unknown":
            unknown.append("sources[%d].sha256" % i)
        elif not (isinstance(sha, str) and len(sha) == 64 and set(sha) <= HEX):
            err("provenance.sources[%d]: bad sha256" % i)
        if s.get("bytes") is None:
            unknown.append("sources[%d].bytes" % i)
        elif not (is_int(s["bytes"]) and s["bytes"] > 0):
            err("provenance.sources[%d]: bad bytes" % i)
        if not s.get("adr"):
            err("provenance.sources[%d]: missing adr" % i)
    rep["provenance_unknown"] = unknown


def _roadside(roadside, eid, err):
    items = roadside.get("items") if isinstance(roadside, dict) else None
    if not isinstance(items, list):
        err("roadside.json: no items")
        return 0
    by = {}
    for it in items:
        e = eid.get(it.get("edge"))
        if e is None:
            err("roadside item for unknown edge %r" % (it.get("edge"),))
            continue
        if it["edge"] in by:
            err("roadside: edge %s twice" % it["edge"])
        by[it["edge"]] = it
        want = max(1, int(math.ceil(e["length_m"] / 100.0 - 1e-9)))
        bins = it.get("bins")
        if not isinstance(bins, list) or len(bins) != want:
            err("roadside edge %s: %s bins, expected %d" % (it["edge"], len(bins) if isinstance(bins, list) else None, want))
            continue
        for k, b in enumerate(bins):
            if b.get("bin") != k or not all(isinstance(b.get(side), dict) for side in ("left", "right")):
                err("roadside edge %s bin %d: malformed" % (it["edge"], k))
                break
            for side in ("left", "right"):
                sd = b[side]
                if not (is_int(sd.get("cells")) and is_int(sd.get("occupied")) and 0 <= sd["occupied"] <= sd["cells"] and isinstance(sd.get("occupancy"), dict)):
                    err("roadside edge %s bin %d %s: cells/occupied/occupancy" % (it["edge"], k, side))
                    break
    for i in eid:
        if i not in by:
            err("edge %s has no roadside bins" % i)
    return len(by)


def validate(data):
    """Returns (errors, report). data = dict of the parsed files (see FILES)."""
    errs, rep = [], {}

    def err(msg):
        errs.append(msg)

    for name, doc in data.items():
        if name != "stats" and doc.get("schema") != graph.SCHEMA:
            err("%s.json: schema is %r, expected %r" % (name, doc.get("schema"), graph.SCHEMA))
    prov = data.get("provenance", {})
    kind_src = prov.get("source_kind") or ("generator" if "generator" in prov else "osm")   # RG-115: a W3 export carries a generator block instead of source_kind
    if kind_src not in ("osm", "kn5", "generator"):
        err("provenance: source_kind %r" % (kind_src,))
    if kind_src == "kn5":
        _provenance_kn5(prov, err, rep)
    elif kind_src == "generator":
        _provenance_generator(prov, err)
    else:
        _provenance_osm(prov, err)
    half = float(prov.get("window", {}).get("size_km", 0)) * 500.0 + 50.0

    nodes = data.get("nodes", {}).get("items", [])
    edges = data.get("edges", {}).get("items", [])
    profs = data.get("profiles", {}).get("items", [])
    juncs = data.get("junctions", {}).get("items", [])
    spans = data.get("spans", {}).get("items", [])
    if not nodes or not edges:
        err("no nodes or no edges")
    nid = {}
    for n in nodes:
        i = n.get("id")
        if not is_int(i) or i in nid:
            err("node id %r missing or duplicated" % (i,))
            continue
        nid[i] = n
        p = n.get("pos")
        if not (isinstance(p, list) and len(p) == 3 and all(finite(v) for v in p)):
            err("node %s: bad pos" % i)
        else:
            if abs(p[0]) > half or abs(p[2]) > half:
                err("node %s: pos outside the window (%s)" % (i, p))
            if not -500 <= p[1] <= 9000:
                err("node %s: implausible height %s m" % (i, p[1]))
        if n.get("kind") not in graph.NODE_KINDS:
            err("node %s: kind %r" % (i, n.get("kind")))
        if not (is_int(n.get("degree")) and n["degree"] >= 1) or not is_int(n.get("layer")):
            err("node %s: bad degree/layer" % i)
    deg = {}
    eid = {}
    no_lanes, no_width, per_class = {}, {}, {}
    for e in edges:
        i = e.get("id")
        if not is_int(i) or i in eid:
            err("edge id %r missing or duplicated" % (i,))
            continue
        eid[i] = e
        if e.get("a") not in nid or e.get("b") not in nid:
            err("edge %s: node reference a=%r b=%r not found" % (i, e.get("a"), e.get("b")))
        else:
            deg[e["a"]] = deg.get(e["a"], 0) + 1
            deg[e["b"]] = deg.get(e["b"], 0) + 1
        c = e.get("class")
        if c not in graph.CLASSES:
            err("edge %s: class %r" % (i, c))
        if e.get("span") not in graph.SPANS:
            err("edge %s: span %r" % (i, e.get("span")))
        if not is_int(e.get("layer")):
            err("edge %s: layer" % i)
        if e.get("lanes") is not None and not (is_int(e["lanes"]) and 1 <= e["lanes"] <= 12):
            err("edge %s: lanes %r" % (i, e["lanes"]))
        if e.get("width_m") is not None and not (finite(e["width_m"]) and 0 < e["width_m"] <= 60):
            err("edge %s: width_m %r" % (i, e["width_m"]))
        if e.get("maxspeed") is not None and not (is_int(e["maxspeed"]) and 5 <= e["maxspeed"] <= 200):
            err("edge %s: maxspeed %r" % (i, e["maxspeed"]))
        if not isinstance(e.get("oneway"), bool):
            err("edge %s: oneway" % i)
        if not (finite(e.get("length_m")) and e["length_m"] > 0):
            err("edge %s: length_m %r" % (i, e.get("length_m")))
        if not (finite(e.get("step_m")) and 0 < e["step_m"] <= 15):
            err("edge %s: step_m %r" % (i, e.get("step_m")))
        cl = e.get("centreline")
        if not (isinstance(cl, list) and len(cl) >= 6 and len(cl) % 3 == 0 and all(finite(v) for v in cl)):
            err("edge %s: bad centreline" % i)
        else:
            L = sum(math.hypot(cl[k + 3] - cl[k], cl[k + 5] - cl[k + 2]) for k in range(0, len(cl) - 3, 3))
            if L > e["length_m"] + 0.5 or L < 0.65 * e["length_m"] - 1.0:  # 10 m chords cut corners (tight hairpins and loops lose up to ~30 %): the sampled line is never longer
                err("edge %s: length_m %s vs centreline %.1f" % (i, e["length_m"], L))
            if any(not -500 <= cl[k] <= 9000 for k in range(1, len(cl), 3)):
                err("edge %s: implausible centreline height" % i)
        e_c = per_class.setdefault(c, [0, 0, 0, 0.0, 0.0, 0.0])
        e_c[0] += 1
        e_c[3] += e.get("length_m") or 0.0
        if e.get("lanes") is None:
            e_c[1] += 1
            e_c[4] += e.get("length_m") or 0.0
        if e.get("width_m") is None:
            e_c[2] += 1
            e_c[5] += e.get("length_m") or 0.0
    for i, n in nid.items():
        if deg.get(i, 0) != n.get("degree"):
            err("node %s: degree %s but %d edge ends" % (i, n.get("degree"), deg.get(i, 0)))
    seen = set()
    for p in profs:
        i = p.get("edge")
        e = eid.get(i)
        if e is None:
            err("profile for unknown edge %r" % (i,))
            continue
        seen.add(i)
        n = len(e["centreline"]) // 3 if isinstance(e.get("centreline"), list) else -1
        for k in ("curvature", "grade_pct"):
            v = p.get(k)
            if not (isinstance(v, list) and len(v) == n and all(finite(x) for x in v)):
                err("profile edge %s: %s length/finite check failed (expected %d)" % (i, k, n))
        if isinstance(p.get("grade_pct"), list) and any(finite(x) and abs(x) > 100 for x in p["grade_pct"]):
            err("profile edge %s: grade over 100 percent" % i)
        for k in ("vradius_crest_min_m", "vradius_sag_min_m"):   # RG-123, optional: smallest vertical radius over the edge (null = no curved station)
            if k in p and p[k] is not None and not (finite(p[k]) and p[k] > 0):
                err("profile edge %s: %s must be null or a positive number" % (i, k))
        cv = p.get("curv_valid")
        if cv is not None and not (isinstance(cv, list) and len(cv) == 2 and 0 <= cv[0] <= cv[1] < n):
            err("profile edge %s: curv_valid %r" % (i, cv))
    for i in eid:
        if i not in seen:
            err("edge %s has no profile" % i)
    for j in juncs:
        n = nid.get(j.get("node"))
        if n is None:
            err("junction at unknown node %r" % (j.get("node"),))
            continue
        b = j.get("bearings")
        if not (isinstance(b, list) and len(b) == j.get("legs") == n["degree"] >= 3 and all(finite(x) and 0 <= x < 360.0 for x in b)):
            err("junction node %s: legs/bearings do not match degree %s" % (j.get("node"), n["degree"]))
        if not finite(j.get("min_leg_angle_deg")):
            err("junction node %s: min_leg_angle_deg" % j.get("node"))
    if sum(1 for n in nodes if n.get("degree", 0) >= 3) != len(juncs):
        err("junctions.json has %d records for %d nodes of degree >= 3" % (len(juncs), sum(1 for n in nodes if n.get("degree", 0) >= 3)))
    for s in spans:
        if s.get("kind") not in ("tunnel", "bridge"):
            err("span %s: kind %r" % (s.get("id"), s.get("kind")))
        if not (finite(s.get("length_m")) and s["length_m"] > 0):
            err("span %s: length_m" % s.get("id"))
        if not (isinstance(s.get("edges"), list) and s["edges"] and all(i in eid for i in s["edges"])):
            err("span %s: edge references" % s.get("id"))
    if kind_src == "kn5":
        if "roadside" not in data:
            err("missing file roadside.json (required for kn5)")
        else:
            rep["roadside_edges"] = _roadside(data["roadside"], eid, err)
        q = {"samples": 0, "frame": 0, "cross": 0, "none": 0, "surface_missing": 0, "open": 0}
        for e in edges:
            for k in q:
                q[k] += (e.get("quality") or {}).get(k, 0)
        n = max(1, q["samples"])
        rep["kn5_shares"] = {"samples": q["samples"], "width_none": round(q["none"] / n, 4), "surface_missing": round(q["surface_missing"] / n, 4), "cross": round(q["cross"] / n, 4),
                             "frame": round(q["frame"] / n, 4), "open_side": round(q["open"] / n, 4)}
        rep["class_source"] = sorted({e.get("class_source") for e in edges}, key=str)
        rep["lanes_source"] = sorted({e.get("lanes_source") for e in edges}, key=str)
        rep["span_edges"] = dict((k, sum(1 for e in edges if e.get("span") == k)) for k in graph.SPANS)
    if "stats" in data and not data["stats"].get("classes"):
        err("stats.json has no classes")
    rep["edges_per_class"] = {c: {"n": v[0], "share_no_lanes": round(v[1] / v[0], 3) if v[0] else None, "share_no_width": round(v[2] / v[0], 3) if v[0] else None,
                                  "length_share_no_lanes": round(v[4] / v[3], 3) if v[3] else None,
                                  "length_share_no_width": round(v[5] / v[3], 3) if v[3] else None} for c, v in sorted(per_class.items())}
    return errs, rep


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", required=True)
    a = ap.parse_args(argv)
    errs = []
    data = load(a.dir, errs)
    if not errs:
        e2, rep = validate(data)
        errs += e2
        print("validate: %d nodes, %d edges, %d profiles, %d junctions, %d spans" % tuple(len(data[k].get("items", [])) for k in ("nodes", "edges", "profiles", "junctions", "spans")))
        if "kn5_shares" in rep:
            print("  kn5 shares by sample: %s" % json.dumps(rep["kn5_shares"]))
            print("  class_source %s, lanes_source %s, roadside edges %d, edges by span %s" % (rep["class_source"], rep["lanes_source"], rep.get("roadside_edges", 0), rep["span_edges"]))
            if rep.get("provenance_unknown"):
                print("  provenance fields recorded as unknown: %s" % ", ".join(rep["provenance_unknown"]))
        for c, v in rep["edges_per_class"].items():
            print("  %-9s n=%-6d without lanes %5.1f%% (by length %5.1f%%), without width %5.1f%% (by length %5.1f%%)" % (
                c, v["n"], 100 * v["share_no_lanes"], 100 * v["length_share_no_lanes"], 100 * v["share_no_width"], 100 * v["length_share_no_width"]))
    if errs:
        print("validate: FAILED with %d error(s)" % len(errs))
        for m in errs[:30]:
            print("  - " + m)
        return 1
    print("validate: OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
