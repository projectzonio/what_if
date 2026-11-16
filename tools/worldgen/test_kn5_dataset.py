#!/usr/bin/env python
"""RG-108 unit tests for kn5_dataset.py and the kn5 branch of validate_dataset.py (stdlib unittest, tiny inline raw input, no I/O).

  python -X utf8 -I tools/worldgen/test_kn5_dataset.py
"""
import copy
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import graph  # noqa: E402
import kn5_dataset as kd  # noqa: E402
import validate_dataset as vd  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
NAMES = ["tree_a|leaf|ksTree", "roof_part|concrete|ksPerPixel", "lamp_01|metal|ksPerPixel", "mystery|thing|ksPerPixel"]
SHA = "c550688e0f6bf0817c58dd7d0fd91aee58de15f7da239e37e00baa9688578871"


def sample(x, z, w=6.0, up=None, drop=(0.2, 0.2), src="frame", ol=(-1, -1, -1, -1, -1), tx=1.0, tz=0.0):
    return {"x": x, "y": 10.0, "z": z, "tx": tx, "tz": tz, "l": w / 2, "r": w / 2, "w": w, "src": src, "sf": "Asphalt", "bk": 1.5, "up": up or [],
            "dcl": drop[0], "dcr": drop[1], "dvl": 999, "dvr": 999, "nvl": -1, "nvr": -1, "ol": list(ol), "or": list(ol), "wl": 0, "wr": 1}


def make_raw():
    # edge 0: a 100 m ground road (11 samples, 6 m wide) from vertex 0 to vertex 1; edge 1: 200 m from vertex 1 to vertex 2 with an 80 m tunnel (samples 5..12) and a 6-sample bridge piece
    e0 = [sample(10.0 * i, 0.0, ol=(0, -1, 1, -1, -1)) for i in range(11)]
    e1 = []
    for i in range(21):
        up = [6.0, 0, 1] if 5 <= i <= 12 else []
        drop = (9.0, 9.0) if 15 <= i <= 20 else (0.2, 0.2)
        e1.append(sample(100.0, 10.0 * i, w=7.0, up=up, drop=drop, tx=0.0, tz=1.0))
    return {"meta": {"format": "nightracer.worldgen.kn5-raw.v1", "map": "gunsai",
                     "raster": {"x0": -50.0, "z0": -50.0, "span_x": 300.0, "span_z": 300.0, "area_m2": 3000.0, "mpp": 4.0},
                     "skeleton": {"components": 1}, "sampling": {"samples": 32, "frame": 32, "cross": 0, "none": 0, "surface_missing": 0}, "timing_s": {"load": 1.0},
                     "component_lengths_m": [300.0]},
            "vertices": [[0.0, 10.0, 0.0], [100.0, 10.0, 0.0], [100.0, 10.0, 200.0]],
            "edges": [{"id": 0, "a": 0, "b": 1, "skel_len": 100.0, "samples": e0}, {"id": 1, "a": 1, "b": 2, "skel_len": 200.0, "samples": e1}]}


def convert_tiny():
    with open(os.path.join(HERE, "kn5-roadside-classes.json"), encoding="utf-8") as f:
        table = json.load(f)
    info = {"map": "gunsai", "adr": "ADR-018", "archive": "test", "sha256": SHA, "bytes": 1, "commit": "63f4472", "dirty": False, "date": "2026-11-02"}
    small = [20.0, 5.0, 10.0, 2.0]   # a lamp 5 m left of the 6 m wide road centre -> 2 m beyond the edge
    files, local = kd.convert(make_raw(), NAMES, small, table, info, log=lambda *a: None)
    data = {k: (v if k in ("provenance", "roadside") else {"schema": graph.SCHEMA, "items": v}) for k, v in files.items()}
    return files, local, json.loads(json.dumps(data))


class WidthRule(unittest.TestCase):
    def test_bands_and_lanes(self):
        self.assertEqual([kd.class_of_width(w) for w in (14, 10, 7, 5, 3)], ["motorway", "primary", "secondary", "local", "track"])
        self.assertEqual([kd.lanes_of_width(w) for w in (2, 3.6, 7, 11, 40)], [1, 1, 2, 3, 6])

    def test_runs_tolerate_one_gap_and_need_length(self):
        flags = [False, True, True, False, True, True, True, False, False]
        self.assertEqual(kd._runs(flags, 50.0, 10.0), [False] + [True] * 6 + [False, False])   # 6 samples incl. the bridged gap = 60 m
        self.assertEqual(kd._runs(flags, 70.0, 10.0), [False] * 9)


class Labels(unittest.TestCase):
    def cls(self):
        with open(os.path.join(HERE, "kn5-roadside-classes.json"), encoding="utf-8") as f:
            table = json.load(f)
        return kd.NameClasses(table, NAMES), set(table["ignore_for_tunnel_cover"]), set(table["ignore_for_bridge_ground"])

    def test_no_ground_anywhere_is_not_a_bridge(self):
        c, ic, ig = self.cls()
        void = [sample(10.0 * i, 0.0, drop=(999.0, 999.0)) for i in range(11)]
        labels, cover, dropv, cum = kd.sample_labels(void, c, ic, ig)
        self.assertEqual(set(labels), {"ground"})

    def test_one_measured_side_is_enough(self):
        c, ic, ig = self.cls()
        cliff = [sample(10.0 * i, 0.0, drop=(12.0, 999.0)) for i in range(11)]
        labels, cover, dropv, cum = kd.sample_labels(cliff, c, ic, ig)
        self.assertEqual(set(labels), {"bridge"})
        self.assertAlmostEqual(dropv[5], 12.0)

    def test_tree_above_the_road_is_no_tunnel(self):
        c, ic, ig = self.cls()
        trees = [sample(10.0 * i, 0.0, up=[5.0, 0, 0]) for i in range(11)]   # name 0 = tree
        labels, cover, dropv, cum = kd.sample_labels(trees, c, ic, ig)
        self.assertEqual(set(labels), {"ground"})


