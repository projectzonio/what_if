using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using WhatIf.Planet;
using WhatIf.Planet.Surface;

namespace WhatIf.Planet.Surface.Experiment
{
    public sealed class Row
    {
        public string Id, Kind, Value, Threshold, Result, Note;
        public string Csv() => string.Join(",", new[] { Id, Kind, Q(Value), Q(Threshold), Result, Q(Note) });
        static string Q(string s) => s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        public static Row Proto(string id, string value, string thr, bool pass, string note) => new Row { Id = id, Kind = "protocol", Value = value, Threshold = thr, Result = pass ? "PASS" : "FAIL", Note = note };
        public static Row Report(string id, string value, string note) => new Row { Id = id, Kind = "report", Value = value, Threshold = "report", Result = "report", Note = note };
    }

    public static class Checks
    {
        public static readonly ulong[] Seeds = { 20261112UL, 7UL, 424242UL };
        static string F(double v, string f = "F3") => v.ToString(f, CultureInfo.InvariantCulture);
        public static Vec3 RandomDir(ref ulong st)
        {
            while (true)
            {
                var v = new Vec3(SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1, SplitMix64.NextDouble(ref st) * 2 - 1);
                double l = v.Length; if (l > 0.05 && l <= 1) return v / l;
            }
        }
        static bool SameBits(in SurfaceSample a, in SurfaceSample b) =>
            BitConverter.DoubleToInt64Bits(a.HeightM) == BitConverter.DoubleToInt64Bits(b.HeightM) && a.Kind == b.Kind && BitConverter.DoubleToInt64Bits(a.WaterLevelM) == BitConverter.DoubleToInt64Bits(b.WaterLevelM)
            && BitConverter.SingleToInt32Bits(a.Forest) == BitConverter.SingleToInt32Bits(b.Forest) && a.Biome == b.Biome;

        // ---------- determinism file (two separate processes must write the same bytes) ----------
        public static string DetDigest()
        {
            var sb = new System.Text.StringBuilder(); ulong st = 99;
            foreach (ulong seed in Seeds)
            {
                var s = new PlanetSurface(seed);
                for (int i = 0; i < 10000; i++)
                {
                    var o = s.Sample(RandomDir(ref st));
                    sb.Append(BitConverter.DoubleToInt64Bits(o.HeightM)).Append(' ').Append((int)o.Kind).Append(' ').Append(BitConverter.DoubleToInt64Bits(o.WaterLevelM)).Append(' ').Append(BitConverter.SingleToInt32Bits(o.Forest)).Append(' ').Append(o.Biome).Append('\n');
                }
            }
            return sb.ToString();
        }

