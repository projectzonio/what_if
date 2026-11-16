#!/usr/bin/env python
"""RG-107 unit tests on tiny inline inputs (stdlib unittest, no network, no files outside a temp dir).

Run: python -X utf8 -I tools/worldgen/test_worldgen.py     (exit code 0 = all passed)
"""
import copy
import io
import json
import math
import os
import struct
import sys
import tempfile
import unittest
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dataset_stats  # noqa: E402
import dem  # noqa: E402
import graph  # noqa: E402
import osm_extract  # noqa: E402
import street_stats  # noqa: E402
import validate_dataset  # noqa: E402

WIN = graph.Window("test", 50.0, 14.0, 60.0)


def osm_xml(points, ways):
    """points {id: (x, z)} in metres, ways [(id, [node ids], {tags})] -> Overpass-like XML bytes."""
    out = ['<?xml version="1.0"?><osm version="0.6"><meta osm_base="2026-10-29T00:00:00Z"/>']
    for wid, nds, tags in ways:
        out.append('<way id="%d">%s%s</way>' % (wid, "".join('<nd ref="%d"/>' % n for n in nds), "".join('<tag k="%s" v="%s"/>' % kv for kv in tags.items())))
    for nid, (x, z) in points.items():
        lat, lon = WIN.unproject(x, z)
        out.append('<node id="%d" lat="%.8f" lon="%.8f"/>' % (nid, lat, lon))
    out.append("</osm>")
    return "".join(out).encode()


# T junction at node 2: primary west-east (2 lanes, 7.5 m), residential going north, a tunnel piece east of node 3 (oneway=-1)
T_POINTS = {1: (-100.0, 0.0), 2: (0.0, 0.0), 3: (100.0, 0.0), 4: (0.0, 100.0), 5: (200.0, 0.0), 6: (300.0, 0.0)}
T_WAYS = [(10, [1, 2, 3], {"highway": "primary", "lanes": "2", "width": "7.5 m", "maxspeed": "CZ:rural", "surface": "asphalt"}),
          (11, [2, 4], {"highway": "residential"}),
          (12, [3, 5], {"highway": "primary", "tunnel": "yes", "layer": "-1", "oneway": "-1"}),
          (13, [5, 6], {"highway": "primary", "tunnel": "yes", "layer": "-1"}),
          (14, [1, 4], {"highway": "footway"})]


def t_graph():
    nodes_ll, ways = osm_extract.parse_osm_xml([io.BytesIO(osm_xml(T_POINTS, T_WAYS))])
    nodes, edges = graph.build_graph(nodes_ll, list(ways.values()), WIN)
    return nodes, edges


def drape(edges, f=lambda x, z: 100.0):
    graph.apply_heights(edges, f)
    return edges


class TestTags(unittest.TestCase):
    def test_parsers(self):
        self.assertEqual(graph.parse_width("3.5 m"), 3.5)
        self.assertEqual(graph.parse_width("3,5"), 3.5)
        self.assertIsNone(graph.parse_width("12 ft"))
        self.assertEqual(graph.parse_maxspeed("CZ:urban"), 50)
        self.assertEqual(graph.parse_maxspeed("80"), 80)
        self.assertEqual(graph.parse_maxspeed("30 mph"), 48)
        self.assertIsNone(graph.parse_maxspeed("walk"))
        self.assertEqual(graph.parse_lanes("2;3"), 2)
        self.assertIsNone(graph.parse_lanes("0"))
        self.assertEqual(graph.way_attrs({"highway": "motorway"})["oneway"], True)
        self.assertIsNone(graph.way_attrs({"highway": "footway"}))
        self.assertEqual(graph.way_attrs({"highway": "trunk_link"})["class"], "link")


