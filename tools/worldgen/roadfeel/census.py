"""Whole-world census from a roadfeel CSV: surface above design by class, who stands above (road/ground), per-metre lips."""
import csv, sys, collections
path = sys.argv[1]
edge = collections.defaultdict(lambda: collections.defaultdict(list))
for r in csv.DictReader(open(path, encoding="utf-8")):
    t = float(r["top"])
    if t < -1e6:
        continue
    edge[int(r["edge"])][r["lat"]].append((float(r["s"]), t - float(r["design"]), r["road"] == "1", float(r["x"]), float(r["z"])))
ground = 0
lips = []
for e, lats in edge.items():
    for lat, seq in lats.items():
        for i, (s, d, road, x, z) in enumerate(seq):
            if not road and d > 0.02:
                ground += 1
            if i and seq[i - 1][0] == s - 1:
                j = d - seq[i - 1][1]
                if abs(j) >= 0.05:
                    lips.append((abs(j), j, e, lat, s, x, z))
lips.sort(reverse=True)
print("samples with GROUND above design > 2 cm:", ground)
for th in (0.05, 0.10, 0.20, 0.30, 0.50):
    hit = [l for l in lips if l[0] >= th]
    print(f"lips >= {th:.2f} m per 1 m of travel: {len(hit)} on {len({l[2] for l in hit})} edges")
print("worst:")
seen = set()
for l in lips:
    if l[2] in seen:
        continue
    seen.add(l[2])
    print(f"  edge {l[2]} lat {l[3]} s {l[4]:.0f} lip {l[1]:+.2f} m at {l[5]:.0f},{l[6]:.0f}")
    if len(seen) >= 12:
        break
