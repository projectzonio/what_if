#!/usr/bin/env python
"""RG-110 debug view for agents: top-down PNG of a road-dataset v1 folder (Pillow). Classes coloured, junctions dotted, bridges marked.

  (Pillow lives in the user site-packages: no -I)
  python -X utf8 tools/worldgen/render_net.py --dir artifacts/worldgen/gen-1 [--out gen-1.png] [--px 1600] [--centre X,Z --span-m 3000]  (zoom, RG-116)

RG-118: --terrain-dump <file> (written by the CLI: --terrain-dump <file> [--window x0,z0,size,step]) puts a hillshade of the carved terrain under the roads and
uses the dump window as the view. Tunnels are drawn black with a cyan outline (dashed look), viaducts with an orange outline, crossing bridges keep the blue one.

RG-151: --scatter <scatter.csv> (CLI --scatter-dump) overlays the instances: trees dark green, bushes light green, street trees teal, houses brown squares (drawn under the roads' junction dots).

Colours: motorway red, primary orange, secondary yellow, local light grey, link magenta; bridge edges are drawn thicker with a blue outline;
junction nodes (degree >= 3) are white dots (interchange nodes green); ends of roads are small grey squares. Scale bar 1 km bottom left.
"""
import argparse
import array
import json
import math
import os
import sys

from PIL import Image, ImageDraw

COLOURS = {"motorway": (230, 60, 50), "primary": (240, 150, 40), "secondary": (235, 215, 70), "local": (170, 170, 180), "track": (120, 100, 80), "link": (220, 70, 220)}
WIDTH = {"motorway": 5, "primary": 4, "secondary": 3, "local": 2, "track": 1, "link": 3}