class TestXmlToEdges(unittest.TestCase):
    def test_edges_nodes_and_attributes(self):
        nodes, edges = t_graph()
        self.assertEqual(len(edges), 5)  # primary split at node 2, residential, two tunnel pieces; the footway is no road class
        by_way = {}
        for e in edges:
            by_way.setdefault(e["osm_way"], []).append(e)
        self.assertEqual(len(by_way[10]), 2)
        e = by_way[10][0]
        self.assertEqual((e["class"], e["lanes"], e["width_m"], e["maxspeed"], e["surface"], e["span"]), ("primary", 2, 7.5, 90, "asphalt", "ground"))
        self.assertAlmostEqual(e["length_m"], 100.0, delta=0.5)
        self.assertEqual(by_way[11][0]["class"], "local")
        self.assertTrue(by_way[11][0]["lanes"] is None and by_way[11][0]["width_m"] is None)
        tun = by_way[12][0]
        self.assertEqual((tun["span"], tun["layer"], tun["oneway"]), ("tunnel", -1, True))
        osm_a = nodes[tun["a"]]["osm_id"]
        self.assertEqual(osm_a, 5)  # oneway=-1 reverses the way: it now runs 5 -> 3
        self.assertEqual(nodes[tun["b"]]["osm_id"], 3)

    def test_node_degrees_and_kinds(self):
        nodes, edges = t_graph()
        k = {n["osm_id"]: n for n in nodes}
        self.assertEqual(k[2]["degree"], 3)
        self.assertEqual(k[2]["kind"], "junction")
        self.assertEqual(k[1]["kind"], "end")
        self.assertEqual(k[3]["kind"], "portal")  # ground -> tunnel
        self.assertEqual(k[5]["kind"], "joint")  # tunnel -> tunnel
        self.assertEqual(sum(n["degree"] for n in nodes), 2 * len(edges))

    def test_window_cut_marks_boundary(self):
        pts = {1: (0.0, 0.0), 2: (20000.0, 0.0), 3: (40000.0, 0.0)}  # node 3 is outside the 60 km window (half = 30 km)
        nodes_ll, ways = osm_extract.parse_osm_xml([io.BytesIO(osm_xml(pts, [(1, [1, 2, 3], {"highway": "secondary"})]))])
        nodes, edges = graph.build_graph(nodes_ll, list(ways.values()), WIN)
        self.assertEqual(len(edges), 1)
        self.assertTrue([n for n in nodes if n["osm_id"] == 2][0]["boundary"])


