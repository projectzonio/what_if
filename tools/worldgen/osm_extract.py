#!/usr/bin/env python
"""RG-107 OSM extractor: tiled Overpass fetch (cached) -> road-geometry dataset nightracer.worldgen.road-dataset.v1 (stdlib only).

OSM is used to LEARN RULES only (ADR-032): the dataset stays outside Git (artifacts/, git-ignored), raw Overpass answers stay under
D:/workspace/race_game/temp/opendata/ (override: env NR_OPENDATA_DIR). Licence: ODbL 1.0, credit "© OpenStreetMap contributors".

Commands (run from the repo root):
  python -X utf8 -I tools/worldgen/osm_extract.py all [--window ustinad]   fetch (cache) + build + validate + stats; re-runs need no network
  python -X utf8 -I tools/worldgen/osm_extract.py fetch                    only the Overpass tiles and the DEM tiles into the cache
  python -X utf8 -I tools/worldgen/osm_extract.py build                    cache -> artifacts/worldgen/osm-<window>/ (then validate_dataset.py, dataset_stats.py)
"""
import argparse
import hashlib
import json
import os
import socket
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dem  # noqa: E402
import graph  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
EXTRACTOR_VERSION = "rg107-1"
OVERPASS_URL = "https://overpass-api.de/api/interpreter"
USER_AGENT = "NightRacer-worldgen/0.1 (personal noncommercial research; rules only, ADR-032)"
HIGHWAY_RE = "^(motorway|motorway_link|trunk|trunk_link|primary|primary_link|secondary|secondary_link|tertiary|tertiary_link|unclassified|residential|living_street|track)$"
OSM_LICENCE = ("ODbL 1.0 (https://opendatacommons.org/licenses/odbl/1-0/); credit: © OpenStreetMap contributors "
               "(https://www.openstreetmap.org/copyright). Used to learn rules only; the derived dataset is not redistributed.")
WINDOWS = {  # name -> (centre lat, centre lon, size km)
    "ustinad": (50.66, 14.04, 60.0),  # Usti nad Labem: D8 + tunnels (Ceske stredohori), city, villages, Krusne hory foothills
}
PAUSE_S = 8.0


def opendata_dir():
    return os.environ.get("NR_OPENDATA_DIR", "D:/workspace/race_game/temp/opendata")


def make_query(bbox):
    s, w, n, e = bbox
    return ('[out:xml][timeout:300];\nway["highway"~"%s"](%.6f,%.6f,%.6f,%.6f)->.w;\n.w out body;\nnode(w.w);\nout skel qt;\n' % (HIGHWAY_RE, s, w, n, e))


def _ipv4_only():
    """overpass-api.de answers on IPv6 too, but the TLS handshake over it stalled on this notebook: resolve A records only."""
    real = socket.getaddrinfo

    def v4(host, port, family=0, type=0, proto=0, flags=0):
        return real(host, port, socket.AF_INET, type, proto, flags)
    socket.getaddrinfo = v4
    return real


def fetch_tile(bbox, path, log=print, attempts=5):
    """One Overpass request, cached: an existing file with its sidecar is returned without any network use."""
    side = path + ".meta.json"
    if os.path.exists(path) and os.path.exists(side):
        with open(side, encoding="utf-8") as f:
            return json.load(f)
    query = make_query(bbox)
    body = urllib.parse.urlencode({"data": query}).encode()
    for attempt in range(1, attempts + 1):
        try:
            req = urllib.request.Request(OVERPASS_URL, data=body, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=400) as r:
                data = r.read()
            if b"</osm>" not in data[-200:] or b"<remark>" in data:
                raise RuntimeError("incomplete or error answer: %r" % data[-300:])
            break
        except (urllib.error.URLError, OSError, RuntimeError) as ex:
            log("overpass: attempt %d/%d failed: %s" % (attempt, attempts, ex))
            if attempt == attempts:
                raise
            time.sleep(60.0 * attempt)
    tmp = path + ".part"
    with open(tmp, "wb") as f:
        f.write(data)
    os.replace(tmp, path)
    osm_base = None
    head = data[:1500].decode("utf-8", "replace")
    k = head.find("osm_base=")
    if k >= 0:
        osm_base = head[k + 10:k + 10 + 20]
    meta = {"kind": "overpass-tile", "url": OVERPASS_URL, "query": query, "date": time.strftime("%Y-%m-%d", time.gmtime()), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(), "licence": OSM_LICENCE, "osm_base": osm_base, "bbox": list(bbox), "file": os.path.basename(path)}
    with open(side, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)
    return meta