def load(d, name):
    with open(os.path.join(d, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def read_dump(path):
    """Header line 'x0 z0 size step n', then (n+1)^2 float32 heights (z slow, x fast), then (n+1)^2 hole bytes."""
    with open(path, "rb") as f:
        head = f.readline().decode("ascii").split()
        x0, z0, size, step = (float(v) for v in head[:4])
        n = int(head[4])
        w = n + 1
        h = array.array("f")
        h.fromfile(f, w * w)
        holes = f.read(w * w)
    return x0, z0, size, step, n, h, holes


def hillshade(dump, px):
    """Hypsometric tint times a NW-lit relief shade (pure Python, 1 sample = 1 pixel, then resized to px)."""
    x0, z0, size, step, n, h, holes = dump
    w = n + 1
    lo, hi = min(h), max(h)
    span = max(hi - lo, 1.0)
    az, alt = math.radians(315.0), math.radians(45.0)
    lx, lz, ly = math.cos(alt) * math.sin(az), math.cos(alt) * math.cos(az), math.sin(alt)
    img = Image.new("RGB", (w, w))
    pix = img.load()
    for iz in range(w):
        row = iz * w
        for ix in range(w):
            i0, i1 = max(ix - 1, 0), min(ix + 1, n)
            j0, j1 = max(iz - 1, 0), min(iz + 1, n)
            dx = (h[row + i1] - h[row + i0]) / ((i1 - i0) * step)
            dz = (h[j1 * w + ix] - h[j0 * w + ix]) / ((j1 - j0) * step)
            nl = math.sqrt(dx * dx + dz * dz + 1.0)
            s = (-dx * lx - dz * lz + ly) / nl
            s = 0.35 + 0.65 * max(0.0, min(1.0, s * 1.15))
            t = (h[row + ix] - lo) / span
            if t < 0.35:
                u = t / 0.35
                c = (70 + 60 * u, 105 + 20 * u, 60 + 10 * u)
            elif t < 0.75:
                u = (t - 0.35) / 0.4
                c = (130 + 40 * u, 125 - 15 * u, 70 + 30 * u)
            else:
                u = (t - 0.75) / 0.25
                c = (170 + 70 * u, 110 + 120 * u, 100 + 135 * u)
            pix[ix, w - 1 - iz] = (int(c[0] * s), int(c[1] * s), int(c[2] * s))
    return img.resize((px, px), Image.BILINEAR)


SCATTER = {"tree": ((30, 110, 40), 1.6), "bush": ((120, 200, 80), 1.2), "street_tree": ((0, 170, 150), 2.0), "house": ((150, 85, 45), 3.2)}


def draw_scatter(dr, path, tf, sc, px):
    """Instances over the net: dots for plants, squares for houses (about 9 x 11 m, at least 6 px). Rows outside the picture are skipped."""
    counts = {}
    rows = []
    with open(path, encoding="utf-8") as f:
        next(f)
        for line in f:
            c = line.strip().split(",")
            rows.append((c[0], float(c[2]), float(c[4])))
    order = ["bush", "tree", "street_tree", "house"]
    for kind in order:
        col, r = SCATTER[kind]
        r = max(r, 0.5 * sc * 5.5) if kind == "house" else r
        for k, x, z in rows:
            if k != kind:
                continue
            px_, py_ = tf(x, z)
            if px_ < -5 or py_ < -5 or px_ > px + 5 or py_ > px + 5:
                continue
            counts[kind] = counts.get(kind, 0) + 1
            if kind == "house":
                dr.rectangle([px_ - r, py_ - r, px_ + r, py_ + r], fill=col, outline=(255, 235, 200))
            else:
                dr.ellipse([px_ - r, py_ - r, px_ + r, py_ + r], fill=col)
    return counts


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dir", required=True)
    ap.add_argument("--out")
    ap.add_argument("--px", type=int, default=1600)
    ap.add_argument("--centre", help="x,z of the view centre in metres (zoom); default the world centre")
    ap.add_argument("--span-m", type=float, help="side of the square view in metres; default the whole world")
    ap.add_argument("--scatter", help="scatter.csv from the CLI (RG-151): trees, bushes, houses, street trees drawn over the net")
    ap.add_argument("--terrain-dump", help="hillshade base from the CLI dump (RG-118); its window becomes the view")
    a = ap.parse_args(argv)
    prov = load(a.dir, "provenance")
    nodes = load(a.dir, "nodes")["items"]
    edges = load(a.dir, "edges")["items"]
    size = float(prov["window"]["size_km"]) * 1000.0
    half = (a.span_m / 2) if a.span_m else size / 2 + 100
    cx, cz = (float(v) for v in a.centre.split(",")) if a.centre else (0.0, 0.0)
    px = a.px
    dump = None
    if a.terrain_dump:
        dump = read_dump(a.terrain_dump)
        half = dump[2] / 2
        cx, cz = dump[0] + half, dump[1] + half
    sc = px / (2 * half)

    def tf(x, z):
        return ((x - cx + half) * sc, (cz + half - z) * sc)

    img = hillshade(dump, px) if dump else Image.new("RGB", (px, px), (24, 28, 34))
    dr = ImageDraw.Draw(img)
    # world frame
    x0, y0 = tf(-size / 2, size / 2)
    x1, y1 = tf(size / 2, -size / 2)
    dr.rectangle([x0, y0, x1, y1], outline=(70, 80, 90))
    order = ["track", "local", "secondary", "primary", "link", "motorway"]
    for cls in order:
        for e in edges:
            if e["class"] != cls:
                continue
            c = e["centreline"]
            pts = [tf(c[i], c[i + 2]) for i in range(0, len(c), 3)]
            if e["span"] == "bridge":
                dr.line(pts, fill=(60, 130, 255) if e["layer"] == 1 else (255, 140, 0), width=WIDTH[cls] + 6)   # layer 1: deck over a road, layer 0: viaduct over ground
            if e["span"] == "tunnel":
                dr.line(pts, fill=(0, 220, 230), width=WIDTH[cls] + 6)
                dr.line(pts, fill=(10, 10, 10), width=WIDTH[cls])
                continue
            dr.line(pts, fill=COLOURS.get(cls, (255, 255, 255)), width=WIDTH[cls])
    if a.scatter:
        counts = draw_scatter(dr, a.scatter, tf, sc, px)
        print("render_net: scatter overlay", counts)
    for n in nodes:
        x, y = tf(n["pos"][0], n["pos"][2])
        if n["degree"] >= 3:
            r = 4 if n["kind"] == "interchange" else 2.5
            col = (80, 255, 120) if n["kind"] == "interchange" else (255, 255, 255)
            dr.ellipse([x - r, y - r, x + r, y + r], fill=col)
        elif n["degree"] == 1:
            dr.rectangle([x - 1.5, y - 1.5, x + 1.5, y + 1.5], fill=(150, 150, 150))
    bx, by = 20, px - 25
    bar = 1000 if half > 2000 else (500 if half > 600 else 100)
    dr.line([(bx, by), (bx + bar * sc, by)], fill=(255, 255, 255), width=3)
    dr.text((bx, by - 14), "1 km" if bar == 1000 else "%d m" % bar, fill=(255, 255, 255))
    out = a.out or os.path.join(a.dir, "net.png")
    img.save(out)
    print("render_net: wrote", out, "(%d edges, %d nodes)" % (len(edges), len(nodes)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