class TestCurvatureAndGrade(unittest.TestCase):
    def test_radius_from_known_arc(self):
        R = 100.0
        pts = [(-R + R * math.cos(t / 100.0 * 2.0), R * math.sin(t / 100.0 * 2.0)) for t in range(0, 101)]  # 2 rad of a left-hand circle, dense
        samples, s, ds = graph.resample(pts, 10.0)
        kap, valid = graph.curvature_series(samples, ds)
        self.assertIsNotNone(valid)
        for k in kap[valid[0]:valid[1] + 1]:
            self.assertAlmostEqual(k, 1.0 / R, delta=0.0001)  # left turn is positive, within 1 %
        right = [(-x, z) for x, z in pts]
        kap2, _ = graph.curvature_series(graph.resample(right, 10.0)[0], ds)
        self.assertLess(kap2[len(kap2) // 2], 0)

    def test_short_edge_has_no_valid_curvature(self):
        samples, _, ds = graph.resample([(0.0, 0.0), (40.0, 0.0)], 10.0)
        kap, valid = graph.curvature_series(samples, ds)
        self.assertIsNone(valid)
        self.assertEqual(kap, [0.0] * len(samples))

    def test_straight_has_zero_curvature(self):
        samples, _, ds = graph.resample([(0.0, 0.0), (500.0, 0.0)], 10.0)
        kap, valid = graph.curvature_series(samples, ds)
        self.assertTrue(all(abs(k) < 1e-9 for k in kap))

    def test_grade_from_known_profile(self):
        s = [i * 10.0 for i in range(21)]
        y = [3.0 + 0.05 * v for v in s]
        g = dem.grade_series(s, y, 30.0)
        self.assertEqual(len(g), 21)
        for v in g:
            self.assertAlmostEqual(v, 5.0, places=6)

    def test_grade_along_draped_edge_and_tunnel_interpolation(self):
        nodes, edges = t_graph()
        drape(edges, lambda x, z: 100.0 + 0.05 * x + (50.0 if 150.0 < x < 250.0 else 0.0))  # a 50 m hill over the tunnel
        graph.finish_nodes(nodes, edges)
        prof = {p["edge"]: p for p in graph.profiles(edges)}
        prim = [e for e in edges if e["osm_way"] == 10 and e["class"] == "primary"][0]
        self.assertAlmostEqual(prof[prim["id"]]["grade_pct"][5], 5.0, places=1)
        tun = [e for e in edges if e["osm_way"] == 12][0]  # 100 m, x 200 -> 100 (reversed); the hill is ignored: straight line between the portals
        ys = tun["centreline"][1::3]
        self.assertAlmostEqual(ys[0], 110.0, delta=0.3)  # straight line from the portal at x = 100 (105 m) to the far end at x = 300 (115 m)
        self.assertAlmostEqual(ys[-1], 105.0, delta=0.3)
        steps = [ys[i + 1] - ys[i] for i in range(len(ys) - 1)]
        self.assertAlmostEqual(max(steps) - min(steps), 0.0, delta=0.11)


class TestJunctionsAndSpans(unittest.TestCase):
    def test_junction_degree_and_legs(self):
        nodes, edges = t_graph()
        drape(edges)
        graph.finish_nodes(nodes, edges)
        js = graph.junction_records(nodes, edges)
        self.assertEqual(len(js), 1)
        j = js[0]
        self.assertEqual(j["legs"], 3)
        for got, want in zip(j["bearings"], (0.0, 90.0, 270.0)):
            self.assertAlmostEqual(got, want, delta=1.0)
        self.assertAlmostEqual(j["min_leg_angle_deg"], 90.0, delta=1.0)
        self.assertEqual(sorted(j["leg_classes"]), ["local", "primary", "primary"])

    def test_span_chain_joins_two_tunnel_edges(self):
        nodes, edges = t_graph()
        drape(edges)
        sp = graph.span_records(nodes, edges)
        self.assertEqual(len(sp), 1)
        self.assertEqual(sp[0]["kind"], "tunnel")
        self.assertAlmostEqual(sp[0]["length_m"], 200.0, delta=1.0)
        self.assertEqual(len(sp[0]["edges"]), 2)

    def test_roundabout_radius(self):
        ring = {i: (30.0 * math.cos(i * math.pi / 4), 30.0 * math.sin(i * math.pi / 4)) for i in range(8)}
        pts = dict(ring)
        pts[20] = (130.0, 0.0)
        nds = list(range(8)) + [0]
        nodes_ll, ways = osm_extract.parse_osm_xml([io.BytesIO(osm_xml(pts, [(1, nds, {"highway": "primary", "junction": "roundabout"}),
                                                                         (2, [0, 20], {"highway": "primary"})]))])
        nodes, edges = graph.build_graph(nodes_ll, list(ways.values()), WIN)
        n0 = [n for n in nodes if n["osm_id"] == 0][0]
        self.assertAlmostEqual(n0["roundabout_radius_m"], 30.0, delta=0.5)
        self.assertTrue([e for e in edges if e["roundabout"]][0]["oneway"])


def full_dataset():
    nodes, edges = t_graph()
    drape(edges, lambda x, z: 100.0 + 0.02 * x)
    graph.finish_nodes(nodes, edges)
    profs = graph.profiles(edges)
    juncs = graph.junction_records(nodes, edges)
    spans = graph.span_records(nodes, edges)
    for e in edges:
        for k in ("pts", "_xz", "_s", "_y", "_dem"):
            e.pop(k, None)
    sha = "a" * 64
    prov = {"schema": graph.SCHEMA, "adr": "x", "extractor": {"version": "t"}, "date": "2026-10-29", "window": {"size_km": 60.0}, "parameters": {},
            "osm": {"licence": "ODbL 1.0"}, "dem": {"credit": "provided under COPERNICUS by the EU and ESA"},
            "sources": [{"kind": "overpass-tile", "url": "u", "date": "d", "bytes": 5, "sha256": sha, "licence": "ODbL"},
                        {"kind": "dem-tile", "url": "u", "date": "d", "bytes": 5, "sha256": sha, "licence": "c"}]}
    mk = lambda items: {"schema": graph.SCHEMA, "items": items}
    return {"provenance": prov, "nodes": mk(nodes), "edges": mk(edges), "profiles": mk(profs), "junctions": mk(juncs), "spans": mk(spans)}


class TestValidator(unittest.TestCase):
    def test_good_dataset_passes_and_reports_missing_tags(self):
        errs, rep = validate_dataset.validate(full_dataset())
        self.assertEqual(errs, [])
        self.assertEqual(rep["edges_per_class"]["local"]["share_no_lanes"], 1.0)
        self.assertEqual(rep["edges_per_class"]["primary"]["n"], 4)

    def _fails(self, mutate, needle):
        d = copy.deepcopy(full_dataset())
        mutate(d)
        errs, _ = validate_dataset.validate(d)
        self.assertTrue(any(needle in m for m in errs), "expected %r in %r" % (needle, errs))

    def test_nan_in_centreline(self):
        self._fails(lambda d: d["edges"]["items"][0]["centreline"].__setitem__(1, float("nan")), "bad centreline")

    def test_missing_provenance_sha(self):
        self._fails(lambda d: d["provenance"]["sources"][0].pop("sha256"), "bad sha256")

    def test_missing_licence_credit(self):
        self._fails(lambda d: d["provenance"]["dem"].pop("credit"), "Copernicus DEM credit")

    def test_dangling_edge_reference(self):
        self._fails(lambda d: d["edges"]["items"][0].__setitem__("b", 999), "node reference")

    def test_degree_mismatch(self):
        self._fails(lambda d: d["nodes"]["items"][0].__setitem__("degree", 7), "degree")

    def test_unit_range_height(self):
        self._fails(lambda d: d["nodes"]["items"][0]["pos"].__setitem__(1, 99999.0), "implausible height")

    def test_profile_length(self):
        self._fails(lambda d: d["profiles"]["items"][0]["grade_pct"].pop(), "length/finite")

    def test_vertical_radius_fields_optional_and_checked(self):
        d = copy.deepcopy(full_dataset())
        d["profiles"]["items"][0]["vradius_crest_min_m"] = 6123.5
        d["profiles"]["items"][0]["vradius_sag_min_m"] = None
        errs, _ = validate_dataset.validate(d)
        self.assertEqual(errs, [])
        self._fails(lambda d: d["profiles"]["items"][0].__setitem__("vradius_sag_min_m", -5.0), "vradius_sag_min_m")
        self._fails(lambda d: d["profiles"]["items"][0].__setitem__("vradius_crest_min_m", float("nan")), "vradius_crest_min_m")

    def test_bad_class_and_schema(self):
        self._fails(lambda d: d["edges"]["items"][0].__setitem__("class", "autobahn"), "class")
        self._fails(lambda d: d["nodes"].__setitem__("schema", "nope"), "schema")

    # RG-115: a W3 export (generator block, no source tiles) validates without --source-provenance; each reproducibility field is required
    @staticmethod
    def generator_dataset():
        d = copy.deepcopy(full_dataset())
        d["provenance"] = {"schema": graph.SCHEMA, "adr": "ADR-030/031/032", "extractor": {"version": "rg110-1"}, "date": "not recorded", "purpose": "generated road network",
                           "generator": {"seed": 1, "spec": {"seed": 1, "size_km": 10}, "rules_sha256": "b" * 64}, "window": {"name": "generated", "size_km": 60.0},
                           "parameters": {"step_m": 10}, "osm": {"licence": "ODbL 1.0 (rules learned only)"}, "dem": {"credit": "provided under COPERNICUS by the EU and ESA"}, "sources": []}
        return d

    def test_generator_provenance_passes_without_sources(self):
        errs, _ = validate_dataset.validate(self.generator_dataset())
        self.assertEqual(errs, [])

    def _generator_fails(self, mutate, needle):
        d = self.generator_dataset()
        mutate(d)
        errs, _ = validate_dataset.validate(d)
        self.assertTrue(any(needle in m for m in errs), "expected %r in %r" % (needle, errs))

    def test_generator_provenance_negative(self):
        self._generator_fails(lambda d: d["provenance"]["generator"].pop("rules_sha256"), "rules_sha256")
        self._generator_fails(lambda d: d["provenance"]["generator"].__setitem__("rules_sha256", "xyz"), "rules_sha256")
        self._generator_fails(lambda d: d["provenance"]["generator"].pop("seed"), "seed")
        self._generator_fails(lambda d: d["provenance"]["generator"].pop("spec"), "spec")
        self._generator_fails(lambda d: d["provenance"].pop("purpose"), "missing purpose")
        self._generator_fails(lambda d: d["provenance"]["dem"].pop("credit"), "Copernicus DEM credit")
        self._generator_fails(lambda d: d["provenance"].__setitem__("source_kind", "bogus"), "source_kind")

    def test_cli_exit_codes(self):
        with tempfile.TemporaryDirectory() as td:
            self.assertEqual(validate_dataset.main(["--dir", td]), 1)  # nothing there
            for k, doc in full_dataset().items():
                with open(os.path.join(td, k + ".json"), "w", encoding="utf-8") as f:
                    json.dump(doc, f)
            self.assertEqual(validate_dataset.main(["--dir", td]), 0)
            with open(os.path.join(td, "nodes.json"), "w", encoding="utf-8") as f:
                f.write('{"schema": "%s", "items": [{"id": 0, "pos": [NaN, 0, 0]}]}' % graph.SCHEMA)
            self.assertEqual(validate_dataset.main(["--dir", td]), 1)  # NaN constant is refused at load time


def make_geotiff(path, w, h, tw, th, value, point=True, lon0=14.0, lat0=51.0, dx=0.001, dy=0.001):
    """Tiled float32 GeoTIFF, deflate + floating-point predictor 3, little-endian classic TIFF (what GLO-30 uses)."""
    tiles = []
    for ty in range(0, h, th):
        for tx in range(0, w, tw):
            rows = []
            for r in range(th):
                vals = [value(tx + c, ty + r) for c in range(tw)]
                raw = b"".join(struct.pack(">f", v) for v in vals)
                planes = bytes(raw[4 * c + b] for b in range(4) for c in range(tw))  # byte planes, most significant first
                diff = bytes([planes[0]] + [(planes[i] - planes[i - 1]) & 255 for i in range(1, len(planes))])
                rows.append(diff)
            tiles.append(zlib.compress(b"".join(rows)))
    entries = []  # (tag, type, count, bytes)
    body = bytearray()
    ifd_entries = 14
    data_start = 8 + 2 + ifd_entries * 12 + 4
    offsets_tiles = []
    pos = data_start
    extra = bytearray()

    def add_extra(b):
        nonlocal pos
        off = pos + len(extra)
        extra.extend(b)
        return off
    tile_bytes = b""
    tile_offs = []
    # tile data goes after the IFD extras; compute extras first with placeholders
    toff_placeholder = add_extra(b"\0" * 4 * len(tiles))
    tcnt = add_extra(struct.pack("<%dI" % len(tiles), *[len(t) for t in tiles]))
    scale = add_extra(struct.pack("<3d", dx, dy, 0.0))
    tie = add_extra(struct.pack("<6d", 0.0, 0.0, 0.0, lon0, lat0, 0.0))
    keys = add_extra(struct.pack("<8H", 1, 1, 0, 1, 1025, 0, 1, 2 if point else 1))
    base = pos + len(extra)
    off = base
    for t in tiles:
        tile_offs.append(off)
        off += len(t)
    extra[toff_placeholder - pos:toff_placeholder - pos + 4 * len(tiles)] = struct.pack("<%dI" % len(tiles), *tile_offs)
    n_tx = (w + tw - 1) // tw
    if len(tiles) == 1:  # a single LONG value is stored inline in the IFD entry
        toff_placeholder, tcnt = tile_offs[0], len(tiles[0])
    ent = [(256, 4, 1, w), (257, 4, 1, h), (258, 3, 1, 32), (259, 3, 1, 8), (277, 3, 1, 1), (317, 3, 1, 3), (322, 4, 1, tw), (323, 4, 1, th),
           (324, 4, len(tiles), toff_placeholder), (325, 4, len(tiles), tcnt), (339, 3, 1, 3), (33550, 12, 3, scale), (33922, 12, 6, tie), (34735, 3, 8, keys)]
    ent.sort()
    ifd = struct.pack("<H", len(ent))
    for tag, typ, cnt, val in ent:
        if typ == 3 and cnt == 1:
            ifd += struct.pack("<HHIHH", tag, typ, cnt, val, 0)
        else:
            ifd += struct.pack("<HHII", tag, typ, cnt, val)
    ifd += struct.pack("<I", 0)
    blob = b"II*\0" + struct.pack("<I", 8) + ifd + bytes(extra) + b"".join(tiles)
    assert len(blob) - sum(len(t) for t in tiles) == base
    with open(path, "wb") as f:
        f.write(blob)
    return n_tx


class TestDem(unittest.TestCase):
    def test_float_predictor_tiles_and_bilinear(self):
        f = lambda c, r: 100.0 + 10.0 * c + 5.0 * r
        with tempfile.TemporaryDirectory() as td:
            p = os.path.join(td, "t.tif")
            make_geotiff(p, 6, 4, 4, 2, f, point=True)  # 2 x 2 tiles, the right ones padded
            t = dem.DemTile(p)
            self.assertEqual((t.width, t.rows, t.tw, t.th, t.pixel_is_area), (6, 4, 4, 2, False))
            self.assertAlmostEqual(t.height(51.0 - 0.001 * 1, 14.0 + 0.001 * 2), f(2, 1), places=3)  # pixel centre of column 2, row 1
            self.assertAlmostEqual(t.height(51.0 - 0.001 * 1.5, 14.0 + 0.001 * 4.25), 100 + 10 * 4.25 + 5 * 1.5, places=3)  # across the tile edge
            make_geotiff(p, 6, 4, 4, 2, f, point=False)
            ta = dem.DemTile(p)
            self.assertTrue(ta.pixel_is_area)
            self.assertAlmostEqual(ta.height(51.0 - 0.001 * 1.5, 14.0 + 0.001 * 2.5), f(2, 1), places=3)  # centre of column 2 / row 1 is at +0.5 pixel

    def test_demset_counts_voids(self):
        with tempfile.TemporaryDirectory() as td:
            p = os.path.join(td, "t.tif")
            make_geotiff(p, 4, 2, 4, 2, lambda c, r: -32767.0 if c < 2 else 50.0)
            ds = dem.DemSet([p])
            self.assertAlmostEqual(ds.height(51.0 - 0.001, 14.0 + 0.003), 50.0, places=3)
            self.assertEqual(ds.height(60.0, 5.0), 0.0)  # outside every tile
            self.assertEqual(ds.voids, 1)


class TestStats(unittest.TestCase):
    def test_percentile_and_flags(self):
        self.assertEqual(dataset_stats.pct(list(range(1, 102)), 0.5), 51)
        self.assertEqual(dataset_stats.dist([1.0, 2.0, 3.0])["flag"], "n<30")
        self.assertNotIn("flag", dataset_stats.dist(list(range(40))))

    def test_compute_on_tiny_dataset(self):
        d = full_dataset()
        st = dataset_stats.compute(d["nodes"]["items"], d["edges"]["items"], d["profiles"]["items"], d["junctions"]["items"], d["spans"]["items"], d["provenance"])
        pr = st["classes"]["primary"]
        self.assertEqual(pr["edges"], 4)
        self.assertEqual(pr["tunnels"]["n"], 1)
        self.assertAlmostEqual(pr["tunnels"]["p50"], 200.0, delta=1.0)
        self.assertEqual(pr["junction_degree"], {"3": 1})
        self.assertEqual(pr["radius_m"]["flag"], "n<30")
        self.assertAlmostEqual(pr["grade_abs_pct"]["p50"], 2.0, delta=0.1)  # y = 100 + 0.02 x on ground edges
        self.assertEqual(st["classes"]["local"]["share_edges_no_lanes"], 1.0)
        self.assertIn("| primary |", dataset_stats.markdown(st))

    def test_junction_spacing_contracts_degree_two(self):
        # junction - joint - joint - junction on one road class: one chain of 300 m; the four dead-end stubs must not count
        nodes = [{"id": i, "degree": dg, "kind": "junction"} for i, dg in enumerate((3, 2, 2, 3, 1, 1, 1, 1))]
        mk = lambda i, a, b: {"id": i, "a": a, "b": b, "class": "local", "length_m": 100.0, "span": "ground"}
        edges = [mk(0, 0, 1), mk(1, 1, 2), mk(2, 2, 3), mk(3, 0, 4), mk(4, 3, 5), mk(5, 0, 6), mk(6, 3, 7)]
        st = dataset_stats.compute(nodes, [dict(e, lanes=None, width_m=None, maxspeed=None) for e in edges], [], [], [], {})
        sp = st["classes"]["local"]["junction_spacing_m"]
        self.assertEqual((sp["n"], sp["p50"]), (1, 300.0))


class TestStreetStats(unittest.TestCase):
    """RG-116: orientation order phi (Boeing 2019) and the T share / spacing on a tiny crafted grid and a star."""

    @staticmethod
    def seg(i, a, b, p, q):
        return {"id": i, "a": a, "b": b, "class": "local", "length_m": math.hypot(q[0] - p[0], q[1] - p[1]),
                "centreline": [p[0], 0.0, p[1], q[0], 0.0, q[1]]}

    def test_phi_grid_is_one_and_uniform_is_near_zero(self):
        grid = [self.seg(i, 0, 1, (0, 0), (100 * math.cos(math.radians(a)), 100 * math.sin(math.radians(a)))) for i, a in enumerate((0, 90, 180, 270))]
        self.assertAlmostEqual(street_stats.orientation_phi(grid), 1.0, places=6)
        # 36 bins of 10 deg: one street per 10 deg bin centre (counted both ways) = uniform -> phi well below 0.1
        uni = [self.seg(i, 0, 1, (0, 0), (100 * math.cos(math.radians(5 + 10 * i)), 100 * math.sin(math.radians(5 + 10 * i)))) for i in range(36)]
        self.assertLess(street_stats.orientation_phi(uni), 0.1)
        self.assertIsNone(street_stats.orientation_phi([]))

    def test_t_share_spacing_and_dead_ends(self):
        # H shape: nodes 0..5; 0-1 and 2-3 vertical bars, 4 / 5 on the crossbar: T junctions at 4 and 5, four dead ends
        pos = {0: (0, 0), 1: (0, 200), 4: (0, 100), 5: (100, 100), 2: (100, 0), 3: (100, 200)}
        nodes = [{"id": i, "degree": d} for i, d in enumerate((1, 1, 1, 1, 3, 3))]
        mk = lambda i, a, b: self.seg(i, a, b, pos[a], pos[b])
        edges = [mk(0, 0, 4), mk(1, 4, 1), mk(2, 2, 5), mk(3, 5, 3), mk(4, 4, 5)]
        res = street_stats.compute(nodes, edges)["all"]
        self.assertEqual((res["junction_nodes"], res["t_share"], res["spacing_p50"]), (2, 1.0, 100.0))
        self.assertAlmostEqual(res["dead_end_share"], 4 / 6, places=3)
        self.assertEqual(res["phi"], round(street_stats.orientation_phi(edges), 4))


if __name__ == "__main__":
    unittest.main(verbosity=1)