def fetch_all(window, tiles_n, osm_dir, log=print):
    os.makedirs(osm_dir, exist_ok=True)
    metas = []
    for i, bbox in enumerate(window.tiles(tiles_n)):
        path = os.path.join(osm_dir, "tile_%02d.osm" % i)
        cached = os.path.exists(path) and os.path.exists(path + ".meta.json")
        log("overpass tile %d/%d %s" % (i + 1, tiles_n * tiles_n, "(cache)" if cached else "(fetch)"))
        metas.append(fetch_tile(bbox, path, log))
        if not cached:
            time.sleep(PAUSE_S)
    return metas


def parse_osm_xml(paths, nodes=None, ways=None):
    """Streams Overpass XML files into nodes {id: (lat, lon)} and ways {id: {id, nds, tags}} (duplicates from tile overlap collapse by id)."""
    nodes = {} if nodes is None else nodes
    ways = {} if ways is None else ways
    for p in paths:
        src = p if hasattr(p, "read") else open(p, "rb")
        try:
            for _, el in ET.iterparse(src, events=("end",)):
                if el.tag == "node":
                    nodes[int(el.get("id"))] = (float(el.get("lat")), float(el.get("lon")))
                    el.clear()
                elif el.tag == "way":
                    wid = int(el.get("id"))
                    if wid not in ways:
                        ways[wid] = {"id": wid, "nds": [int(c.get("ref")) for c in el.findall("nd")],
                                     "tags": {c.get("k"): c.get("v") for c in el.findall("tag")}}
                    el.clear()
        finally:
            if not hasattr(p, "read"):
                src.close()
    return nodes, ways


def _dump(path, obj):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, separators=(",", ":"), allow_nan=False)


