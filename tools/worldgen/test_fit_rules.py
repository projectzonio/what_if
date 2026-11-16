#!/usr/bin/env python
"""RG-109 unit tests of fit_rules.py on tiny inline inputs (stdlib unittest, no network, files only in a temp dir).

Run: python -X utf8 -I tools/worldgen/test_fit_rules.py     (exit code 0 = all passed)
"""
import contextlib
import hashlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fit_rules  # noqa: E402
import graph  # noqa: E402
import osm_extract  # noqa: E402
import test_worldgen as tw  # noqa: E402


def dataset_from(points, ways, dem=lambda x, z: 100.0):
    """Tiny OSM -> dataset dict as fit_rules.load returns it (same pipeline as osm_extract.build_dataset)."""
    nodes_ll, wmap = osm_extract.parse_osm_xml([io.BytesIO(tw.osm_xml(points, ways))])
    nodes, edges = graph.build_graph(nodes_ll, list(wmap.values()), tw.WIN)
    graph.apply_heights(edges, dem)
    graph.finish_nodes(nodes, edges)
    profs = graph.profiles(edges)
    juncs = graph.junction_records(nodes, edges)
    spans = graph.span_records(nodes, edges)
    for e in edges:
        for k in ("pts", "_xz", "_s", "_y", "_dem"):
            e.pop(k, None)
    mk = lambda items: {"items": items}
    return {"nodes": mk(nodes), "edges": mk(edges), "profiles": mk(profs), "junctions": mk(juncs), "spans": mk(spans), "provenance": {"window": {"name": "t"}}}


def filtered(data):
    return fit_rules.filter_junctions(data["nodes"]["items"], data["edges"]["items"], data["junctions"]["items"])


class TestNumbers(unittest.TestCase):
    def test_block_quantiles(self):
        b = fit_rules.block({"a": list(range(1, 102))})
        self.assertEqual((b["n"], b["p05"], b["p50"], b["p95"]), (101, 6.0, 51.0, 96.0))
        self.assertEqual(b["sources"], ["a"])
        self.assertEqual(fit_rules.block({"a": [1, 2], "b": [3]})["n_by_source"], {"a": 2, "b": 1})

    def test_provisional_flag_at_29_and_30(self):
        self.assertTrue(fit_rules.block({"a": list(range(29))})["provisional"])
        self.assertFalse(fit_rules.block({"a": list(range(30))})["provisional"])
        self.assertFalse(fit_rules.block({"a": list(range(20)), "b": list(range(10))})["provisional"])  # pooled over sources

    def test_run_length_split(self):
        runs = fit_rules.run_lengths([False, False, False, True, True, False], [10.0] * 6)
        self.assertEqual(runs, [(False, 30.0), (True, 20.0), (False, 10.0)])
        self.assertEqual(fit_rules.run_lengths([], []), [])

    def test_angle_gaps(self):
        self.assertEqual(fit_rules.angle_gaps([0.0, 90.0, 270.0]), [90.0, 180.0, 90.0])
        self.assertAlmostEqual(fit_rules.bearing_diff(350.0, 10.0), 20.0)