        // ---------- s1 ----------
        public static IEnumerable<Row> S1()
        {
            long compared = 0, bad = 0, hs = 0, hsBad = 0, secBad = 0, secN = 0; ulong st = 5;
            foreach (ulong seed in Seeds)
            {
                var surf = new PlanetSurface(seed); var fresh = new PlanetSurface(seed);
                // random directions: Height bits == Sample.HeightM bits, and an independent instance (cold caches) gives identical bits
                for (int i = 0; i < 10000; i++)
                {
                    Vec3 d = RandomDir(ref st); var a = surf.Sample(d); double h = fresh.Height(d); hs++;
                    if (BitConverter.DoubleToInt64Bits(h) != BitConverter.DoubleToInt64Bits(a.HeightM)) hsBad++;
                    if (!SameBits(a, new PlanetSurface(seed).Sample(d)) && i < 50) hsBad++;
                }
                // cube edge / vertex lattice points shared by sectors of two or three faces
                int n = SectorId.PerFaceSide, last = n - 1; long done = 0; var h1 = new float[65 * 65]; var k1 = new byte[65 * 65]; var f1 = new float[65 * 65]; var h2 = new float[65 * 65]; var k2 = new byte[65 * 65]; var f2 = new float[65 * 65];
                var rows = new List<SectorId>();
                for (int f = 0; f < 6; f++) foreach (int rr in new[] { 0, last }) foreach (int cc in new[] { 0, last }) rows.Add(new SectorId(f, rr, cc));   // 24 cube-vertex sectors
                while (rows.Count < 160)
                {
                    int f = (int)(SplitMix64.Next(ref st) % 6), side = (int)(SplitMix64.Next(ref st) % 4), t = (int)(SplitMix64.Next(ref st) % (ulong)n);
                    rows.Add(side == 0 ? new SectorId(f, t, 0) : side == 1 ? new SectorId(f, t, last) : side == 2 ? new SectorId(f, 0, t) : new SectorId(f, last, t));
                }
                foreach (var id in rows)
                {
                    var qa = new QuadSector(id); SurfaceSector.Sample(surf, id, h1, k1, f1);
                    for (int dir = 0; dir < 4; dir++)
                    {
                        bool onSide = dir == 0 ? id.Col == last : dir == 1 ? id.Col == 0 : dir == 2 ? id.Row == last : id.Row == 0;
                        if (!onSide) continue;
                        var nid = id.Neighbour(dir); var qb = new QuadSector(nid); SurfaceSector.Sample(fresh, nid, h2, k2, f2);
                        for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++)
                            {
                                bool edge = dir == 0 ? i == 64 : dir == 1 ? i == 0 : dir == 2 ? j == 64 : j == 0; if (!edge) continue;
                                Vec3 da = qa.Direction(i, j); int m = -1;
                                for (int b = 0; b < 65 * 4 && m < 0; b++) { int side = b / 65, t = b % 65; int bi = side == 0 ? t : side == 1 ? t : side == 2 ? 0 : 64, bj = side == 0 ? 0 : side == 1 ? 64 : t; Vec3 db = qb.Direction(bi, bj); if (db.X == da.X && db.Y == da.Y && db.Z == da.Z) m = bj * 65 + bi; }
                                compared++; done++;
                                if (m < 0) { bad++; continue; }
                                if (BitConverter.SingleToInt32Bits(h1[j * 65 + i]) != BitConverter.SingleToInt32Bits(h2[m]) || k1[j * 65 + i] != k2[m] || BitConverter.SingleToInt32Bits(f1[j * 65 + i]) != BitConverter.SingleToInt32Bits(f2[m])) bad++;
                            }
                    }
                }
                // the sector batch equals the plain sample on its own directions
                var qs = new QuadSector(new SectorId(2, 12345, 6789)); SurfaceSector.Sample(surf, qs.Id, h1, k1, f1);
                for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i += 3) { var o = fresh.Sample(qs.Direction(i, j)); secN++; if ((float)o.HeightM != h1[j * 65 + i] || (byte)o.Kind != k1[j * 65 + i] || o.Forest != f1[j * 65 + i]) secBad++; }
            }
            yield return Row.Proto("s1.edges", bad.ToString(), "0 of " + compared + " vertices differ", bad == 0, $"{compared} canonical lattice vertices on cube edges/vertices, each evaluated from both (all) sectors that share it, 3 seeds, 2 separate PlanetSurface instances; differing bits of height/kind/forest");
            yield return Row.Proto("s1.height_eq_sample", hsBad.ToString(), "0 of " + hs, hsBad == 0, "Height(dir) bits == Sample(dir).HeightM bits on 30000 random directions (fresh vs warm instance); 150 cold-instance full-sample comparisons");
            yield return Row.Proto("s1.sector_eq_sample", secBad.ToString(), "0 of " + secN, secBad == 0, "SurfaceSector.Sample (cached batch) == PlanetSurface.Sample (cold instance) at the same directions");
        }

        // ---------- s2 + s5 shares ----------
        public static IEnumerable<Row> S2S5(ulong seed, Raster map)
        {
            var surf = new PlanetSurface(seed); ulong st = seed * 31 + 1; const int N = 1000000;
            long[] k = new long[5]; long forested = 0, bad = 0, badTree = 0; double fsum = 0;
            for (int i = 0; i < N; i++)
            {
                var o = surf.Sample(RandomDir(ref st)); k[(int)o.Kind]++;
                bool land = o.Kind >= SurfaceKind.Beach;
                if (land) { fsum += o.Forest; if (o.Forest >= 0.5f) forested++; }
                if (o.Forest > 0 && (o.Kind != SurfaceKind.Land)) bad++;
                if (o.Forest > 0 && o.HeightM > SurfaceParams.TreeLineM) badTree++;
            }
            double landN = k[2] + k[3] + k[4]; double share = landN / N;
            string s = "seed " + seed + ": ";
            yield return Row.Proto("s2.land_share", F(share * 100, "F2") + " %", "25..40 %", share >= 0.25 && share <= 0.40, s + $"Land+Beach+Lake share of 1000000 uniform directions");
            yield return Row.Report("s2.kind_shares", $"ocean {F(100.0 * k[0] / N, "F2")} % / sea {F(100.0 * k[1] / N, "F2")} % / beach {F(100.0 * k[2] / N, "F3")} % / land {F(100.0 * k[3] / N, "F2")} % / lake {F(100.0 * k[4] / N, "F4")} %", s + "of all directions");
            double fsh = forested / landN;
            yield return Row.Proto("s5.forest_share", F(fsh * 100, "F2") + " %", "20..55 %", fsh >= 0.20 && fsh <= 0.55, s + $"land samples with Forest >= 0.5; mean density {F(fsum / landN, "F3")}");
            yield return Row.Proto("s5.forest_on_water_or_beach", bad.ToString(), "0", bad == 0, s + "samples with Forest > 0 and Kind != Land");
            yield return Row.Proto("s5.forest_above_treeline", badTree.ToString(), "0", badTree == 0, s + $"samples with Forest > 0 and height > {SurfaceParams.TreeLineM} m");
            // continents on the 2048 x 1024 map (area weighted, wraps in longitude)
            int w = map.W, h = map.H; var par = new int[w * h]; for (int i = 0; i < par.Length; i++) par[i] = i;
            Func<int, int> find = null; find = a => { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; };
            Action<int, int> uni = (a, b) => { a = find(a); b = find(b); if (a != b) par[a] = b; };
            bool[] isLand = new bool[w * h]; for (int i = 0; i < isLand.Length; i++) isLand[i] = map.Kind[i] >= 2;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int n = y * w + x; if (!isLand[n]) continue; int xr = (x + 1) % w; if (isLand[y * w + xr]) uni(n, y * w + xr); if (y + 1 < h && isLand[n + w]) uni(n, n + w); }
            var area = new Dictionary<int, double>(); double total = 0, R = CubeSphere.Radius;
            for (int y = 0; y < h; y++) { double lat = Math.PI * (0.5 - (y + 0.5) / h), a = (2 * Math.PI * R / w) * (Math.PI * R / h) * Math.Cos(lat) / 1e6; for (int x = 0; x < w; x++) { int n = y * w + x; if (!isLand[n]) continue; int r = find(n); double v; area.TryGetValue(r, out v); area[r] = v + a; total += a; } }
            var big = area.Values.Where(a => a >= 1e6).OrderByDescending(a => a).ToList();
            yield return Row.Report("s2.continents", $"{big.Count} continents >= 1e6 km2; largest {F(100 * (big.Count > 0 ? big[0] : 0) / total, "F1")} % of land; land {F(total / 1e6, "F1")} M km2", s + "2048 x 1024 map, 4-connected, wraps in longitude, poles not joined; expect >= 3 and largest < 70 %");
        }

        // ---------- lakes: random land regions, s4 ----------
        public static Vec3 FindLand(PlanetSurface surf, ref ulong st) { while (true) { Vec3 d = RandomDir(ref st); if (surf.Sample(d).Kind == SurfaceKind.Land) return d; } }

        public static List<Lake> LakesInRegion(PlanetSurface surf, RegionId rid, out double landKm2)
        {
            var found = new Dictionary<(long, long, long), Lake>(); int land = 0, tot = 0; const int G = 49;
            Vec3 c = CubeSphere.RegionCentre(rid); int face; double a0, b0; CubeSphere.ToFaceAB(c, out face, out a0, out b0);
            double span = 2.0 / (1 << rid.Level);
            for (int j = 0; j < G; j++) for (int i = 0; i < G; i++)
                {
                    Vec3 d = CubeSphere.FromFaceAB(rid.Face, -1.0 + (rid.Col + (i + 0.5) / G) * span, -1.0 + (rid.Row + (j + 0.5) / G) * span); Vec3 p = d * CubeSphere.Radius;
                    tot++; if (surf.Sample(d).Kind >= SurfaceKind.Beach) land++;
                    var key = (PlanetSurface.CellOf(p.X), PlanetSurface.CellOf(p.Y), PlanetSurface.CellOf(p.Z));
                    if (!found.ContainsKey(key)) { var l = surf.LakeOfCell(key.Item1, key.Item2, key.Item3); if (l != null) found[key] = l; }
                }
            landKm2 = 96.0 * land / tot;    // ~9.8 km x 9.8 km region
            return found.Values.Where(l => CubeSphere.RegionOf(l.Centre, rid.Level).Equals(rid)).ToList();
        }

        public static IEnumerable<Row> S4(ulong seed)
        {
            var surf = new PlanetSurface(seed); ulong st = seed * 17 + 3; string s = "seed " + seed + ": ";
            double landTot = 0; int lakes = 0; long spill = 0, outer = 0, walked = 0, levelBad = 0, lowLevel = 0; double minLevel = 1e9, maxLevel = -1e9;
            for (int r = 0; r < 200; r++)
            {
                Vec3 d = FindLand(surf, ref st); var rid = CubeSphere.RegionOf(d, 10); double km2;
                var list = LakesInRegion(surf, rid, out km2); landTot += km2;
                foreach (var l in list)
                {
                    lakes++; minLevel = Math.Min(minLevel, l.Level); maxLevel = Math.Max(maxLevel, l.Level); if (l.Level < 2.0) lowLevel++;
                    double perim = 2 * Math.PI * l.Rho0 * 1.0; int steps = (int)Math.Ceiling(perim * 1.2);   // ~1 m spacing along the shore (radius varies 0.8..1.2)
                    for (int k = 0; k < steps; k++)
                    {
                        double th = 2 * Math.PI * k / steps; double rho = l.Radius(th);
                        foreach (double u in new[] { 0.5, 2, 8, 20, 40, 60, 90, 120, 200, 300 })
                        {
                            var o = surf.Sample(l.PointAt(th, rho + u)); walked++;
                            if (o.Kind == SurfaceKind.Lake || (u <= Lake.FillM && o.HeightM < l.Level - 1e-9)) spill++; else if (o.HeightM < l.Level - 1e-9) outer++;
                        }
                        var inside = surf.Sample(l.PointAt(th, Math.Max(0.0, rho - 3)));
                        if (inside.Kind == SurfaceKind.Lake && inside.WaterLevelM != l.Level) levelBad++;
                    }
                }
            }
            yield return Row.Report("s4.lake_density", F(lakes * 1e4 / Math.Max(landTot, 1), "F2") + " per 10000 km2 land", s + $"{lakes} lakes in 200 random land regions of 9.8 km (land {F(landTot, "F0")} km2); aim 2..20");
            yield return Row.Proto("s4.no_spill", spill.ToString(), "0", spill == 0, s + $"{walked} samples at 0.5..300 m outside the shoreline (1 m steps along it); violations = inside the lake kind, or below the level within the 120 m guaranteed band (the water is enclosed by a >= level ring). Diag: {outer} samples at 200/300 m outside are below the level (natural terrain, fade zone, not adjacent to the water)");
            yield return Row.Proto("s4.level_constant", levelBad.ToString(), "0", levelBad == 0, s + "lake samples whose WaterLevelM differs from the lake's level (constant by construction)");
            yield return Row.Proto("s4.level_above_sea", lakes == 0 ? "no lakes" : F(minLevel, "F2") + " m", ">= 2 m", lakes > 0 && lowLevel == 0, s + $"lowest lake level over {lakes} lakes (max {F(maxLevel, "F1")} m)");
        }

        // ---------- s6 continuity ----------
        public static Vec3 FindCoast(PlanetSurface surf, ref ulong st)
        {
            while (true)
            {
                Vec3 d = FindLand(surf, ref st); Vec3 e = Vec3.Cross(new Vec3(0, 1, 0), d).Normalized(), n = Vec3.Cross(d, e); double ang = SplitMix64.NextDouble(ref st) * 2 * Math.PI;
                Vec3 t = e * Math.Cos(ang) + n * Math.Sin(ang); Vec3 prev = d; bool hit = false; Vec3 cur = d;
                for (int k = 1; k <= 400 && !hit; k++)
                {
                    cur = (d * CubeSphere.Radius + t * (k * 3000.0)).Normalized(); var o = surf.Sample(cur);
                    if (o.Kind <= SurfaceKind.Sea) hit = true; else prev = cur;
                }
                if (!hit) continue;
                for (int it = 0; it < 18; it++) { Vec3 m = ((prev + cur) * 0.5).Normalized(); if (surf.Sample(m).Kind <= SurfaceKind.Sea) cur = m; else prev = m; }
                return prev;
            }
        }
        public static Lake FindLake(PlanetSurface surf, ref ulong st)
        {
            while (true) { Vec3 p = RandomDir(ref st) * CubeSphere.Radius; var l = surf.LakeOfCell(PlanetSurface.CellOf(p.X), PlanetSurface.CellOf(p.Y), PlanetSurface.CellOf(p.Z)); if (l != null) return l; }
        }

        public static IEnumerable<Row> S6(ulong seed)
        {
            var surf = new PlanetSurface(seed); ulong st = seed * 13 + 9; string s = "seed " + seed + ": ";
            var h = new float[65 * 65]; var k = new byte[65 * 65]; var f = new float[65 * 65];
            var groups = new[] { "coast", "lake shore", "random land" }; var rows = new List<Row>();
            for (int g = 0; g < 3; g++)
            {
                double worst = 0; SectorId worstId = default(SectorId); var all = new List<double>(); double worstF = 0;
                for (int q = 0; q < 100; q++)
                {
                    Vec3 d = g == 0 ? FindCoast(surf, ref st) : g == 1 ? FindLakeShore(surf, ref st) : FindLand(surf, ref st);
                    var id = SectorId.At(d); SurfaceSector.Sample(surf, id, h, k, f); double sw = 0;
                    for (int j = 0; j < 65; j++) for (int i = 0; i < 65; i++)
                        {
                            if (i < 64) { double dd = Math.Abs(h[j * 65 + i + 1] - h[j * 65 + i]); all.Add(dd); if (dd > sw) sw = dd; worstF = Math.Max(worstF, Math.Abs(f[j * 65 + i + 1] - f[j * 65 + i])); }
                            if (j < 64) { double dd = Math.Abs(h[(j + 1) * 65 + i] - h[j * 65 + i]); all.Add(dd); if (dd > sw) sw = dd; worstF = Math.Max(worstF, Math.Abs(f[(j + 1) * 65 + i] - f[j * 65 + i])); }
                        }
                    if (sw > worst) { worst = sw; worstId = id; }
                }
                all.Sort(); double p99 = all[(int)(all.Count * 0.99)];
                rows.Add(Row.Proto("s6.max_dh." + groups[g].Replace(' ', '_'), F(worst, "F3") + " m", "<= 4 m", worst <= 4.0, s + $"100 sectors ({groups[g]}), neighbour vertices ~4 m apart; P99 {F(p99, "F4")} m; worst sector {worstId}"));
                rows.Add(Row.Proto("s5.forest_continuity." + groups[g].Replace(' ', '_'), F(worstF, "F4"), "<= 0.05", worstF <= 0.05, s + $"max |delta Forest| between neighbour vertices in the same 100 sectors"));
            }
            return rows;
        }
        static Vec3 FindLakeShore(PlanetSurface surf, ref ulong st) { var l = FindLake(surf, ref st); double th = SplitMix64.NextDouble(ref st) * 2 * Math.PI; return l.PointAt(th, l.Radius(th) + (SplitMix64.NextDouble(ref st) - 0.5) * 60); }

        // ---------- s3 coast quality on a region-sized crop ----------
        public static IEnumerable<Row> S3(string name, Raster r, string note)
        {
            int w = r.W, h = r.H; bool[] land = new bool[w * h]; for (int i = 0; i < land.Length; i++) land[i] = r.Kind[i] == (byte)SurfaceKind.Land || r.Kind[i] == (byte)SurfaceKind.Beach;
            int landPx = land.Count(b => b); var seen = new bool[w * h]; int islandPx = 0, islands = 0; var stack = new Stack<int>();
            for (int s0 = 0; s0 < w * h; s0++)
            {
                if (!land[s0] || seen[s0]) continue; int cnt = 0; bool border = false; stack.Push(s0); seen[s0] = true;
                while (stack.Count > 0)
                {
                    int n = stack.Pop(); cnt++; int x = n % w, y = n / w; if (x == 0 || y == 0 || x == w - 1 || y == h - 1) border = true;
                    if (x > 0 && land[n - 1] && !seen[n - 1]) { seen[n - 1] = true; stack.Push(n - 1); }
                    if (x < w - 1 && land[n + 1] && !seen[n + 1]) { seen[n + 1] = true; stack.Push(n + 1); }
                    if (y > 0 && land[n - w] && !seen[n - w]) { seen[n - w] = true; stack.Push(n - w); }
                    if (y < h - 1 && land[n + w] && !seen[n + w]) { seen[n + w] = true; stack.Push(n + w); }
                }
                if (!border && cnt * 256.0 < 10000.0) { islandPx += cnt; islands++; }
            }
            long edges = 0; double maxCoastH = double.NegativeInfinity;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int n = y * w + x; bool sea = r.Kind[n] <= (byte)SurfaceKind.Sea;
                    if (x < w - 1 && sea != (r.Kind[n + 1] <= (byte)SurfaceKind.Sea)) edges++;
                    if (y < h - 1 && sea != (r.Kind[n + w] <= (byte)SurfaceKind.Sea)) edges++;
                    if (!sea && land[n])
                    {
                        bool near = false; for (int dy = -6; dy <= 6 && !near; dy++) for (int dx = -6; dx <= 6 && !near; dx++) { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < w && yy < h && r.Kind[yy * w + xx] <= (byte)SurfaceKind.Sea && dx * dx + dy * dy <= 36) near = true; }
                        if (near) maxCoastH = Math.Max(maxCoastH, r.Height[n]);
                    }
                }
            double share = landPx == 0 ? 0 : 100.0 * islandPx / landPx;
            yield return Row.Report("s3." + name, $"islands < 1 ha: {islands} ({F(share, "F2")} % of land); coast {F(edges * 16.0 / 1000, "F2")} km; max height within 100 m of shore {(double.IsNegativeInfinity(maxCoastH) ? "n/a" : F(maxCoastH, "F1") + " m")}; land {F(100.0 * landPx / (w * h), "F1")} % of the crop", note + (share > 5 ? " -> FINDING: speckled coast (> 5 % in islands < 1 ha)" : ""));
        }

        // ---------- s7 cost (one process = one repetition) ----------
        public static List<string> Cost(ulong seed)
        {
            var surf = new PlanetSurface(seed); ulong st = 4242; var lines = new List<string>();
            var h = new float[65 * 65]; var k = new byte[65 * 65]; var f = new float[65 * 65];
            var sets = new Dictionary<string, List<SectorId>>();
            var land = new List<SectorId>();
            { Vec3 d = FindLand(surf, ref st); var id = SectorId.At(d); int dr = 0; for (int i = 0; i < 100; i++) land.Add(new SectorId(id.Face, Math.Min(id.Row + i / 10, 32767), Math.Min(id.Col + i % 10, 32767))); dr++; }
            var scat = new List<SectorId>(); for (int i = 0; i < 100; i++) scat.Add(SectorId.At(FindLand(surf, ref st)));
            var coast = new List<SectorId>(); for (int i = 0; i < 100; i++) coast.Add(SectorId.At(FindCoast(surf, ref st)));
            var lake = new List<SectorId>(); for (int i = 0; i < 100; i++) lake.Add(SectorId.At(FindLakeShore(surf, ref st)));
            var ocean = new List<SectorId>(); while (ocean.Count < 100) { Vec3 d = RandomDir(ref st); if (surf.Sample(d).Kind == SurfaceKind.Ocean) ocean.Add(SectorId.At(d)); }
            sets["land_strip"] = land; sets["land_scattered"] = scat; sets["coast"] = coast; sets["lake_shore"] = lake; sets["ocean"] = ocean;
            var sw = new Stopwatch();
            for (int w = 0; w < 20; w++) SurfaceSector.Sample(surf, scat[w], h, k, f);   // JIT + caches
            foreach (var kv in sets)
            {
                var t = new List<double>();
                foreach (var id in kv.Value) { sw.Restart(); SurfaceSector.Sample(surf, id, h, k, f); sw.Stop(); t.Add(sw.Elapsed.TotalMilliseconds); }
                t.Sort(); lines.Add($"{kv.Key},{F(t[t.Count / 2], "F5")},{F(t[(int)(t.Count * 0.95)], "F5")}");
            }
            // Height(dir) ns/call
            var dirs = new Vec3[20000]; for (int i = 0; i < dirs.Length; i++) dirs[i] = FindNear(ref st, scat[i % 100]);
            double acc = 0; for (int i = 0; i < 2000; i++) acc += surf.Height(dirs[i]);
            sw.Restart(); for (int i = 0; i < dirs.Length; i++) acc += surf.Height(dirs[i]); sw.Stop();
            lines.Add($"height_ns_per_call,{F(sw.Elapsed.TotalMilliseconds * 1e6 / dirs.Length, "F1")},{acc * 0}");
            // memory after 1000 sectors (new instance so the number is the layer + lake caches only)
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long before = GC.GetTotalMemory(true);
            var s2 = new PlanetSurface(seed ^ 1); ulong st2 = 77;
            for (int i = 0; i < 1000; i++) SurfaceSector.Sample(s2, SectorId.At(RandomDir(ref st2)), h, k, f);
            long after = GC.GetTotalMemory(true);
            lines.Add($"memory_mib_after_1000_sectors,{F((after - before) / 1048576.0, "F3")},{s2.LakeCacheCount}");
            GC.KeepAlive(s2);
            sw.Restart(); Maps.Planet(seed, 2048, 1024, false); sw.Stop();
            lines.Add($"planet_map_2048x1024_s,{F(sw.Elapsed.TotalSeconds, "F2")},single thread");
            return lines;
        }
        static Vec3 FindNear(ref ulong st, SectorId id) { var q = new QuadSector(id); return q.Direction((int)(SplitMix64.Next(ref st) % 65), (int)(SplitMix64.Next(ref st) % 65)); }
    }
}