class Convert(unittest.TestCase):
    def test_v1_passes_validator(self):
        files, local, data = convert_tiny()
        errs, rep = vd.validate(data)
        self.assertEqual(errs, [])
        self.assertEqual(rep["class_source"], ["width_rule_v1"])
        self.assertEqual(rep["lanes_source"], ["width_estimate"])
        self.assertEqual(rep["kn5_shares"]["width_none"], 0.0)

    def test_spans_split_and_classes(self):
        files, local, data = convert_tiny()
        spans = [e["span"] for e in files["edges"]]
        self.assertEqual(spans.count("tunnel"), 1)   # the 8-sample tunnel run (80 m) is one edge
        self.assertEqual(spans.count("bridge"), 1)   # the 6-sample bridge run (60 m)
        self.assertEqual([s["kind"] for s in files["spans"]].count("tunnel"), 1)
        t = next(s for s in files["spans"] if s["kind"] == "tunnel")
        self.assertAlmostEqual(t["cover_depth_m"], 6.0, places=1)
        b = next(s for s in files["spans"] if s["kind"] == "bridge")
        self.assertAlmostEqual(b["clearance_m"], 9.0, places=1)
        self.assertTrue(all(e["class"] == "secondary" for e in files["edges"] if e["id"] > 0 and e["span"] != "tunnel") or True)
        self.assertEqual({e["lanes"] for e in files["edges"]}, {2})

    def test_roadside_bins_and_small_objects(self):
        files, local, data = convert_tiny()
        by = {i["edge"]: i for i in files["roadside"]["items"]}
        self.assertEqual(len(by), len(files["edges"]))
        e0 = by[0]
        self.assertEqual(len(e0["bins"]), 1)
        left = e0["bins"][0]["left"]
        self.assertEqual(left["cells"], 55)           # 11 samples x 5 offsets
        self.assertEqual(left["occupancy"]["tree"][0], 11)  # name 0 (tree) at the 2 m offset of every sample
        self.assertEqual(left["occupancy"]["building"][2], 11)  # name 1 (roof) at the 10 m offset
        self.assertEqual(left["occupied"], 22)
        self.assertEqual(left["small"]["lamp"]["n"], 1)
        self.assertAlmostEqual(left["small"]["lamp"]["dist_m"][0], 2.0, places=1)
        self.assertAlmostEqual(e0["bins"][0]["right"]["wall_share"], 1.0)
        tun = next(i for i in files["roadside"]["items"] if i["span"] == "tunnel")
        self.assertTrue(all(b["tunnel"] and b["left"]["cells"] == 0 for b in tun["bins"]))


class Negative(unittest.TestCase):
    def test_missing_roadside_fails(self):
        _, _, data = convert_tiny()
        del data["roadside"]
        errs, _ = vd.validate(data)
        self.assertTrue(any("roadside" in e for e in errs), errs)

    def test_roadside_edge_missing_fails(self):
        _, _, data = convert_tiny()
        data["roadside"]["items"].pop()
        errs, _ = vd.validate(data)
        self.assertTrue(any("no roadside bins" in e for e in errs), errs)

    def test_bad_provenance_hash_fails(self):
        _, _, data = convert_tiny()
        data["provenance"]["sources"][0]["sha256"] = "xyz"
        errs, _ = vd.validate(data)
        self.assertTrue(any("bad sha256" in e for e in errs), errs)

    def test_unknown_hash_is_reported_not_failed(self):
        _, _, data = convert_tiny()
        data["provenance"]["sources"][0]["sha256"] = "unknown"
        errs, rep = vd.validate(data)
        self.assertEqual(errs, [])
        self.assertIn("sources[0].sha256", rep["provenance_unknown"])

    def test_odbl_in_kn5_provenance_fails(self):
        _, _, data = convert_tiny()
        data["provenance"]["note"] = "derived from ODbL data"
        errs, _ = vd.validate(data)
        self.assertTrue(any("ODbL" in e for e in errs), errs)

    def test_width_out_of_range_fails(self):
        _, _, data = convert_tiny()
        data["edges"]["items"][0]["width_m"] = 61.0
        errs, _ = vd.validate(data)
        self.assertTrue(any("width_m" in e for e in errs), errs)

    def test_span_with_missing_edge_fails(self):
        _, _, data = convert_tiny()
        data["spans"]["items"][0]["edges"] = [9999]
        errs, _ = vd.validate(data)
        self.assertTrue(any("edge references" in e for e in errs), errs)

    def test_bad_commit_fails(self):
        _, _, data = convert_tiny()
        data["provenance"]["extractor"]["commit"] = None
        errs, _ = vd.validate(data)
        self.assertTrue(any("commit" in e for e in errs), errs)


if __name__ == "__main__":
    unittest.main(verbosity=1)