class TestJunctionFilter(unittest.TestCase):
    def test_dual_carriageway_crossing_is_one_intersection_with_four_legs(self):
        # eastbound carriageway z=0, westbound z=15 (both oneway, flow opposite), a local road crossing both (nodes 2 and 5, edge 2-5 between)
        pts = {1: (-100.0, 0.0), 2: (0.0, 0.0), 3: (100.0, 0.0), 4: (100.0, 15.0), 5: (0.0, 15.0), 6: (-100.0, 15.0), 7: (0.0, -100.0), 8: (0.0, 115.0)}
        ways = [(1, [1, 2, 3], {"highway": "primary", "oneway": "yes"}), (2, [4, 5, 6], {"highway": "primary", "oneway": "yes"}),
                (3, [7, 2, 5, 8], {"highway": "residential"})]
        d = dataset_from(pts, ways)
        self.assertEqual(len(d["junctions"]["items"]), 2)  # two raw junction nodes
        inter, cnt = filtered(d)
        real = [c for c in inter if c["min_leg_angle_deg"] is not None]
        self.assertEqual(len(real), 1)
        self.assertEqual(len(real[0]["legs"]), 4)
        self.assertEqual(cnt["merged_pairs"], 2)
        self.assertAlmostEqual(real[0]["min_leg_angle_deg"], 90.0, delta=2.0)
        self.assertEqual(real[0]["classes"], ["local", "primary"])

    def test_oneway_y_split_is_not_an_intersection_but_a_two_way_fork_stays(self):
        # stem from the west to node 2; east of it a oneway pair diverging by 20 degrees (dual carriageway start)
        pts = {1: (-100.0, 0.0), 2: (0.0, 0.0), 3: (100.0, 17.6), 4: (100.0, -17.6)}
        ways = [(1, [1, 2], {"highway": "primary"}), (2, [2, 3], {"highway": "primary", "oneway": "yes"}), (3, [4, 2], {"highway": "primary", "oneway": "yes"})]
        inter, cnt = filtered(dataset_from(pts, ways))
        self.assertEqual(cnt["merged_pairs"], 1)
        self.assertEqual([c["min_leg_angle_deg"] for c in inter], [None])  # 2 legs left -> no intersection
        # the same fork built from two-way roads: real Y-fork, stays data with its 40 degree angle
        ways = [(1, [1, 2], {"highway": "secondary"}), (2, [2, 3], {"highway": "secondary"}), (3, [2, 4], {"highway": "secondary"})]
        inter, cnt = filtered(dataset_from(pts, ways))
        self.assertEqual(cnt["merged_pairs"], 0)
        self.assertEqual(len(inter[0]["legs"]), 3)
        self.assertAlmostEqual(inter[0]["min_leg_angle_deg"], 20.0, delta=1.0)  # +-10 deg either side of east: 20 deg apart, stays < 70

    def test_two_way_fork_at_forty_degrees_keeps_its_angle(self):
        pts = {1: (-100.0, 0.0), 2: (0.0, 0.0), 3: (100.0, 36.4), 4: (100.0, -36.4)}  # +-20 deg around east
        ways = [(1, [1, 2], {"highway": "secondary"}), (2, [2, 3], {"highway": "secondary"}), (3, [2, 4], {"highway": "secondary"})]
        inter, _ = filtered(dataset_from(pts, ways))
        self.assertAlmostEqual(inter[0]["min_leg_angle_deg"], 40.0, delta=1.0)

    def test_ramp_legs_are_dropped(self):
        pts = {1: (-100.0, 0.0), 2: (0.0, 0.0), 3: (100.0, 0.0), 4: (100.0, 40.0)}
        ways = [(1, [1, 2, 3], {"highway": "motorway"}), (2, [2, 4], {"highway": "motorway_link"})]
        inter, cnt = filtered(dataset_from(pts, ways))
        self.assertEqual(cnt["link_legs"], 1)
        self.assertEqual(inter[0]["min_leg_angle_deg"], None)  # motorway pair only


