#!/usr/bin/env python
"""RG-129 unit tests for geometry_probe.py on synthetic grids (stdlib unittest, no I/O, no Unity).

  python -X utf8 -I tools/worldgen/test_geometry_probe.py
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import geometry_probe as gp  # noqa: E402

N = 60


def make_grid(height, layers=None, gid="t"):
    """60 x 60 grid, a road strip |z - 30| < 4 m; height(x, z) in metres; layers(x, z) -> extra heights (overpass)."""
    cells = []
    for j in range(N):
        for i in range(N):
            x, z = i + 0.5, j + 0.5
            c = []
            if abs(z - 30) < 4:
                c.append(round(height(x, z) * 100))
                if layers: c += [round(v * 100) for v in layers(x, z)]
            cells.append(sorted(c))
    return gp.Grid({"id": gid, "n": N, "x0": 0.0, "z0": 0.0, "cells": cells})


def line(x0=2.0, x1=57.0, z=30.5):
    return [(x0 + k, 10.0, z) for k in range(int(x1 - x0))]


class Probe(unittest.TestCase):
    def lips(self, g, pts=None):
        pts = pts or line()
        fl = gp.follow(g, pts, None)
        return gp.lip_summary(gp.steps_of(fl)), fl

    def test_step_30cm_is_found(self):
        g = make_grid(lambda x, z: 10.0 if x < 30 else 10.3)
        s, _ = self.lips(g)
        # bilinear spreads a real step over 1 m: samples on cell centres (x.5) read all of it, samples on cell edges (integer x) two halves
        self.assertGreaterEqual(s["max"], 0.14)
        self.assertLessEqual(s["max"], 0.35)
        self.assertGreaterEqual(self.lips(g, line(2.5, 57.5))[0]["max"], 0.25)
        self.assertAlmostEqual(gp.grid_adjacent_max(g)[0], 0.3, delta=0.01)

    def test_plain_slope_gives_no_lip(self):
        g = make_grid(lambda x, z: 10.0 + 0.08 * x)      # 8 % grade: 8 cm per metre, not a lip
        s, _ = self.lips(g)
        self.assertLess(s["max"], 0.02)
        self.assertEqual(s["ge10"], 0)
        self.assertAlmostEqual(s["raw_max"], 0.08, places=2)

    def test_cross_slope_gives_no_lip(self):
        g = make_grid(lambda x, z: 10.0 + 0.05 * (z - 30))
        pts = line()
        fl = gp.follow(g, pts, None)
        ac = gp.across_steps(g, pts, fl, 3)
        self.assertLess(max(w for _, w, _ in ac), 0.02)

    def test_diagonal_plane_26_percent_gives_no_lip_and_no_step(self):
        k = 0.26 / 2 ** 0.5
        plane = lambda x, z: 10.0 + k * (x + z)
        g = make_grid(plane)
        zig = [(2.0 + m, 10.0, 28.5 + m * 0.07) for m in range(55)]      # slow diagonal: crosses cell rows at irregular x
        fl = gp.follow(g, zig, plane(2.0, 28.5))
        self.assertEqual([f for _, f in fl].count("ok") + [f for _, f in fl].count("recentred"), len(zig))
        self.assertLess(gp.lip_summary(gp.steps_of(fl))["max"], 0.03)
        cross = gp.follow(g, [(30.5, 10.0, 26.5 + m * 0.5) for m in range(15)], plane(30.5, 26.5))    # across the strip, the fork separation test
        self.assertLess(max((abs(d) for _, d in gp.detrend(gp.steps_of(cross))), default=0), 0.03)
        self.assertLess(gp.grid_adjacent_max(g)[0], 0.03)
        self.assertAlmostEqual(gp.surface_slope(g), 0.26, delta=0.02)
        step = make_grid(lambda x, z: plane(x, z) + (0.3 if x >= 30 else 0.0))
        self.assertGreaterEqual(gp.lip_summary(gp.steps_of(gp.follow(step, line(2.5, 57.5), plane(2.5, 30.5))))["max"], 0.2)
        self.assertGreaterEqual(gp.grid_adjacent_max(step)[0], 0.2)

    def test_overpass_is_a_layer_not_a_lip(self):
        g = make_grid(lambda x, z: 10.0, layers=lambda x, z: [14.0] if 20 <= x < 40 else [])
        s, fl = self.lips(g)
        self.assertLess(s["max"], 0.02)                  # the followed surface stays on the lower layer
        self.assertTrue(all(h is None or abs(h - 10.0) < 0.02 for h, _ in fl))
        # a walk that started on the upper layer jumps when it meets the lower only: counted as `layer`
        g2 = make_grid(lambda x, z: 10.0 if x < 30 else 13.0)
        fl2 = gp.follow(g2, line(), None)
        self.assertIn("layer", [f for _, f in fl2])
        self.assertEqual(gp.lip_summary(gp.steps_of(fl2))["max"], 0.0)

    def test_gap_is_not_followed_across(self):
        g = make_grid(lambda x, z: 10.0)
        pts = [(30.5, 0.0, 10.5 + k) for k in range(6)]  # z 10..16 is off the road strip
        fl = gp.follow(g, pts, 10.0)
        self.assertTrue(all(f in ("gap",) for _, f in fl))

    def test_fork_sheet_with_a_gap_separates(self):
        # two legs diverging from (5, 30) at +-12 degrees, road only where the strip is: the gap strip between them opens after ~12 m
        def h(x, z): return 10.0
        cells = []
        for j in range(N):
            for i in range(N):
                x, z = i + 0.5, j + 0.5
                d = abs(z - 30) * 1.0
                on = d < 1.0 + (x - 5) * 0.21 + 1.0 and (x < 5 or d < 1.0 or d > (x - 5) * 0.21 - 1.0)
                cells.append([1000] if on else [])
        grid = gp.Grid({"id": "f", "n": N, "x0": 0.0, "z0": 0.0, "cells": cells})
        legs = []
        for sgn in (1, -1):
            legs.append({"edge": sgn, "cls": "primary", "width": 6.0, "span": "ground", "hdg": sgn * 12.0,
                         "pts": [[5.0 + k * 0.978, 10.0, 30.0 + sgn * k * 0.208] for k in range(45)]})
        case = {"id": "f", "cls": "fork", "legs": legs, "cx": 30, "cz": 30, "y": 10.0}
        r = gp.analyse_fork(grid, case)
        self.assertEqual(r["separation"]["type"], "gap")
        self.assertGreater(r["shared_len_m"], 3)
        self.assertLess(r["shared_len_m"], 20)


if __name__ == "__main__":
    unittest.main(verbosity=1)