def build_dataset(window, osm_metas, dem_metas, osm_dir, dem_dir, out_dir, tiles_n, log=print):
    t0 = time.time()
    nodes_ll, ways = parse_osm_xml([os.path.join(osm_dir, m["file"]) for m in osm_metas])
    log("parsed %d nodes, %d ways in %.0f s" % (len(nodes_ll), len(ways), time.time() - t0))
    nodes, edges = graph.build_graph(nodes_ll, list(ways.values()), window)
    n_osm_nodes = len(nodes_ll)
    del nodes_ll
    log("graph: %d nodes, %d edges" % (len(nodes), len(edges)))
    demset = dem.DemSet([os.path.join(dem_dir, m["file"]) for m in dem_metas])

    def height_fn(x, z):
        lat, lon = window.unproject(x, z)
        return demset.height(lat, lon)

    graph.apply_heights(edges, height_fn)
    graph.finish_nodes(nodes, edges)
    profs = graph.profiles(edges)
    juncs = graph.junction_records(nodes, edges)
    spans = graph.span_records(nodes, edges)
    log("dem: %d height queries, %d voids; %d junctions, %d spans (%.0f s)" % (demset.queries, demset.voids, len(juncs), len(spans), time.time() - t0))
    for e in edges:
        for k in ("pts", "_xz", "_s", "_y", "_dem"):
            del e[k]
    os.makedirs(out_dir, exist_ok=True)
    sch = graph.SCHEMA
    _dump(os.path.join(out_dir, "nodes.json"), {"schema": sch, "items": nodes})
    _dump(os.path.join(out_dir, "edges.json"), {"schema": sch, "items": edges})
    _dump(os.path.join(out_dir, "profiles.json"), {"schema": sch, "items": profs})
    _dump(os.path.join(out_dir, "junctions.json"), {"schema": sch, "items": juncs})
    _dump(os.path.join(out_dir, "spans.json"), {"schema": sch, "items": spans})
    bases = sorted(m["osm_base"] for m in osm_metas if m.get("osm_base"))
    s, w, n, e = window.bbox
    prov = {"schema": sch, "adr": "ADR-030/031/032 (accepted 2026-10-29)", "extractor": {"script": "tools/worldgen/osm_extract.py", "version": EXTRACTOR_VERSION},
            "date": time.strftime("%Y-%m-%d", time.gmtime()), "purpose": "learn road rules only; no real road shape goes into the game (ADR-032)",
            "window": {"name": window.name, "centre_lat": window.lat0, "centre_lon": window.lon0, "size_km": window.size_km,
                       "bbox_south_west_north_east": [round(v, 6) for v in (s, w, n, e)],
                       "frame": "local equirectangular, metres, x east, y up, z north, origin at the centre (not the ADR-015 KN5 mapping)"},
            "parameters": {"overpass_tiles": "%dx%d" % (tiles_n, tiles_n), "highway_filter": HIGHWAY_RE, "service_roads_fetched": False, "step_m": graph.STEP_M,
                           "curvature_baseline_m": graph.CURV_HALF_M, "grade_baseline_m": graph.GRADE_HALF_M},
            "osm": {"licence": OSM_LICENCE, "osm_base_first": bases[0] if bases else None, "osm_base_last": bases[-1] if bases else None},
            "dem": {"licence": dem.DEM_LICENCE, "credit": dem_metas[0]["credit"], "voids": demset.voids, "queries": demset.queries},
            "counts": {"nodes": len(nodes), "edges": len(edges), "junctions": len(juncs), "spans": len(spans), "osm_nodes_read": n_osm_nodes, "osm_ways_read": len(ways)},
            "sources": [{k: m[k] for k in ("kind", "url", "date", "bytes", "sha256", "licence", "file") if k in m} | ({"credit": m["credit"]} if "credit" in m else {})
                        | ({"osm_base": m.get("osm_base"), "bbox": m["bbox"]} if m["kind"] == "overpass-tile" else {}) for m in list(osm_metas) + list(dem_metas)]}
    _dump(os.path.join(out_dir, "provenance.json"), prov)
    return prov


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["fetch", "build", "all"])
    ap.add_argument("--window", default="ustinad", choices=sorted(WINDOWS))
    ap.add_argument("--tiles", type=int, default=6, help="Overpass tiles per side (default 6 -> 10 x 10 km)")
    ap.add_argument("--out", default=None, help="output folder (default artifacts/worldgen/osm-<window>)")
    a = ap.parse_args(argv)
    lat0, lon0, size = WINDOWS[a.window]
    window = graph.Window(a.window, lat0, lon0, size)
    base = opendata_dir()
    osm_dir = os.path.join(base, "osm-%s" % a.window)
    dem_dir = os.path.join(base, "dem")
    out_dir = a.out or os.path.join(REPO, "artifacts", "worldgen", "osm-%s" % a.window)
    _ipv4_only()
    if a.cmd in ("fetch", "all", "build"):
        osm_metas = fetch_all(window, a.tiles, osm_dir)  # cached tiles do not touch the network
        dem_metas = dem.fetch_tiles(window.bbox, dem_dir)
    if a.cmd in ("build", "all"):
        build_dataset(window, osm_metas, dem_metas, osm_dir, dem_dir, out_dir, a.tiles)
    if a.cmd == "all":
        import dataset_stats
        import validate_dataset
        rc = validate_dataset.main(["--dir", out_dir])
        if rc != 0:
            return rc
        return dataset_stats.main(["--dir", out_dir])
    return 0


if __name__ == "__main__":
    sys.exit(main())