class TestSpansAndFit(unittest.TestCase):
    def test_cover_depth_on_known_profile(self):
        # hill over the tunnel x = 100 .. 300 m: DEM 100 + 10 (1 - u^2), u = (x - 200) / 100 -> portals at 100 m, median cover 7.5 m
        def dem(x, z):
            u = (x - 200.0) / 100.0
            return 100.0 + 10.0 * (1.0 - u * u) if abs(u) <= 1.0 else 100.0
        d = dataset_from(tw.T_POINTS, tw.T_WAYS, dem)
        (sp,) = d["spans"]["items"]
        self.assertEqual(sp["kind"], "tunnel")
        self.assertAlmostEqual(sp["cover_depth_m"], 7.5, delta=0.7)
        self.assertIsNone(sp["clearance_m"])

    def test_bridge_clearance_on_known_profile(self):
        pts = {1: (0.0, 0.0), 2: (200.0, 0.0), 3: (400.0, 0.0)}
        ways = [(1, [1, 2], {"highway": "primary"}), (2, [2, 3], {"highway": "primary", "bridge": "yes", "layer": "1"})]
        # valley 10 m deep in the middle of the 200 m bridge, ends at 100 m: clearance median = median of 10 (1 - u^2) = 7.5 m
        def dem(x, z):
            u = (x - 300.0) / 100.0
            return 100.0 - 10.0 * (1.0 - u * u) if abs(u) <= 1.0 else 100.0
        d = dataset_from(pts, ways, dem)
        (sp,) = d["spans"]["items"]
        self.assertEqual(sp["kind"], "bridge")
        self.assertAlmostEqual(sp["clearance_m"], 7.5, delta=0.9)
        self.assertIsNone(sp["cover_depth_m"])

    def test_fit_of_tiny_dataset_flags_everything_provisional(self):
        d = dataset_from(tw.T_POINTS, tw.T_WAYS)
        rules = fit_rules.fit([("t", d)])
        pr = rules["classes"]["primary"]
        self.assertEqual(rules["schema"], fit_rules.SCHEMA)
        self.assertTrue(pr["radius"]["provisional"])
        self.assertEqual(pr["spans"]["tunnel"]["length_m"]["n"], 1)
        self.assertEqual(pr["spans"]["tunnel"]["length_m"]["sources"], ["t"])
        self.assertIn("primary.spans.tunnel.length_m", rules["provisional_rules"])
        self.assertEqual(pr["segments"]["straight_radius_m"], 1000.0)

    def test_spacing_between_intersections_contracts_through_non_junctions(self):
        # two T junctions 300 m apart on one primary road with a joint in between
        pts = {1: (0.0, 0.0), 2: (150.0, 0.0), 3: (300.0, 0.0), 4: (0.0, 100.0), 5: (300.0, 100.0), 6: (-100.0, 0.0), 7: (400.0, 0.0)}
        ways = [(1, [6, 1, 2, 3, 7], {"highway": "primary"}), (2, [1, 4], {"highway": "residential"}), (3, [3, 5], {"highway": "residential"})]
        d = dataset_from(pts, ways)
        inter, _ = filtered(d)
        sp = fit_rules.spacing_lengths(d["edges"]["items"], inter)
        self.assertEqual([round(v) for v in sp["primary"]], [300])
        self.assertNotIn("local", sp)  # the stubs end in dead ends

    def test_two_datasets_pool_and_name_sources(self):
        d = dataset_from(tw.T_POINTS, tw.T_WAYS)
        rules = fit_rules.fit([("a", d), ("b", d)])
        self.assertEqual(rules["sources"], ["a", "b"])
        self.assertEqual(rules["classes"]["primary"]["spans"]["tunnel"]["length_m"]["n_by_source"], {"a": 1, "b": 1})


class TestDeterminism(unittest.TestCase):
    def test_same_input_same_hash(self):
        hashes = []
        for _ in range(2):
            d = dataset_from(tw.T_POINTS, tw.T_WAYS, lambda x, z: 100.0 + 0.02 * x)
            hashes.append(hashlib.sha256(fit_rules.dumps(fit_rules.fit([("t", d)])).encode("utf-8")).hexdigest())
        self.assertEqual(hashes[0], hashes[1])

    def test_main_writes_identical_files_and_rounded_values(self):
        d = tw.full_dataset()
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "ds")
            os.makedirs(src)
            for k, v in d.items():
                with open(os.path.join(src, k + ".json"), "w", encoding="utf-8", newline="\n") as f:
                    json.dump(v, f)
            outs = []
            quiet = contextlib.redirect_stdout(io.StringIO())
            quiet.__enter__()
            self.addCleanup(quiet.__exit__, None, None, None)
            for i in range(2):
                out = os.path.join(tmp, "r%d.json" % i)
                self.assertEqual(fit_rules.main(["--dir", src, "--out", out]), 0)
                with open(out, "rb") as f:
                    outs.append(f.read())
            self.assertEqual(hashlib.sha256(outs[0]).hexdigest(), hashlib.sha256(outs[1]).hexdigest())

            def check(node):
                if isinstance(node, float):
                    self.assertEqual(node, round(node, 2))
                elif isinstance(node, dict):
                    for v in node.values():
                        check(v)
                elif isinstance(node, list):
                    for v in node:
                        check(v)
            check(json.loads(outs[0]))
            self.assertEqual(fit_rules.main(["--dir", src, "--dir", src, "--out", os.path.join(tmp, "x.json")]), 2)  # duplicate window name


if __name__ == "__main__":
    unittest.main(verbosity=1)
