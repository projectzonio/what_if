using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WhatIf.WorldGen
{
    /// <summary>One placed object (RG-151, V1). Yaw is the heading of the object's width axis in radians (V2.Heading convention, atan2(z, x)).</summary>
    public struct Instance
    {
        public byte Kind, Variant;
        public float X, Y, Z, Yaw, Scale;
    }

    public static class ScatterKind
    {
        public const byte Tree = 0, Bush = 1, House = 2, StreetTree = 3;
        public static string Name(byte k) { return k == Tree ? "tree" : k == Bush ? "bush" : k == House ? "house" : "street_tree"; }
    }

    /// <summary>Counters of the CLI/test checks (a)-(c) of design section 5. Every instance is audited; nothing is excluded.</summary>
    public sealed class ScatterAudit
    {
        public int Sectors, Instances, Trees, Bushes, Houses, StreetTrees;
        public int ViolationsA, ViolationsB, ViolationsC;     // (a) road clearance, (b) span/hole/height band/slope, (c) caps
        public int Excluded;                                  // instances the audit skipped: always 0
        public double MinMarginA = double.MaxValue;           // smallest (distance - half width - clearance) over all instances, metres
        public int MaxTreesInSector, MaxBuildingsInSector;
        public readonly List<string> Samples = new List<string>();
        public bool Pass { get { return ViolationsA == 0 && ViolationsB == 0 && ViolationsC == 0 && Excluded == 0; } }
    }

    /// <summary>Deterministic placement of trees, bushes, houses and street trees (RG-151, V1 of worldgen-vegetation-buildings.md).
    /// Pure function of (seed, sector, terrain, road graph, settlements); no Unity types, no state shared between sectors (the lazily built village lot
    /// lists are a cache of a pure function of the graph). The road dataset and its hash are not touched.</summary>
    public sealed class ScatterModel
    {
        public const double SectorM = 256;                      // mirror of WorldGenMap.CellSize (Unity side)
        // ---- design values (section 4); every change is logged in the V1 result section of the design document ----
        public const int TreeGrid = 26, BushGrid = 18;          // cells per sector side: 9.85 m and 14.2 m (about 10 / 14 m)
        public const double MaskWavelengthM = 500, MaskThreshold = 0.15;
        public const double ClearPrimaryM = 10, ClearLocalM = 6, ClearStreetTreeM = 3, ClearHouseM = 5;
        public const double SightLookM = 70, SightRadiusMaxM = 250;
        public const double TreeLineM = 120, WaterLineM = -3, TreeMaxSlopeDeg = 30, HouseMaxSlopeDeg = 6;
        public const double LotFrontageM = 22, SetbackM = 6, HouseAlongM = 9, HouseDepthM = 11, JunctionM = 14;
        public const double StreetTreeSpacingM = 18, StreetTreeOffsetM = 3;
        public const double LotsEdgeFactor = 1.1;
        public const double ForestFromLotM = 40, SettlementFadeM = 60;
        public const double BushBandM = 6;
        const double QueryM = 45;

        public double MaskOffset;                               // added to the mask before the threshold (first tuning knob, design section 9)
        public int TreeCap = 1500, BuildingCap = 150;
        /// <summary>RG-169: optional tree density multiplier (x, z) -> [0, 1] (the planet forest); multiplies the mask probability, never replaces it. null = bit-identical to before.</summary>
        public Func<double, double, double> DensityFactor;
        /// <summary>RG-169: optional water mask (x, z) -> true on water: no tree, bush, house or street tree there. null = none.</summary>
        public Func<double, double, bool> IsWater;
        /// <summary>RG-169: per-instance tree line / water line (default = the constants, bit-identical). A surface-base world opens both: the planet Forest carries the tree line.</summary>
        public double TreeLine = TreeLineM, WaterLine = WaterLineM;
        /// <summary>RG-169: tree candidate rejection counters (diagnostics only): 0 mask/density, 1 margin, 2 water, 3 lot, 4 tree line, 5 water line, 6 hole, 7 slope.</summary>
        public readonly long[] TreeRejected = new long[8];
        public static readonly string[] RejectNames = { "mask/density", "margin", "water", "lot", "tree line", "water line", "hole", "slope" };

        readonly long _seed;
        readonly ulong _maskSeed;
        readonly RoadGraph _g;
        readonly TerrainModel _t;
        readonly List<SettlementInfo> _set;
        readonly SegIndex _idx = new SegIndex(32);
        readonly List<V2> _junctions = new List<V2>();
        readonly double[] _ebx0, _ebz0, _ebx1, _ebz1;
        readonly List<int>[] _villageEdges;                     // per settlement: local ground edges inside the village (empty for a city)
        readonly double[][] _villageBox;                        // per settlement: x0, z0, x1, z1 of the lot area (empty = no village)
        readonly Dictionary<int, List<Lot>> _lots = new Dictionary<int, List<Lot>>();
        [ThreadStatic] static List<int> _scratch;

        sealed class Lot { public double X, Z, Yaw, Y; public int Edge, K, Side, Variant; public double Scale; }

        public ScatterModel(WorldSpec spec, GenResult res)
        {
            _seed = spec.Seed;
            _maskSeed = Rng.ForStage(spec.Seed, "scatter-mask").NextU64();
            _g = res.Graph; _t = res.Terrain; _set = res.Settlements;
            int ne = _g.Edges.Count;
            _ebx0 = new double[ne]; _ebz0 = new double[ne]; _ebx1 = new double[ne]; _ebz1 = new double[ne];
            foreach (var e in _g.Edges)
            {
                double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
                for (int i = 0; i < e.Pts.Length; i++)
                {
                    x0 = Math.Min(x0, e.Pts[i].X); x1 = Math.Max(x1, e.Pts[i].X); z0 = Math.Min(z0, e.Pts[i].Z); z1 = Math.Max(z1, e.Pts[i].Z);
                    if (i + 1 < e.Pts.Length) _idx.Add(e.Pts[i], e.Pts[i + 1], (e.Id << 20) | i);
                }
                _ebx0[e.Id] = x0; _ebz0[e.Id] = z0; _ebx1[e.Id] = x1; _ebz1[e.Id] = z1;
            }
            var deg = _g.Degrees();
            foreach (var n in _g.Nodes) if (deg[n.Id] >= 3) _junctions.Add(n.P);
            _villageEdges = new List<int>[_set.Count];
            _villageBox = new double[_set.Count][];
            for (int s = 0; s < _set.Count; s++) { _villageEdges[s] = new List<int>(); _villageBox[s] = null; }
            foreach (var e in _g.Edges)
            {
                if (e.Class != RoadClass.Local || e.Span != "ground") continue;
                V2 mid = e.Pts[e.Pts.Length / 2];
                for (int s = 0; s < _set.Count; s++)
                {
                    if (_set[s].IsCity || V2.Dist(mid, _set[s].C) > _set[s].Radius * LotsEdgeFactor) continue;
                    _villageEdges[s].Add(e.Id);
                    var b = _villageBox[s];
                    if (b == null) b = _villageBox[s] = new[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
                    b[0] = Math.Min(b[0], _ebx0[e.Id] - 40); b[1] = Math.Min(b[1], _ebz0[e.Id] - 40);
                    b[2] = Math.Max(b[2], _ebx1[e.Id] + 40); b[3] = Math.Max(b[3], _ebz1[e.Id] + 40);
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- mask

        static double Smooth(double t) { t = t < 0 ? 0 : (t > 1 ? 1 : t); return t * t * (3 - 2 * t); }

        double Lattice(long ix, long iz)
        {
            unchecked
            {
                ulong h = _maskSeed ^ ((ulong)ix * 0x9E3779B97F4A7C15UL) ^ ((ulong)iz * 0xC2B2AE3D27D4EB4FUL);
                h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL; h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL; h ^= h >> 33;
                return (h >> 11) * (1.0 / 9007199254740992.0) * 2.0 - 1.0;
            }
        }

        double ValueNoise(double x, double z)
        {
            double fx = Math.Floor(x), fz = Math.Floor(z);
            long ix = (long)fx, iz = (long)fz;
            double u = Smooth(x - fx), v = Smooth(z - fz);
            double a = Lattice(ix, iz), b = Lattice(ix + 1, iz), c = Lattice(ix, iz + 1), d = Lattice(ix + 1, iz + 1);
            return (a + (b - a) * u) + ((c + (d - c) * u) - (a + (b - a) * u)) * v;
        }

        /// <summary>Forest mask, about [-1, 1] (three octaves of seeded value noise, base wavelength 500 m). Forest where Mask + MaskOffset &gt; MaskThreshold.</summary>
        public double Mask(double x, double z)
        {
            double sum = 0, amp = 1, norm = 0, f = 1.0 / MaskWavelengthM;
            for (int o = 0; o < 3; o++)
            {
                sum += amp * ValueNoise(x * f + o * 37.0, z * f + o * 53.0);
                norm += amp; amp *= 0.5; f *= 2.0;
            }
            return sum / norm;
        }

        /// <summary>0 inside a settlement (radius), rising to 1 over SettlementFadeM beyond it.</summary>
        double SettlementFactor(double x, double z)
        {
            double f = 1;
            for (int i = 0; i < _set.Count; i++)
            {
                double d = Math.Sqrt((x - _set[i].C.X) * (x - _set[i].C.X) + (z - _set[i].C.Z) * (z - _set[i].C.Z));
                f = Math.Min(f, Smooth((d - _set[i].Radius) / SettlementFadeM));
            }
            return f;
        }

        // ---------------------------------------------------------------- road clearance

        static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        /// <summary>Clearance (beyond the half width) a kind needs from one road segment. Trees and bushes: 10 m (6 m local/track), larger on the inside of a tight
        /// curve so that the forest edge never blocks a 70 m sight line; street trees 3 m; house footprint corners 5 m.</summary>
        static double Clearance(byte kind, GEdge e, int i, V2 ab, V2 ap)
        {
            if (kind == ScatterKind.StreetTree) return ClearStreetTreeM;
            if (kind == ScatterKind.House) return ClearHouseM;
            double c = (e.Class == RoadClass.Local || e.Class == RoadClass.Track) ? ClearLocalM : ClearPrimaryM;
            double k = 0.5 * (e.Curv[i] + e.Curv[i + 1]);
            if (Math.Abs(k) > 1.0 / SightRadiusMaxM && V2.Cross(ab, ap) * k > 0)
            {
                double r = 1.0 / Math.Abs(k);
                c = Math.Max(c, r * (1 - Math.Cos(SightLookM / (2 * r))));
            }
            return c;
        }

        static double SegMargin(byte kind, GEdge e, int i, V2 a, V2 b, V2 p)
        {
            V2 ab = b - a, ap = p - a;
            double l2 = V2.Dot(ab, ab);
            double t = l2 < 1e-12 ? 0 : Clamp01(V2.Dot(ap, ab) / l2);
            V2 q = a + ab * t;
            return V2.Dist(p, q) - e.WidthM * 0.5 - Clearance(kind, e, i, ab, ap);
        }

        /// <summary>Smallest (distance to a centreline - half width - clearance of the kind) over the roads around p, through the segment index. Negative = too close.</summary>
        double Margin(byte kind, double x, double z)
        {
            var c = _scratch ?? (_scratch = new List<int>());
            _idx.Query(new V2(x - QueryM, z - QueryM), new V2(x + QueryM, z + QueryM), c);
            var p = new V2(x, z);
            double best = double.MaxValue;
            for (int n = 0; n < c.Count; n++)
            {
                int id = c[n], o = _idx.Owner[id];
                best = Math.Min(best, SegMargin(kind, _g.Edges[o >> 20], o & 0xFFFFF, _idx.A[id], _idx.B[id], p));
            }
            return best;
        }

        /// <summary>The same quantity by brute force over every edge (bounding-box culled), independent of the segment index. Used by the audit.</summary>
        double MarginBrute(byte kind, double x, double z)
        {
            var p = new V2(x, z);
            double best = double.MaxValue;
            foreach (var e in _g.Edges)
            {
                if (x < _ebx0[e.Id] - QueryM || x > _ebx1[e.Id] + QueryM || z < _ebz0[e.Id] - QueryM || z > _ebz1[e.Id] + QueryM) continue;
                for (int i = 0; i + 1 < e.Pts.Length; i++) best = Math.Min(best, SegMargin(kind, e, i, e.Pts[i], e.Pts[i + 1], p));
            }
            return best;
        }

        // ---------------------------------------------------------------- terrain helpers

        double SlopeDeg(double x, double z)
        {
            const double h = 2.0;
            double dx = (_t.Height(x + h, z) - _t.Height(x - h, z)) / (2 * h);
            double dz = (_t.Height(x, z + h) - _t.Height(x, z - h)) / (2 * h);
            return Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180.0 / Math.PI;
        }

        bool InHole(double x, double z)
        {
            double m = TerrainModel.HoleMarginM;
            return _t.Hole(x, z) || _t.Hole(x + m, z) || _t.Hole(x - m, z) || _t.Hole(x, z + m) || _t.Hole(x, z - m);
        }

        // ---------------------------------------------------------------- village lots

        static void At(GEdge e, double s, out V2 p, out V2 t)
        {
            double acc = 0;
            for (int i = 0; i + 1 < e.Pts.Length; i++)
            {
                double l = V2.Dist(e.Pts[i], e.Pts[i + 1]);
                if (acc + l >= s || i + 2 == e.Pts.Length)
                {
                    double u = l < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (s - acc) / l));
                    p = e.Pts[i] + (e.Pts[i + 1] - e.Pts[i]) * u;
                    t = (e.Pts[i + 1] - e.Pts[i]).Unit;
                    return;
                }
                acc += l;
            }
            p = e.Pts[0]; t = new V2(1, 0);
        }

        double JunctionDist(V2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < _junctions.Count; i++) best = Math.Min(best, V2.Dist(p, _junctions[i]));
            return best;
        }

        List<Lot> LotsOf(int s)
        {
            List<Lot> lots;
            if (_lots.TryGetValue(s, out lots)) return lots;
            lots = new List<Lot>();
            foreach (int ei in _villageEdges[s])
            {
                var e = _g.Edges[ei];
                int n = (int)Math.Floor(e.LengthM / LotFrontageM);
                for (int k = 0; k < n; k++)
                {
                    V2 p, t;
                    At(e, (k + 0.5) * LotFrontageM, out p, out t);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        V2 nrm = t.Left * side;
                        V2 c = p + nrm * (e.WidthM * 0.5 + SetbackM + HouseDepthM * 0.5);
                        if (JunctionDist(c) < JunctionM) continue;
                        if (SlopeDeg(c.X, c.Z) >= HouseMaxSlopeDeg) continue;
                        if (InHole(c.X, c.Z)) continue;
                        if (IsWater != null && IsWater(c.X, c.Z)) continue;
                        if (FootprintMargin(c, t, nrm, 1.05) < 0) continue;
                        bool overlap = false;
                        foreach (var o in lots) if (Math.Abs(o.X - c.X) < 12 && Math.Abs(o.Z - c.Z) < 12 && V2.Dist(new V2(o.X, o.Z), c) < 12) { overlap = true; break; }
                        if (overlap) continue;
                        var r = Rng.ForStage(_seed, "scatter-lot" + e.Id + "," + k + "," + side);
                        lots.Add(new Lot
                        {
                            X = c.X,
                            Z = c.Z,
                            Y = _t.Height(c.X, c.Z),
                            Edge = e.Id,
                            K = k,
                            Side = side,
                            Yaw = side > 0 ? t.Heading : t.Heading + Math.PI,
                            Variant = r.Int(4),
                            Scale = r.Range(0.95, 1.05)
                        });
                    }
                }
            }
            _lots[s] = lots;
            return lots;
        }

        /// <summary>Smallest road margin (kind House) over the centre and the four corners of a footprint; u = unit along the road, nrm = unit across.</summary>
        double FootprintMargin(V2 c, V2 u, V2 nrm, double grow)
        {
            double best = Margin(ScatterKind.House, c.X, c.Z);
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    V2 q = c + u * (a * HouseAlongM * 0.5 * grow) + nrm * (b * HouseDepthM * 0.5 * grow);
                    best = Math.Min(best, Margin(ScatterKind.House, q.X, q.Z));
                }
            return best;
        }

        bool NearLot(double x, double z, double dist)
        {
            for (int s = 0; s < _set.Count; s++)
            {
                var box = _villageBox[s];
                if (box == null || x < box[0] - dist || x > box[2] + dist || z < box[1] - dist || z > box[3] + dist) continue;
                foreach (var l in LotsOf(s))
                    if (Math.Abs(l.X - x) < dist && Math.Abs(l.Z - z) < dist && Math.Sqrt((l.X - x) * (l.X - x) + (l.Z - z) * (l.Z - z)) < dist) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- sector

        /// <summary>Instances of one 256 m sector, in the fixed order trees, bushes, houses, street trees. Independent of every other sector.</summary>
        public List<Instance> Sector(int sx, int sz)
        {
            double ox = sx * SectorM, oz = sz * SectorM;
            var trees = new List<Instance>();
            var bushes = new List<Instance>();
            var rng = Rng.ForStage(_seed, "scatter" + sx + "," + sz);

            // trees: jittered grid, one cell = one candidate, six draws per cell whatever happens to it (stable thinning)
            double cell = SectorM / TreeGrid;
            for (int i = 0; i < TreeGrid; i++)
                for (int j = 0; j < TreeGrid; j++)
                {
                    double u = rng.NextDouble(), jx = rng.NextDouble(), jz = rng.NextDouble(), yaw = rng.NextDouble(), sc = rng.NextDouble(), vr = rng.NextDouble();
                    double x = ox + (i + jx) * cell, z = oz + (j + jz) * cell;
                    double m = Mask(x, z) + MaskOffset;
                    double p = Smooth((m - MaskThreshold) / 0.10);
                    if (DensityFactor != null && p > 0) p *= DensityFactor(x, z);
                    if (p <= 0 || u >= p * SettlementFactor(x, z)) { TreeRejected[0]++; continue; }
                    if (Margin(ScatterKind.Tree, x, z) < 0) { TreeRejected[1]++; continue; }
                    if (IsWater != null && IsWater(x, z)) { TreeRejected[2]++; continue; }
                    if (NearLot(x, z, ForestFromLotM)) { TreeRejected[3]++; continue; }
                    double y = _t.Height(x, z);
                    if (y > TreeLine) { TreeRejected[4]++; continue; }
                    if (y < WaterLine) { TreeRejected[5]++; continue; }
                    if (InHole(x, z)) { TreeRejected[6]++; continue; }
                    if (SlopeDeg(x, z) > TreeMaxSlopeDeg) { TreeRejected[7]++; continue; }
                    trees.Add(new Instance { Kind = ScatterKind.Tree, Variant = (byte)(vr * 3), X = (float)x, Y = (float)y, Z = (float)z, Yaw = (float)(yaw * 2 * Math.PI), Scale = (float)(0.8 + 0.5 * sc) });
                }

            // bushes: a band along the forest boundary and along the road clearance line inside the forest
            double bcell = SectorM / BushGrid;
            for (int i = 0; i < BushGrid; i++)
                for (int j = 0; j < BushGrid; j++)
                {
                    double u = rng.NextDouble(), jx = rng.NextDouble(), jz = rng.NextDouble(), yaw = rng.NextDouble(), sc = rng.NextDouble(), vr = rng.NextDouble();
                    double x = ox + (i + jx) * bcell, z = oz + (j + jz) * bcell;
                    double m = Mask(x, z) + MaskOffset - MaskThreshold;
                    if (m < -0.08) continue;
                    double f = SettlementFactor(x, z);
                    if (f <= 0) continue;
                    double mg = Margin(ScatterKind.Bush, x, z);
                    if (mg < 0) continue;
                    if (IsWater != null && IsWater(x, z)) continue;
                    bool boundary = m < 0.04;
                    bool roadBand = m >= 0 && mg < BushBandM;
                    if (!boundary && !roadBand) continue;
                    if (u >= 0.6 * f) continue;
                    if (NearLot(x, z, ForestFromLotM)) continue;
                    double y = _t.Height(x, z);
                    if (y > TreeLine || y < WaterLine || InHole(x, z) || SlopeDeg(x, z) > TreeMaxSlopeDeg) continue;
                    bushes.Add(new Instance { Kind = ScatterKind.Bush, Variant = (byte)(vr * 2), X = (float)x, Y = (float)y, Z = (float)z, Yaw = (float)(yaw * 2 * Math.PI), Scale = (float)(0.7 + 0.4 * sc) });
                }

            var houses = new List<Instance>();
            var street = new List<Instance>();
            for (int s = 0; s < _set.Count; s++)
            {
                var box = _villageBox[s];
                if (box == null || ox + SectorM < box[0] || ox > box[2] || oz + SectorM < box[1] || oz > box[3]) continue;
                foreach (var l in LotsOf(s))
                {
                    if (Math.Floor(l.X / SectorM) != sx || Math.Floor(l.Z / SectorM) != sz) continue;
                    houses.Add(new Instance { Kind = ScatterKind.House, Variant = (byte)l.Variant, X = (float)l.X, Y = (float)l.Y, Z = (float)l.Z, Yaw = (float)l.Yaw, Scale = (float)l.Scale });
                }
                foreach (int ei in _villageEdges[s]) StreetTrees(s, _g.Edges[ei], sx, sz, street);
            }

            Thin(trees, TreeCap); Thin(bushes, TreeCap); Thin(houses, BuildingCap);
            var all = new List<Instance>(trees.Count + bushes.Count + houses.Count + street.Count);
            all.AddRange(trees); all.AddRange(bushes); all.AddRange(houses); all.AddRange(street);
            return all;
        }

        /// <summary>Deterministic thinning: keep every n-th instance, n = ceil(count / cap).</summary>
        static void Thin(List<Instance> l, int cap)
        {
            if (l.Count <= cap) return;
            int n = (l.Count + cap - 1) / cap, w = 0;
            for (int i = 0; i < l.Count; i += n) l[w++] = l[i];
            l.RemoveRange(w, l.Count - w);
        }

        void StreetTrees(int s, GEdge e, int sx, int sz, List<Instance> into)
        {
            int n = (int)Math.Floor((e.LengthM - 9) / StreetTreeSpacingM) + 1;
            var lots = LotsOf(s);
            for (int k = 0; k < n; k++)
            {
                V2 p, t;
                At(e, 9 + k * StreetTreeSpacingM, out p, out t);
                for (int side = -1; side <= 1; side += 2)
                {
                    var r = Rng.ForStage(_seed, "scatter-st" + e.Id + "," + k + "," + side);
                    double keep = r.NextDouble(), sc = r.NextDouble(), yaw = r.NextDouble();
                    V2 q = p + t.Left * (side * (e.WidthM * 0.5 + StreetTreeOffsetM));
                    if (Math.Floor(q.X / SectorM) != sx || Math.Floor(q.Z / SectorM) != sz) continue;
                    if (keep >= 0.5) continue;
                    if (IsWater != null && IsWater(q.X, q.Z)) continue;
                    if (Margin(ScatterKind.StreetTree, q.X, q.Z) < 0) continue;
                    bool onLot = false;
                    foreach (var l in lots)
                    {
                        if (Math.Abs(l.X - q.X) > 12 || Math.Abs(l.Z - q.Z) > 12) continue;
                        double cs = Math.Cos(l.Yaw), sn = Math.Sin(l.Yaw);
                        double du = (q.X - l.X) * cs + (q.Z - l.Z) * sn, dn = -(q.X - l.X) * sn + (q.Z - l.Z) * cs;
                        if (Math.Abs(du) < HouseAlongM * 0.5 + 2 && Math.Abs(dn) < HouseDepthM * 0.5 + 2) { onLot = true; break; }
                    }
                    if (onLot) continue;
                    double y = _t.Height(q.X, q.Z);
                    if (y > TreeLine || y < WaterLine || InHole(q.X, q.Z)) continue;
                    into.Add(new Instance { Kind = ScatterKind.StreetTree, Variant = 0, X = (float)q.X, Y = (float)y, Z = (float)q.Z, Yaw = (float)(yaw * 2 * Math.PI), Scale = (float)(0.9 + 0.2 * sc) });
                }
            }
        }

        // ---------------------------------------------------------------- window helpers, hash, audit

        public static int SectorOf(double v) { return (int)Math.Floor(v / SectorM); }

        /// <summary>SHA-256 (hex) over every instance of the sectors that overlap the window, rows ordered by sector (z slow, x fast) then by the Sector() order.
        /// Values are rounded to 1 mm (1e-3 rad for the yaw, 1e-3 for the scale).</summary>
        public string Hash(double x0, double z0, double size)
        {
            var sb = new StringBuilder();
            for (int sz = SectorOf(z0); sz <= SectorOf(z0 + size - 1e-9); sz++)
                for (int sx = SectorOf(x0); sx <= SectorOf(x0 + size - 1e-9); sx++)
                {
                    sb.Append("S").Append(sx).Append(',').Append(sz).Append('\n');
                    foreach (var it in Sector(sx, sz)) AppendRow(sb, it);
                }
            using (var h = SHA256.Create())
            {
                var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                foreach (var b in bytes) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        public static string Hash(IEnumerable<KeyValuePair<string, List<Instance>>> sectors)
        {
            var sb = new StringBuilder();
            foreach (var kv in sectors) { sb.Append(kv.Key).Append('\n'); foreach (var it in kv.Value) AppendRow(sb, it); }
            using (var h = SHA256.Create())
            {
                var hex = new StringBuilder();
                foreach (var b in h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        static long Q(double v) { return (long)Math.Round(v * 1000.0); }

        static void AppendRow(StringBuilder sb, Instance it)
        {
            sb.Append(it.Kind).Append(',').Append(it.Variant).Append(',').Append(Q(it.X).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Q(it.Y).ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Q(it.Z).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Q(it.Yaw).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Q(it.Scale).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        /// <summary>Checks (a)-(c) of the design: (a) road clearance by brute force over every edge, (b) no instance in a hole or on a span, trees/bushes/street trees inside the height band,
        /// trees under 30 deg and houses under 6 deg slope, (c) per-sector counts under the caps. Every instance is audited (acc.Excluded stays 0).</summary>
        public void AuditSector(ScatterAudit acc, int sx, int sz, List<Instance> inst)
        {
            acc.Sectors++;
            int trees = 0, bushes = 0, houses = 0, street = 0;
            foreach (var it in inst)
            {
                acc.Instances++;
                byte k = it.Kind;
                if (k == ScatterKind.Tree) trees++; else if (k == ScatterKind.Bush) bushes++; else if (k == ScatterKind.House) houses++; else street++;
                double x = it.X, z = it.Z;
                double mg = MarginBrute(k, x, z);
                if (k == ScatterKind.House)
                {
                    double cs = Math.Cos(it.Yaw), sn = Math.Sin(it.Yaw);
                    V2 u = new V2(cs, sn), nrm = new V2(-sn, cs);
                    for (int a = -1; a <= 1; a += 2)
                        for (int b = -1; b <= 1; b += 2)
                        {
                            V2 q = new V2(x, z) + u * (a * HouseAlongM * 0.5 * it.Scale) + nrm * (b * HouseDepthM * 0.5 * it.Scale);
                            mg = Math.Min(mg, MarginBrute(k, q.X, q.Z));
                        }
                }
                acc.MinMarginA = Math.Min(acc.MinMarginA, mg);
                if (mg < -1e-3) { acc.ViolationsA++; Note(acc, "a", it, sx, sz, mg); }
                bool bad = false;
                // span: not within the half width of a bridge or tunnel centreline (the margin above also keeps the clearance); hole; band; slope
                foreach (var e in _g.Edges)
                {
                    if (e.Span == "ground" || x < _ebx0[e.Id] - QueryM || x > _ebx1[e.Id] + QueryM || z < _ebz0[e.Id] - QueryM || z > _ebz1[e.Id] + QueryM) continue;
                    for (int i = 0; i + 1 < e.Pts.Length && !bad; i++)
                    {
                        V2 ab = e.Pts[i + 1] - e.Pts[i], ap = new V2(x, z) - e.Pts[i];
                        double l2 = V2.Dot(ab, ab), t = l2 < 1e-12 ? 0 : Clamp01(V2.Dot(ap, ab) / l2);
                        if (V2.Dist(new V2(x, z), e.Pts[i] + ab * t) < e.WidthM * 0.5) bad = true;
                    }
                }
                if (_t.Hole(x, z)) bad = true;
                double yTrue = _t.Height(x, z);
                if (Math.Abs(yTrue - it.Y) > 0.01) bad = true;
                if (k != ScatterKind.House && (it.Y > TreeLine + 1e-3 || it.Y < WaterLine - 1e-3)) bad = true;
                double slope = SlopeDeg(x, z);
                if ((k == ScatterKind.Tree || k == ScatterKind.Bush) && slope > TreeMaxSlopeDeg + 1e-6) bad = true;
                if (k == ScatterKind.House && slope >= HouseMaxSlopeDeg) bad = true;
                if (bad) { acc.ViolationsB++; Note(acc, "b", it, sx, sz, mg); }
            }
            acc.Trees += trees; acc.Bushes += bushes; acc.Houses += houses; acc.StreetTrees += street;
            acc.MaxTreesInSector = Math.Max(acc.MaxTreesInSector, trees);
            acc.MaxBuildingsInSector = Math.Max(acc.MaxBuildingsInSector, houses);
            if (trees > TreeCap || houses > BuildingCap) { acc.ViolationsC++; if (acc.Samples.Count < 8) acc.Samples.Add("c sector " + sx + "," + sz + " trees " + trees + " houses " + houses); }
        }

        static void Note(ScatterAudit acc, string chk, Instance it, int sx, int sz, double mg)
        {
            if (acc.Samples.Count < 8)
                acc.Samples.Add(chk + " " + ScatterKind.Name(it.Kind) + " at " + it.X.ToString("0.0", CultureInfo.InvariantCulture) + "," + it.Z.ToString("0.0", CultureInfo.InvariantCulture) + " sector " + sx + "," + sz + " margin " + mg.ToString("0.00", CultureInfo.InvariantCulture));
        }
    }
}
